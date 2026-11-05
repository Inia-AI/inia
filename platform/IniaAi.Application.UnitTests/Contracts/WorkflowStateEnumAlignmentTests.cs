using ContractsWorkflowState = IniaAi.WebApi.Contracts.Enums.WorkflowState;
using DomainWorkflowState = IniaAi.Domain.Entities.WorkflowExecutions.WorkflowState;

namespace IniaAi.Application.UnitTests.Contracts;

/// <summary>
/// Guards against drift between the two parallel <c>WorkflowState</c> enums:
/// <see cref="DomainWorkflowState"/> on the persisted entity and
/// <see cref="ContractsWorkflowState"/> on the API surface. The codebase casts
/// between them by integer value (e.g. in
/// <c>UpdateWorkflowExecutionStateCommandHandler</c> and <c>WorkflowExecutionProjections</c>),
/// so a reordered or mismatched value would silently corrupt API responses.
/// </summary>
public class WorkflowStateEnumAlignmentTests
{
    [Fact]
    public void DomainAndContractsEnums_HaveIdenticalNames()
    {
        string[] domainNames = Enum.GetNames<DomainWorkflowState>();
        string[] contractsNames = Enum.GetNames<ContractsWorkflowState>();
        Assert.Equal(domainNames, contractsNames);
    }

    [Fact]
    public void DomainAndContractsEnums_HaveIdenticalIntegerValues()
    {
        int[] domainValues = [.. Enum.GetValues<DomainWorkflowState>().Select(v => (int)v)];
        int[] contractsValues = [.. Enum.GetValues<ContractsWorkflowState>().Select(v => (int)v)];
        Assert.Equal(domainValues, contractsValues);
    }
}
