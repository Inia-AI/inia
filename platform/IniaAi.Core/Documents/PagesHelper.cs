using System.Runtime.CompilerServices;
using IniaAi.Common;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.FileFormats;
using IniaAi.FileFormats.Images;
using IniaAi.FileFormats.Pdf;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Documents;

public class PagesHelper
{
    private const int DEFAULT_PDF_RENDER_DPI = 100;

    // Blocks that run in parallel, such as one per fund in a parameterized section, ask for the
    // same document's pages at the same moment. The first one renders them under the document's
    // lock, and the rest find the images set when they get it.
    private static readonly ConditionalWeakTable<Document, Lock> _renderLocks = new();

    public static void GenerateImagesFromPdfPages(
        IPdfFormat pdfFormat,
        ILogger logger,
        List<Page> pages)
    {
        logger.LogDebug("Generating images for PDF pages");

        foreach (IGrouping<Document, Page> documentPages in pages
            .Where(page => page.Image is null && page.Document is not null && IsRenderable(page.Document.MediaType))
            .GroupBy(page => page.Document!))
        {
            lock (_renderLocks.GetOrCreateValue(documentPages.Key))
            {
                GenerateMissingImages(pdfFormat, logger, documentPages.Key, [.. documentPages.Where(page => page.Image is null)]);
            }
        }

        logger.LogDebug("Generated images from PDF pages");
    }

