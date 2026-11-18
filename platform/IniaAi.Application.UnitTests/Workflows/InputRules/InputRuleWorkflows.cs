using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.InputRules;

namespace IniaAi.Application.UnitTests.Workflows.InputRules;

/// <summary>
/// A workflow with an interim period that must not stay at NA when a quarterly report is attached,
/// and must be NA when none is: the example the input rules were built for.
/// </summary>
internal static class InputRuleWorkflows
{
    public const string WORKFLOW_TYPE_ID = "type-input-rules";
    public const string ANNUAL = "AnnualReport";
    public const string QUARTERLY = "QuarterlyReport";
    public const string INTERIM = "InterimPeriod";
    public const string NOT_AN_INTERIM = "NA";
    public const string SIX_MONTHS = "6";

    public const string ATTACHED_BUT_NA =
        "\"Interim Period\" cannot be \"NA\" because \"Quarterly Report\" is attached.";

    public const string NONE_ATTACHED_BUT_SIX =
        "\"Interim Period\" must be \"NA\" because no \"Quarterly Report\" is attached.";

    public static Workflow Interim()
    {
        var workflow = new Workflow("Interim report") { WorkflowTypeId = WORKFLOW_TYPE_ID };
        var section = new Section("Report", workflow);
        _ = new Block(BlockTypes.StringManipulation, section) { Name = "Summary", Properties = new StringManipulationProperties() };
        _ = new DocumentClass(workflow, ANNUAL) { DisplayName = "1. Annual Report" };
        _ = new DocumentClass(workflow, QUARTERLY) { DisplayName = "2. Quarterly Report" };
        _ = new WorkflowVariable(workflow, INTERIM)
        {
            DisplayName = "3. Interim Period",
            Type = WorkflowVariableTypes.Dropdown,
            DefaultValue = NOT_AN_INTERIM,
            Options = new Dictionary<string, string> { [NOT_AN_INTERIM] = NOT_AN_INTERIM, [SIX_MONTHS] = "6 months" },
            InputRules =
            [
                Rule(InputRuleConditionOperator.AnyAttached, InputRuleRequirementOperator.NotEqualTo),
                Rule(InputRuleConditionOperator.NoneAttached, InputRuleRequirementOperator.EqualTo),
            ],
        };
        return workflow;
    }

    private static VariableInputRule Rule(InputRuleConditionOperator attached, InputRuleRequirementOperator requirement)
    {
        return new VariableInputRule
        {
            Conditions = [new InputRuleCondition { Operator = attached, DocumentClassKeys = [QUARTERLY] }],
            Requirement = new InputRuleRequirement { Operator = requirement, Value = NOT_AN_INTERIM },
        };
    }
}
