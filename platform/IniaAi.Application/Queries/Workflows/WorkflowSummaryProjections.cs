using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.RunLimits;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.InputRules;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Models.Dto;
using System.Linq.Expressions;

namespace IniaAi.Application.Queries.Workflows;

/// <summary>
/// Builds a <see cref="WorkflowDto"/> from the few parts of a workflow it shows, so a read never
/// loads the workflow's run state or citation traces. It leaves out what a reader must not see: a
/// document class's description written for the model, and a hidden variable's default value, which
/// the run fills in on the server.
/// </summary>
public static class WorkflowSummaryProjections
{
    private static readonly Expression<Func<WorkflowVariable, WorkflowVariableDto>> _toVariableDto = variable => new WorkflowVariableDto
    {
        Id = variable.Id,
        ReplacementTag = variable.ReplacementTag,
        DisplayName = variable.DisplayName,
        Description = variable.Description,
        Type = variable.Type.ToString(),
        IsRequired = variable.IsRequired,
        IsHidden = variable.IsHidden,
        IsConfidential = variable.IsConfidential,
        DefaultValue = variable.IsHidden ? null : variable.DefaultValue,
        Options = variable.Options,
        InputRules = InputRuleDtosOf(variable.InputRules),
    };

    public static readonly Expression<Func<Workflow, WorkflowParts>> ToParts = workflow => new WorkflowParts(
        workflow.Id,
        workflow.WorkflowTypeId,
        workflow.WorkflowType!.Name,
        workflow.WorkflowType!.CounterpartyTypeMappings!.Select(mapping => mapping.CounterpartyTypeId).ToList(),
        workflow.Configuration,
        workflow.Variables.AsQueryable().Select(_toVariableDto).ToList(),
        workflow.DocumentClasses.ToList());

    /// <summary>
    /// The columns a type list entry shows. It reads the configuration column for the credit cost,
    /// which <see cref="ToSummaryDto"/> takes from it in memory through <see cref="IWorkflowCreditCost"/>.
    /// </summary>
    public static readonly Expression<Func<Workflow, WorkflowListRow>> ToListRow = workflow => new WorkflowListRow
    {
        Id = workflow.Id,
        WorkflowTypeId = workflow.WorkflowTypeId,
        WorkflowTypeName = workflow.WorkflowType!.Name,
        Configuration = workflow.Configuration,
    };

    public static WorkflowSummaryDto ToSummaryDto(WorkflowListRow workflow, IWorkflowCreditCost creditCost, IWorkflowPageLimit pageLimit)
    {
        return new WorkflowSummaryDto
        {
            Id = workflow.Id,
            WorkflowTypeId = workflow.WorkflowTypeId ?? string.Empty,
            WorkflowTypeName = workflow.WorkflowTypeName,
            IsAvailable = true,
            CreditCost = creditCost.Of(workflow.Configuration),
            MaxPages = pageLimit.Of(workflow.Configuration),
        };
    }

    public static WorkflowDto ToDto(WorkflowParts workflow, IWorkflowCreditCost creditCost, IWorkflowPageLimit pageLimit)
    {
        return new WorkflowDto
        {
            Id = workflow.Id,
            WorkflowTypeId = workflow.WorkflowTypeId!,
            WorkflowTypeName = workflow.WorkflowTypeName,
            IsAvailable = Workflow.GetAvailability(workflow.WorkflowTypeId),
            DocumentClassKeys = Workflow.DocumentClassKeysOf(workflow.DocumentClasses),
            Variables = workflow.Variables,
            DocumentClasses = [.. workflow.DocumentClasses.Select(ToDocumentClassDto)],
            CounterpartyTypeMappings = workflow.CounterpartyTypeIds,
            CreditCost = creditCost.Of(workflow.Configuration),
            MaxPages = pageLimit.Of(workflow.Configuration),
        };
    }

    // A projection reads the column itself, so a variable without rules arrives as null.
    private static List<WorkflowVariableInputRuleDto> InputRuleDtosOf(List<VariableInputRule>? rules)
    {
        if (rules is null)
        {
            return [];
        }

        return [.. rules.Select(rule => new WorkflowVariableInputRuleDto
        {
            Conditions = [.. rule.Conditions.Select(condition => new InputRuleConditionDto
            {
                Operator = condition.Operator.ToString(),
                DocumentClassKeys = condition.DocumentClassKeys is null ? null : [.. condition.DocumentClassKeys],
                Variable = condition.Variable,
                Value = condition.Value,
            })],
            Requirement = new InputRuleRequirementDto
            {
                Operator = rule.Requirement.Operator.ToString(),
                Value = rule.Requirement.Value,
            },
        })];
    }

    private static DocumentClassDto ToDocumentClassDto(DocumentClass documentClass)
    {
        return new DocumentClassDto
        {
            Key = documentClass.Key,
            DisplayName = documentClass.DisplayName,
            Description = documentClass.Description,
            Requirement = documentClass.Requirement.ToString(),
            ShouldBeSingular = documentClass.ShouldBeSingular,
            IsOutputDocument = documentClass.IsOutputDocument,
            DocumentCategoryId = documentClass.DocumentCategoryId,
        };
    }

    public sealed class WorkflowListRow
    {
        public required string Id { get; init; }

        public string? WorkflowTypeId { get; init; }

        public string? WorkflowTypeName { get; init; }

        public WorkflowConfiguration? Configuration { get; init; }
    }

    public sealed record WorkflowParts(
        string Id,
        string? WorkflowTypeId,
        string? WorkflowTypeName,
        List<string> CounterpartyTypeIds,
        WorkflowConfiguration? Configuration,
        List<WorkflowVariableDto> Variables,
        List<DocumentClass> DocumentClasses);
}
