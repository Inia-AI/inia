using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Workflows.Interfaces;
using IniaAi.Application.Workflows.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.DocumentClassValidation;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowExecutions;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetReRunInputs;

/// <summary>
/// Compares a run's inputs with its workflow's current template and works out what a re-run
/// submits. It checks the inputs with the same variable and document class rules a submission is
/// checked with, so a plan it calls ready passes those rules on submission. Rules about the
/// template itself, such as an Agent block the deployment cannot run, are not part of the plan.
/// </summary>
public sealed class ReRunInputsPlanner
{
    private readonly IWorkflowVariableValidationService _variableValidation;
    private readonly IDocumentClassValidationService _documentClassValidation;

    public ReRunInputsPlanner(
        IWorkflowVariableValidationService variableValidation,
        IDocumentClassValidationService documentClassValidation)
    {
        _variableValidation = variableValidation;
        _documentClassValidation = documentClassValidation;
    }

    /// <summary>
    /// Plans a re-run of a run against the workflow's current template.
    /// </summary>
    /// <param name="template">The workflow's current template, with its variables and document classes.</param>
    /// <param name="runVariables">The variable definitions of the workflow the run executed.</param>
    /// <param name="runValues">The variable values the run was submitted with.</param>
    /// <param name="inputDocuments">The run's input documents.</param>
    public ReRunInputsDto Plan(
        Workflow template,
        IReadOnlyCollection<WorkflowVariable> runVariables,
        Dictionary<string, object>? runValues,
        IReadOnlyCollection<RunInputDocument> inputDocuments)
    {
        Dictionary<string, JsonElement> runValuesByTag = VariableKeyCanonicalizer.CanonicalizeVariableKeys(
            ExecutionVariableJson.From(runValues),
            template.Variables);

        HashSet<string> confidentialTags = new(
            runVariables.Concat(template.Variables)
                .Where(variable => variable.IsConfidential)
                .Select(variable => variable.ReplacementTag),
            StringComparer.OrdinalIgnoreCase);

        HashSet<string> templateTags = [.. template.Variables.Select(variable => variable.ReplacementTag)];

        Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
        HashSet<string> withheldTags = new(StringComparer.Ordinal);
        List<ReRunVariableChangeDto> defaulted = [];

        foreach (WorkflowVariable variable in template.Variables)
        {
            string tag = variable.ReplacementTag;

            if (runValuesByTag.TryGetValue(tag, out JsonElement runValue) && HasValue(runValue))
            {
                if (confidentialTags.Contains(tag))
                {
                    _ = withheldTags.Add(tag);
                    continue;
                }

                values[tag] = runValue;
                continue;
            }

            if (variable.DefaultValueAsJson() is { } defaultValue)
            {
                values[tag] = defaultValue;
                defaulted.Add(ChangeOf(variable));
            }
        }

        List<ReRunVariableNeedingInputDto> needingInput = NeedingInput(template, values, runValuesByTag, withheldTags);

        foreach (ReRunVariableNeedingInputDto variable in needingInput)
        {
            _ = values.Remove(variable.ReplacementTag);
        }

        foreach (string tag in withheldTags.Where(tag => needingInput.All(variable => variable.ReplacementTag != tag)))
        {
            WorkflowVariable variable = template.Variables.First(candidate => candidate.ReplacementTag == tag);
            needingInput.Add(new ReRunVariableNeedingInputDto
            {
                ReplacementTag = tag,
                DisplayName = DisplayNameOf(variable),
                Reason = ReRunInputReason.Confidential,
                Message = $"Confidential variable '{DisplayNameOf(variable)}' ({tag}) is not carried over to a re-run.",
            });
        }

        List<ReRunDocumentClassNeedingInputDto> documentClassesNeedingInput = DocumentClassesNeedingInput(
            template,
            [.. inputDocuments.Where(document => document.IsAvailable).Select(document => (string?)document.DocumentClass)]);
        List<ReRunUnavailableDocumentDto> unavailableDocuments = [.. inputDocuments
            .Where(document => !document.IsAvailable)
            .Select(document => new ReRunUnavailableDocumentDto { DocumentId = document.DocumentId, FileName = document.FileName })];

        return new ReRunInputsDto
        {
            WorkflowId = template.Id,
            WorkflowTypeId = template.WorkflowTypeId ?? string.Empty,
            IsReady = needingInput.Count == 0 && documentClassesNeedingInput.Count == 0 && unavailableDocuments.Count == 0,
            Variables = [.. values.Select(entry => new ReRunVariableValueDto { ReplacementTag = entry.Key, Value = entry.Value })],
            VariablesNeedingInput = needingInput,
            DefaultedVariables = [.. defaulted.Where(change => needingInput.All(variable => variable.ReplacementTag != change.ReplacementTag))],
            RemovedVariables = Removed(runVariables, runValuesByTag, templateTags),
            DocumentClassesNeedingInput = documentClassesNeedingInput,
            UnavailableDocuments = unavailableDocuments,
        };
    }

