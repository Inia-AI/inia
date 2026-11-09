using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace IniaAi.Testing;

/// <summary>
/// A real logger provider whose loggers keep every line of every category at every level in one
/// list, for a test that asserts what a whole path writes to the logs. Safe to share between threads.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(categoryName, _entries);
    }

    public void Dispose() { }
}
