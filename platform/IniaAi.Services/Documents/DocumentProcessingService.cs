using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Services;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Pages;
using IniaAi.FileFormats.Word;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using static IniaAi.Core.Services.OcrTranscriptionService;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Services.Documents;

/// <summary>
/// Service responsible for processing documents and loading context into
/// workflow handlers.
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    #region Constants

    private const string ERROR_VECTOR_MODEL = "ERROR";
    private const int EMBEDDINGS_BATCH_THRESHOLD = 40;
    private const double MAX_PLAUSIBLE_AVG_WORD_LENGTH = 15;
    private const int MIN_WORDS_FOR_WORD_LENGTH_CHECK = 5;
    private const double MIN_LETTER_SHARE_PER_WORD = 0.3;
    private const double MIN_LATIN_LETTER_RATIO = 0.9;
    private const char LATIN_SCRIPT_MAX_CHAR = '\u024F';
    private const char LATIN_EXTENDED_MIN_CHAR = '\u0100';
    private const int MIN_LETTERS_FOR_SCRAMBLED_LINE = 8;
    private const double MIN_LATIN_EXTENDED_SHARE_OF_SCRAMBLED_LINE = 0.4;
    private const int MIN_CONTROL_CHARS_AT_LETTERS_OF_SCRAMBLED_LINE = 2;
    private const int MIN_LATIN_EXTENDED_LETTERS_BESIDE_CONTROL_CHARS = 4;
    private const char LATIN_ACCENTED_MIN_CHAR = '\u00C0';
    private const int MIN_LETTERS_FOR_SCRAMBLED_WORD = 6;
    private const int MIN_LATIN_EXTENDED_LETTERS_OF_SCRAMBLED_WORD = 3;
    private const double MIN_ACCENTED_SHARE_OF_SCRAMBLED_WORD = 0.5;
    private const int MIN_CASE_FLIPS_OF_SCRAMBLED_WORD = 2;

    private static readonly char[] WORD_TRIM_CHARS = ['.', '\u2026'];

    #endregion

    #region Private fields

    private readonly PlatformSettings _platformSettings;
    private readonly IEmbeddingsConnector _embeddingsConnector;
    private readonly ILogger _logger;
    private readonly PdfExtractor _pdfExtractor;
    private readonly IPdfFormat _pdfFormat;
    private readonly IWorkbookFormat _workbookFormat;
    private readonly IWordPageReader _wordPageReader;
    private readonly DocumentAnalysisService _documentAnalyzer;
    private readonly ExcelSheetAnalysisService _excelSheetAnalysisService;
    private readonly OcrTranscriptionService _ocrTranscriptionService;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly TenantModelFamilyResolver _tenantModelFamilyResolver;
    private readonly ITokenUsageService _tokenUsageService;

    // Current workflow execution context for token usage tracking
    private string? _currentWorkflowTypeId;
    private string? _currentWorkflowExecutionId;
    private string? _currentUserId;

    #endregion

    #region Constructors

    /// <summary>
    /// Constructor for tests.
    /// </summary>
    /// <param name="platformSettings"></param>
    /// <param name="logger"></param>
    /// <param name="pdfExtractor"></param>
    /// <param name="embeddingsConnector"></param>
    /// <param name="llamaParseConnector"></param>
    /// <param name="tokenUsageService"></param>
    internal DocumentProcessingService(
        PlatformSettings platformSettings,
        ILogger<DocumentProcessingService> logger,
        PdfExtractor pdfExtractor,
        IPdfFormat pdfFormat,
        IWorkbookFormat workbookFormat,
        IWordPageReader wordPageReader,
        IEmbeddingsConnector embeddingsConnector,
        DocumentAnalysisService documentAnalyzer,
        ExcelSheetAnalysisService excelSheetAnalysisService,
        OcrTranscriptionService ocrTranscriptionService,
        IFileStorageManager fileStorageManager,
        TenantModelFamilyResolver tenantModelFamilyResolver,
        ITokenUsageService tokenUsageService)
    {
        _platformSettings = platformSettings;
        _logger = logger;
        _pdfExtractor = pdfExtractor;
        _pdfFormat = pdfFormat;
        _workbookFormat = workbookFormat;
        _wordPageReader = wordPageReader;
        _embeddingsConnector = embeddingsConnector;
        _documentAnalyzer = documentAnalyzer;
        _excelSheetAnalysisService = excelSheetAnalysisService;
        _ocrTranscriptionService = ocrTranscriptionService;
        _fileStorageManager = fileStorageManager;
        _tenantModelFamilyResolver = tenantModelFamilyResolver;
        _tokenUsageService = tokenUsageService;
    }

    /// <summary>
    /// Constructor for dependency injection.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    /// <param name="pdfExtractor"></param>
    /// <param name="embeddingsConnector"></param>
    /// <param name="llamaParseConnector"></param>
    /// <param name="tokenUsageService"></param>
    public DocumentProcessingService(
        IOptions<PlatformSettings> options,
        ILogger<DocumentProcessingService> logger,
        PdfExtractor pdfExtractor,
        IPdfFormat pdfFormat,
        IWorkbookFormat workbookFormat,
        IWordPageReader wordPageReader,
        IEmbeddingsConnector embeddingsConnector,
        DocumentAnalysisService documentAnalyzer,
        ExcelSheetAnalysisService excelSheetAnalysisService,
        OcrTranscriptionService ocrTranscriptionService,
        IFileStorageManager fileStorageManager,
        TenantModelFamilyResolver tenantModelFamilyResolver,
        ITokenUsageService tokenUsageService) : this(
            options.Value,
            logger,
            pdfExtractor,
            pdfFormat,
            workbookFormat,
            wordPageReader,
            embeddingsConnector,
            documentAnalyzer,
            excelSheetAnalysisService,
            ocrTranscriptionService,
            fileStorageManager,
            tenantModelFamilyResolver,
            tokenUsageService)
    {
    }

    #endregion

    #region Public methods

    public async Task ProcessDocumentsAsync(
        List<DocumentInfo> documentInfos,
        IUserDataUnitOfWork userDataUnitOfWork,
        Workflow? workflow,
        string? workflowTypeId = null,
        string? workflowExecutionId = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // Store workflow execution context for token usage tracking
        _currentWorkflowTypeId = workflowTypeId;
        _currentWorkflowExecutionId = workflowExecutionId;
        _currentUserId = userId;

        _logger.LogInformation(
            "Processing {CountDocuments} documents.",
            documentInfos.Count);

        await _ocrTranscriptionService.SetupPromptsAsync()
            .ConfigureAwait(false);

        foreach (DocumentInfo documentInfo in documentInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (documentInfo.Document is null)
            {
                _logger.LogError(
                    "DocumentInfo {DocumentId} has null Document.",
                    documentInfo.DocumentId);
                continue;
            }

            // Email is not processed for pages; legacy binary .doc is never
            // ingested (native ingestion covers OOXML .docx only).
            if (documentInfo.Document.MediaType is MediaType.MessageRfc822
                or MediaType.ApplicationVndMsOutlook
                or MediaType.ApplicationMsWord)
            {
                _logger.LogInformation(
                    "Skipping processing for email or legacy Word document {DocumentId}.",
                    documentInfo.Document.Id);
                continue;
            }

            _logger.LogInformation(
                "Processing document {DocumentId}.",
                documentInfo.Document.Id);

            DocumentClass? documentClass = workflow?.DocumentClasses
                .FirstOrDefault(dc => dc.Key == documentInfo.DocumentClass);

            await ProcessPagesAsync(
                documentInfo.Document,
                userDataUnitOfWork,
                workflow,
                documentClass,
                workflowTypeId,
                shouldUseOcr: documentInfo.ShouldUseOcr,
                cancellationToken: cancellationToken);

            documentInfo.Document.RecordProcessed(DateTime.UtcNow);

            // Quick fix. ideally this will come in workflowhandler on the block
            // itself so we also load only what is needed from the classes
            // however restricted currently due to circulare dependency of projects.
            // Here we are loading all
            if (documentInfo.Document.FileMetadata is not null)
            {
                using Stream fileStream = await GetFileStreamAsync(documentInfo.Document.FileMetadata);
            }

            _logger.LogInformation(
                "Saving document {DocumentId} after processing.",
                documentInfo.Document.Id);

            _ = await userDataUnitOfWork.CompleteAsync(cancellationToken);

            _logger.LogInformation(
                "Document {DocumentId} processed.",
                documentInfo.Document.Id);
        }

        _logger.LogInformation(
            "Documents processed.");
    }

    /// <summary>
    /// Loads context from attached files into the workflow handler's workflow.
    /// </summary>
    /// <param name="documentInfos"></param>
    /// <param name="workflow"></param>
    /// <param name="allPages"></param>
    /// <param name="onDocumentProcessed"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task LoadContextToWorkflowAsync(
        ICollection<DocumentInfo> documentInfos,
        Workflow workflow,
        List<Page> allPages,
        Func<string?, Task>? onDocumentProcessed = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Loading context to workflow handler with {CountDocuments} documents.",
            documentInfos.Count);

        foreach (DocumentInfo documentInfo in documentInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (documentInfo.Document is null)
            {
                _logger.LogError(
                    "Document {DocumentId} is null.",
                    documentInfo.DocumentId);

                continue;
            }

            _logger.LogInformation(
                "Loading document {DocumentId} to workflow.",
                documentInfo.Document.Id);

            // Consumers that match on Document.Class (Json2Xlsx inject and extract) search
            // AllPages, so a document a block produced mid-run must carry its class here,
            // the same way the orchestrator stamps uploaded documents at submission.
            documentInfo.Document.Class = documentInfo.DocumentClass;

            AppendPagesToAllPagesList(allPages, documentInfo.Document);

            AttachDocumentToBlocks(
                workflow,
                documentInfo);

            _logger.LogInformation(
                "Document {DocumentId} processed.",
                documentInfo.Document.Id);
        }

        if (WorkflowUsesToc(workflow))
        {
            _logger.LogInformation("Workflow uses auto-detected TOC, generating TOC.");

            await GenerateTocsAsync(documentInfos, workflow, onDocumentProcessed, cancellationToken);
        }
    }

    #endregion

    #region Private methods

    private static bool WorkflowUsesToc(Workflow workflow)
    {
        return workflow.Sections
            .SelectMany(s => s.Blocks)
            .Any(b => b.RagSettings.Any(r => r.Type is RagTypes.UseAutoDetectedTableOfContents));
    }

    private async Task GenerateTocsAsync(
        ICollection<DocumentInfo> documentInfos,
        Workflow workflow,
        Func<string?, Task>? onDocumentProcessed,
        CancellationToken cancellationToken = default)
    {
        var autoDetectedTocRags = workflow
            .Sections
            .OrderBy(s => s.Order)
            .SelectMany(s => s.Blocks.OrderBy(b => b.Order))
            .SelectMany(b => b.RagSettings)
            .Where(r => r.Type is RagTypes.UseAutoDetectedTableOfContents)
            .ToList();

        List<Document> documents = SelectDocumentsForToc(documentInfos, autoDetectedTocRags);

        if (documents.Count == 0)
        {
            _logger.LogInformation("No documents to generate TOC for.");

            return;
        }

        Dictionary<string, string?> tocModelsMap = GetTocModelsMap(documentInfos, autoDetectedTocRags);

        foreach (Document document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tocModelsMap.TryGetValue(document.Id, out string? tocModel)
                && tocModel is not null)
            {
                _logger.LogInformation(
                    "Generating TOC for document {DocumentId} with model {TocModel}.",
                    document.Id,
                    tocModel);
            }
            else
            {
                _logger.LogWarning(
                    "No TOC model found for document {DocumentId}.",
                    document.Id);

                return;
            }

            _documentAnalyzer.GenerateTocUsingModelToc(document, tocModel);

            if (onDocumentProcessed is not null)
            {
                await onDocumentProcessed(document.Id)
                    .ConfigureAwait(false);
            }
        }
    }

    private Dictionary<string, string?> GetTocModelsMap(
        ICollection<DocumentInfo> documentInfos,
        List<RagSettings> autoDetectedTocRags)
    {
        _logger.LogInformation(
            "Getting TOC models map for {CountDocuments} documents.",
            documentInfos.Count);

        Dictionary<string, string?> tocModelsMap = [];

        foreach (DocumentInfo documentInfo in documentInfos)
        {
            _logger.LogTrace(
                "Searching for TOC models for document {DocumentId}.",
                documentInfo.Document?.Id);

            foreach (RagSettings rag in autoDetectedTocRags)
            {
                if (!documentInfo.CanBeProcessedForBlock(rag.Block
                    ?? throw new InvalidOperationException("Block is null.")))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(rag.TableOfContentsInput))
                {
                    _logger.LogWarning(
                        "Auto TOC RAG settings for block {BlockName} do not have a TOC model.",
                        rag.Block?.Name);

                    continue;
                }

                tocModelsMap[documentInfo.DocumentId] = rag.TableOfContentsInput;

                break;
            }

            if (tocModelsMap.TryGetValue(documentInfo.DocumentId, out string? tocModel))
            {
                _logger.LogInformation(
                    "TOC model for document {DocumentId} found: {TocModel}.",
                    documentInfo.Document?.Id,
                    tocModel);
            }
            else
            {
                _logger.LogInformation(
                    "TOC model for document {DocumentId} not found.",
                    documentInfo.Document?.Id);
            }
        }

        _logger.LogInformation(
            "TOC models map for {CountDocuments} documents obtained.",
            documentInfos.Count);
        return tocModelsMap;
    }

    private List<Document> SelectDocumentsForToc(ICollection<DocumentInfo> documentInfos, List<RagSettings> autoDetectedTocRags)
    {
        List<Document> documents = [];

        foreach (DocumentInfo documentInfo in documentInfos)
        {
            if (documentInfo.Document is null)
            {
                _logger.LogError(
                    "Document {DocumentId} is null.",
                    documentInfo.DocumentId);
                continue;
            }

            foreach (RagSettings rag in autoDetectedTocRags)
            {
                if (documentInfo.CanBeProcessedForBlock(rag.Block
                    ?? throw new InvalidOperationException("Block is null.")))
                {
                    documents.Add(documentInfo.Document);

                    break;
                }
            }
        }

        return documents;
    }

    private async Task ProcessPagesAsync(
        Document document,
        IUserDataUnitOfWork userDataUnitOfWork,
        Workflow? workflow,
        DocumentClass? documentClass,
        string? workflowTypeId,
        bool shouldUseOcr,
        CancellationToken cancellationToken = default)
    {
        // Outside a workflow there is no overview prompt: the overviews an earlier run paid for stay,
        // so the next run of that workflow reuses them. Output documents' overviews are not used, so
        // skipping them saves tokens.
        string? skipOverviewsBecause = workflow is null ? "it being processed outside a workflow"
            : documentClass?.IsOutputDocument is true ? "it being an output document class"
            : document.Name.EndsWith("_no_overview", StringComparison.OrdinalIgnoreCase) ? "its name ending with '_no_overview'"
            : null;
        bool skipOverviewGeneration = skipOverviewsBecause is not null;

        // The current workflow class's category and key win over whatever a
        // previous run persisted, so the stored document reads as the class
        // actually in use. The persisted value is only a fallback for runs
        // without class context.
        if (!skipOverviewGeneration
            && documentClass is not null
            && !string.IsNullOrEmpty(documentClass.DocumentCategoryId))
        {
            if (!string.Equals(document.DocumentCategoryId, documentClass.DocumentCategoryId, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Document {DocumentId} category changing from {OldCategory} to {NewCategory} (class {ClassKey}).",
                    document.Id,
                    document.DocumentCategoryId ?? "(null)",
                    documentClass.DocumentCategoryId,
                    documentClass.Key);
            }

            document.DocumentCategoryId = documentClass.DocumentCategoryId;
            document.Class = documentClass.Key;
        }

        // Skip page extraction if already fully processed - overview generation and embeddings will still run
        if (ShouldSkipProcessing(document))
        {
            _logger.LogInformation(
                "Document {DocumentId} already has all pages processed, skipping page extraction.",
                document.Id);
        }
        else
        {
            Func<Document, bool, CancellationToken, Task> createPages = PageCreatorFor(document.MediaType)
                ?? throw new InvalidOperationException($"Documents of type {document.MediaType} are never processed into pages.");
            await createPages(document, shouldUseOcr, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (skipOverviewGeneration)
        {
            _logger.LogInformation(
                "Skipping overview generation for document {DocumentId} due to {Reason}.",
                document.Id,
                skipOverviewsBecause);
        }
        else
        {
            // Reset token accumulator before overview generation
            _documentAnalyzer.ResetTokenAccumulator();

            await _documentAnalyzer
                .GenerateOverviewsAsync(
                    document,
                    document.OrganizationId,
                    documentClass?.PageOverviews,
                    workflow?.Configuration?.PageOverviews,
                    workflowTypeId,
                    cancellationToken);

            // Delete embedding vectors orphaned by overview regeneration
            if (_documentAnalyzer.OrphanedEmbeddingVectors.Count > 0)
            {
                _logger.LogInformation(
                    "Removing {Count} orphaned embedding vectors for document {DocumentId}.",
                    _documentAnalyzer.OrphanedEmbeddingVectors.Count,
                    document.Id);

                userDataUnitOfWork.Documents.RemoveEmbeddingVectorsRange(
                    _documentAnalyzer.OrphanedEmbeddingVectors);
            }

            // Record overview generation LLM token usage (fire-and-forget)
            RecordOverviewGenerationLlmUsage(document);
        }

        cancellationToken.ThrowIfCancellationRequested();

        await GetEmbeddingsAsync(
                document,
                cancellationToken: cancellationToken);
    }

    private void AppendPagesToAllPagesList(
        List<Page> allPages,
        Document document)
    {
        if (allPages.Any(p => p.DocumentId == document.Id))
        {
            _logger.LogInformation(
                "Document {DocumentId}'s pages are already in the workflow.",
                document.Id);

            return;
        }

        List<Page> pagesFromDocsWithTheSameName = [.. allPages
            .Where(p => p.Document is not null
                && p.Document.Name == document.Name)];

        if (pagesFromDocsWithTheSameName.Count > 0)
        {
            _logger.LogWarning(
                "Document {DocumentId} has {CountPages} pages already in the workflow with the same name, removing them to avoid duplicates.",
                document.Id,
                pagesFromDocsWithTheSameName.Count);

            foreach (Page page in pagesFromDocsWithTheSameName)
            {
                // This is hack to prevent legacy editor from adding multiple
                // documents with same name to the list of all pages. Now that
                // Json2Xlsx, IntelligentSearch, and other blocks create documents
                // they need to be added to the list, but if the same block is
                // executed multiple times, then multiple documents with same name
                // are added. So we remove the existing document with same name.
                _ = allPages.Remove(page);
            }
        }

        foreach (Page page in document.Pages.OrderBy(p => p.PageNumber))
        {
            allPages.Add(page);
        }

        _logger.LogInformation(
            "Document {DocumentId}'s pages added to the workflow.",
            document.Id);
    }

    private void AttachDocumentToBlocks(
        Workflow workflow,
        DocumentInfo documentInfo)
    {
        foreach (Section section in workflow.Sections)
        {
            foreach (Block sectionBlock in section.Blocks)
            {
                AttachDocumentToBlock(sectionBlock, documentInfo);
            }
        }
    }

    private void AttachDocumentToBlock(
        Block block,
        DocumentInfo documentInfo)
    {
        if (documentInfo.Document is null)
        {
            _logger.LogError(
                "Document {documentId} is null.",
                documentInfo.DocumentId);

            return;
        }

        if (!documentInfo.CanBeProcessedForBlock(block)
            || !block.ShouldUseDocuments())
        {
            _logger.LogDebug(
                "Document {DocumentId} cannot be processed for block {BlockName}.",
                documentInfo.Document.Id,
                block.Name);

            return;
        }

        if (!block.ReceivesDocuments())
        {
            _logger.LogWarning(
                "Block {BlockName} does not have RAG settings.",
                block.Name);

            return;
        }

        if (block.Documents.Any(d => d.Id == documentInfo.DocumentId))
        {
            _logger.LogDebug(
                "Document {DocumentId} is already attached to block {BlockName}.",
                documentInfo.Document.Id,
                block.Name);

            return;
        }

        if (block.Documents.FirstOrDefault(d => d.Name == documentInfo.Document.Name) is Document existingDocument)
        {
            // This is hack to prevent legacy editor from adding multiple
            // documents with same name to the block. Now that
            // Json2Xlsx, IntelligentSearch, and other blocks create documents
            // then need to be added to the blocks, but if the same block is
            // executed multiple times, then multiple documents with same name
            // are added. So we remove the existing document with same name.
            _ = block.Documents.Remove(existingDocument);

            _logger.LogWarning(
                "A document with the same name as document {DocumentId} already exists in block {BlockName}, removing it to avoid a name conflict.",
                documentInfo.Document.Id,
                block.Name);
        }

        _logger.LogInformation(
            "Attaching document {DocumentId} to block {BlockName}.",
            documentInfo.Document.Id,
            block.Name);

        documentInfo.Document.CurrentDocumentInfo = documentInfo;

        block.Documents.Add(documentInfo.Document);
    }

    private async Task CreatePdfPagesAsync(
        Document document,
        bool shouldUseOcr = false,
        Func<string?, Task>? onDocumentProcessed = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing PDF document {DocumentId}.",
            document.Id);

        if (document.FileMetadata is null)
        {
            _logger.LogError(
                "Document {DocumentId} does not have FileMetadata.",
                document.Id);

            return;
        }

        if (document.Pages.Count > 0 && document.Pages.All(IsPageProcessed))
        {
            _logger.LogWarning(
                "Document {DocumentId} already has all pages processed.",
                document.Id);
            return;
        }

        (List<int> pagesToProcess, List<PdfExtractionPage> pagesTexts) = await ExtractPdfTextAsync(document, document.FileMetadata, cancellationToken);

        foreach (PdfExtractionPage extractedPage in pagesTexts)
        {
            //TODO: Sanitize the batch text to avoid issues with large inputs
            //var sanitizedPageText = TextSanitizer.Sanitize(pageText);

            int pageNumber = GetPageNumber(extractedPage, document);

            if (extractedPage.IsLeftSide is not bool isLeftSide)
            {
                SetExtractedTextToPage(
                    document,
                    extractedPage,
                    pageNumber,
                    isLeftSide: null);

                _logger.LogDebug(
                    "Document {DocumentId} page {PageNumber} is single page.",
                    document.Id,
                    pageNumber);

                continue;
            }

            if (isLeftSide)
            {
                _logger.LogDebug(
                    "Document {DocumentId} page {PageNumber} is left side, making space for right side.",
                    document.Id,
                    pageNumber);

                MakeRoomForRightSidePage(
                    document,
                    pageNumber);
            }
            else
            {
                pageNumber += 1;

                _logger.LogDebug(
                    "Document {DocumentId} page {PageNumber} is right side.",
                    document.Id,
                    pageNumber);
            }

            SetExtractedTextToPage(
                document,
                extractedPage,
                pageNumber,
                isLeftSide);
        }

        if (onDocumentProcessed is not null)
        {
            await onDocumentProcessed(document.Id);
        }

        // determine what to ocr
        List<int> pagesToOcr = [];

        if (shouldUseOcr)
        {
            pagesToOcr.AddRange(pagesToProcess);
        }
        else
        {
            // only ocr some pages
            pagesToOcr = [.. pagesTexts
                .Where(s => IsPageBad(s.Text, s.FilePageNumber))
                .Select(s => s.FilePageNumber)
                .Distinct()
                .Order()];
            _logger.LogInformation(
                "Document {DocumentId} has {CountPagesToOcr} pages to OCR.",
                document.Id,
                pagesToOcr.Count);
        }

        if (pagesToOcr.Count != 0)
        {
            foreach (int filePageNumber in pagesToOcr)
            {
                foreach (Page page in document.Pages.Where(p => p.FilePageNumber == filePageNumber))
                {
                    page.Status = PageStatus.OCRFallback;
                }
            }

            byte[] fileBytesForOcr = await GetFileBytesAsync(document.FileMetadata);

            OcrTokenRecordingContext? tokenRecordingContext = BuildOcrTokenRecordingContext(document);

            ConcurrentDictionary<int, OcrProcessingResult> results = await _ocrTranscriptionService.ProcessPdf(
                fileBytesForOcr,
                document.Name,
                pagesToOcr,
                _tenantModelFamilyResolver.GetModelFamilyForFunction(document.OrganizationId, FunctionType.DocumentProcessing),
                tokenRecordingContext,
                cancellationToken);

            // Note: Per-page token recording is now done inside OcrTranscriptionService
            // with separate UsageTypes for transcription and the fidelity judge

            foreach ((int filePageNumber, OcrProcessingResult result) in results)
            {
                foreach (Page page in document.Pages.Where(p => p.FilePageNumber == filePageNumber))
                {
                    ApplyOcrResultToPage(page, result);
                }
            }
        }

        if (onDocumentProcessed is not null)
        {
            await onDocumentProcessed(document.Id);
        }
    }

    /// <summary>
    /// Reads the text of the pages still to process and closes the file before OCR opens its own copy, so one flow never
    /// holds two copies of a PDF file: in the open build each holds a PDF reader process, and their limit would wait for itself.
    /// </summary>
    private async Task<(List<int> PagesToProcess, List<PdfExtractionPage> PagesTexts)> ExtractPdfTextAsync(
        Document document,
        FileMetadata fileMetadata,
        CancellationToken cancellationToken)
    {
        using IPdfDocument pdf = _pdfFormat.Open(await GetFileStreamAsync(fileMetadata), document.Name);

        if (pdf.RepairedBytes is byte[] repairedBytes)
        {
            await PersistRepairedPdfAsync(fileMetadata, repairedBytes, cancellationToken);
        }

        List<int> pagesWithErrors = [.. document.Pages
            .Where(IsPageTextError)
            .Select(p => p.FilePageNumber)
            .Distinct()
            .Order()];
        List<int> processedPages = [.. document.Pages
            .Where(IsPageProcessed)
            .Select(p => p.FilePageNumber)
            .Except(pagesWithErrors)
            .Distinct()
            .Order()];
        List<int> pagesToProcess = [.. Enumerable
            .Range(1, pdf.PageCount)
            .Except(processedPages)];

        _logger.LogInformation(
            "Document {DocumentId} has {CountPages} pages, {CountProcessed} " +
            "pages already processed, {CountPagesWithErrors} pages with errors, " +
            "{CountPagesToProcess} pages to process.",
            document.Id,
            pdf.PageCount,
            processedPages.Count,
            pagesWithErrors.Count,
            pagesToProcess.Count);

        return (pagesToProcess, _pdfExtractor.ExtractPaginatedText(pdf, pagesToProcess));
    }

    private static void SetExtractedTextToPage(
        Document document,
        PdfExtractionPage extractedPage,
        int pageNumber,
        bool? isLeftSide)
    {
        Page page = document[pageNumber]
            ?? new(document, pageNumber, extractedPage.FilePageNumber);
        page.RawText = extractedPage.Text;
        page.Location = isLeftSide.HasValue
            ? isLeftSide.Value
                ? PageLocation.LeftSide
                : PageLocation.RightSide
            : PageLocation.FullPage;

        page.Status = extractedPage.Text.StartsWith(IChatConnector.ERROR_PREFIX) ? PageStatus.ExtractedWithErrors : PageStatus.ProcessedSuccessfully;
    }

    private static void MakeRoomForRightSidePage(Document document, int pageNumber)
    {
        // we assume, that if there is a reason for the page to be inserted,
        // that means this is a page that had a failure to be extracted previously,
        // and now it was identified as a double page, so we need to shift all
        // subsequent pages by 1
        List<Page> pagesToMove = [.. document.Pages
            .Where(p => p.PageNumber > pageNumber)
            .OrderByDescending(p => p.PageNumber)];

        foreach (Page page in pagesToMove)
        {
            page.PageNumber += 1;
        }
    }

    private int GetPageNumber(PdfExtractionPage extractedPage, Document document)
    {
        // find the page number of the last page that in the fil
        int lastPageNumberBefore = document.Pages
            .Where(p => p.FilePageNumber < extractedPage.FilePageNumber)
            .Select(p => p.PageNumber)
            .DefaultIfEmpty(0)
            .Max();

        _logger.LogDebug(
            "Document {DocumentId} extracted page {DocumentPageNumber} " +
            "maps to page number {PageNumber}.",
            document.Id,
            extractedPage.FilePageNumber,
            lastPageNumberBefore + 1);

        return lastPageNumberBefore + 1;
    }

    private async Task CreateExcelPagesAsync(
        Document document,
        Func<string?, Task>? onDocumentProcessed = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Processing Excel document {DocumentId}.",
            document.Id);

        if (document.FileMetadata is null)
        {
            _logger.LogError("" +
                "Document {DocumentId} does not have FileMetadata.",
                document.Id);

            return;
        }

        if (ShouldSkipProcessing(document))
        {
            _logger.LogInformation(
                "Document {DocumentId} already has all pages processed.",
                document.Id);

            return;
        }

        if (document.FileMetadata is null)
        {
            throw new InvalidOperationException("Document FileMetadata is null.");
        }

        byte[] fileBytes = await GetFileBytesAsync(document.FileMetadata);

        using IWorkbookDocument workbook = _workbookFormat.Open(new MemoryStream(fileBytes), document.Name);

        int existingPageCount = document.Pages.Count;

        // Reset token accumulator before analysis
        _excelSheetAnalysisService.ResetTokenAccumulator();

        await _excelSheetAnalysisService
            .ChunkSheetsAsync(
                document,
                workbook,
                organizationId: document.OrganizationId,
                cancellationToken: cancellationToken);

        // Record token usage (fire-and-forget)
        RecordExcelAnalysisLlmUsage(document);

        foreach (Page page in document.Pages)
        {
            page.Status = PageStatus.ProcessedSuccessfully;
        }

        if (onDocumentProcessed is not null)
        {
            await onDocumentProcessed(document.Id);
        }
    }

    private async Task CreateWordPagesAsync(
        Document document,
        Func<string?, Task>? onDocumentProcessed = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Processing Word document {DocumentId}.",
            document.Id);

        if (document.FileMetadata is null)
        {
            _logger.LogError(
                "Document {DocumentId} does not have FileMetadata.",
                document.Id);

            return;
        }

        if (ShouldSkipProcessing(document))
        {
            _logger.LogInformation(
                "Document {DocumentId} already has all pages processed.",
                document.Id);

            return;
        }

        byte[] fileBytes = await GetFileBytesAsync(document.FileMetadata);

        IReadOnlyList<WordPageContent> pageContents = _wordPageReader.ExtractPages(
            fileBytes,
            document.Name,
            _ocrTranscriptionService.GetVisionTier());

        OcrTokenRecordingContext? tokenRecordingContext = BuildOcrTokenRecordingContext(document);
        ModelFamily modelFamily = _tenantModelFamilyResolver.GetModelFamilyForFunction(
            document.OrganizationId,
            FunctionType.DocumentProcessing);

        Dictionary<string, OcrProcessingResult> readPictures = [];

        for (int i = 0; i < pageContents.Count; i++)
        {
            int pageNumber = i + 1;
            Page page = document[pageNumber]
                ?? new(document, pageNumber, pageNumber);
            if (IsPageProcessed(page))
            {
                continue;
            }

            WordPageContent pageContent = pageContents[i];
            List<OcrProcessingResult> regionResults = [];
            foreach (BinaryContent imageRegion in pageContent.ImageRegions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string picture = Convert.ToHexString(SHA256.HashData(imageRegion.BinaryData.ToMemory().Span));
                if (!readPictures.TryGetValue(picture, out OcrProcessingResult? result))
                {
                    result = await _ocrTranscriptionService.ProcessImageAsync(
                        imageRegion,
                        modelFamily,
                        tokenRecordingContext,
                        pageNumber,
                        cancellationToken);
                    if (result.Succeeded || result.NeedsReview || result.EmptyTranscription)
                    {
                        readPictures[picture] = result;
                    }
                }

                regionResults.Add(result);
            }

            int declinedRegionCount = regionResults.Count(result => result.NeedsReview);
            if (declinedRegionCount > 0)
            {
                _logger.LogWarning(
                    "Document {DocumentId} page {PageNumber}: {DeclinedRegionCount} image region(s) were declined by the OCR pass (model refusal, content filter or fidelity review); their content is not extracted.",
                    document.Id,
                    pageNumber,
                    declinedRegionCount);
            }

            ApplyWordOcrResultsToPage(page, pageContent.Text, regionResults);
        }

        _logger.LogInformation(
            "Document {DocumentId} produced {CountPages} pages from Word layout.",
            document.Id,
            pageContents.Count);

        if (onDocumentProcessed is not null)
        {
            await onDocumentProcessed(document.Id);
        }
    }

    private async Task CreateCsvPagesAsync(
        Document document,
        Func<string?, Task>? onDocumentProcessed = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Processing CSV document {DocumentId}.",
            document.Id);

        if (document.FileMetadata is null)
        {
            _logger.LogError(
                "Document {DocumentId} does not have FileMetadata.",
                document.Id);

            return;
        }

        if (ShouldSkipProcessing(document))
        {
            _logger.LogInformation(
                "Document {DocumentId} already has all pages processed.",
                document.Id);

            return;
        }

        if (document.FileMetadata is null)
        {
            throw new InvalidOperationException("Document FileMetadata is null.");
        }

        byte[] fileBytes = await GetFileBytesAsync(document.FileMetadata);
        using var reader = new StreamReader(new MemoryStream(fileBytes));
        WriteTextPages(document, [.. CsvPages.Read(reader)]);

        if (onDocumentProcessed is not null)
        {
            await onDocumentProcessed(document.Id);
        }
    }

    private async Task CreateTextPagesAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (document.FileMetadata is null)
        {
            throw new InvalidOperationException("Document FileMetadata is null.");
        }

        byte[] fileBytes = await GetFileBytesAsync(document.FileMetadata);
        using StreamReader reader = new(new MemoryStream(fileBytes));
        string text = await reader.ReadToEndAsync(cancellationToken);

        WriteTextPages(document, TextPages.Split(text, markdown: document.MediaType is MediaType.TextMarkdown));
    }

    private static void WriteTextPages(Document document, IReadOnlyList<string> pageTexts)
    {
        for (int index = 0; index < pageTexts.Count; index++)
        {
            int pageNumber = index + 1;
            Page page = document[pageNumber] ?? new(document, pageNumber, pageNumber);
            page.RawText = pageTexts[index];
            page.Text = pageTexts[index];
            page.Status = PageStatus.ProcessedSuccessfully;
        }
    }

    private async Task CreateMediaPagesAsync(
        Document document,
        Func<string?, Task>? onDocumentProcessed = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Processing media document {DocumentId}.",
            document.Id);

        if (document.FileMetadata is null)
        {
            _logger.LogError("" +
                "Document {DocumentId} does not have FileMetadata.",
                document.Id);

            return;
        }

        if (ShouldSkipProcessing(document))
        {
            _logger.LogInformation(
                "Document {DocumentId} already has all pages processed.",
                document.Id);

            return;
        }

        byte[] fileBytes = await GetFileBytesAsync(document.FileMetadata);

        Page page = document[1]
            ?? new(document, 1, 1);
        page.Image ??= new(new BinaryData(fileBytes), document.MediaType);
        page.ImageMediaType = document.MediaType;

        _logger.LogInformation(
            "Processing media document {DocumentId} with OCR.",
            document.Id);

        OcrTokenRecordingContext? tokenRecordingContext = BuildOcrTokenRecordingContext(document);

        OcrProcessingResult ocr_result = await _ocrTranscriptionService.ProcessImageAsync(
            page.Image,
            _tenantModelFamilyResolver.GetModelFamilyForFunction(document.OrganizationId, FunctionType.DocumentProcessing),
            tokenRecordingContext,
            page.PageNumber,
            cancellationToken);

        // Note: Per-page token recording is now done inside OcrTranscriptionService
        // with separate UsageTypes for transcription and the fidelity judge

        ApplyOcrResultToPage(page, ocr_result);

        if (onDocumentProcessed is not null)
        {
            await onDocumentProcessed(document.Id);
        }
    }

    private OcrTokenRecordingContext? BuildOcrTokenRecordingContext(Document document)
    {
        return string.IsNullOrEmpty(document.OrganizationId)
            ? null
            : new OcrTokenRecordingContext
            {
                TokenUsageService = _tokenUsageService,
                OrganizationId = document.OrganizationId,
                DocumentId = document.Id,
                UserId = _currentUserId,
                WorkflowTypeId = _currentWorkflowTypeId,
                WorkflowExecutionId = _currentWorkflowExecutionId
            };
    }

    internal static void ApplyWordOcrResultsToPage(
        Page page,
        string nativeText,
        IReadOnlyList<OcrProcessingResult> regionResults)
    {
        page.RefusalCategory = regionResults.FirstOrDefault(result => result.Refusal is not null)?.Refusal?.Category;

        if (regionResults.Any(result => result is { Succeeded: false, NeedsReview: false, EmptyTranscription: false }))
        {
            page.RawText = null;
            page.Text = null;
            page.Status = PageStatus.OCRFailed;

            return;
        }

        List<string> transcriptions = [.. regionResults
            .Where(result => result.Succeeded && !string.IsNullOrWhiteSpace(result.MarkdownResults))
            .Select(result => result.MarkdownResults!)];

        page.RawText = nativeText;
        if (transcriptions.Count == 0)
        {
            page.Status = PageStatus.ProcessedSuccessfully;

            return;
        }

        IEnumerable<string> textBlocks = string.IsNullOrWhiteSpace(nativeText)
            ? transcriptions
            : [nativeText, .. transcriptions];
        page.Text = string.Join("\n\n", textBlocks);
        page.Status = PageStatus.OCRSuccessful;
    }

    internal static void ApplyOcrResultToPage(Page page, OcrProcessingResult result)
    {
        page.RefusalCategory = result.Refusal?.Category;

        if (result.NeedsReview)
        {
            page.RawText = null;
            page.Text = null;
            page.Overview = null;
            page.EmbeddingVector = null;
            page.EmbeddingVectorId = null;
            page.Status = PageStatus.OCRNeedsReview;
        }
        else if (result.Succeeded)
        {
            page.Text = result.MarkdownResults;
            page.RawText = null;
            page.Status = PageStatus.OCRSuccessful;
        }
        else
        {
            page.RawText = null;
            page.Text = null;
            page.Status = PageStatus.OCRFailed;
        }
    }

    private async Task GetEmbeddingsAsync(
        Document document,
        Func<string?, Task>? onDocumentProcessed = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pagesToEmbed = document.Pages
            .Where(p => !IsPageEmbeddingValid(p)
                && !string.IsNullOrWhiteSpace(p.Overview))
            .ToList();

        int countAlreadyEmbedded = document.Pages.Count(p => p.EmbeddingVector is not null);
        int countErroneous = document.Pages.Count(p => p.EmbeddingVector?.Model is ERROR_VECTOR_MODEL);

        _logger.LogInformation(
            "Document {DocumentId} has {CountPages} pages, {CountAlreadyEmbedded} pages already embedded, " +
            "{CountErroneous} pages with erroneous embeddings, " +
            "{CountToEmbed} pages to embed.",
            document.Id,
            document.Pages.Count,
            countAlreadyEmbedded,
            countErroneous,
            pagesToEmbed.Count);

        if (pagesToEmbed.Count == 0)
        {
            return;
        }

        var texts = pagesToEmbed
            .Select(p => p.Overview ?? $"An overview for this content is currently unavailable")
            .ToList();

        List<double[]> allEmbeddings = [];
        bool embeddingsKeyMissing = false;

        int pagesInBatch = _platformSettings.IniaAi.BatchSizeForEmbeddings;
        int totalTokensConsumed = 0;

        for (int i = 0; i < texts.Count; i += pagesInBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = texts.Skip(i).Take(pagesInBatch).ToList();

            // Track tokens before the call
            int tokensBefore = _embeddingsConnector.TotalTokens;

            List<double[]> batchEmbeddings;

            try
            {
                batchEmbeddings = await _embeddingsConnector
                    .GetResponseAsync(batch, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ApiKeyNotSetException e)
            {
                // Without an embeddings key the pages keep the error vector, as after a failed call.
                _logger.LogWarning("Page overviews of document {DocumentId} get no embeddings: {Reason}", document.Id, e.Message);
                embeddingsKeyMissing = true;
                break;
            }

            // Calculate tokens consumed in this batch
            int tokensAfter = _embeddingsConnector.TotalTokens;
            int batchTokens = tokensAfter - tokensBefore;
            totalTokensConsumed += batchTokens;

            allEmbeddings.AddRange(batchEmbeddings);
        }

        // Record embedding token usage (fire-and-forget)
        if (totalTokensConsumed > 0)
        {
            RecordEmbeddingUsage(document, totalTokensConsumed);
        }

        for (int i = 0; i < pagesToEmbed.Count; i++)
        {
            double[]? vector = i < allEmbeddings.Count
                ? allEmbeddings[i]
                : null;

            string modelName = IsVectorValid(pagesToEmbed[i].Overview, vector)
                ? _embeddingsConnector.ModelName
                : ERROR_VECTOR_MODEL;

            if (modelName is ERROR_VECTOR_MODEL && !embeddingsKeyMissing)
            {
                _logger.LogWarning(
                    "Failed to get embedding for page {PageNumber}.",
                    pagesToEmbed[i].PageNumber);
            }

            EmbeddingVector embeddingVector = pagesToEmbed[i].EmbeddingVector
                ?? new(modelName, []);

            embeddingVector.Model = modelName;
            embeddingVector.Vector = vector ?? [];

            pagesToEmbed[i].EmbeddingVector = embeddingVector;

            if (onDocumentProcessed is not null
                && IsDocumentProcessedThresholdMet(pagesToEmbed, i))
            {
                await onDocumentProcessed(document.Id)
                    .ConfigureAwait(false);
            }
        }
    }

    private static bool IsDocumentProcessedThresholdMet(
        List<Page> pagesToEmbed,
        int i)
    {
        return i > 0
            && (i % EMBEDDINGS_BATCH_THRESHOLD == 0
            || i >= pagesToEmbed.Count - 1);
    }

    private static bool IsVectorValid(string? overview, double[]? vector)
    {
        return overview is not null
            && !overview.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.OrdinalIgnoreCase)
            && vector is not null;
    }

    /// <summary>
    /// Determines whether the page has been processed successfully,
    /// either via status flag or fallback text checks.
    /// TODO: Phase in PageStatus usage gradually this way with the old Error prefix check. Clean in time
    /// </summary>
    /// <param name="p">The page to evaluate.</param>
    /// <returns><c>true</c> if the page is considered successfully processed; otherwise, <c>false</c>.</returns>
    internal static bool IsPageProcessed(Page p)
    {
        return p.Status is PageStatus.ProcessedSuccessfully
            or PageStatus.OCRSuccessful
            or PageStatus.OCRNeedsReview
            || (p.RawText is not null
            && !p.RawText.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPageTextError(Page p)
    {
        return p.Status is PageStatus.ExtractedWithErrors or PageStatus.OCRFailed;
    }

    /// <summary>
    /// Determines if document processing should be skipped because all pages are already successfully processed.
    /// </summary>
    /// <param name="document">The document to check</param>
    /// <returns>True if all pages are already processed and processing can be skipped</returns>
    private static bool ShouldSkipProcessing(Document document)
    {
        return document.Pages.Count > 0 && document.Pages.All(IsPageProcessed);
    }

    /// <summary>
    /// The page creator for a media type, or null for the types the pipeline never pages —
    /// <see cref="MediaTypeExtensions.ProducesPages"/> is the same set, pinned by test.
    /// </summary>
    internal Func<Document, bool, CancellationToken, Task>? PageCreatorFor(MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.ApplicationPdf => (document, shouldUseOcr, cancellationToken) => CreatePdfPagesAsync(document, shouldUseOcr, cancellationToken: cancellationToken),
            MediaType.ImageJpeg or MediaType.ImagePng => (document, _, cancellationToken) => CreateMediaPagesAsync(document, cancellationToken: cancellationToken),
            MediaType.TextCsv => (document, _, cancellationToken) => CreateCsvPagesAsync(document, cancellationToken: cancellationToken),
            MediaType.ApplicationVndMsExcel
            or MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet
            or MediaType.ApplicationVndMsExcelSheetMacroEnabled12 => (document, _, cancellationToken) => CreateExcelPagesAsync(document, cancellationToken: cancellationToken),
            MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument => (document, _, cancellationToken) => CreateWordPagesAsync(document, cancellationToken: cancellationToken),
            MediaType.TextPlain or MediaType.TextMarkdown => (document, _, cancellationToken) => CreateTextPagesAsync(document, cancellationToken),
            _ => null,
        };
    }

    private static bool IsPageEmbeddingValid(Page page)
    {
        return page.EmbeddingVector is not null
            && page.EmbeddingVector.Model is not ERROR_VECTOR_MODEL;
    }

    internal bool IsPageBad(string text, int pageNumber)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogInformation("Page {PageNumber} text is blank or whitespace.", pageNumber);
            return true;
        }

        if (text.Length < 15)
        {
            _logger.LogInformation("Page {PageNumber} text is very short (Length: {Length}).", pageNumber, text.Length);
            return true;
        }

        int controlChars = text.Count(IsNonWhitespaceControl);
        double ratio = controlChars / (double)text.Length;

        if (ratio > 0.1)
        {
            string sample = text.Length > 40 ? text[..40] : text;
            _logger.LogInformation(
                "Page {PageNumber} looks like gibberish (Control char ratio: {Ratio:P2}). Sample: \"{Sample}\"",
                pageNumber,
                ratio,
                sample.Replace("\n", "\\n").Replace("\r", "\\r"));
            return true;
        }

        if (AvgWordLengthIsSuspicious(text))
        {
            _logger.LogInformation("Page {PageNumber} avg word length suspicious.", pageNumber);
            return true;
        }

        if (AvgWordLengthIsImplausiblyLong(text))
        {
            _logger.LogInformation(
                "Page {PageNumber} avg word length implausibly long, likely an obfuscated text layer.",
                pageNumber);
            return true;
        }

        if (HasScrambledLine(text))
        {
            _logger.LogInformation(
                "Page {PageNumber} has a line scrambled by a font with no Unicode map.",
                pageNumber);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A font with no Unicode map extracts as letters from Latin Extended (U+0100–U+024F) with a
    /// control character where each space should be: "November 2025" reads "cŅƴåĵÆåų\u0003ƖǈƖĂ".
    /// No language writes most of a line in those letters, and a line that mixes clean words with
    /// a scrambled run still shows control characters against Latin Extended letters, or one
    /// scrambled word: six or more letters, mostly accented, several from Latin Extended, and
    /// switching from lower to upper case at least twice inside the word. Turkish "ağırlığı" and
    /// Czech "řečiště" are as accented, but a word in any language keeps its case. Control
    /// characters against plain letters alone are not enough: some fonts map only the space to
    /// one, and their words read correctly. One such line is enough: a page can mix a clean font
    /// with a scrambled one, and the scrambled lines are often the figures.
    /// </summary>
    private static bool HasScrambledLine(string text)
    {
        foreach (ReadOnlySpan<char> line in text.AsSpan().EnumerateLines())
        {
            if (IsScrambledLine(line))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsScrambledLine(ReadOnlySpan<char> line)
    {
        int letters = 0;
        int latinExtendedLetters = 0;
        int controlCharsAtLetters = 0;

        for (int index = 0; index < line.Length; index++)
        {
            char c = line[index];

            if (char.IsLetter(c))
            {
                letters++;
                latinExtendedLetters += c is >= LATIN_EXTENDED_MIN_CHAR and <= LATIN_SCRIPT_MAX_CHAR ? 1 : 0;
            }
            else if (IsNonWhitespaceControl(c)
                && ((index > 0 && char.IsLetter(line[index - 1]))
                    || (index + 1 < line.Length && char.IsLetter(line[index + 1]))))
            {
                controlCharsAtLetters++;
            }
        }

        return (letters >= MIN_LETTERS_FOR_SCRAMBLED_LINE
                && latinExtendedLetters > letters * MIN_LATIN_EXTENDED_SHARE_OF_SCRAMBLED_LINE)
            || (controlCharsAtLetters >= MIN_CONTROL_CHARS_AT_LETTERS_OF_SCRAMBLED_LINE
                && latinExtendedLetters >= MIN_LATIN_EXTENDED_LETTERS_BESIDE_CONTROL_CHARS)
            || (latinExtendedLetters >= MIN_LATIN_EXTENDED_LETTERS_OF_SCRAMBLED_WORD && HasScrambledWord(line));
    }

    private static bool HasScrambledWord(ReadOnlySpan<char> line)
    {
        int letters = 0;
        int accentedLetters = 0;
        int latinExtendedLetters = 0;
        int caseFlips = 0;
        bool previousLetterIsLower = false;

        for (int index = 0; index <= line.Length; index++)
        {
            if (index == line.Length || char.IsWhiteSpace(line[index]))
            {
                if (letters >= MIN_LETTERS_FOR_SCRAMBLED_WORD
                    && latinExtendedLetters >= MIN_LATIN_EXTENDED_LETTERS_OF_SCRAMBLED_WORD
                    && accentedLetters > letters * MIN_ACCENTED_SHARE_OF_SCRAMBLED_WORD
                    && caseFlips >= MIN_CASE_FLIPS_OF_SCRAMBLED_WORD)
                {
                    return true;
                }

                letters = accentedLetters = latinExtendedLetters = caseFlips = 0;
                previousLetterIsLower = false;
                continue;
            }

            char c = line[index];
            if (char.IsLetter(c))
            {
                letters++;
                accentedLetters += c is >= LATIN_ACCENTED_MIN_CHAR and <= LATIN_SCRIPT_MAX_CHAR ? 1 : 0;
                latinExtendedLetters += c is >= LATIN_EXTENDED_MIN_CHAR and <= LATIN_SCRIPT_MAX_CHAR ? 1 : 0;
                caseFlips += previousLetterIsLower && char.IsUpper(c) ? 1 : 0;
                previousLetterIsLower = char.IsLower(c);
            }
        }

        return false;
    }

    private static bool IsNonWhitespaceControl(char c)
    {
        return char.IsControl(c) && !char.IsWhiteSpace(c);
    }

    private static bool AvgWordLengthIsSuspicious(string text)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return true;
        }

        double avg = words.Average(w => w.Length);
        return avg < 2.5;
    }

    private static bool AvgWordLengthIsImplausiblyLong(string text)
    {
        // Only meaningful for space-delimited scripts: CJK text legitimately has no
        // spaces (the predecessor PdfObfuscationDetector was removed for false-flagging
        // Japanese documents).
        if (!IsMostlyLatinLetters(text))
        {
            return false;
        }

        // Dot-trimming exempts TOC leader runs glued to their entry ("Japan...... 3");
        // the letter-share floor exempts tokens that are numbers with an incidental
        // letter ("P=11,597,399,876" balance-sheet columns).
        List<string> words = [.. text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(WORD_TRIM_CHARS))
            .Where(w => w.Length > 0 && w.Count(char.IsLetter) >= w.Length * MIN_LETTER_SHARE_PER_WORD)];

        if (words.Count < MIN_WORDS_FOR_WORD_LENGTH_CHECK)
        {
            return false;
        }

        return words.Average(w => w.Length) > MAX_PLAUSIBLE_AVG_WORD_LENGTH;
    }

    private static bool IsMostlyLatinLetters(string text)
    {
        int letters = 0;
        int latinLetters = 0;

        foreach (char c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            letters++;

            if (c <= LATIN_SCRIPT_MAX_CHAR)
            {
                latinLetters++;
            }
        }

        if (letters == 0)
        {
            return false;
        }

        return latinLetters >= letters * MIN_LATIN_LETTER_RATIO;
    }

    /// <summary>
    /// Stores the repaired PDF as a new blob and updates FileMetadata to point to it.
    /// The original corrupted file is kept in storage for debugging/tracing purposes.
    /// </summary>
    private async Task PersistRepairedPdfAsync(
        FileMetadata fileMetadata,
        byte[] repairedBytes,
        CancellationToken cancellationToken)
    {
        string originalKey = fileMetadata.ExternalStoragePath
            ?? throw new InvalidOperationException("ExternalStoragePath is null");

        // Store repaired file with a new key; the original corrupted blob is preserved at originalKey
        string repairedKey = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "Persisting repaired PDF: originalKey={OriginalKey}, repairedKey={RepairedKey}, " +
            "originalSize={OriginalSize}, repairedSize={RepairedSize}",
            originalKey, repairedKey,
            fileMetadata.SizeBytes, repairedBytes.Length);

        string storagePath = await _fileStorageManager.StoreFileAsync(
            fileMetadata.StorageProvider.ToString(),
            fileMetadata.OrganizationId,
            StorageCategories.Documents,
            repairedKey,
            repairedBytes,
            "application/pdf");

        // Update metadata to point to the repaired file
        fileMetadata.ExternalStoragePath = storagePath;
        fileMetadata.SizeBytes = repairedBytes.Length;
        fileMetadata.LoadBytes(repairedBytes);

        _logger.LogInformation(
            "FileMetadata updated to use repaired PDF at {RepairedKey}. " +
            "Original corrupted file preserved at {OriginalKey}.",
            repairedKey, originalKey);
    }

    /// <summary>
    /// Helper method to get file stream using IFileStorageManager - avoids wasteful byte[] conversion
    /// </summary>
    private async Task<Stream> GetFileStreamAsync(FileMetadata fileMetadata)
    {
        if (fileMetadata.AreBytesLoaded)
        {
            return new MemoryStream(fileMetadata.Bytes);
        }

        // Load and cache the bytes for future use
        using Stream stream = await _fileStorageManager.RetrieveFileAsync(
            fileMetadata.StorageProvider.ToString(),
            fileMetadata.OrganizationId,
            StorageCategories.Documents,
            fileMetadata.ExternalStoragePath ?? throw new InvalidOperationException("ExternalStoragePath is null"));

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        byte[] bytes = ms.ToArray();

        fileMetadata.LoadBytes(bytes); // Cache for next time
        return new MemoryStream(bytes);
    }

    /// <summary>
    /// Helper method to get file bytes using IFileStorageManager
    /// </summary>
    private async Task<byte[]> GetFileBytesAsync(FileMetadata fileMetadata)
    {
        if (fileMetadata.AreBytesLoaded)
        {
            return fileMetadata.Bytes;
        }

        byte[] bytes = await _fileStorageManager.RetrieveFileBytesAsync(
            fileMetadata.StorageProvider.ToString(),
            fileMetadata.OrganizationId,
            StorageCategories.Documents,
            fileMetadata.ExternalStoragePath ?? throw new InvalidOperationException("ExternalStoragePath is null"));

        fileMetadata.LoadBytes(bytes); // Cache for next time
        return bytes;
    }

    /// <summary>
    /// Records embedding token usage using fire-and-forget pattern.
    /// </summary>
    private void RecordEmbeddingUsage(Document document, int inputTokens)
    {
        if (string.IsNullOrEmpty(document.OrganizationId))
        {
            _logger.LogWarning(
                "Cannot record embedding token usage for document {DocumentId} - OrganizationId is null",
                document.Id);
            return;
        }

        // Extract provider from model name (format: "Provider: ModelId")
        string modelName = _embeddingsConnector.ModelName;
        string provider = "Unknown";
        string modelId = modelName;

        int colonIndex = modelName.IndexOf(':');
        if (colonIndex > 0)
        {
            provider = modelName[..colonIndex].Trim();
            modelId = modelName[(colonIndex + 1)..].Trim();
        }

        var record = new EmbeddingUsageRecord
        {
            OrganizationId = document.OrganizationId,
            UserId = _currentUserId,
            DocumentId = document.Id,
            WorkflowTypeId = _currentWorkflowTypeId,
            WorkflowExecutionId = _currentWorkflowExecutionId,
            UsageType = UsageType.EmbeddingPage,
            ExecutionContext = _currentWorkflowExecutionId is not null
                ? TokenExecutionContext.WorkflowExecution
                : TokenExecutionContext.DocumentUpload,
            Provider = provider,
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = modelId,
            Components = UsageComponentHelper.ForEmbedding(inputTokens),
            Description = $"Page embeddings for document '{document.Name}'"
        };

        // Fire-and-forget - don't await to avoid blocking document processing
        _ = _tokenUsageService.RecordEmbeddingUsageAsync(record, CancellationToken.None);
    }

    /// <summary>
    /// Records Excel sheet analysis LLM token usage using fire-and-forget pattern.
    /// </summary>
    private void RecordExcelAnalysisLlmUsage(Document document)
    {
        if (string.IsNullOrEmpty(document.OrganizationId))
        {
            _logger.LogWarning(
                "Cannot record Excel analysis LLM token usage for document {DocumentId} - OrganizationId is null",
                document.Id);
            return;
        }

        int inputTokens = _excelSheetAnalysisService.AccumulatedInputTokens;
        int outputTokens = _excelSheetAnalysisService.AccumulatedOutputTokens;

        if (inputTokens == 0 && outputTokens == 0)
        {
            return;
        }

        string modelId = _excelSheetAnalysisService.LastUsedModelId ?? "spreadsheet";
        var record = new LlmUsageRecord
        {
            OrganizationId = document.OrganizationId,
            UserId = _currentUserId,
            DocumentId = document.Id,
            WorkflowTypeId = _currentWorkflowTypeId,
            WorkflowExecutionId = _currentWorkflowExecutionId,
            UsageType = UsageType.LlmSpreadsheetAnalysis,
            ExecutionContext = _currentWorkflowExecutionId is not null
                ? TokenExecutionContext.WorkflowExecution
                : TokenExecutionContext.DocumentUpload,
            Provider = ModelIdNormalizer.GetProvider(modelId),
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = ModelIdNormalizer.Normalize(modelId),
            Components = UsageComponentHelper.FromTokens(inputTokens, outputTokens),
            Description = $"Excel sheet analysis for document '{document.Name}'"
        };

        // Fire-and-forget - don't await to avoid blocking document processing
        _ = _tokenUsageService.RecordLlmUsageAsync(record, CancellationToken.None);
    }

    /// <summary>
    /// Records overview generation LLM token usage using fire-and-forget pattern.
    /// </summary>
    private void RecordOverviewGenerationLlmUsage(Document document)
    {
        if (string.IsNullOrEmpty(document.OrganizationId))
        {
            _logger.LogWarning(
                "Cannot record overview generation LLM token usage for document {DocumentId} - OrganizationId is null",
                document.Id);
            return;
        }

        int inputTokens = _documentAnalyzer.AccumulatedInputTokens;
        int outputTokens = _documentAnalyzer.AccumulatedOutputTokens;

        if (inputTokens == 0 && outputTokens == 0)
        {
            return;
        }

        string modelId = _documentAnalyzer.LastUsedModelId ?? "overview";
        var record = new LlmUsageRecord
        {
            OrganizationId = document.OrganizationId,
            UserId = _currentUserId,
            DocumentId = document.Id,
            WorkflowTypeId = _currentWorkflowTypeId,
            WorkflowExecutionId = _currentWorkflowExecutionId,
            UsageType = UsageType.LlmOverviewGeneration,
            ExecutionContext = _currentWorkflowExecutionId is not null
                ? TokenExecutionContext.WorkflowExecution
                : TokenExecutionContext.DocumentUpload,
            Provider = ModelIdNormalizer.GetProvider(modelId),
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = ModelIdNormalizer.Normalize(modelId),
            Components = UsageComponentHelper.FromTokens(inputTokens, outputTokens),
            Description = $"Overview generation for document '{document.Name}'"
        };

        // Fire-and-forget - don't await to avoid blocking document processing
        _ = _tokenUsageService.RecordLlmUsageAsync(record, CancellationToken.None);
    }

    #endregion
}
