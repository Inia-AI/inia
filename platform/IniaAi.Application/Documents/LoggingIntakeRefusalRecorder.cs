using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Documents;

/// <summary>
/// The platform's own refusal trail: one warning line per refused file. The line leaves out the file name, because
/// an email body's name carries the message subject; the screening record keeps the name where one is registered.
/// </summary>
public sealed class LoggingIntakeRefusalRecorder : IIntakeRefusalRecorder
{
    private readonly ILogger<LoggingIntakeRefusalRecorder> _logger;

    public LoggingIntakeRefusalRecorder(ILogger<LoggingIntakeRefusalRecorder> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(IntakeRefusal refusal, IntakeMouth mouth, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Refused a file ({SizeBytes} bytes, detected {DetectedMediaType}) for organization {OrganizationId} in execution {WorkflowExecutionId}: {ReasonCode}.",
            refusal.SizeBytes,
            refusal.DetectedMediaType,
            mouth.OrganizationId,
            mouth.WorkflowExecutionId,
            refusal.ReasonCode);

        return Task.CompletedTask;
    }
}
