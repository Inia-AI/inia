using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Text;
using IniaAi.Core.Documents;
using IniaAi.Domain.Entities.DocumentAnalysis;
using IniaAi.Domain.Entities.Documents;
using IniaAi.FileFormats.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Services.Documents;

public class ExcelSheetAnalysisService : ITokenAccumulator
{
    private readonly IChatConnector _chatConnector;
    private readonly ILogger<ExcelSheetAnalysisService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TenantModelFamilyResolver _tenantModelFamilyResolver;
    private const int DEFAULT_CHUNK_SIZE = ExcelUtils.ROWS_PER_PAGE;

    // Token accumulation for tracking (thread-safe for parallel calls)
    private int _accumulatedInputTokens;
    private int _accumulatedOutputTokens;

    #region ITokenAccumulator Implementation

    public int AccumulatedInputTokens => _accumulatedInputTokens;
    public int AccumulatedOutputTokens => _accumulatedOutputTokens;
    public int AccumulatedCacheCreationTokens => 0;
    public int AccumulatedCacheReadTokens => 0;
    public string? LastUsedModelId { get; private set; }

    public void ResetTokenAccumulator()
    {
        _ = Interlocked.Exchange(ref _accumulatedInputTokens, 0);
        _ = Interlocked.Exchange(ref _accumulatedOutputTokens, 0);
        LastUsedModelId = null;
    }