    /// <summary>
    /// Rehydrates <see cref="Page.Image"/> from the document's retained source in storage,
    /// rendering the page when the source is a PDF or using the stored bytes when it is an image.
    /// Idempotent: returns immediately when the image is already present. Used to route a page by
    /// image when its OCR text was judged unreliable and left null.
    /// </summary>
    public static async Task EnsurePageImageAsync(
        Page page,
        IPdfFormat pdfFormat,
        IFileStorageManager fileStorageManager,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (page.Image is not null)
        {
            return;
        }

        Document? document = page.Document;
        FileMetadata? fileMetadata = document?.FileMetadata;

        if (document is null || fileMetadata is null)
        {
            logger.LogWarning(
                "Cannot rehydrate image for page {pageNumber}: missing document or file metadata.",
                page.PageNumber);

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        byte[]? fileBytes = fileMetadata.AreBytesLoaded ? fileMetadata.Bytes : null;

        if (fileBytes is null)
        {
            if (string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
            {
                logger.LogWarning(
                    "Cannot rehydrate image for page {pageNumber}: no loaded bytes and no storage path.",
                    page.PageNumber);

                return;
            }

            fileBytes = await fileStorageManager.RetrieveFileBytesAsync(
                fileMetadata.StorageProvider.ToString(),
                fileMetadata.OrganizationId,
                StorageCategories.Documents,
                fileMetadata.ExternalStoragePath);
        }

        if (IsImageMediaType(document.MediaType))
        {
            BinaryContent? normalized = NormalizeImageForModel(fileBytes, logger);

            if (normalized is not null)
            {
                page.Image = normalized;
                page.ImageMediaType = MediaType.ImagePng;
            }

            return;
        }

        if (document.MediaType is MediaType.ApplicationPdf)
        {
            using IPdfDocument pdf = OpenPdfForRendering(pdfFormat, document, fileBytes);

            page.Image = RenderPdfPageToImage(pdf, page);
            page.ImageMediaType = MediaType.ImagePng;

            return;
        }

        logger.LogWarning(
            "Cannot rehydrate image for page {pageNumber}: unsupported media type {mediaType}.",
            page.PageNumber,
            document.MediaType);
    }

    /// <summary>
    /// Rehydrates images for a batch of image-only pages, loading each source document's bytes at
    /// most once (via storage when not already in memory) before rendering, instead of re-fetching
    /// and re-parsing the whole document per page. Best-effort: a document whose bytes cannot be
    /// loaded is skipped so its pages fall back to per-page rehydration rather than aborting the batch.
    /// </summary>
    public static async Task EnsureImageOnlyPageImagesAsync(
        List<Page> imageOnlyPages,
        IPdfFormat pdfFormat,
        IFileStorageManager fileStorageManager,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        List<Document> documents = [.. imageOnlyPages
            .Select(page => page.Document)
            .OfType<Document>()
            .Distinct()];

        foreach (Document document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await EnsureDocumentBytesLoadedAsync(document, fileStorageManager, logger);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Failed to load bytes for document {documentId}; its pages fall back to per-page rehydration.",
                    document.Id);
            }
        }

        List<Page> pagesWithLoadedBytes = [.. imageOnlyPages
            .Where(page => page.Document?.FileMetadata?.AreBytesLoaded == true)];

        if (pagesWithLoadedBytes.Count == 0)
        {
            return;
        }

        try
        {
            GenerateImagesFromPdfPages(pdfFormat, logger, pagesWithLoadedBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Batch image generation failed; pages fall back to per-page rehydration.");
        }
    }

    private static async Task EnsureDocumentBytesLoadedAsync(
        Document document,
        IFileStorageManager fileStorageManager,
        ILogger logger)
    {
        FileMetadata? fileMetadata = document.FileMetadata;

        if (fileMetadata is null || fileMetadata.AreBytesLoaded)
        {
            return;
        }

        if (string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
        {
            logger.LogWarning(
                "Cannot load bytes for document {documentId}: no storage path.",
                document.Id);

            return;
        }

        byte[] fileBytes = await fileStorageManager.RetrieveFileBytesAsync(
            fileMetadata.StorageProvider.ToString(),
            fileMetadata.OrganizationId,
            StorageCategories.Documents,
            fileMetadata.ExternalStoragePath);

        fileMetadata.LoadBytes(fileBytes);
    }

    private static bool IsImageMediaType(MediaType mediaType)
    {
        return mediaType is MediaType.ImagePng
            or MediaType.ImageJpeg
            or MediaType.ImageGif
            or MediaType.ImageWebp
            or MediaType.ImageBmp;
    }

    private static BinaryContent? RenderPdfPageToImage(IPdfDocument pdf, Page page)
    {
        bool? isLeftSide = page.Location switch
        {
            PageLocation.LeftSide => true,
            PageLocation.RightSide => false,
            _ => null
        };

        PdfBox pageBox = pdf.DisplayedBox(page.FilePageNumber);
        double renderedWidthPt = isLeftSide is null ? pageBox.Width : pageBox.Width / 2;
        PdfBox? area = isLeftSide is bool left ? pdf.Box(page.FilePageNumber).Half(left) : null;

        return pdf.RenderPng(page.FilePageNumber, ComputeModelBudgetDpi(renderedWidthPt, pageBox.Height), area);
    }

    private static int ComputeModelBudgetDpi(double widthPt, double heightPt)
    {
        return ModelImageLimits.ResolveRenderDpi(widthPt, heightPt, VisionTier.Standard, DEFAULT_PDF_RENDER_DPI);
    }

    private static void PopulateImageDocumentImage(Page page, ILogger logger)
    {
        FileMetadata? fileMetadata = page.Document?.FileMetadata;

        if (fileMetadata is null || !fileMetadata.AreBytesLoaded)
        {
            logger.LogWarning(
                "Cannot generate image for page {pageNumber}: source image bytes are not loaded.",
                page.PageNumber);

            return;
        }

        BinaryContent? normalized = NormalizeImageForModel(fileMetadata.Bytes, logger);

        if (normalized is null)
        {
            return;
        }

        page.Image = normalized;
        page.ImageMediaType = MediaType.ImagePng;
    }

    private static BinaryContent? NormalizeImageForModel(byte[] imageBytes, ILogger logger)
    {
        try
        {
            return ToModelImage(imageBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to normalize stored image for model input.");

            return null;
        }
    }

    /// <summary>
    /// A picture as a model reads it: a PNG whose longest edge is at most
    /// <see cref="VisionTier.STANDARD_LONG_EDGE_PX"/>. Throws when the bytes are not a picture.
    /// </summary>
    public static BinaryContent ToModelImage(byte[] imageBytes)
    {
        return new BinaryContent(Pictures.ToPngWithin(imageBytes, VisionTier.STANDARD_LONG_EDGE_PX), MediaType.ImagePng);
    }

    /// <summary>
    /// Returns the text of the pages in the indexed dictionary,
    /// each page within its own tag: page{Index+1}({DocumentName})
    /// </summary>
    /// <param name="pages">Indexed dictionary of pages.</param>
    /// <returns>Concatenated page text wrapped in tags.</returns>
    public static string GetTextsInputContext(
        Dictionary<int, Page> pages,
        bool useReadableText = false)
    {
        string result = string.Empty;

        foreach ((int index, Page page) in pages.OrderBy(p => p.Key))
        {
            int pageNumber = index + 1;
            string documentName = GetDocumentName(page);
            string? pageText = useReadableText
                ? page.ReadableText
                : page.Text;

            if (!string.IsNullOrEmpty(result))
            {
                result += "\n\n" + PageFormatting.FormatPageTag(
                    pageNumber,
                    documentName,
                    pageText);
            }
            else
            {
                result = PageFormatting.FormatPageTag(
                    pageNumber,
                    documentName,
                    pageText);
            }
        }

        return result;
    }

    /// <summary>
    /// The readable text of one document's pages, each within its own page tag under the
    /// given document name — the same assembly a model reads for a document, for a caller
    /// that holds the pages of exactly one document in order.
    /// </summary>
    public static string GetTextsInputContext(IEnumerable<Page> orderedPages, string documentName)
    {
        return string.Join(
            "\n\n",
            orderedPages.Select(page => PageFormatting.FormatPageTag(page.PageNumber, documentName, page.ReadableText)));
    }

    public static string GetOverviewsInputContext(
        Dictionary<int, Page> pages)
    {
        string result = string.Empty;

        foreach ((int index, Page page) in pages.OrderBy(p => p.Key))
        {
            int pageNumber = index + 1;
            string documentName = GetDocumentName(page);

            if (!string.IsNullOrEmpty(result))
            {
                result += "\n\n" + PageFormatting.FormatPageTag(
                    pageNumber,
                    documentName,
                    page.Overview);
            }
            else
            {
                result = PageFormatting.FormatPageTag(
                    pageNumber,
                    documentName,
                    page.Overview);
            }
        }

        return result;
    }

    public static string GetOverviewsFromPages(Dictionary<int, Page> pages)
    {
        string result = string.Empty;

        foreach ((int index, Page page) in pages.OrderBy(p => p.Key))
        {
            int pageNumber = index + 1;

            if (!string.IsNullOrEmpty(result))
            {
                result +=
                $$"""

                Page {{pageNumber}}: {{page.Overview}}
                """;
            }
            else
            {
                result =
                $$"""
                Page {{pageNumber}}: {{page.Overview}}
                """;
            }
        }

        return result;
    }

    public static List<Page> GetOrderedPages(IEnumerable<Document> documents)
    {
        return [.. documents
            .OrderBy(document => document.Name)
            .SelectMany(document => document.Pages.OrderBy(page => page.PageNumber))];
    }

    public static string GetPageName(int index, Page page)
    {
        int pageNumber = index + 1;
        string documentName = GetDocumentName(page);

        return PageFormatting.FormatPageName(
            pageNumber,
            documentName);
    }

    private static string GetDocumentName(Page page)
    {
        string extensionPart = page.Document?.FileMetadata?.Extension is null
            ? ""
            : page.Document.FileMetadata.Extension; // includes the dot already

        return page.Document is null
            ? ""
            : PageFormatting.SanitizeDocumentName($"{page.Document.Name}{extensionPart}");
    }

    private static void GenerateMissingImages(IPdfFormat pdfFormat, ILogger logger, Document document, List<Page> pages)
    {
        if (pages.Count == 0)
        {
            return;
        }

        if (IsImageMediaType(document.MediaType))
        {
            foreach (Page page in pages)
            {
                PopulateImageDocumentImage(page, logger);
            }

            return;
        }

        using IPdfDocument pdf = OpenPdfForRendering(pdfFormat, document, LoadedBytesOf(document));

        foreach (Page page in pages)
        {
            page.Image = RenderPdfPageToImage(pdf, page);

            if (page.Image is null)
            {
                logger.LogError("Failed to convert page {pageNumber} of document {documentId} to PNG",
                    page.PageNumber,
                    document.Id);

                continue;
            }

            logger.LogDebug("Converted page {pageNumber} of document {documentId} to PNG",
                page.PageNumber,
                document.Id);
        }
    }

    private static bool IsRenderable(MediaType mediaType)
    {
        return IsImageMediaType(mediaType) || mediaType is MediaType.ApplicationPdf;
    }

    private static byte[] LoadedBytesOf(Document document)
    {
        if (document.FileMetadata is null)
        {
            throw new InvalidOperationException(
                $"Document {document.Id} has no file bytes available for conversion to images.");
        }

        if (!document.FileMetadata.AreBytesLoaded)
        {
            throw new InvalidOperationException(
                $"Document {document.Id} file bytes are not loaded. For external storage providers, " +
                $"bytes must be loaded using IFileStorageManager before calling PagesHelper methods.");
        }

        return document.FileMetadata.Bytes;
    }

    private static IPdfDocument OpenPdfForRendering(IPdfFormat pdfFormat, Document document, byte[] pdfBytes)
    {
        return pdfFormat.Open(new MemoryStream(pdfBytes), document.Name, rewriteBeforeRendering: true);
    }
}
