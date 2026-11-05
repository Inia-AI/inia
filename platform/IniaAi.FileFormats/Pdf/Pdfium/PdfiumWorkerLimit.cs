using IniaAi.Configuration.Platform;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// The limit on live PDF worker processes that one server process shares across its jobs. Each open PDF document and
/// each PDF unlock holds one worker until it ends. A start past the limit blocks its thread until a worker ends, since
/// opening a document is synchronous.
/// </summary>
public sealed class PdfiumWorkerLimit
{
    private readonly SemaphoreSlim _free;
    private int _waiting;

    /// <summary>A limit of its own, at the default size, for a format or an unlocker that no host shares.</summary>
    public PdfiumWorkerLimit()
        : this(PdfReaderSettings.DEFAULT_MAX_WORKERS)
    {
    }

    public PdfiumWorkerLimit(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        Limit = limit;
        _free = new SemaphoreSlim(limit, limit);
    }

    internal int Limit { get; }

    internal int Live => Limit - _free.CurrentCount;

    internal int Waiting => Volatile.Read(ref _waiting);

    internal void Take()
    {
        _ = Interlocked.Increment(ref _waiting);
        try
        {
            _free.Wait();
        }
        finally
        {
            _ = Interlocked.Decrement(ref _waiting);
        }
    }

    internal void Return()
    {
        _ = _free.Release();
    }
}
