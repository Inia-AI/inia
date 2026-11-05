using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace IniaAi.Testing;

/// <summary>
/// One log call a <see cref="CapturingLogger{T}"/> kept: its level, its rendered message, its
/// structured values by name, and the exception it carried.
/// </summary>
public sealed record CapturedLogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Values, Exception? Exception = null)
{
    public object? Value(string name)
    {
        return Values.FirstOrDefault(value => value.Key == name).Value;
    }
}

/// <summary>
/// A real logger that keeps every call, for a test that asserts what a component logs. Safe to
/// share between threads.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        IReadOnlyList<KeyValuePair<string, object?>> values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        _entries.Enqueue(new CapturedLogEntry(logLevel, formatter(state, exception), values, exception));
    }
}
