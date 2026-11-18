using ErrorOr;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.InputRules;

namespace IniaAi.Application.Workflows.Services;

/// <summary>
/// Turns the outcome of <see cref="VariableInputRuleCheck"/> into what a submission returns and
/// what a run's log keeps, so a submission, a re-run plan and a regression start refuse with the
/// same errors.
/// </summary>
public static class VariableInputRuleErrors
{
    public const string INPUT_RULE_CODE = "WorkflowVariable.InputRule";
    public const string RUN_INPUTS_SECTION_NAME = "Run inputs";

    /// <summary>One validation error per broken rule, carrying the variable's replacement tag like the other variable errors.</summary>
    public static List<Error> Of(InputRuleOutcome outcome)
    {
        return [.. outcome.Violations.Select(violation => Error.Validation(
            INPUT_RULE_CODE,
            violation.Message,
            new Dictionary<string, object>
            {
                [WorkflowVariableValidationService.REPLACEMENT_TAG_METADATA_KEY] = violation.ReplacementTag,
            }))];
    }

    /// <summary>A log row of the run for each rule the submission skipped.</summary>
    public static List<WorkflowExecutionLog> LogRowsOf(InputRuleOutcome outcome, string workflowExecutionId)
    {
        return [.. outcome.Skipped.Select(skipped => new WorkflowExecutionLog
        {
            SectionName = RUN_INPUTS_SECTION_NAME,
            BlockName = skipped.VariableName,
            ReplacementTag = skipped.ReplacementTag,
            Status = BlockExecutionStatus.Skipped,
            Error = VariableInputRuleCheck.LogLineOf(skipped),
            Duration = TimeSpan.Zero,
            WorkflowExecutionId = workflowExecutionId,
        })];
    }
}
