using System.Text.Json;
using IniaAi.Application.Queries.WorkflowExecutions.GetReRunInputs;
using IniaAi.Application.Workflows.Services;
using IniaAi.Core.Services;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using ReRunInputReason = IniaAi.WebApi.Contracts.Enums.ReRunInputReason;
using IniaAi.WebApi.Contracts.Responses.WorkflowExecutions;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetReRunInputs;

/// <summary>
/// A run's inputs against a template that changed after the run. The plan is checked
/// with the real variable and document class validators a submission uses.
/// </summary>
public class ReRunInputsPlannerTests
{
    private const string ACCOUNT_HOLDER_TAG = "account_holder_type";
    private const string ACCOUNT_HOLDER_NAME = "Account Holder";
    private const string COMPANY_TAG = "CompanyName";
    private const string STATEMENT_CLASS = "BankStatement";

    private readonly WorkflowVariableValidationService _variableValidation = new();
    private readonly ReRunInputsPlanner _planner;

    public ReRunInputsPlannerTests()
    {
        _planner = new ReRunInputsPlanner(
            _variableValidation,
            new DocumentClassValidationService(NullLogger<DocumentClassValidationService>.Instance));
    }

    [Fact]
    public void Plan_WhenTheTemplateGainedARequiredDropdownWithoutADefault_AsksForIt_AndKeepsTheRunsOtherValues()
    {
        Workflow run = WorkflowWithCompanyName();
        Workflow template = WorkflowWithCompanyName();
        AddAccountHolder(template, defaultValue: null);

        ReRunInputsDto plan = _planner.Plan(template, [.. run.Variables], RunValues((COMPANY_TAG, "Acme Ltd")), Documents(STATEMENT_CLASS));

        Assert.False(plan.IsReady);
        ReRunVariableNeedingInputDto needed = Assert.Single(plan.VariablesNeedingInput);
        Assert.Equal(ACCOUNT_HOLDER_TAG, needed.ReplacementTag);
        Assert.Equal(ACCOUNT_HOLDER_NAME, needed.DisplayName);
        Assert.Equal(ReRunInputReason.NoValue, needed.Reason);
        Assert.Equal($"Required variable '{ACCOUNT_HOLDER_NAME}' ({ACCOUNT_HOLDER_TAG}) is missing.", needed.Message);
        Assert.Equal([(COMPANY_TAG, "Acme Ltd")], ValuesOf(plan));
        Assert.Empty(plan.DefaultedVariables);
        Assert.Empty(plan.RemovedVariables);
        Assert.Empty(plan.DocumentClassesNeedingInput);
    }

    [Fact]
    public void Plan_WhenTheNewRequiredVariableHasADefault_SubmitsTheDefault_AndIsReady()
    {
        Workflow run = WorkflowWithCompanyName();
        Workflow template = WorkflowWithCompanyName();
        AddAccountHolder(template, defaultValue: "Company");

        ReRunInputsDto plan = _planner.Plan(template, [.. run.Variables], RunValues((COMPANY_TAG, "Acme Ltd")), Documents(STATEMENT_CLASS));

        Assert.True(plan.IsReady);
        Assert.Empty(plan.VariablesNeedingInput);
        Assert.Equal(ACCOUNT_HOLDER_NAME, Assert.Single(plan.DefaultedVariables).DisplayName);
        Assert.Equal([(ACCOUNT_HOLDER_TAG, "Company"), (COMPANY_TAG, "Acme Ltd")], ValuesOf(plan).OrderBy(value => value.Tag).ToList());
        Assert.Empty(_variableValidation.ValidateVariables(template, SubmittedValues(plan)));
    }

    [Fact]
    public void Plan_WhenTheTemplateDroppedAVariable_DropsItsValue_AndNamesIt()
    {
        Workflow run = WorkflowWithCompanyName();
        _ = new WorkflowVariable(run, "Region") { DisplayName = "Region", IsRequired = false };
        Workflow template = WorkflowWithCompanyName();

        ReRunInputsDto plan = _planner.Plan(
            template,
            [.. run.Variables],
            RunValues((COMPANY_TAG, "Acme Ltd"), ("Region", "EMEA"), ("neverAVariable", "x")),
            []);

        Assert.True(plan.IsReady);
        Assert.Equal([(COMPANY_TAG, "Acme Ltd")], ValuesOf(plan));
        ReRunVariableChangeDto removed = Assert.Single(plan.RemovedVariables);
        Assert.Equal("Region", removed.ReplacementTag);
        Assert.Equal("Region", removed.DisplayName);
    }

