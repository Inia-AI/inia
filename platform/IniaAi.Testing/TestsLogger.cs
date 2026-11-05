using Microsoft.Extensions.Logging;
using System.Text;
using Xunit;

namespace IniaAi.Testing;

/// <summary>
/// A logger that implements the <see cref="ILogger"/> interface
/// and writes log output to the XUnit <see cref="ITestOutputHelper"/>.
/// If the test output helper is already disposed, it safely falls back to <see cref="Console"/>.
/// </summary>
public class TestsLogger : ILogger
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly string _categoryName;
    private readonly LoggerExternalScopeProvider _scopeProvider;

    /// <summary>
    /// Gets or sets the minimum log level.
    /// </summary>
    public LogLevel LogLevel { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestsLogger"/> class.
    /// </summary>
    /// <param name="testOutputHelper">The XUnit test output helper.</param>
    /// <param name="scopeProvider">The logger scope provider.</param>
    /// <param name="categoryName">The category name for this logger.</param>
    public TestsLogger(ITestOutputHelper testOutputHelper, LoggerExternalScopeProvider scopeProvider, string categoryName)
    {
        _testOutputHelper = testOutputHelper;
        _categoryName = categoryName;
        _scopeProvider = scopeProvider;
    }

    /// <summary>
    /// Creates a non-generic logger instance.
    /// </summary>
    /// <param name="testOutputHelper">The XUnit test output helper.</param>
    /// <returns>A new <see cref="TestsLogger"/> instance.</returns>
    public static ILogger CreateLogger(ITestOutputHelper testOutputHelper)
    {
        return new TestsLogger(testOutputHelper, new LoggerExternalScopeProvider(), string.Empty);
    }

    /// <summary>
    /// Creates a generic logger instance for the specified type.
    /// </summary>
    /// <typeparam name="T">The type to use as the logger category name.</typeparam>
    /// <param name="testOutputHelper">The XUnit test output helper.</param>
    /// <returns>A new <see cref="TestsLogger{T}"/> instance.</returns>
    public static ILogger<T> CreateLogger<T>(ITestOutputHelper testOutputHelper)
    {
        return new TestsLogger<T>(testOutputHelper, new LoggerExternalScopeProvider());
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel)
    {
        return LogLevel <= logLevel;
    }

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var sb = new StringBuilder();
        _ = sb.Append($"[{DateTime.Now:HH:mm:ss.fff}]")
              .Append(GetLogLevelString(logLevel))
              .Append($"[{_categoryName}] ")
              .Append(formatter(state, exception));

        if (exception is not null)
        {
            _ = sb.Append('\n').Append(exception);
        }

        // Add any active scopes
        _scopeProvider.ForEachScope((scope, builder) =>
        {
            _ = builder.Append("\n => ").Append(scope);
        }, sb);

        try
        {
            // Try writing to the test output
            _testOutputHelper.WriteLine(sb.ToString());
        }
        catch (InvalidOperationException)
        {
            // If the test output is disposed, fallback
            Console.WriteLine(sb.ToString());
        }
    }

    /// <summary>
    /// Logs a simple message at <see cref="LogLevel.Information"/>.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Log(string message)
    {
        Log(LogLevel.Information, new EventId(), message, null, (state, _) => state?.ToString() ?? string.Empty);
    }

    /// <summary>
    /// Gets the log level prefix string.
    /// </summary>
    /// <param name="logLevel">The log level.</param>
    /// <returns>A short string representing the log level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for unknown log levels.</exception>
    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "[trce]",
            LogLevel.Debug => "[dbug]",
            LogLevel.Information => "[info]",
            LogLevel.Warning => "[warn]",
            LogLevel.Error => "[fail]",
            LogLevel.Critical => "[crit]",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }
}

/// <summary>
/// A logger that implements the <see cref="ILogger{T}"/> interface,
/// using the specified type as its category name.
/// </summary>
/// <typeparam name="T">The logger category type.</typeparam>
public sealed class TestsLogger<T> : TestsLogger, ILogger<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestsLogger{T}"/> class.
    /// </summary>
    /// <param name="testOutputHelper">The XUnit test output helper.</param>
    /// <param name="scopeProvider">The logger scope provider.</param>
    public TestsLogger(ITestOutputHelper testOutputHelper, LoggerExternalScopeProvider scopeProvider)
        : base(testOutputHelper, scopeProvider, typeof(T).Name)
    {
    }
}

/// <summary>
/// Provides <see cref="TestsLogger"/> instances for the specified XUnit output helper.
/// </summary>
public sealed class TestsLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TestsLoggerProvider"/> class.
    /// </summary>
    /// <param name="testOutputHelper">The XUnit test output helper.</param>
    public TestsLoggerProvider(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return new TestsLogger(_testOutputHelper, _scopeProvider, categoryName);
    }

    /// <inheritdoc/>
    public void Dispose() { }
}
