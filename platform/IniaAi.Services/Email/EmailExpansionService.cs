using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Constants;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces;
using IniaAi.Configuration;
using IniaAi.FileFormats.Email;
using IniaAi.FileFormats.Images;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Unlock;
using IniaAi.Services.FileDownload;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace IniaAi.Services.Email;

public partial class EmailExpansionService : IEmailExpansionService
{
    public const string UNSUPPORTED_ATTACHMENT_REASON =
        "This type of attachment is not extracted. Save it as a PDF, Word, CSV or Excel file, then upload it on its own.";

    public const string EMPTY_ATTACHMENT_REASON = "The attachment is empty, so it was left out.";

    public const string NESTED_EMAIL_REASON = "An email attached to the email is not opened. Save it as a PDF, then upload it on its own.";

    public const string NESTED_ARCHIVE_REASON = "A ZIP file inside a ZIP attachment is not opened. Extract its files, then upload them on their own.";

    public const string ARCHIVE_UNREADABLE_REASON =
        "The ZIP attachment is damaged and cannot be opened. Extract its files with another program, then upload them on their own.";

    public const string URL_DOWNLOAD_FAILED_REASON = "The linked file could not be downloaded as a PDF. Download it yourself, then upload it.";

    private const string EMAIL_INSPECTION_NAME = "email";
    private const string EMAIL_BODY_FILE_NAME_PREFIX = "email_body_";
    private const string UNNAMED_ATTACHMENT_FILE_NAME_PREFIX = "unnamed_attachment_";
    private const string FALLBACK_SUBJECT = "email";
    private const string NESTED_EMAIL_FALLBACK_FILE_NAME = "nested_email.eml";
    private const string DOWNLOAD_FOLDER_PREFIX = "inia-email-expansion-";
    private const int MAX_FILE_NAME_BASE_LENGTH = 100;
    private const string BODY_TEXT_FILE_NAME = "body.txt";
    private const string CONTENT_ID_LINK = "cid:";

    // A picture the body gives no size to shows at its own size, a CSS pixel to 3/4 of a point.
    private const double CSS_PIXELS_PER_POINT = 96d / 72d;
    private const string EMAIL_NOT_READ =
        $"The platform does not read email files. Save the email as a PDF, then upload it with its attachments. {ProductEditions.FULL_EDITION} reads email files directly and extracts their attachments.";

    private readonly PlatformSettings _platformSettings;
    private readonly FileInspectionLimits _limits;
    private readonly IDocumentUnlocker _unlocker;
    private readonly IDocumentPageCounter _pageCounter;
    private readonly ILogger<EmailExpansionService> _logger;
    private readonly IEmailToPdfConverter? _emailToPdfConverter;

    public EmailExpansionService(
        IOptions<PlatformSettings> options,
        IDocumentUnlocker unlocker,
        IDocumentPageCounter pageCounter,
        ILogger<EmailExpansionService> logger,
        IEmailToPdfConverter? emailToPdfConverter = null)
    {
        _platformSettings = options.Value;
        _limits = FileInspectionLimits.FromSettings(_platformSettings.IniaAi);
        _unlocker = unlocker;
        _pageCounter = pageCounter;
        _logger = logger;
        _emailToPdfConverter = emailToPdfConverter;
    }

    private IEmailToPdfConverter EmailToPdf => _emailToPdfConverter
        ?? throw new EmailConversionException(EMAIL_NOT_READ);

    /// <summary>
    /// Where a part goes, decided from its bytes. The expansion skips only a type it recognises
    /// and does not expand; bytes no detector names are admitted onward to the intake gate,
    /// which refuses them with a screening record. The list is narrower than the gate's on
    /// purpose: most emails carry signature images and plain-text disclaimers, which the gate
    /// would admit as documents.
    /// </summary>
    private enum PartRoute
    {
        Admit,
        Skip,
        ExpandArchive,
    }

