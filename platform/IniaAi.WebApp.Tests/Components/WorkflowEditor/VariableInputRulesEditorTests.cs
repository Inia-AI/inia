using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.InputRules;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using MudPopoverProvider = MudBlazor.MudPopoverProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// The input rules of a variable are edited with picklists in the variable's dialog, and the panel
/// says on screen how values are compared.
/// </summary>
public class VariableInputRulesEditorTests : SignedInRenderTest
{
    private const string INTERIM = "InterimPeriod";

    [Fact]
    public async Task ThePanel_ExplainsHowValuesAreCompared()
    {
        IRenderedComponent<VariableInputRulesEditor> editor = await RenderEditorAsync([]);

        string help = editor.Find("[data-testid=input-rules-comparison-help]").TextContent;
        Assert.Contains("capital letters are ignored, and so are spaces at the start or end", help);
        Assert.Contains("work only for text and dropdown fields", help);
        Assert.Contains("\"Attached\" covers every document on the run", help);
    }

    [Fact]
    public async Task AddRule_StartsARuleOnAttachedDocumentsThatAsksForAValue()
    {
        List<VariableInputRule> rules = [];
        IRenderedComponent<VariableInputRulesEditor> editor = await RenderEditorAsync(rules);

        editor.FindAll("button").Single(button => button.TextContent.Trim() == "Add rule").Click();

        VariableInputRule rule = Assert.Single(rules);
        Assert.Equal(InputRuleConditionOperator.AnyAttached, Assert.Single(rule.Conditions).Operator);
        Assert.Equal(InputRuleRequirementOperator.NotEmpty, rule.Requirement.Operator);
        Assert.NotNull(editor.Find("[data-testid=input-rule-1]"));
    }

    [Fact]
    public async Task ARule_ShowsItsDocumentClassesAndOptionsByTheirNames()
    {
        List<VariableInputRule> rules =
        [
            new VariableInputRule
            {
                Conditions = [new InputRuleCondition { Operator = InputRuleConditionOperator.AnyAttached, DocumentClassKeys = ["QuarterlyReport"] }],
                Requirement = new InputRuleRequirement { Operator = InputRuleRequirementOperator.NotEqualTo, Value = "NA" },
            },
        ];

        IRenderedComponent<VariableInputRulesEditor> editor = await RenderEditorAsync(rules);

        Assert.Contains("Quarterly Report", editor.Markup);
        Assert.Contains("Not an interim", editor.Markup);
        Assert.Contains("Remove condition 1 of rule 1", editor.Markup);
    }

    [Fact]
    public async Task RemoveRule_TakesTheRuleOut()
    {
        List<VariableInputRule> rules =
        [
            new VariableInputRule
            {
                Conditions = [new InputRuleCondition { Operator = InputRuleConditionOperator.IsEmpty, Variable = "CompanyName" }],
                Requirement = new InputRuleRequirement { Operator = InputRuleRequirementOperator.NotEmpty },
            },
        ];
        IRenderedComponent<VariableInputRulesEditor> editor = await RenderEditorAsync(rules);

        editor.Find("button[aria-label='Remove rule 1']").Click();

        Assert.Empty(rules);
    }

    private async Task<IRenderedComponent<VariableInputRulesEditor>> RenderEditorAsync(List<VariableInputRule> rules)
    {
        _ = await SignInAsync("editor@example.com", Roles.ADMIN);
        _ = Render<MudPopoverProvider>();

        var workflow = new Workflow("Interim report");
        _ = new DocumentClass(workflow, "QuarterlyReport") { DisplayName = "Quarterly Report" };
        _ = new WorkflowVariable(workflow, "CompanyName") { DisplayName = "Company Name" };
        var interim = new WorkflowVariable(workflow, INTERIM)
        {
            DisplayName = "Interim Period",
            Type = WorkflowVariableTypes.Dropdown,
            Options = new Dictionary<string, string> { ["NA"] = "Not an interim", ["6"] = "6 months" },
        };

        return Render<VariableInputRulesEditor>(p => p
            .Add(c => c.Rules, rules)
            .Add(c => c.Workflow, workflow)
            .Add(c => c.OwnerTag, INTERIM)
            .Add(c => c.OwnerType, interim.Type)
            .Add(c => c.OwnerOptions, interim.Options));
    }
}
