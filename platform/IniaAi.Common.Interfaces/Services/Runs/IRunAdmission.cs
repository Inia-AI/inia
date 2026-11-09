using ErrorOr;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Common.Interfaces.Services.Runs;

/// <summary>
/// Decides whether a submitted run may start. Called after every other submission check has passed,
/// inside the UserData transaction that inserts the run, so a write it makes commits with the run
/// and a refusal rolls the run back. A refusal comes back to the caller as the submission's errors.
/// <para>
/// An implementation writes through the request's own scoped UserData context, never a scope of its
/// own, and neither begins a transaction nor clears the change tracker: the transaction is open
/// already, and the caller's tracked entities are not its to drop. A transient failure runs the
/// attempt again, so it has no effect outside the database.
/// </para>
/// </summary>
public interface IRunAdmission
{
    Task<ErrorOr<Success>> AdmitAsync(RunAdmissionRequest request, CancellationToken cancellationToken);
}

/// <param name="ExecutionId">The run's execution row, inserted but not yet committed.</param>
/// <param name="DocumentIds">The input documents the submission names.</param>
/// <param name="EmailIntakeMode">
/// How the run reads its emails (an <c>EmailIntakeModes</c> value), or null when the run has no email stage and reads
/// nothing from an email.
/// </param>
public sealed record RunAdmissionRequest(
    string ExecutionId,
    string OrganizationId,
    string UserId,
    Workflow Workflow,
    IReadOnlyList<string> DocumentIds,
    string? EmailIntakeMode);
