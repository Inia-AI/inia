using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Application.Documents;

/// <summary>
/// One place where bytes enter the platform. Carries what a refusal record needs. A mouth inside a run names the run,
/// so the refusal joins the run's trail; an upload names none. <paramref name="ContentIsVouchedFor"/> marks a mouth
/// whose sender answers for the file, as a workflow engineer does in the editor: the gate then keeps only the refusals
/// that protect the platform and admits the rest as the type its bytes are, or its name claims when the bytes name none.
/// </summary>
public sealed record IntakeMouth(
    IUserDataUnitOfWork UserData,
    string OrganizationId,
    string? WorkflowExecutionId = null,
    bool ContentIsVouchedFor = false);
