namespace IniaAi.FileFormats.Unlock;

/// <summary>
/// Raised when the processing pipeline opens a document that is still password-protected —
/// a file the ingest gate should have decrypted or rejected. The message is user-actionable
/// and is surfaced on the execution's durable log.
/// </summary>
public sealed class DocumentPasswordProtectedException : Exception
{
    public DocumentPasswordProtectedException(string documentName, Exception innerException)
        : base(MessageFor(documentName), innerException)
    {
        DocumentName = documentName;
    }

    public DocumentPasswordProtectedException(string documentName)
        : base(MessageFor(documentName))
    {
        DocumentName = documentName;
    }

    public string DocumentName { get; }

    private static string MessageFor(string documentName)
    {
        return $"Document '{documentName}' is password-protected. Re-upload it and provide its password.";
    }
}
