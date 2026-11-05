namespace IniaAi.Configuration.Platform;

/// <summary>Settings for the open build's PDF reader, which reads each open PDF file in a worker process of its own.</summary>
public class PdfReaderSettings
{
    public const int DEFAULT_MAX_WORKERS = 8;

    /// <summary>
    /// How many PDF reader processes one server process runs at once. A file that would start one more waits until an
    /// open file closes. Each process holds its whole file and PDFium's memory for it.
    /// </summary>
    public int MaxWorkers { get; set; } = DEFAULT_MAX_WORKERS;
}
