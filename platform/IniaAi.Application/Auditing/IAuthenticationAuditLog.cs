namespace IniaAi.Application.Auditing;

/// <summary>
/// Where every sign-in outcome and every two-factor change is reported, in the WebApi and in the
/// admin app alike. The platform's own log writes a structured log record; a compliance module
/// registers its own log, which keeps the records wherever it needs them, with no change to the
/// call sites.
/// </summary>
public interface IAuthenticationAuditLog
{
    Task RecordAsync(AuthenticationAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
