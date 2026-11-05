namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Expands an uploaded email into the files a workflow execution should receive: the body
/// rendered to PDF, the raw email as an audit artifact, and every supported attachment, with
/// unsupported or unreadable parts reported as skipped. The bytes decide whether they are an
/// Outlook message or an RFC 822 message; no name takes part.
/// </summary>
public interface IEmailExpansionService
{
    /// <summary>
    /// Expands <paramref name="emailBytes"/> into its constituent files. Throws
    /// <c>EmailConversionException</c> when the bytes are not an email, cannot be parsed, or
    /// the body cannot be rendered to PDF; the caller rejects the whole submission then.
    /// </summary>
    /// <param name="emailBytes">The raw email file content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EmailExpansionResult> ExpandAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders only the email body to PDF, leaving attachments untouched. Throws
    /// <c>EmailConversionException</c> when the bytes are not an email or cannot be rendered.
    /// </summary>
    /// <param name="emailBytes">The raw email file content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EmailBodyRenderResult> RenderBodyAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Derives the trigger email identifier (sanitized subject + sent date) by parsing the email
    /// only, no PDF rendering. Lets callers detect an already-expanded email (its products carry
    /// the identifier in <c>Document.Sources</c>) before paying for expansion. Throws
    /// <c>EmailConversionException</c> when the bytes are not an email or cannot be parsed.
    /// </summary>
    /// <param name="emailBytes">The raw email file content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> DeriveTriggerEmailIdentifierAsync(
        byte[] emailBytes,
        CancellationToken cancellationToken = default);
}