    public async Task<EmailExpansionResult> ExpandAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken = default)
    {
        ParsedEmailBody parsed = await ParseAndRenderBodyAsync(emailBytes, cancellationToken);

        var state = new ExpansionState();

        var bodyPdf = new ExpandedEmailPart(
            state.ClaimFileName(BuildBodyPdfFileName(parsed.Identifier)),
            parsed.BodyPdfBytes,
            MediaType.ApplicationPdf.GetEnumMember());

        var rawEmail = new ExpandedEmailPart(
            state.ClaimFileName($"{Truncate(parsed.Identifier)}{parsed.Extension}"),
            emailBytes,
            parsed.IsOutlookMessage
                ? MediaType.ApplicationVndMsOutlook.GetEnumMember()
                : MediaType.MessageRfc822.GetEnumMember());

        await CollectPartsAsync(parsed.Message, state, cancellationToken);
        await DownloadUrlPartsAsync(parsed.Message, state, cancellationToken);

        _logger.LogInformation(
            "Expanded email into {PartCount} part(s), {SkippedCount} skipped part(s)",
            state.Parts.Count,
            state.SkippedParts.Count);

        return new EmailExpansionResult(
            parsed.Identifier,
            parsed.Message.MessageId,
            bodyPdf,
            parsed.BodyShowsContentImage,
            rawEmail,
            state.Parts,
            state.SkippedParts);
    }

    public async Task<EmailBodyRenderResult> RenderBodyAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken = default)
    {
        ParsedEmailBody parsed = await ParseAndRenderBodyAsync(emailBytes, cancellationToken);

        var bodyPdf = new ExpandedEmailPart(
            BuildBodyPdfFileName(parsed.Identifier),
            parsed.BodyPdfBytes,
            MediaType.ApplicationPdf.GetEnumMember());

        return new EmailBodyRenderResult(parsed.Identifier, bodyPdf, parsed.BodyShowsContentImage);
    }

    public async Task<string> DeriveTriggerEmailIdentifierAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken = default)
    {
        ParsedEmail parsed = await ParseAsync(emailBytes, cancellationToken);

        return parsed.Identifier;
    }

    public async Task<EmailPageCount> CountPagesAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken = default)
    {
        (MimeMessage message, string identifier) = await ParseForCountAsync(emailBytes, cancellationToken);

        var state = new ExpansionState(PagesOf);
        await CollectPartsAsync(message, state, cancellationToken);

        int bodyPages = await BodyPagesAsync(message, cancellationToken);
        return new EmailPageCount(identifier, bodyPages, state.CountedPages);
    }

    private async Task<(MimeMessage Message, string Identifier)> ParseForCountAsync(byte[] emailBytes, CancellationToken cancellationToken)
    {
        ParsedEmail parsed = await ParseAsync(emailBytes, cancellationToken);
        return (parsed.Message, parsed.Identifier);
    }

    private async Task CollectPartsAsync(MimeMessage message, ExpansionState state, CancellationToken cancellationToken)
    {
        foreach (MimeEntity entity in GetCandidateEntities(message))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessEntityAsync(entity, state, cancellationToken);
        }
    }

    private int? PagesOf(ExpandedEmailPart part)
    {
        byte[] content = part.UnlockedContent;
        return Inspect(content, part.FileName).MediaType is { } mediaType
            ? _pageCounter.Count(content, mediaType, part.FileName)
            : 0;
    }

    private async Task<int> BodyPagesAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        string? html = message.HtmlBody;
        string text = html is null ? message.TextBody ?? string.Empty : TextOf(html);
        int textPages = _pageCounter.Count(Encoding.UTF8.GetBytes(text), MediaType.TextPlain, BODY_TEXT_FILE_NAME) ?? 0;

        int contentPictures = 0;
        foreach (MimePart picture in message.BodyParts.OfType<MimePart>().Where(part => IsShownBy(part, html)))
        {
            if (picture.Content is not null
                && Pictures.ReadSize(await DecodeAsync(picture.Content, cancellationToken)) is { } size
                && size.Width >= EmailPdf.MIN_CONTENT_IMAGE_WIDTH_PT * CSS_PIXELS_PER_POINT
                && size.Height >= EmailPdf.MIN_CONTENT_IMAGE_HEIGHT_PT * CSS_PIXELS_PER_POINT)
            {
                contentPictures++;
            }
        }

        return Math.Max(1, textPages) + contentPictures;
    }

    private static bool IsShownBy(MimePart part, string? html)
    {
        return html is not null
            && part.ContentType.IsMimeType("image", "*")
            && part.ContentId is { Length: > 0 } contentId
            && Regex.IsMatch(html, $@"{CONTENT_ID_LINK}{Regex.Escape(contentId)}(?![\w.@-])", RegexOptions.IgnoreCase);
    }

    private static string TextOf(string html)
    {
        var text = new StringBuilder();
        var tokenizer = new HtmlTokenizer(new StringReader(html)) { DecodeCharacterReferences = true };
        bool hidden = false;
        while (tokenizer.ReadNextToken(out HtmlToken? token))
        {
            if (token is HtmlTagToken tag)
            {
                if (tag.Id is HtmlTagId.Script or HtmlTagId.Style or HtmlTagId.Title)
                {
                    hidden = !tag.IsEndTag;
                }
                else if (tag.Id is HtmlTagId.Br)
                {
                    _ = text.Append('\n');
                }
                else if (tag.IsEndTag && tag.Id is HtmlTagId.P or HtmlTagId.Div or HtmlTagId.LI or HtmlTagId.TR or HtmlTagId.Table
                    or HtmlTagId.H1 or HtmlTagId.H2 or HtmlTagId.H3 or HtmlTagId.H4 or HtmlTagId.H5 or HtmlTagId.H6)
                {
                    _ = text.Append("\n\n");
                }
            }
            else if (token is HtmlDataToken data && !hidden)
            {
                _ = text.Append(WhitespaceRunRegex().Replace(data.Data, " "));
            }
        }

        return text.ToString();
    }

    private static string BuildBodyPdfFileName(string identifier)
    {
        return $"{Truncate(EMAIL_BODY_FILE_NAME_PREFIX + identifier)}.pdf";
    }

    private sealed record ParsedEmail(
        MimeMessage Message,
        string Identifier,
        string Extension,
        bool IsOutlookMessage,
        byte[] EmlBytes);

    private sealed record ParsedEmailBody(
        MimeMessage Message,
        string Identifier,
        string Extension,
        bool IsOutlookMessage,
        byte[] BodyPdfBytes,
        bool BodyShowsContentImage);

    private async Task<ParsedEmail> ParseAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emailBytes);

        MediaType? detected = Inspect(emailBytes, EMAIL_INSPECTION_NAME).MediaType;
        bool isOutlookMessage = detected is MediaType.ApplicationVndMsOutlook;
        if (!isOutlookMessage && detected is not MediaType.MessageRfc822)
        {
            throw new EmailConversionException(
                "The file is not an email, so it cannot be expanded. Upload it on its own as a document instead.");
        }

        string extension = isOutlookMessage ? EmailFileExtensions.MSG : EmailFileExtensions.EML;

        byte[] emlBytes = isOutlookMessage
            ? EmailToPdf.ConvertMsgToEml(emailBytes)
            : emailBytes;

        MimeMessage message = await ParseMessageAsync(emlBytes, cancellationToken);
        string identifier = DeriveTriggerEmailIdentifier(message.Subject, message.Date);

        return new ParsedEmail(message, identifier, extension, isOutlookMessage, emlBytes);
    }

    private async Task<ParsedEmailBody> ParseAndRenderBodyAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken)
    {
        ParsedEmail parsed = await ParseAsync(emailBytes, cancellationToken);

        IEmailToPdfConverter emailToPdf = EmailToPdf;
        EmailPdf conversion = await Task.Run(
            () => emailToPdf.ConvertToPdf(parsed.EmlBytes),
            cancellationToken);

        return new ParsedEmailBody(
            parsed.Message,
            parsed.Identifier,
            parsed.Extension,
            parsed.IsOutlookMessage,
            conversion.PdfBytes,
            conversion.ShowsContentImage);
    }

    public static string DeriveTriggerEmailIdentifier(string? subject, DateTimeOffset sentDate)
    {
        return $"{SanitizeSubject(subject)}_{sentDate.DateTime:yyyy-MM-dd_HH-mm-ss}";
    }

    internal HashSet<Uri> GetUrlsFromMessage(MimeMessage message)
    {
        List<Uri> urls = [];

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            IEnumerable<MimeEntity> htmlParts = message.BodyParts
                .Where(b => b.ContentType.MimeType.Equals(
                    MediaType.TextHtml.GetEnumMember(), StringComparison.OrdinalIgnoreCase));

            foreach (TextPart htmlPart in htmlParts.Cast<TextPart>())
            {
                urls.AddRange(ParseUrls(htmlPart.Text));
            }
        }
        else
        {
            urls.AddRange(ParseUrls(message.TextBody));
        }

        FilterOutNonDownloadableUrls(urls);

        return [.. urls];
    }

    public static string BuildArchiveOverEntryLimitReason(int entryCount, int maxEntries)
    {
        return $"The ZIP attachment holds {entryCount.ToString("N0", CultureInfo.InvariantCulture)} files, more than the limit of {maxEntries.ToString("N0", CultureInfo.InvariantCulture)}, so it was left out. Split it into smaller ZIP files, then upload them.";
    }

    public static string BuildEntryTooLargeReason(int maxFileSizeMb)
    {
        return $"The file is larger than the limit of {maxFileSizeMb} MB, so it was left out. Upload a smaller copy on its own.";
    }

    private static string SanitizeSubject(string? subject)
    {
        string sanitized = (subject ?? string.Empty).Trim();
        sanitized = EmailReplyForwardRegex().Replace(sanitized, string.Empty).Trim();
        sanitized = InvalidFileNameCharactersRegex().Replace(sanitized, "_");
        sanitized = CollapseUnderscoresRegex().Replace(sanitized, "_");
        sanitized = sanitized.Trim('_');

        return sanitized.Length == 0
            ? FALLBACK_SUBJECT
            : sanitized;
    }

    private static string Truncate(string fileNameBase)
    {
        return fileNameBase.Length > MAX_FILE_NAME_BASE_LENGTH
            ? fileNameBase[..MAX_FILE_NAME_BASE_LENGTH]
            : fileNameBase;
    }

    private static async Task<MimeMessage> ParseMessageAsync(byte[] emlBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(emlBytes, writable: false);

            return await MimeMessage.LoadAsync(stream, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailConversionException(EmailConversionException.DAMAGED_EMAIL, ex);
        }
    }

    private static IEnumerable<MimeEntity> GetCandidateEntities(MimeMessage message)
    {
        var seen = new HashSet<MimeEntity>();

        foreach (MimeEntity attachment in message.Attachments)
        {
            if (seen.Add(attachment))
            {
                yield return attachment;
            }
        }

        foreach (MimeEntity entity in message.BodyParts)
        {
            if (!seen.Add(entity))
            {
                continue;
            }

            if (entity is MessagePart || (entity is MimePart part && !string.IsNullOrEmpty(part.FileName)))
            {
                yield return entity;
            }
        }
    }

    private async Task ProcessEntityAsync(MimeEntity entity, ExpansionState state, CancellationToken cancellationToken)
    {
        if (entity is MessagePart)
        {
            state.SkippedParts.Add(new SkippedEmailPart(
                entity.ContentDisposition?.FileName ?? NESTED_EMAIL_FALLBACK_FILE_NAME,
                NESTED_EMAIL_REASON));
            return;
        }

        if (entity is not MimePart part)
        {
            state.SkippedParts.Add(new SkippedEmailPart(entity.ContentType.MimeType, UNSUPPORTED_ATTACHMENT_REASON));
            return;
        }

        string label = ResolveSkipLabel(part);

        if (part.Content is null)
        {
            state.SkippedParts.Add(new SkippedEmailPart(label, EMPTY_ATTACHMENT_REASON));
            return;
        }

        byte[] content = await DecodeAsync(part.Content, cancellationToken);
        FileInspection verdict = Inspect(content, ResolveInspectionName(part));

        switch (Route(verdict))
        {
            case PartRoute.ExpandArchive:
                await ExpandZipAsync(content, label, state, cancellationToken);
                return;
            case PartRoute.Skip:
                state.SkippedParts.Add(new SkippedEmailPart(label, UNSUPPORTED_ATTACHMENT_REASON));
                return;
            case PartRoute.Admit:
            default:
                string partFileName = ResolvePartFileName(part, verdict.Detected, state);
                await AdmitPartAsync(partFileName, content, verdict, verdict.DetectedMediaType ?? part.ContentType.MimeType, state, cancellationToken);
                return;
        }
    }

    private async Task AdmitPartAsync(
        string fileName,
        byte[] content,
        FileInspection verdict,
        string? contentType,
        ExpansionState state,
        CancellationToken cancellationToken)
    {
        FileUnlockResult unlock = verdict.Outcome is FileInspectionOutcome.Encrypted
            ? await _unlocker.TryUnlockAsync(content, verdict.Container!.Value, password: null, cancellationToken)
            : FileUnlockResult.NotEncrypted();
        string? unlockSkipReason = NonInteractiveUnlockPolicy.GetSkipReason(unlock.Status);

        if (unlockSkipReason is not null)
        {
            state.SkippedParts.Add(new SkippedEmailPart(fileName, unlockSkipReason));
            return;
        }

        state.Admit(new ExpandedEmailPart(state.ClaimFileName(fileName), content, contentType, unlock));
    }

    private async Task ExpandZipAsync(byte[] zipBytes, string zipLabel, ExpansionState state, CancellationToken cancellationToken)
    {
        int maxEntries = _platformSettings.EmailExpansion.MaxArchiveEntries;
        long maxEntryBytes = (long)_platformSettings.IniaAi.MaxFileSizeMb * 1024 * 1024;

        try
        {
            using var zipStream = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            if (archive.Entries.Count > maxEntries)
            {
                state.SkippedParts.Add(new SkippedEmailPart(
                    zipLabel,
                    BuildArchiveOverEntryLimitReason(archive.Entries.Count, maxEntries)));
                return;
            }

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessZipEntryAsync(entry, maxEntryBytes, state, cancellationToken);
            }
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Unreadable ZIP attachment in expanded email");
            state.SkippedParts.Add(new SkippedEmailPart(zipLabel, ARCHIVE_UNREADABLE_REASON));
        }
    }

    private async Task ProcessZipEntryAsync(
        ZipArchiveEntry entry,
        long maxEntryBytes,
        ExpansionState state,
        CancellationToken cancellationToken)
    {
        if (!ZipEntries.IsContent(entry))
        {
            return;
        }

        string entryFileName = Path.GetFileName(entry.FullName);

        byte[]? content = await ReadEntryBoundedAsync(entry, maxEntryBytes, cancellationToken);

        if (content is null)
        {
            state.SkippedParts.Add(new SkippedEmailPart(
                entryFileName,
                BuildEntryTooLargeReason(_platformSettings.IniaAi.MaxFileSizeMb)));
            return;
        }

        FileInspection verdict = Inspect(content, entryFileName);

        switch (Route(verdict))
        {
            case PartRoute.ExpandArchive:
                state.SkippedParts.Add(new SkippedEmailPart(entryFileName, NESTED_ARCHIVE_REASON));
                return;
            case PartRoute.Skip:
                state.SkippedParts.Add(new SkippedEmailPart(entryFileName, UNSUPPORTED_ATTACHMENT_REASON));
                return;
            case PartRoute.Admit:
            default:
                await AdmitPartAsync(entryFileName, content, verdict, verdict.DetectedMediaType, state, cancellationToken);
                return;
        }
    }

    private FileInspection Inspect(byte[] content, string fileName)
    {
        return FileContentInspector.Default.Inspect(content, fileName, _limits);
    }

    private static PartRoute Route(FileInspection verdict)
    {
        return verdict.MediaType switch
        {
            null => PartRoute.Admit,
            MediaType.ApplicationZip => PartRoute.ExpandArchive,
            MediaType.ApplicationPdf
                or MediaType.TextCsv
                or MediaType.ApplicationVndMsExcel
                or MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet
                or MediaType.ApplicationVndMsExcelSheetMacroEnabled12
                or MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument => PartRoute.Admit,
            _ => PartRoute.Skip,
        };
    }

    private async Task DownloadUrlPartsAsync(
        MimeMessage message,
        ExpansionState state,
        CancellationToken cancellationToken)
    {
        if (!_platformSettings.EmailExpansion.ShouldTryDownloadUrls)
        {
            return;
        }

        HashSet<Uri> urls = GetUrlsFromMessage(message);

        if (urls.Count == 0)
        {
            return;
        }

        // A folder of its own straight under the temp folder: a shared parent with a fixed name is one another user, such
        // as the PDF reader's, could create first and own.
        string downloadDirectory = Directory.CreateTempSubdirectory(DOWNLOAD_FOLDER_PREFIX).FullName;

        try
        {
            using (var pdfDownloadClient = new PdfDownloadClient(
                downloadDirectory,
                _logger,
                _platformSettings.EmailExpansion.ChromePageLoadDelayMs,
                _platformSettings.OutboundAccess.Guard))
            {
                foreach (Uri url in urls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DownloadUrlAsync(pdfDownloadClient, url, downloadDirectory, state, cancellationToken);
                }
            }

            foreach (string filePath in Directory.EnumerateFiles(downloadDirectory).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                string fileName = Path.GetFileName(filePath);
                FileUnlockResult unlock = await _unlocker.TryUnlockAsync(content, password: null, cancellationToken);
                string? unlockSkipReason = NonInteractiveUnlockPolicy.GetSkipReason(unlock.Status);

                if (unlockSkipReason is not null)
                {
                    state.SkippedParts.Add(new SkippedEmailPart(fileName, unlockSkipReason));
                    continue;
                }

                state.Admit(new ExpandedEmailPart(
                    state.ClaimFileName(fileName),
                    content,
                    MediaType.ApplicationPdf.GetEnumMember(),
                    unlock));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(downloadDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete the email-expansion download directory");
            }
        }
    }

    private async Task DownloadUrlAsync(
        PdfDownloadClient pdfDownloadClient,
        Uri url,
        string downloadDirectory,
        ExpansionState state,
        CancellationToken cancellationToken)
    {
        int fileCountBeforeDownload = Directory.GetFiles(downloadDirectory).Length;

        try
        {
            HttpResponseMessage? response = await pdfDownloadClient.TryDownloadPdfWithRetryAsync(url);

            if (response is null || !response.IsSuccessStatusCode)
            {
                state.SkippedParts.Add(new SkippedEmailPart(url.ToString(), URL_DOWNLOAD_FAILED_REASON));
                return;
            }

            if (response.StatusCode is HttpStatusCode.NoContent)
            {
                if (Directory.GetFiles(downloadDirectory).Length == fileCountBeforeDownload)
                {
                    state.SkippedParts.Add(new SkippedEmailPart(url.ToString(), URL_DOWNLOAD_FAILED_REASON));
                }

                return;
            }

            string filePath = Path.Combine(downloadDirectory, BuildUrlDerivedFileName(url));
            using FileStream fileStream = File.Create(filePath);
            using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await contentStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to download a PDF from an email body URL");
            state.SkippedParts.Add(new SkippedEmailPart(url.ToString(), URL_DOWNLOAD_FAILED_REASON));
        }
    }

    private IEnumerable<Uri> ParseUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        HashSet<Uri> urls = [];

        foreach (Match match in MatchUrlRegex().Matches(text).Cast<Match>())
        {
            if (!match.Success || match.Groups.Count <= 1)
            {
                continue;
            }

            string matchValue = match.Groups[1].Value;

            if (string.IsNullOrWhiteSpace(matchValue))
            {
                continue;
            }

            matchValue = HttpUtility.HtmlDecode(matchValue);

            Match linkGuardMatch = LinkGuardRegex().Match(matchValue);

            if (linkGuardMatch.Success)
            {
                matchValue = linkGuardMatch.Groups[1].Value;
            }

            if (Uri.TryCreate(matchValue, UriKind.Absolute, out Uri? url))
            {
                _ = urls.Add(url);
            }
            else
            {
                _logger.LogWarning("Failed to create a URL from a string found in the email body");
            }
        }

        return urls;
    }

    private void FilterOutNonDownloadableUrls(List<Uri> urls)
    {
        _ = urls.RemoveAll(url => _platformSettings.EmailExpansion.SkippedLinkHosts.Any(host => IsOnHost(url, host)));
        _ = urls.RemoveAll(IsBaseUrl);
    }

    private static bool IsOnHost(Uri url, string host)
    {
        return url.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase)
            && (url.Host.Length == host.Length || url.Host[url.Host.Length - host.Length - 1] == '.');
    }

    private static bool IsBaseUrl(Uri uri)
    {
        return (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath))
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static string BuildUrlDerivedFileName(Uri url)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string fileName = string
            .Join("_", url.PathAndQuery.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('.');
        fileName = Truncate(fileName);

        if (fileName.Length == 0)
        {
            fileName = "download";
        }

        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.pdf";
    }

    private static async Task<byte[]?> ReadEntryBoundedAsync(
        ZipArchiveEntry entry,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using Stream source = entry.Open();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];

        while (true)
        {
            int bytesRead = await source.ReadAsync(chunk, cancellationToken);

            if (bytesRead == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + bytesRead > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, bytesRead);
        }
    }

    private static async Task<byte[]> DecodeAsync(IMimeContent content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await content.DecodeToAsync(stream, cancellationToken);

        return stream.ToArray();
    }

    private static string ResolveSkipLabel(MimePart part)
    {
        string? name = Path.GetFileName(part.FileName);

        return string.IsNullOrEmpty(name)
            ? part.ContentType.MimeType
            : name;
    }

    /// <summary>
    /// The name the inspector refines a signature-less text part by. An unnamed part borrows the
    /// extension its declared MIME type implies, so a bare <c>text/csv</c> attachment stays CSV.
    /// </summary>
    private static string ResolveInspectionName(MimePart part)
    {
        string? name = Path.GetFileName(part.FileName);

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return MimeTypes.TryGetExtension(part.ContentType.MimeType, out string? extension)
            ? $"{UNNAMED_ATTACHMENT_FILE_NAME_PREFIX}{extension}"
            : UNNAMED_ATTACHMENT_FILE_NAME_PREFIX;
    }

    private static string ResolvePartFileName(MimePart part, DetectedContent? detected, ExpansionState state)
    {
        string? name = Path.GetFileName(part.FileName);

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        state.UnnamedAttachmentCount++;

        return $"{UNNAMED_ATTACHMENT_FILE_NAME_PREFIX}{state.UnnamedAttachmentCount}{GetExtension(detected)}";
    }

    private static string GetExtension(DetectedContent? detected)
    {
        return detected is not null && MimeTypes.TryGetExtension(detected.IanaMediaType, out string? extension)
            ? extension
            : string.Empty;
    }

    [GeneratedRegex(@"^(RE:|FW:)\s*", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex EmailReplyForwardRegex();

    [GeneratedRegex(@"(?<!xmlns(?::\w+)?=[""'])(https?://.*?)[\s|'|<|""]")]
    private static partial Regex MatchUrlRegex();

    [GeneratedRegex("__(https?://.*)__")]
    private static partial Regex LinkGuardRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9\-_]")]
    private static partial Regex InvalidFileNameCharactersRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();

    [GeneratedRegex(@"_{2,}")]
    private static partial Regex CollapseUnderscoresRegex();

    /// <summary>
    /// The parts of one email as they are admitted. Given a page count, the state counts each part as it comes and
    /// keeps none, so a count never holds every attachment at once.
    /// </summary>
    private sealed class ExpansionState(Func<ExpandedEmailPart, int?>? countPages = null)
    {
        private readonly HashSet<string> _claimedFileNames = new(StringComparer.OrdinalIgnoreCase);

        public List<ExpandedEmailPart> Parts { get; } = [];

        public int? CountedPages { get; private set; } = 0;

        public List<SkippedEmailPart> SkippedParts { get; } = [];

        public int UnnamedAttachmentCount { get; set; }

        public void Admit(ExpandedEmailPart part)
        {
            if (countPages is null)
            {
                Parts.Add(part);
                return;
            }

            CountedPages += countPages(part);
        }

        public string ClaimFileName(string fileName)
        {
            if (_claimedFileNames.Add(fileName))
            {
                return fileName;
            }

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);

            for (int suffix = 2; ; suffix++)
            {
                string candidate = $"{baseName} ({suffix}){extension}";

                if (_claimedFileNames.Add(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
