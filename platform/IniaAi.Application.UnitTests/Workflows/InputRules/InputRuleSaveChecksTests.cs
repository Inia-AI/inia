using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.InputRules;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Workflows.InputRules;

/// <summary>
/// A save keeps a rule that names something the workflow no longer has and warns about it; it
/// refuses a rule that cannot be evaluated at all.
/// </summary>
public sealed class InputRuleSaveChecksTests
{
    private readonly WorkflowTemplateStructuralValidator _validator = new();

    public static TheoryData<string, string> StaleRules()
    {
        return new TheoryData<string, string>
        {
            { "MissingDocumentClass", "A rule on \"Interim Period\" will not be checked because it names the document class \"QuarterlyOld\", which this workflow does not have. Fix the rule or remove it." },
            { "MissingVariable", "A rule on \"Interim Period\" will not be checked because it names the field \"OldField\", which this workflow does not have. Fix the rule or remove it." },
            { "NotAnOption", "A rule on \"Interim Period\" will not be checked because \"12\" is not an option of \"Interim Period\". Fix the rule or remove it." },
            { "NotComparable", "A rule on \"Interim Period\" will not be checked because \"Employees\" is not a text or dropdown field, so it cannot be compared with a value. Fix the rule or remove it." },
        };
    }

    [Theory]
    [MemberData(nameof(StaleRules))]
    public void AStaleRule_SavesWithAWarningThatNamesIt(string kind, string warning)
    {
        Workflow workflow = InputRuleWorkflows.Interim();
        _ = new WorkflowVariable(workflow, "Employees") { DisplayName = "Employees", Type = WorkflowVariableTypes.Number };
        VariableInputRule rule = workflow.Variables.First().InputRules[0];
        switch (Enum.Parse<InputRuleProblem>(kind))
        {
            case InputRuleProblem.MissingDocumentClass:
                rule.Conditions[0].DocumentClassKeys = ["QuarterlyOld"];
                break;
            case InputRuleProblem.MissingVariable:
                rule.Conditions = [new InputRuleCondition { Operator = InputRuleConditionOperator.IsEmpty, Variable = "OldField" }];
                break;
            case InputRuleProblem.NotAnOption:
                rule.Requirement.Value = "12";
                break;
            default:
                rule.Conditions = [new InputRuleCondition { Operator = InputRuleConditionOperator.EqualTo, Variable = "Employees", Value = "10" }];
                break;
        }

        WorkflowStructuralValidationResult result = _validator.Validate(JsonOf(workflow));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.Description)));
        WorkflowStructuralValidationWarning saved = Assert.Single(result.Warnings);
        Assert.Equal("Variables[InterimPeriod].InputRules", saved.Path);
        Assert.Equal(warning, saved.Message);
    }

    [Fact]
    public void ARuleWithoutACondition_IsRefused()
    {
        Workflow workflow = InputRuleWorkflows.Interim();
        workflow.Variables.First().InputRules[1].Conditions = [];

        WorkflowStructuralValidationResult result = _validator.Validate(JsonOf(workflow));

        Error error = Assert.Single(result.Errors);
        Assert.Equal("Variables[InterimPeriod].InputRules[1]", error.Code);
        Assert.Equal("A rule on \"Interim Period\" cannot be saved because it has no condition. Complete the rule or remove it.", error.Description);
        Assert.True(WorkflowTemplateStructuralValidator.IsInputRuleError(error));
    }

    [Theory]
    [InlineData("{\"Conditions\":null,\"Requirement\":null}")]
    [InlineData("{\"Conditions\":[null],\"Requirement\":{\"Operator\":\"NotEmpty\"}}")]
    [InlineData("{\"Conditions\":[{\"Operator\":\"IsEmpty\",\"Variable\":\"InterimPeriod\"}],\"Requirement\":{\"Operator\":7}}")]
    public void AMalformedRule_IsRefusedRatherThanFailingTheSave(string ruleJson)
    {
        JObject workflow = JObject.Parse(JsonOf(InputRuleWorkflows.Interim()));
        workflow["Variables"]![0]!["InputRules"] = new JArray(JToken.Parse(ruleJson), JValue.CreateNull());

        WorkflowStructuralValidationResult result = _validator.Validate(workflow.ToString());

        Assert.Equal("Variables[InterimPeriod].InputRules[0]", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void SoundRules_SaveWithoutAWarning()
    {
        WorkflowStructuralValidationResult result = _validator.Validate(JsonOf(InputRuleWorkflows.Interim()));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    private static string JsonOf(Workflow workflow)
    {
        return new WorkflowCanonicalizer().ToCanonicalJson(workflow);
    }
}
