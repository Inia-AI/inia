using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.InputRules;

namespace IniaAi.Domain.Tests.InputRules;

/// <summary>
/// A rule keeps pointing at what it names when a document class key, a variable's tag or a
/// dropdown option value is renamed, and a copied workflow gets its own copy of every rule.
/// </summary>
public sealed class VariableInputRuleReferenceTests
{
    [Fact]
    public void RenamingADocumentClassKey_RenamesItInTheRules()
    {
        (Workflow workflow, DocumentClass quarterly, WorkflowVariable owner) = Build();

        workflow.RenameDocumentClassKey(quarterly, "QuarterlyReportV2");

        Assert.Equal(["QuarterlyReportV2", "Annual"], owner.InputRules[0].Conditions[0].DocumentClassKeys);
    }

    [Fact]
    public void RenamingAVariableTagAndSwappingOptions_RenamesThemInTheRules()
    {
        (_, _, WorkflowVariable owner) = Build();

        owner.RenameInputRuleVariable("hasparent", "HasParentCompany");
        owner.RenameInputRuleOptions("HasParentCompany", new Dictionary<string, string> { ["Y"] = "N", ["N"] = "Y" });

        InputRuleCondition condition = owner.InputRules[0].Conditions[1];
        Assert.Equal("HasParentCompany", condition.Variable);
        Assert.Equal("N", condition.Value);
    }

    [Fact]
    public void CopyingAWorkflow_CopiesTheRulesRatherThanSharingThem()
    {
        (Workflow workflow, _, WorkflowVariable owner) = Build();
        var copy = new Workflow("Copy");

        WorkflowVariable copied = owner.CloneToWorkflow(copy);
        copied.InputRules[0].Requirement.Value = "Changed";

        Assert.Equal("NA", owner.InputRules[0].Requirement.Value);
        Assert.Single(workflow.Variables, variable => variable.ReplacementTag == owner.ReplacementTag);
    }

    private static (Workflow Workflow, DocumentClass Quarterly, WorkflowVariable Owner) Build()
    {
        var workflow = new Workflow("Interim report");
        var quarterly = new DocumentClass(workflow, "QuarterlyReport");
        _ = new DocumentClass(workflow, "Annual");
        var owner = new WorkflowVariable(workflow, "InterimPeriod")
        {
            Type = WorkflowVariableTypes.Dropdown,
            Options = new Dictionary<string, string> { ["NA"] = "NA", ["6"] = "6 months" },
            InputRules =
            [
                new VariableInputRule
                {
                    Conditions =
                    [
                        new InputRuleCondition { Operator = InputRuleConditionOperator.AnyAttached, DocumentClassKeys = ["QuarterlyReport", "Annual"] },
                        new InputRuleCondition { Operator = InputRuleConditionOperator.EqualTo, Variable = "HasParent", Value = "Y" },
                    ],
                    Requirement = new InputRuleRequirement { Operator = InputRuleRequirementOperator.NotEqualTo, Value = "NA" },
                },
            ],
        };
        return (workflow, quarterly, owner);
    }
}
