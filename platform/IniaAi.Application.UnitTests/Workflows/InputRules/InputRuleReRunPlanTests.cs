using IniaAi.Application.Queries.WorkflowExecutions.GetReRunInputs;
using IniaAi.Application.Workflows.Services;
using IniaAi.Core.Services;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowExecutions;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Workflows.InputRules;

/// <summary>
/// A re-run of an old report whose inputs break a rule the template gained since asks for the
/// value through "Needs your input", with the rule's message.
/// </summary>
public sealed class InputRuleReRunPlanTests
{
    private readonly ReRunInputsPlanner _planner = new(
        new WorkflowVariableValidationService(),
        new DocumentClassValidationService(NullLogger<DocumentClassValidationService>.Instance));

    [Fact]
    public void ARunWithAQuarterlyReportAndTheInterimPeriodAtNA_NeedsInputWithTheRuleMessage()
    {
        Workflow template = InputRuleWorkflows.Interim();

        ReRunInputsDto plan = _planner.Plan(
            template,
            [.. template.Variables],
            new Dictionary<string, object> { [InputRuleWorkflows.INTERIM] = InputRuleWorkflows.NOT_AN_INTERIM },
            [new RunInputDocument("document-1", InputRuleWorkflows.QUARTERLY, "Quarterly.pdf", IsAvailable: true)]);

        Assert.False(plan.IsReady);
        ReRunVariableNeedingInputDto needed = Assert.Single(plan.VariablesNeedingInput);
        Assert.Equal(InputRuleWorkflows.INTERIM, needed.ReplacementTag);
        Assert.Equal(ReRunInputReason.InvalidValue, needed.Reason);
        Assert.Equal(InputRuleWorkflows.ATTACHED_BUT_NA, needed.Message);
        Assert.DoesNotContain(plan.Variables, value => value.ReplacementTag == InputRuleWorkflows.INTERIM);
    }

    [Fact]
    public void ARunWhoseInputsMeetTheRules_IsReady()
    {
        Workflow template = InputRuleWorkflows.Interim();

        ReRunInputsDto plan = _planner.Plan(
            template,
            [.. template.Variables],
            new Dictionary<string, object> { [InputRuleWorkflows.INTERIM] = InputRuleWorkflows.SIX_MONTHS },
            [new RunInputDocument("document-1", InputRuleWorkflows.QUARTERLY, "Quarterly.pdf", IsAvailable: true)]);

        Assert.True(plan.IsReady);
    }
}
