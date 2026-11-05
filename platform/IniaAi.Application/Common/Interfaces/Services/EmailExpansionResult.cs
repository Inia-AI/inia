using IniaAi.FileFormats.Unlock;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// A file produced by email expansion, ready to be uploaded as a document.
/// </summary>
/// <param name="FileName">The file name, unique within one expansion.</param>
/// <param name="Content">The file content.</param>
/// <param name="ContentType">The MIME content type, when known.</param>
/// <param name="Unlock">The unlock gate's verdict for parts that pass through it; carries the
/// decrypted copy to persist when protection was stripped. Null for parts the gate never sees,
/// such as the rendered body and the raw email.</param>
public sealed record ExpandedEmailPart(
    string FileName,
    byte[] Content,
    string? ContentType,
    FileUnlockResult? Unlock = null);

/// <summary>
/// A part of the email that was not turned into a file, with the operator-facing reason.
/// </summary>
/// <param name="FileName">The part's file name, or another identifying label such as a URL.</param>
/// <param name="Reason">Why the part was skipped.</param>
public sealed record SkippedEmailPart(string FileName, string Reason);

/// <summary>
/// The outcome of rendering one email's body without touching its attachments.
/// </summary>
/// <param name="TriggerEmailIdentifier">"{sanitized subject}_{sent date}"; never null.</param>
/// <param name="BodyPdf">The email body rendered to PDF, with no page cap.</param>
/// <param name="BodyShowsContentImage">Whether the body shows an image only OCR reads, such as a
/// pasted table; the body goes through OCR only then.</param>
public sealed record EmailBodyRenderResult(
    string TriggerEmailIdentifier,
    ExpandedEmailPart BodyPdf,
    bool BodyShowsContentImage);

/// <summary>
/// The outcome of expanding one email: the rendered body, the raw email audit artifact, the
/// extracted parts, and everything that had to be skipped.
/// </summary>
/// <param name="TriggerEmailIdentifier">"{sanitized subject}_{sent date}"; never null.</param>
/// <param name="MessageId">The email's Message-ID header value, when present.</param>
/// <param name="BodyPdf">The email body rendered to PDF, with no page cap.</param>
/// <param name="BodyShowsContentImage">Whether the body shows an image only OCR reads, such as a
/// pasted table; the body goes through OCR only then.</param>
/// <param name="RawEmail">The original email bytes, preserved as an audit artifact.</param>
/// <param name="Parts">Extracted attachments, archive entries, and downloaded files.</param>
/// <param name="SkippedParts">Parts that could not be extracted, with reasons.</param>
public sealed record EmailExpansionResult(
    string TriggerEmailIdentifier,
    string? MessageId,
    ExpandedEmailPart BodyPdf,
    bool BodyShowsContentImage,
    ExpandedEmailPart RawEmail,
    IReadOnlyList<ExpandedEmailPart> Parts,
    IReadOnlyList<SkippedEmailPart> SkippedParts);