    private void AccumulateTokens(ResponseContentSequence response)
    {
        _ = Interlocked.Add(ref _accumulatedInputTokens, response.InputTokens);
        _ = Interlocked.Add(ref _accumulatedOutputTokens, response.OutputTokens);

        // Capture model ID from response metadata
        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? modelValue)
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString))
        {
            LastUsedModelId = modelString;
        }
    }

    #endregion

    public ExcelSheetAnalysisService(
        IChatConnector chatConnector,
        ILogger<ExcelSheetAnalysisService> logger,
        IServiceScopeFactory serviceScopeFactory,
        TenantModelFamilyResolver tenantModelFamilyResolver)
    {
        _chatConnector = chatConnector
            ?? throw new ArgumentNullException(nameof(chatConnector));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
        _serviceScopeFactory = serviceScopeFactory
            ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _tenantModelFamilyResolver = tenantModelFamilyResolver
            ?? throw new ArgumentNullException(nameof(tenantModelFamilyResolver));
    }

    private string RequireUserPrompt(string? userPrompt, string promptName)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            _logger.LogError(
                "The Excel analysis prompt {PromptName} has no user prompt. Check the Excel analysis tab in Tools Settings.",
                promptName);

            throw new InvalidOperationException(
                $"The Excel analysis prompt '{promptName}' has no user prompt.");
        }

        return userPrompt;
    }

    public async Task ChunkSheetsAsync(
        Document document,
        IWorkbookDocument workbook,
        int chunkSize = -1,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        chunkSize = chunkSize <= 0 ? DEFAULT_CHUNK_SIZE : chunkSize;

        // Block-generated workbooks carry a very-hidden config sheet stamped by
        // Json2XlsxBlockStrategy (when its StampAnalysisConfig flag is set). When
        // present, bypass the LLM layout analysis entirely and chunk by rules.
        // Stage 1 default: one page per visible sheet, full markdown of used area.
        ExcelAnalysisConfig? analysisConfig = ExcelAnalysisConfigSheet.TryRead(workbook);
        if (analysisConfig is not null)
        {
            _logger.LogInformation(
                "Document {DocumentId} carries analysis config (source={Source}); " +
                "applying rule-based chunking instead of LLM analysis.",
                document.Id,
                analysisConfig.Source);
            ChunkByDefaultRule(document, workbook, cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Starting sheet chunking process for document {DocumentId} with chunk size {ChunkSize}",
            document.Id,
            chunkSize);

        ExcelAnalysisPrompts prompts = await new ExcelAnalysisPromptsLoader(_logger, _serviceScopeFactory).LoadAsync().ConfigureAwait(false);

        int totalSheets = workbook.Sheets.Count;
        int processedSheets = 0;

        for (int i = 0; i < totalSheets; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IWorkbookSheet sheet = workbook.Sheets[i];

            if (!sheet.IsVisible)
            {
                _logger.LogInformation(
                    "Skipping hidden sheet {SheetName} ({Current}/{Total})",
                    sheet.Name,
                    ++processedSheets,
                    totalSheets);

                continue;
            }

            string operationId = Guid.NewGuid().ToString("N");

            _logger.LogInformation(
                "Processing sheet {SheetName} ({Current}/{Total}) [OperationId: {OperationId}]",
                sheet.Name,
                ++processedSheets,
                totalSheets,
                operationId);

            try
            {
                SheetAnalysisResult analysisResult = await AnalyzeSheetAsync(
                    sheet,
                    prompts,
                    operationId,
                    organizationId,
                    cancellationToken).ConfigureAwait(false);

                _logger.LogDebug(
                    "Sheet analysis completed for {SheetName}. Layout: {LayoutType} [OperationId: {OperationId}]",
                    sheet.Name,
                    analysisResult.WorksheetLayoutType,
                    operationId);

                PaginateWorksheet(
                    document,
                    sheet,
                    analysisResult,
                    chunkSize,
                    operationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process sheet {SheetName} [OperationId: {OperationId}]",
                    sheet.Name,
                    operationId);
            }
        }

        _logger.LogInformation(
            "Completed sheet chunking process. Processed {ProcessedSheets} sheets for document {DocumentId}",
            processedSheets,
            document.Id);
    }

    // Rule-based chunker for block-generated workbooks. Stage 1 rule: one page per
    // visible sheet, full markdown of the sheet's used area. Stage 2 will honour
    // per-file rules carried in ExcelAnalysisConfig (not yet populated).
    private void ChunkByDefaultRule(
        Document document,
        IWorkbookDocument workbook,
        CancellationToken cancellationToken)
    {
        int totalSheets = workbook.Sheets.Count;
        int pageCounter = document.Pages.Count + 1;
        int processedSheets = 0;

        for (int i = 0; i < totalSheets; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IWorkbookSheet sheet = workbook.Sheets[i];
            if (!sheet.IsVisible)
            {
                continue;
            }

            string markdown = ExcelUtils.GetSheetMarkdown(sheet);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                _logger.LogInformation(
                    "Rule-based chunking: skipping empty sheet {SheetName}",
                    sheet.Name);
                continue;
            }

            _ = document[pageCounter] ?? new Page(document, pageCounter, i + 1);
            Page page = document[pageCounter]!;
            page.RawText =
                $"""
                {sheet.Name}
                {markdown}
                """;

            pageCounter++;
            processedSheets++;
        }

        _logger.LogInformation(
            "Completed rule-based chunking for document {DocumentId}: {Processed} pages from visible sheets.",
            document.Id,
            processedSheets);
    }

    public void ProcessDatabaseLayout(
        Document document,
        IWorkbookSheet sheet,
        SheetAnalysisResult analysis,
        int chunkSize,
        string operationId)
    {
        _logger.LogInformation(
            "Processing database layout for sheet '{SheetName}' [OperationId: {OperationId}]",
            sheet.Name,
            operationId);

        if (!TryGetHeaderRange(analysis, out int firstHeaderRow, out int headerRowCount))
        {
            _logger.LogInformation(
                "No valid header rows for database layout on sheet '{SheetName}', treating as unidentified [OperationId: {OperationId}]",
                sheet.Name,
                operationId);

            ProcessUnidentifiedLayout(document, sheet, chunkSize, operationId);
            return;
        }

        (int firstRow, int firstCol, int totalRows, int totalCols)? dataRange = ExcelUtils.GetActualDataRange(sheet);
        int firstColumn = dataRange?.firstCol ?? 0;
        int columnCount = dataRange?.totalCols ?? ExcelUtils.GetMaxColumn(sheet);
        int maxRow = dataRange is not null
            ? dataRange.Value.firstRow + dataRange.Value.totalRows - 1
            : sheet.MaxDataRow;

        CreatePages(
            document,
            sheet,
            chunkSize,
            firstHeaderRow,
            headerRowCount,
            firstColumn,
            columnCount,
            maxRow,
            operationId);
    }

    public void ProcessMultiNoSharedColumnsLayout(
        Document document,
        IWorkbookSheet sheet,
        SheetAnalysisResult analysis,
        int chunkSize,
        string operationId)
    {
        _logger.LogInformation(
            "Processing multiple tables layout for sheet '{SheetName}' [OperationId: {OperationId}]",
            sheet.Name,
            operationId);

        if (analysis.TableColumns is null
            || analysis.TableColumns.Count == 0)
        {
            ProcessDatabaseLayout(
                document,
                sheet,
                analysis,
                chunkSize,
                operationId);

            return;
        }

        if (!TryGetHeaderRange(analysis, out int firstHeaderRow, out int headerRowCount))
        {
            _logger.LogInformation(
                "No valid header rows for multi-table layout on sheet '{SheetName}', treating as unidentified [OperationId: {OperationId}]",
                sheet.Name,
                operationId);

            ProcessUnidentifiedLayout(document, sheet, chunkSize, operationId);
            return;
        }

        (int firstRow, int firstCol, int totalRows, int totalCols)? actualDataRange = ExcelUtils.GetActualDataRange(sheet);
        int maxRow = actualDataRange is not null
            ? actualDataRange.Value.firstRow + actualDataRange.Value.totalRows - 1
            : sheet.MaxDataRow;

        foreach (string tableColumns in analysis.TableColumns)
        {
            if (!TryParseExcelColumnRange(
                sheet,
                operationId,
                tableColumns,
                out int firstCol,
                out int colCount))
            {
                continue;
            }

            CreatePages(
                document,
                sheet,
                chunkSize,
                firstHeaderRow,
                headerRowCount,
                firstCol,
                colCount,
                maxRow,
                operationId);
        }
    }

    public void ProcessMultiSharedColumnsLayout(
        Document document,
        IWorkbookSheet sheet,
        SheetAnalysisResult analysis,
        int chunkSize,
        string operationId)
    {
        _logger.LogInformation(
            "Processing multiple tables layout with shared columns for sheet '{SheetName}' [OperationId: {OperationId}]",
            sheet.Name,
            operationId);

        if (analysis.TableColumns is null || analysis.TableColumns.Count == 0)
        {
            ProcessDatabaseLayout(
                document,
                sheet,
                analysis,
                chunkSize,
                operationId);

            return;
        }

        if (!TryGetHeaderRange(analysis, out int firstHeaderRow, out int headerRowCount))
        {
            _logger.LogInformation(
                "No valid header rows for multi-shared columns layout on sheet '{SheetName}', treating as unidentified [OperationId: {OperationId}]",
                sheet.Name,
                operationId);

            ProcessUnidentifiedLayout(document, sheet, chunkSize, operationId);
            return;
        }

        List<ExcelRange> sharedHeaderBlocks = [.. analysis.SharedColumns
            .Select(sc =>
            {
                int colIndex = Utils.ConvertExcelColumnLettersToInt(sc) - 1;
                return new ExcelRange(sheet, firstHeaderRow, colIndex, headerRowCount, 1);
            })];

        int pageCounter = document.Pages.Count + 1;

        (int firstRow, int firstCol, int totalRows, int totalCols)? actualDataRange = ExcelUtils.GetActualDataRange(sheet);
        int maxRow = actualDataRange is not null
            ? actualDataRange.Value.firstRow + actualDataRange.Value.totalRows - 1
            : sheet.MaxDataRow;

        foreach (string tableColumns in analysis.TableColumns)
        {
            if (!TryParseExcelColumnRange(
                sheet,
                operationId,
                tableColumns,
                out int firstCol,
                out int colCount))
            {
                continue;
            }

            var headerRange = new ExcelRange(
                sheet,
                firstHeaderRow,
                firstCol,
                headerRowCount,
                colCount);

            string headerMarkdown = ExcelRange.CombineBlocksMarkdown(
                [.. sharedHeaderBlocks, .. new List<ExcelRange> { headerRange }],
                headerRowCount,
                isHeader: true);

            int firstDataRow = headerRange.FirstRow + headerRange.RowCount;
            int tablePageCountBefore = document.Pages.Count;

            for (int row = firstDataRow; row <= maxRow; row += chunkSize)
            {
                int endRow = Math.Min(row + chunkSize - 1, maxRow);

                List<ExcelRange> sharedDataBlocks = [.. analysis.SharedColumns
                    .Select(sc =>
                    {
                        int colIndex = Utils.ConvertExcelColumnLettersToInt(sc) - 1;
                        return new ExcelRange(sheet, row, colIndex, endRow - row + 1, 1);
                    })];

                var dataRange = new ExcelRange(
                    sheet,
                    row,
                    headerRange.FirstColumn,
                    endRow - row + 1,
                    headerRange.ColumnCount);

                if (dataRange.IsEmpty(dataRange.FirstRow, dataRange.FirstColumn))
                {
                    _logger.LogInformation(
                        "Skipping empty data chunk on sheet '{SheetName}' [OperationId: {OperationId}]",
                        sheet.Name,
                        operationId);
                    continue;
                }

                string dataMarkdown = ExcelRange.CombineBlocksMarkdown(
                    [.. sharedDataBlocks, .. new List<ExcelRange> { dataRange }],
                    endRow - row + 1,
                    isHeader: false);

                string pageMarkdown = $"{headerMarkdown}\n{dataMarkdown}";

                Page page = document[pageCounter]
                    ?? new(document, pageCounter, sheet.Index + 1);
                page.RawText =
                $"""
                {sheet.Name}
                {MarkdownFormatter.FormatMarkdownTable(pageMarkdown, alignColumns: false)}
                """;

                pageCounter++;
            }

            if (document.Pages.Count == tablePageCountBefore
                && HasAnyHeaderContent(headerRange, sharedHeaderBlocks))
            {
                EmitHeaderOnlyPage(document, sheet, headerMarkdown, pageCounter, operationId);
                pageCounter = document.Pages.Count + 1;
            }
        }
    }

    private static bool HasAnyHeaderContent(ExcelRange headerRange, List<ExcelRange> sharedHeaderBlocks)
    {
        if (!headerRange.IsEmpty(headerRange.FirstRow, headerRange.FirstColumn))
        {
            return true;
        }

        foreach (ExcelRange sharedBlock in sharedHeaderBlocks)
        {
            if (!sharedBlock.IsEmpty(sharedBlock.FirstRow, sharedBlock.FirstColumn))
            {
                return true;
            }
        }

        return false;
    }

    public void ProcessUnidentifiedLayout(
        Document document,
        IWorkbookSheet sheet,
        int chunkSize,
        string operationId)
    {
        _logger.LogInformation(
            "Processing unidentified layout for sheet '{SheetName}' [OperationId: {OperationId}]",
            sheet.Name,
            operationId);

        (int firstRow, int firstCol, int totalRows, int totalCols)? dataRegion = ExcelUtils.GetActualDataRange(sheet);

        if (dataRegion is null)
        {
            _logger.LogInformation(
                "No non-empty data found on sheet '{SheetName}' [OperationId: {OperationId}]",
                sheet.Name,
                operationId);

            return;
        }

        int totalRows = dataRegion.Value.totalRows;

        if (totalRows < 1)
        {
            _logger.LogInformation(
                "Data region is empty on sheet '{SheetName}' [OperationId: {OperationId}]",
                sheet.Name,
                operationId);

            return;
        }

        int pageCounter = document.Pages.Count + 1;

        for (int start = 0; start < totalRows; start += chunkSize)
        {
            int currentChunkSize = Math.Min(chunkSize, totalRows - start);

            var dataChunk = new ExcelRange(
                sheet,
                dataRegion.Value.firstRow + start,
                dataRegion.Value.firstCol,
                currentChunkSize,
                dataRegion.Value.totalCols);

            if (dataChunk.IsEmpty(dataChunk.FirstRow, dataChunk.FirstColumn))
            {
                _logger.LogInformation(
                    "Skipping empty data chunk on sheet '{SheetName}' [OperationId: {OperationId}]",
                    sheet.Name,
                    operationId);
                continue;
            }

            string pageMarkdown = ExcelRange.GetMarkdownFromRange(dataChunk, isHeader: false);

            Page page = document[pageCounter]
                ?? new(document, pageCounter, sheet.Index + 1);
            page.RawText =
            $"""
            {sheet.Name}
            {MarkdownFormatter.FormatMarkdownTable(pageMarkdown, alignColumns: false)}
            """;

            pageCounter++;
        }
    }

    private bool TryParseExcelColumnRange(
        IWorkbookSheet sheet,
        string operationId,
        string tableColumns,
        out int firstCol,
        out int colCount)
    {
        if (string.IsNullOrWhiteSpace(tableColumns))
        {
            _logger.LogError(
                "Invalid table columns format '{TableColumns}' for sheet '{SheetName}' [OperationId: {OperationId}]",
                tableColumns,
                sheet.Name,
                operationId);

            firstCol = -1;
            colCount = -1;

            return false;
        }

        string[] columns = tableColumns.Split('-');

        if (columns.Length is < 1 or > 2 || columns.Any(string.IsNullOrWhiteSpace))
        {
            _logger.LogError(
                "Invalid table columns format '{TableColumns}' for sheet '{SheetName}' [OperationId: {OperationId}]",
                tableColumns,
                sheet.Name,
                operationId);

            firstCol = -1;
            colCount = -1;

            return false;
        }

        firstCol = Utils.ConvertExcelColumnLettersToInt(columns[0]) - 1;
        int lastCol = columns.Length == 2
            ? Utils.ConvertExcelColumnLettersToInt(columns[1]) - 1
            : firstCol;
        colCount = lastCol - firstCol + 1;

        _logger.LogDebug(
            "Parsed table columns '{TableColumns}' to firstCol={FirstCol}, colCount={ColCount} for sheet '{SheetName}' [OperationId: {OperationId}]",
            tableColumns,
            firstCol,
            colCount,
            sheet.Name,
            operationId);

        return true;
    }

    private async Task<SheetAnalysisResult> AnalyzeSheetAsync(
        IWorkbookSheet sheet,
        ExcelAnalysisPrompts prompts,
        string operationId,
        string? organizationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug(
            "Starting sheet analysis for {SheetName} [OperationId: {OperationId}]",
            sheet.Name,
            operationId);

        string sample = ExcelUtils.GetSheetSampleMarkdown(sheet);

        SheetAnalysisResult analysisResult = await GetLayoutAnalysisResultAsync(
            sheet,
            sample,
            prompts,
            operationId,
            organizationId).ConfigureAwait(false);

        if (analysisResult.WorksheetLayoutType is WorksheetLayoutTypes.Unidentified)
        {
            _logger.LogWarning(
                "Unable to identify layout for sheet {SheetName} [OperationId: {OperationId}]",
                sheet.Name,
                operationId);

            return analysisResult;
        }

        return await IdentifyHeadersBasedOnLayoutAsync(
            sheet,
            sample,
            prompts,
            analysisResult,
            operationId,
            organizationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SheetAnalysisResult> IdentifyHeadersBasedOnLayoutAsync(
        IWorkbookSheet sheet,
        string sample,
        ExcelAnalysisPrompts prompts,
        SheetAnalysisResult analysisResult,
        string operationId,
        string? organizationId,
        CancellationToken cancellationToken)
    {
        (string? SystemPrompt, string? UserPrompt)? headerPrompts = analysisResult.WorksheetLayoutType switch
        {
            WorksheetLayoutTypes.Database => (prompts.HeaderDatabaseSystemPrompt, prompts.HeaderDatabaseUserPrompt),
            WorksheetLayoutTypes.MultiSharedColumns => (prompts.HeaderMultiSharedSystemPrompt, prompts.HeaderMultiSharedUserPrompt),
            WorksheetLayoutTypes.MultiNoSharedColumns => (prompts.HeaderMultiNoSharedSystemPrompt, prompts.HeaderMultiNoSharedUserPrompt),
            _ => null,
        };

        if (headerPrompts is not { } header)
        {
            return analysisResult;
        }

        string? systemPrompt = header.SystemPrompt;
        string userPromptTemplate = RequireUserPrompt(
            header.UserPrompt,
            $"header identification ({analysisResult.WorksheetLayoutType})");

        string headerPrompt = analysisResult.WorksheetLayoutType switch
        {
            WorksheetLayoutTypes.MultiSharedColumns =>
                string.Format(
                    userPromptTemplate,
                    sample,
                    analysisResult.SharedColumns.Count != 0
                        ? string.Join(", ", analysisResult.SharedColumns)
                        : string.Empty),
            _ => string.Format(userPromptTemplate, sample),
        };

        return await IdentifyHeadersAsync(
            sheet,
            headerPrompt,
            systemPrompt,
            analysisResult,
            operationId,
            organizationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SheetAnalysisResult> IdentifyHeadersAsync(
        IWorkbookSheet sheet,
        string headerPrompt,
        string? systemPrompt,
        SheetAnalysisResult analysisResult,
        string operationId,
        string? organizationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Sending header identification prompt for sheet {SheetName}: {Prompt}",
            sheet.Name,
            headerPrompt);

        ModelFamily family = _tenantModelFamilyResolver.GetModelFamilyForFunction(organizationId, FunctionType.SpreadsheetAnalysis);
        ResponseContentSequence responseContent = await _chatConnector
            .GetResponseAsync(
                headerPrompt,
                systemPrompt,
                shouldUseAdvancedModel: true,
                model: _tenantModelFamilyResolver.GetModelOverride(organizationId, true),
                family: family,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        AccumulateTokens(responseContent);

        string response = responseContent.GetPlainText() ?? string.Empty;

        _logger.LogInformation(
            "Received header identification response for sheet {SheetName}: {Response}",
            sheet.Name,
            response);

        SheetAnalysisResult headerResult = ParseSheetAnalysisResponse(
            response,
            operationId);
        analysisResult.HeaderRows = headerResult.HeaderRows;
        analysisResult.TableColumns = headerResult.TableColumns;

        return analysisResult;
    }

    private async Task<SheetAnalysisResult> GetLayoutAnalysisResultAsync(
        IWorkbookSheet sheet,
        string sample,
        ExcelAnalysisPrompts prompts,
        string operationId,
        string? organizationId)
    {
        try
        {
            string? systemPrompt = prompts.LayoutAnalysisSystemPrompt;
            string userPromptTemplate = RequireUserPrompt(prompts.LayoutAnalysisUserPrompt, "layout analysis");

            string prompt = string.Format(userPromptTemplate, sample);
            _logger.LogDebug(
                "Sending layout analysis prompt for {SheetName} [OperationId: {OperationId}] {Prompt}",
                sheet.Name,
                operationId,
                prompt);

            ModelFamily family = _tenantModelFamilyResolver.GetModelFamilyForFunction(organizationId, FunctionType.SpreadsheetAnalysis);
            ResponseContentSequence responseContent = await _chatConnector
                .GetResponseAsync(
                    prompt,
                    systemPrompt,
                    shouldUseAdvancedModel: true,
                    model: _tenantModelFamilyResolver.GetModelOverride(organizationId, true),
                    family: family)
                .ConfigureAwait(false);

            AccumulateTokens(responseContent);

            string response = responseContent.GetPlainText() ?? string.Empty;

            _logger.LogTrace(
                "AI Response for {SheetName}: {Response} [OperationId: {OperationId}]",
                sheet.Name,
                response,
                operationId);

            return ParseSheetAnalysisResponse(response, operationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get AI analysis for sheet {SheetName} [OperationId: {OperationId}]",
                sheet.Name,
                operationId);

            return new SheetAnalysisResult();
        }
    }

    private SheetAnalysisResult ParseSheetAnalysisResponse(
        string response,
        string operationId)
    {
        try
        {
            var result = SheetAnalysisResult.FromJson(response);

            if (result is null)
            {
                _logger.LogWarning(
                    "Analysis response parsing returned null result [OperationId: {OperationId}]",
                    operationId);

                return new SheetAnalysisResult();
            }

            // Remove "NA" from shared columns if present, as it indicates
            // no shared columns rather than an actual column name
            // There is a possibility that "NA" could be a valid column name,
            // but this is a common convention used by the AI to indicate
            // absence of shared columns, so we will remove it to avoid
            // confusion in downstream processing.
            // Also, it is a very remote edge case that "NA" would be an actual
            // column name in a sheet that is identified as having shared
            // columns, so the risk of incorrectly removing a valid column is low.
            _ = result.SharedColumns.Remove("NA");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse analysis response: {Response} [OperationId: {OperationId}]",
                response,
                operationId);

            return new SheetAnalysisResult();
        }
    }

    private void PaginateWorksheet(
        Document document,
        IWorkbookSheet sheet,
        SheetAnalysisResult analysis,
        int chunkSize,
        string operationId)
    {
        _logger.LogInformation(
            "Sheet {SheetName} analysis result: Type={SheetType}, Header rows={HeaderRows}, Table columns={TableColumns}, Shared columns='{SharedColumns}' [OperationId: {OperationId}]",
            sheet.Name,
            analysis.WorksheetLayoutType,
            analysis.HeaderRows,
            analysis.TableColumns,
            string.Join(", ", analysis.SharedColumns),
            operationId);

        if (string.IsNullOrWhiteSpace(analysis.HeaderRows))
        {
            _logger.LogInformation(
                "No header rows provided, treating layout as Unidentified for sheet {SheetName} [OperationId: {OperationId}]",
                sheet.Name,
                operationId);

            ProcessUnidentifiedLayout(document, sheet, chunkSize, operationId);

            return;
        }

        switch (analysis.WorksheetLayoutType)
        {
            case WorksheetLayoutTypes.Database:
                ProcessDatabaseLayout(document, sheet, analysis, chunkSize, operationId);
                break;
            case WorksheetLayoutTypes.MultiNoSharedColumns:
                ProcessMultiNoSharedColumnsLayout(document, sheet, analysis, chunkSize, operationId);
                break;
            case WorksheetLayoutTypes.MultiSharedColumns:
                ProcessMultiSharedColumnsLayout(document, sheet, analysis, chunkSize, operationId);
                break;
            case WorksheetLayoutTypes.Unidentified:
            default:
                ProcessUnidentifiedLayout(document, sheet, chunkSize, operationId);
                break;
        }
    }

    private static bool TryGetHeaderRange(
        SheetAnalysisResult analysis,
        out int firstHeaderRow,
        out int headerRowCount)
    {
        firstHeaderRow = 0;
        headerRowCount = 0;

        if (string.IsNullOrWhiteSpace(analysis.HeaderRows))
        {
            return false;
        }

        List<int> headerRows = Utils.ParseStringForNumbersList(analysis.HeaderRows);

        if (headerRows.Count == 0 || headerRows.Contains(0))
        {
            return false;
        }

        firstHeaderRow = headerRows.Min() - 1;
        int lastHeaderRow = headerRows.Max() - 1;
        headerRowCount = lastHeaderRow - firstHeaderRow + 1;

        return true;
    }

    private void CreatePages(
        Document document,
        IWorkbookSheet sheet,
        int chunkSize,
        int firstHeaderRow,
        int headerRowCount,
        int firstColumn,
        int columnCount,
        int maxRow,
        string operationId)
    {
        int initialPageCount = document.Pages.Count;
        int pageCounter = initialPageCount + 1;

        var headerRange = new ExcelRange(
            sheet,
            firstHeaderRow,
            firstColumn,
            headerRowCount,
            columnCount);

        string headerMarkdown = ExcelRange.GetMarkdownFromRange(headerRange, isHeader: true);

        int firstDataRow = headerRange.FirstRow + headerRange.RowCount;

        for (int row = firstDataRow; row <= maxRow; row += chunkSize)
        {
            int endRow = Math.Min(row + chunkSize - 1, maxRow);

            var dataRange = new ExcelRange(
                sheet,
                row,
                headerRange.FirstColumn,
                endRow - row + 1,
                headerRange.ColumnCount);

            string dataMarkdown = ExcelRange.GetMarkdownFromRange(dataRange, isHeader: false);

            if (dataRange.IsEmpty(dataRange.FirstRow, dataRange.FirstColumn))
            {
                _logger.LogInformation(
                    "Skipping empty data chunk on sheet '{SheetName}' [OperationId: {OperationId}]",
                    sheet.Name,
                    operationId);
                continue;
            }

            string pageMarkdown = $"{headerMarkdown}\n{dataMarkdown}";

            Page page = document[pageCounter]
                ?? new(document, pageCounter, sheet.Index + 1);
            page.RawText =
            $"""
            {sheet.Name}
            {MarkdownFormatter.FormatMarkdownTable(pageMarkdown, alignColumns: false)}
            """;

            pageCounter++;
        }

        if (document.Pages.Count == initialPageCount
            && !headerRange.IsEmpty(headerRange.FirstRow, headerRange.FirstColumn))
        {
            EmitHeaderOnlyPage(document, sheet, headerMarkdown, pageCounter, operationId);
        }
    }

    private void EmitHeaderOnlyPage(
        Document document,
        IWorkbookSheet sheet,
        string headerMarkdown,
        int pageCounter,
        string operationId)
    {
        _logger.LogInformation(
            "No data rows below headers on sheet '{SheetName}'; emitting header-only page [OperationId: {OperationId}]",
            sheet.Name,
            operationId);

        Page page = document[pageCounter]
            ?? new(document, pageCounter, sheet.Index + 1);
        page.RawText =
        $"""
        {sheet.Name}
        {MarkdownFormatter.FormatMarkdownTable(headerMarkdown, alignColumns: false)}
        """;
    }
}
