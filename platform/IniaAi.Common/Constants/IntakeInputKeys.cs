namespace IniaAi.Common.Constants;

/// <summary>
/// Job-input keys intake channels use to hand data to the execution pipeline. Every key here is
/// consumed and removed before inputs are copied into SharedInputs — none is a replacement tag
/// and none can ever be referenced from a template (those live in <see cref="SharedInputKeys"/>).
/// </summary>
public struct IntakeInputKeys
{
    public const string SKIPPED_PROTECTED_FILES = "SkippedProtectedFiles";
    public const string EMAIL_INTAKE_MODE = "EmailIntakeMode";
    public const string EMAIL_ATTACHMENTS_CLASS = "EmailAttachmentsClass";
}
