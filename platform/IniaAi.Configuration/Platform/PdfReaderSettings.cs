namespace IniaAi.Configuration.Platform;

/// <summary>Settings for the open build's PDF reader, which reads each open PDF file in a worker process of its own.</summary>
public class PdfReaderSettings
{
    public const int DEFAULT_MAX_WORKERS = 8;
    public const int DEFAULT_MAX_COUNTING_WORKERS = 2;

    /// <summary>
    /// How many PDF reader processes one server process runs at once. A file that would start one more waits until an
    /// open file closes. Each process holds its whole file and PDFium's memory for it.
    /// </summary>
    public int MaxWorkers { get; set; } = DEFAULT_MAX_WORKERS;

    /// <summary>
    /// How many PDF reader processes counting the pages of uploads runs at once, apart from <see cref="MaxWorkers"/>, so
    /// an upload never waits for a report to close a file.
    /// </summary>
    public int MaxCountingWorkers { get; set; } = DEFAULT_MAX_COUNTING_WORKERS;
}