    [Fact]
    public void Plan_WhenTheRunsDropdownOptionWasRemoved_AsksForANewValue_AndDoesNotSubmitTheOldOne()
    {
        Workflow run = WorkflowWithCompanyName();
        AddAccountHolder(run, defaultValue: null);
        Workflow template = WorkflowWithCompanyName();
        AddAccountHolder(template, defaultValue: null).Options.Remove("Individual");

        ReRunInputsDto plan = _planner.Plan(
            template,
            [.. run.Variables],
            RunValues((COMPANY_TAG, "Acme Ltd"), (ACCOUNT_HOLDER_TAG, "Individual")),
            []);

        Assert.False(plan.IsReady);
        ReRunVariableNeedingInputDto needed = Assert.Single(plan.VariablesNeedingInput);
        Assert.Equal(ReRunInputReason.InvalidValue, needed.Reason);
        Assert.DoesNotContain(plan.Variables, value => value.ReplacementTag == ACCOUNT_HOLDER_TAG);
    }

    [Fact]
    public void Plan_NeverReturnsAConfidentialValue_AndAsksForItWhenItIsRequired()
    {
        Workflow run = WorkflowWithCompanyName();
        _ = new WorkflowVariable(run, "ApiKey") { DisplayName = "API key", IsRequired = true, IsConfidential = true };
        Workflow template = WorkflowWithCompanyName();
        _ = new WorkflowVariable(template, "ApiKey") { DisplayName = "API key", IsRequired = true, IsConfidential = true };

        ReRunInputsDto plan = _planner.Plan(
            template,
            [.. run.Variables],
            RunValues((COMPANY_TAG, "Acme Ltd"), ("ApiKey", "secret-value")),
            []);

        Assert.False(plan.IsReady);
        Assert.Equal(ReRunInputReason.Confidential, Assert.Single(plan.VariablesNeedingInput).Reason);
        Assert.DoesNotContain(plan.Variables, value => value.Value.ToString() == "secret-value");
    }

    [Fact]
    public void Plan_WhenTheTemplateGainedARequiredDocumentClass_NamesIt()
    {
        Workflow run = WorkflowWithCompanyName();
        Workflow template = WorkflowWithCompanyName();
        _ = new DocumentClass(template, STATEMENT_CLASS) { DisplayName = "Bank statement", Requirement = DocumentClassRequirement.Optional };
        _ = new DocumentClass(template, "IdDocument") { DisplayName = "ID document", Requirement = DocumentClassRequirement.Required };

        ReRunInputsDto plan = _planner.Plan(template, [.. run.Variables], RunValues((COMPANY_TAG, "Acme Ltd")), Documents(STATEMENT_CLASS));

        Assert.False(plan.IsReady);
        Assert.Empty(plan.VariablesNeedingInput);
        ReRunDocumentClassNeedingInputDto needed = Assert.Single(plan.DocumentClassesNeedingInput);
        Assert.Equal("IdDocument", needed.Key);
        Assert.Equal("ID document", needed.DisplayName);
        Assert.Equal("Required document class 'ID document' is missing", needed.Message);
    }

    [Fact]
    public void Plan_MatchesARunValueStoredUnderADifferentCasing_ToTheTemplatesTag()
    {
        Workflow run = WorkflowWithCompanyName();
        Workflow template = WorkflowWithCompanyName();

        ReRunInputsDto plan = _planner.Plan(template, [.. run.Variables], RunValues(("companyName", "Acme Ltd")), []);

        Assert.True(plan.IsReady);
        Assert.Equal([(COMPANY_TAG, "Acme Ltd")], ValuesOf(plan));
        Assert.Empty(plan.RemovedVariables);
    }