    private List<ReRunVariableNeedingInputDto> NeedingInput(
        Workflow template,
        Dictionary<string, JsonElement> values,
        Dictionary<string, JsonElement> runValuesByTag,
        HashSet<string> withheldTags)
    {
        Dictionary<string, WorkflowVariable> variablesByTag = template.Variables
            .GroupBy(variable => variable.ReplacementTag, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        List<ReRunVariableNeedingInputDto> needingInput = [];

        foreach (Error error in _variableValidation.ValidateVariables(template, values))
        {
            if (error.Metadata?.GetValueOrDefault(WorkflowVariableValidationService.REPLACEMENT_TAG_METADATA_KEY) is not string tag
                || !variablesByTag.TryGetValue(tag, out WorkflowVariable? variable)
                || needingInput.Any(entry => entry.ReplacementTag == tag))
            {
                continue;
            }

            ReRunInputReason reason = withheldTags.Contains(tag)
                ? ReRunInputReason.Confidential
                : values.TryGetValue(tag, out JsonElement value) && runValuesByTag.ContainsKey(tag) && !IsBlank(value)
                    ? ReRunInputReason.InvalidValue
                    : ReRunInputReason.NoValue;

            needingInput.Add(new ReRunVariableNeedingInputDto
            {
                ReplacementTag = tag,
                DisplayName = DisplayNameOf(variable),
                Reason = reason,
                Message = error.Description,
            });
        }

        return needingInput;
    }

    private List<ReRunDocumentClassNeedingInputDto> DocumentClassesNeedingInput(
        Workflow template,
        IReadOnlyCollection<string?> inputDocumentClasses)
    {
        DocumentClassValidationResult validation = _documentClassValidation.ValidateDocumentClasses(template, inputDocumentClasses);

        return [.. SubmissionDocumentClassRules.Refusing(template, validation)
            .Select(status => new ReRunDocumentClassNeedingInputDto
            {
                Key = status.Key,
                DisplayName = string.IsNullOrWhiteSpace(status.DisplayName) ? status.Key : status.DisplayName,
                Message = status.ValidationMessage ?? string.Empty,
            })];
    }

    /// <summary>
    /// The run's variables that had a value and that the current template no longer defines. A
    /// stored key that was never one of the run's variables is dropped without being listed.
    /// </summary>
    private static List<ReRunVariableChangeDto> Removed(
        IReadOnlyCollection<WorkflowVariable> runVariables,
        Dictionary<string, JsonElement> runValuesByTag,
        HashSet<string> templateTags)
    {
        Dictionary<string, JsonElement> leftOver = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string tag, JsonElement value) in runValuesByTag)
        {
            if (!templateTags.Contains(tag) && HasValue(value))
            {
                leftOver[tag] = value;
            }
        }

        return [.. runVariables
            .Where(variable => leftOver.ContainsKey(variable.ReplacementTag))
            .DistinctBy(variable => variable.ReplacementTag, StringComparer.OrdinalIgnoreCase)
            .Select(ChangeOf)];
    }

    private static bool HasValue(JsonElement value)
    {
        return value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    private static bool IsBlank(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false,
        };
    }

    private static ReRunVariableChangeDto ChangeOf(WorkflowVariable variable)
    {
        return new ReRunVariableChangeDto
        {
            ReplacementTag = variable.ReplacementTag,
            DisplayName = DisplayNameOf(variable),
        };
    }

    private static string DisplayNameOf(WorkflowVariable variable)
    {
        return string.IsNullOrWhiteSpace(variable.DisplayName) ? variable.ReplacementTag : variable.DisplayName;
    }
}
