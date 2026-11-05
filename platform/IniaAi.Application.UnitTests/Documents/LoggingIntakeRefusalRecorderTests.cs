using IniaAi.Application.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.UnitTests.Documents;

/// <summary>
/// The platform's own refusal trail: one warning per refused file that names the organization, the run, the reason,
/// the size and the detected type, and never the file name, which for an email body carries the message subject.
/// </summary>
public class LoggingIntakeRefusalRecorderTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-refusal-log";
    private const string EXECUTION_ID = "exec-refusal-log";
    private const string EMAIL_BODY_NAME = "email_body_Margin call for Jane Doe_2026-09-01_10-00-00.pdf";
    private const string SUBJECT_FRAGMENT = "Jane Doe";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CapturingLogger _logger = new();
    private readonly LoggingIntakeRefusalRecorder _recorder;
    private readonly IntakeMouth _mouth;

    public LoggingIntakeRefusalRecorderTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _recorder = new LoggingIntakeRefusalRecorder(_logger);
        _mouth = new IntakeMouth(_serviceProvider.GetRequiredService<IUserDataUnitOfWork>(), ORGANIZATION_ID, EXECUTION_ID);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Refusal_LogsOneWarningWithoutTheFileName()
    {
        IntakeRefusal refusal = IntakeRefusal.Unread(EMAIL_BODY_NAME, 4_096, IntakeRefusalReasons.FILE_TOO_LARGE);

        await _recorder.RecordAsync(refusal, _mouth, TestContext.Current.CancellationToken);

        CapturedLog entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(SUBJECT_FRAGMENT, entry.Message);
        Assert.DoesNotContain(entry.Values, value => value.Value is string text && text.Contains(SUBJECT_FRAGMENT));
    }

    [Fact]
    public async Task Refusal_LogNamesTheOrganizationRunReasonSizeAndType()
    {
        IntakeRefusal refusal = IntakeRefusal.OfBytes(EMAIL_BODY_NAME, [0x4D, 0x5A], IntakeRefusalReasons.FILE_TYPE_MISMATCH, "application/x-msdownload");

        await _recorder.RecordAsync(refusal, _mouth, TestContext.Current.CancellationToken);

        CapturedLog entry = Assert.Single(_logger.Entries);
        Assert.Contains(ORGANIZATION_ID, entry.Message);
        Assert.Contains(EXECUTION_ID, entry.Message);
        Assert.Contains(IntakeRefusalReasons.FILE_TYPE_MISMATCH, entry.Message);
        Assert.Contains("2 bytes", entry.Message);
        Assert.Contains("application/x-msdownload", entry.Message);
    }

    private sealed record CapturedLog(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Values);

    private sealed class CapturingLogger : ILogger<LoggingIntakeRefusalRecorder>
    {
        private readonly List<CapturedLog> _entries = [];

        public IReadOnlyList<CapturedLog> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            IReadOnlyList<KeyValuePair<string, object?>> values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            _entries.Add(new CapturedLog(logLevel, formatter(state, exception), values));
        }
    }
}