    [Fact]
    public void Plan_WhenNothingChanged_IsReady_WithTheRunsValues()
    {
        Workflow run = WorkflowWithCompanyName();
        AddAccountHolder(run, defaultValue: null);
        Workflow template = WorkflowWithCompanyName();
        AddAccountHolder(template, defaultValue: null);

        ReRunInputsDto plan = _planner.Plan(
            template,
            [.. run.Variables],
            RunValues((COMPANY_TAG, "Acme Ltd"), (ACCOUNT_HOLDER_TAG, "Company")),
            []);

        Assert.True(plan.IsReady);
        Assert.Empty(plan.VariablesNeedingInput);
        Assert.Empty(plan.DefaultedVariables);
        Assert.Empty(plan.RemovedVariables);
        Assert.Equal(2, plan.Variables.Count);
    }

    [Fact]
    public void Plan_AsksAgainForAnOptionalConfidentialValueTheRunHad_RatherThanDroppingItSilently()
    {
        Workflow run = WorkflowWithCompanyName();
        _ = new WorkflowVariable(run, "ApiKey") { DisplayName = "API key", IsRequired = false, IsConfidential = true };
        Workflow template = WorkflowWithCompanyName();
        _ = new WorkflowVariable(template, "ApiKey") { DisplayName = "API key", IsRequired = false, IsConfidential = true };

        ReRunInputsDto plan = _planner.Plan(
            template,
            [.. run.Variables],
            RunValues((COMPANY_TAG, "Acme Ltd"), ("ApiKey", "secret-value")),
            []);

        Assert.False(plan.IsReady);
        ReRunVariableNeedingInputDto needed = Assert.Single(plan.VariablesNeedingInput);
        Assert.Equal("ApiKey", needed.ReplacementTag);
        Assert.Equal(ReRunInputReason.Confidential, needed.Reason);
        Assert.Equal([(COMPANY_TAG, "Acme Ltd")], ValuesOf(plan));
    }

    [Fact]
    public void Plan_WhenAnInputDocumentNoLongerExists_NamesIt_AndDoesNotCountItsClass()
    {
        Workflow run = WorkflowWithCompanyName();
        Workflow template = WorkflowWithCompanyName();
        _ = new DocumentClass(template, STATEMENT_CLASS) { DisplayName = "Bank statement", Requirement = DocumentClassRequirement.Required };

        ReRunInputsDto plan = _planner.Plan(
            template,
            [.. run.Variables],
            RunValues((COMPANY_TAG, "Acme Ltd")),
            [new RunInputDocument("deleted-statement", STATEMENT_CLASS, "statement.pdf", IsAvailable: false)]);

        Assert.False(plan.IsReady);
        ReRunUnavailableDocumentDto unavailable = Assert.Single(plan.UnavailableDocuments);
        Assert.Equal("deleted-statement", unavailable.DocumentId);
        Assert.Equal("statement.pdf", unavailable.FileName);
        Assert.Equal(STATEMENT_CLASS, Assert.Single(plan.DocumentClassesNeedingInput).Key);
    }

    private static Workflow WorkflowWithCompanyName()
    {
        var workflow = new Workflow { WorkflowTypeId = "bank-account-analysis" };
        _ = new WorkflowVariable(workflow, COMPANY_TAG) { DisplayName = "Company name", IsRequired = true };
        return workflow;
    }

    private static WorkflowVariable AddAccountHolder(Workflow workflow, string? defaultValue)
    {
        return new WorkflowVariable(workflow, ACCOUNT_HOLDER_TAG)
        {
            DisplayName = ACCOUNT_HOLDER_NAME,
            Type = WorkflowVariableTypes.Dropdown,
            IsRequired = true,
            DefaultValue = defaultValue,
            Options = new Dictionary<string, string> { ["Individual"] = "Individual", ["Company"] = "Company" },
        };
    }

    private static List<RunInputDocument> Documents(params string[] classes)
    {
        return [.. classes.Select((documentClass, index) => new RunInputDocument($"document-{index}", documentClass, $"document-{index}.pdf", IsAvailable: true))];
    }

    private static Dictionary<string, object> RunValues(params (string Tag, string Value)[] values)
    {
        return values.ToDictionary(value => value.Tag, value => (object)value.Value);
    }

    private static List<(string Tag, string? Value)> ValuesOf(ReRunInputsDto plan)
    {
        return [.. plan.Variables.Select(value => (value.ReplacementTag, value.Value.GetString()))];
    }

    private static Dictionary<string, JsonElement> SubmittedValues(ReRunInputsDto plan)
    {
        return plan.Variables.ToDictionary(value => value.ReplacementTag, value => value.Value);
    }
}
