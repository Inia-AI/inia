using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.UserData.Interfaces;
using NSubstitute;
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.Shared;

/// <summary>The stored source execution a freeze resolves against, and the template a submission targets.</summary>
public abstract class FreezeSourceTestBase
{
    protected const string ORG = "org-1";
    protected const string SOURCE_ID = "exec-source";
    protected const string TYPE_ID = "wf-type-1";

    protected FreezeSourceTestBase()
    {
        _ = UnitOfWork.Repository<WorkflowExecution>().Returns(ExecutionRepo);
        _ = UnitOfWork.Workflows.Returns(WorkflowRepo);
    }

    protected IUserDataUnitOfWork UnitOfWork { get; } = Substitute.For<IUserDataUnitOfWork>();

    protected IRepository<WorkflowExecution> ExecutionRepo { get; } = Substitute.For<IRepository<WorkflowExecution>>();

    protected IUserDataWorkflowRepository WorkflowRepo { get; } = Substitute.For<IUserDataWorkflowRepository>();

    protected static Workflow Target(string typeId = TYPE_ID, params (string Tag, string? Default)[] variables)
    {
        var workflow = new Workflow { Id = "template-1", WorkflowTypeId = typeId };
        foreach ((string tag, string? defaultValue) in variables)
        {
            _ = new WorkflowVariable(workflow, tag) { DefaultValue = defaultValue };
        }

        return workflow;
    }

    protected static Dictionary<string, JsonElement> Values(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    protected void SourceIs(WorkflowExecution? execution)
    {
        List<WorkflowExecution> stored = execution is null ? [] : [execution];
        _ = ExecutionRepo
            .GetFirstOrDefaultAsync(Arg.Any<ISpecification<WorkflowExecution>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<WorkflowExecution>>().Evaluate(stored).FirstOrDefault());
    }

    protected static WorkflowExecution CompletedSource(
        string? counterpartyId = null,
        Dictionary<string, object>? variables = null,
        string? workflowId = "clone-1")
    {
        return new WorkflowExecution
        {
            Id = SOURCE_ID,
            OrganizationId = ORG,
            WorkflowTypeId = TYPE_ID,
            WorkflowId = workflowId,
            WorkflowState = WorkflowState.Draft,
            CounterpartyId = counterpartyId,
            Variables = variables,
        };
    }
}
