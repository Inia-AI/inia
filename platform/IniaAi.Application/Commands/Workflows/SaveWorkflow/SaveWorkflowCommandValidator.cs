using FluentValidation;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Commands.Workflows.SaveWorkflow;

public class SaveWorkflowCommandValidator : AbstractValidator<SaveWorkflowCommand>
{
    private readonly AgentRuntimeSettings _agentRuntime;
    private readonly ModelRoutingSettings _modelRouting;
    private readonly IAgentRunLimitsProvider? _runLimitsProvider;
    private readonly WorkflowExecutionJanitorSettings? _janitor;
    private readonly int _maxBlockParallelism;

    public SaveWorkflowCommandValidator(
        IOptions<PlatformSettings> platformSettings,
        IAgentRunLimitsProvider? runLimitsProvider = null,
        IOptions<WorkflowExecutionJanitorSettings>? janitorSettings = null)
    {
        _agentRuntime = platformSettings.Value.AgentRuntime ?? new AgentRuntimeSettings();
        _modelRouting = platformSettings.Value.ModelRouting ?? new ModelRoutingSettings();
        _runLimitsProvider = runLimitsProvider;
        _janitor = janitorSettings?.Value;
        _maxBlockParallelism = platformSettings.Value.MaxBlockParallelism;

        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty();
        RuleFor(x => x.Workflow).NotNull();

        RuleFor(x => x.Workflow)
            .Custom((workflow, context) => ValidateAgentBlocks(workflow, context))
            .When(x => x.Workflow is not null);
    }

    private void ValidateAgentBlocks(
        Workflow workflow,
        ValidationContext<SaveWorkflowCommand> context)
    {
        foreach (AgentBlockGuard.AgentBlockViolation violation
            in AgentBlockGuard.ValidateGraph(
                workflow,
                _agentRuntime,
                _runLimitsProvider?.Enforceable,
                _janitor,
                _maxBlockParallelism,
                AgentModelTiers.For(_agentRuntime, _modelRouting, context.InstanceToValidate.OrganizationId)))
        {
            context.AddFailure(nameof(SaveWorkflowCommand.Workflow), violation.Message);
        }
    }
}
