using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Auditing;

/// <summary>
/// The platform's own authentication audit: one structured log record per event, a warning for a
/// refusal or a lockout and information for the rest.
/// </summary>
public sealed class LoggingAuthenticationAuditLog(ILogger<LoggingAuthenticationAuditLog> logger) : IAuthenticationAuditLog
{
    public Task RecordAsync(AuthenticationAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        LogLevel level = auditEvent.Kind is AuthenticationAuditKind.SignInRefused or AuthenticationAuditKind.SignInLockedOut
            ? LogLevel.Warning
            : LogLevel.Information;

        logger.Log(
            level,
            "Authentication audit: {AuditKind} for user {UserId} by {AuditMethod} from {ClientAddress}",
            auditEvent.Kind,
            auditEvent.UserId,
            auditEvent.Method,
            auditEvent.ClientAddress);

        return Task.CompletedTask;
    }
}
