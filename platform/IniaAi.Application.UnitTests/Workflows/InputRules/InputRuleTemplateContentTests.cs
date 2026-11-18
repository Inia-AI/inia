using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;
using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.RunLimits;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Common.Interfaces.GitHub;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Models.Dto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Workflows.InputRules;

/// <summary>
/// Input rules round-trip through the template content API unchanged, and the template summary
/// that the run form reads carries them.
/// </summary>
public sealed class InputRuleTemplateContentTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-input-rules";
    private const string USER_ID = "user-input-rules";

    private readonly SqliteCoreDatabase _core = new();

    public void Dispose()
    {
        _core.Dispose();
    }

    [Fact]
    public async Task RulesSavedThroughTheContentApi_ReadBackUnchanged_AndReachTheSummary()
    {
        Workflow workflow = InputRuleWorkflows.Interim();
        _ = new WorkflowVariable(workflow, "CompanyName") { DisplayName = "Company Name" };
        string saved = new WorkflowCanonicalizer().ToCanonicalJson(workflow);

        ErrorOr<SaveWorkflowTemplateContentResult> save = await SaveHandler().Handle(
            new SaveWorkflowTemplateContentCommand(ORGANIZATION_ID, USER_ID, InputRuleWorkflows.WORKFLOW_TYPE_ID, saved, BaseVersionId: null, NewTypeName: "Interim report"),
            TestContext.Current.CancellationToken);
        Assert.False(save.IsError, string.Join("; ", save.ErrorsOrEmptyList.Select(error => error.Description)));
        Assert.Empty(save.Value.Warnings);
        _core.Context.ChangeTracker.Clear();

        ErrorOr<WorkflowTemplateContentResult> read = await new GetWorkflowTemplateContentQueryHandler(_core.UnitOfWork, new WorkflowCanonicalizer())
            .Handle(new GetWorkflowTemplateContentQuery(ORGANIZATION_ID, USER_ID, InputRuleWorkflows.WORKFLOW_TYPE_ID), TestContext.Current.CancellationToken);

        Assert.True(JToken.DeepEquals(RulesOf(saved), RulesOf(read.Value.WorkflowJson)));
        Assert.Null(VariableOf(read.Value.WorkflowJson, "CompanyName")["InputRules"]);

        WorkflowDto summary = (await new GetWorkflowSummaryByTypeQueryHandler(
                _core.UnitOfWork,
                new WorkflowTypeAccess(_core.UnitOfWork, new AccessRowsDecideReach()),
                new NoCreditCost(),
                new NoPageLimit())
            .Handle(new GetWorkflowSummaryByTypeQuery(ORGANIZATION_ID, InputRuleWorkflows.WORKFLOW_TYPE_ID), TestContext.Current.CancellationToken)).Value;

        WorkflowVariableDto interim = summary.Variables.Single(variable => variable.ReplacementTag == InputRuleWorkflows.INTERIM);
        Assert.Collection(
            interim.InputRules,
            rule =>
            {
                InputRuleConditionDto condition = Assert.Single(rule.Conditions);
                Assert.Equal("AnyAttached", condition.Operator);
                Assert.Equal([InputRuleWorkflows.QUARTERLY], condition.DocumentClassKeys);
                Assert.Equal("NotEqualTo", rule.Requirement.Operator);
                Assert.Equal(InputRuleWorkflows.NOT_AN_INTERIM, rule.Requirement.Value);
            },
            rule =>
            {
                Assert.Equal("NoneAttached", Assert.Single(rule.Conditions).Operator);
                Assert.Equal("EqualTo", rule.Requirement.Operator);
            });
        Assert.Empty(summary.Variables.Single(variable => variable.ReplacementTag == "CompanyName").InputRules);
    }

    private SaveWorkflowTemplateContentCommandHandler SaveHandler()
    {
        ISender sender = SaveWorkflowSender.Create(
            new WorkflowSavePipeline(
                _core.UnitOfWork,
                _core.Context,
                new WorkflowCanonicalizer(),
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(new AgentRuntimeSettings()),
                NullLogger<WorkflowSavePipeline>.Instance,
                SuperAdminLookups.OverEmptyUserData()),
            _core.Context);

        IGitHubWorkflowVersioningService gitHub = Substitute.For<IGitHubWorkflowVersioningService>();
        return new SaveWorkflowTemplateContentCommandHandler(
            _core.UnitOfWork,
            new WorkflowTemplateStructuralValidator(),
            new WorkflowTemplateSavePreflight(_core.UnitOfWork),
            sender,
            gitHub,
            new ServiceCollection().Configure<GitHubSettings>(_ => { }).BuildServiceProvider().GetRequiredService<IOptionsMonitor<GitHubSettings>>(),
            NullLogger<SaveWorkflowTemplateContentCommandHandler>.Instance);
    }

    private static JToken RulesOf(string workflowJson)
    {
        return VariableOf(workflowJson, InputRuleWorkflows.INTERIM)["InputRules"]!;
    }

    private static JObject VariableOf(string workflowJson, string tag)
    {
        return (JObject)JObject.Parse(workflowJson)["Variables"]!.Single(variable => (string)variable["ReplacementTag"]! == tag);
    }
}
