using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace IniaAi.Testing;

/// <summary>
/// One log call a <see cref="CapturingLogger"/> kept: its level, its rendered message, its
/// structured values by name, the exception it carried, and the logger's category.
/// </summary>
public sealed record CapturedLogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Values, Exception? Exception = null)
{
    public string Category { get; init; } = string.Empty;

    /// <summary>What a log sink writes for the call: the message, then the exception it carried.</summary>
    public string Text => Exception is null ? Message : $"{Message}{Environment.NewLine}{Exception}";

    public object? Value(string name)
    {
        return Values.FirstOrDefault(value => value.Key == name).Value;
    }
}

/// <summary>
/// A real logger that keeps every call, for a test that asserts what a component logs. Safe to
/// share between threads.
/// </summary>
public class CapturingLogger : ILogger
{
    private readonly string _category;
    private readonly ConcurrentQueue<CapturedLogEntry> _entries;

    public CapturingLogger(string category)
        : this(category, new ConcurrentQueue<CapturedLogEntry>())
    {
    }

    internal CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries)
    {
        _category = category;
        _entries = entries;
    }

    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        IReadOnlyList<KeyValuePair<string, object?>> values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        _entries.Enqueue(new CapturedLogEntry(logLevel, formatter(state, exception), values, exception) { Category = _category });
    }
}

/// <summary>A <see cref="CapturingLogger"/> under the category of <typeparamref name="T"/>.</summary>
public sealed class CapturingLogger<T>() : CapturingLogger(typeof(T).FullName ?? typeof(T).Name), ILogger<T>;

/// <summary>Queries over what a test's loggers kept.</summary>
public static class CapturedLogEntries
{
    /// <summary>Each kept line that holds any of the markers a test planted in content, with its level and category.</summary>
    public static string[] Holding(this IEnumerable<CapturedLogEntry> entries, params string[] markers)
    {
        return [.. entries
            .Where(entry => markers.Any(marker => entry.Text.Contains(marker, StringComparison.Ordinal)))
            .Select(entry => $"{entry.Level} {entry.Category}: {entry.Text}")];
    }
}
