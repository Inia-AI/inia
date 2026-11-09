namespace IniaAi.Common.Constants;

/// <summary>
/// Values for the <see cref="IntakeInputKeys.EMAIL_INTAKE_MODE"/> job input: how the email intake
/// job stage processes the emails of a run.
/// </summary>
public static class EmailIntakeModes
{
    public const string PDF_ONLY = "PdfOnly";
    public const string EXTRACT_ATTACHMENTS = "ExtractAttachments";

    /// <summary>
    /// The one mapping from the user's intake choice to the values to carry: the attachments
    /// class travels only when attachments are extracted.
    /// </summary>
    public static (string Mode, string? AttachmentsDocumentClass) Resolve(
        bool extractAttachments,
        string? attachmentsDocumentClass)
    {
        return extractAttachments
            ? (EXTRACT_ATTACHMENTS, attachmentsDocumentClass)
            : (PDF_ONLY, null);
    }

    /// <summary>
    /// The pages a run reads from an email it has not read before: none when the run has no email stage (no mode),
    /// its body, and its attachments as well when the run extracts them. Null when a count it needs is unknown.
    /// </summary>
    public static int? PagesRead(string? mode, int? bodyPages, int? attachmentPages)
    {
        return mode switch
        {
            null => 0,
            EXTRACT_ATTACHMENTS => bodyPages + attachmentPages,
            _ => bodyPages,
        };
    }

    /// <summary>
    /// The one encoding of the intake choice as job inputs, shared by every path that starts a
    /// job (orchestrator and editor): the mode is always written, the attachments class only
    /// when supplied.
    /// </summary>
    public static void WriteJobInputs(
        IDictionary<string, string> jobInputs,
        string mode,
        string? attachmentsDocumentClass)
    {
        jobInputs[IntakeInputKeys.EMAIL_INTAKE_MODE] = mode;

        if (!string.IsNullOrEmpty(attachmentsDocumentClass))
        {
            jobInputs[IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS] = attachmentsDocumentClass;
        }
    }
}
