using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// An AI block the model refuses fails as refused, names the category where the run lists failed
/// blocks, still records the tokens it spent, and hands its dependants no data, never the
/// refusal text.
/// </summary>
public sealed class ModelRefusalBlockTests : IClassFixture<WorkflowsFixture<ModelRefusalBlockTests>>, IDisposable
{
    private const string ORGANIZATION_ID = "refusal-org";
    private const string REFUSED_PROMPT = "Extract the cash balance. Give your reasoning in <reasoning> tags.";
    private const string ANSWERED_PROMPT = "Extract the revenue.";
    private const string REFUSED_TAG = "cash";
    private const string ANSWERED_TAG = "revenue";
    private const string WRITTEN_REFUSAL = "I'm not able to extract the fields from this card. It is an identity document about a private individual.";

    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly IUserDataUnitOfWork _userData;
    private readonly WorkflowHandler _workflowHandler;
    private readonly TestTokenUsageService _tokenUsage;

    public ModelRefusalBlockTests(WorkflowsFixture<ModelRefusalBlockTests> fixture, ITestOutputHelper output)
    {
        var model = new ScriptedChatConnector(
            call => call.Text.Contains("card", StringComparison.Ordinal) ? WRITTEN_REFUSAL : "<answer>1,200</answer>",
            call => call.Text.Contains("<reasoning>", StringComparison.Ordinal)
                ? new ModelRefusal("reasoning_extraction", null, "msg_refused", "us.anthropic.claude-sonnet-5-5")
                : null);

        _serviceProvider = fixture.BuildTestServiceProvider(
            output,
            services => services.AddSingleton<IChatConnector>(model));
        _workflowHandler = _serviceProvider.GetRequiredService<WorkflowHandler>();
        _scope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userData = _scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _tokenUsage = _serviceProvider.GetRequiredService<TestTokenUsageService>();
    }

    [Fact]
    public async Task ARefusedBlock_FailsWithItsCategory_OnTheRunsLogRow()
    {
        (Workflow workflow, Block template, _, _) = BuildWorkflow(REFUSED_PROMPT);

        Block refused = (await RunAsync(workflow))[template.Name];

        Assert.Equal(ExecutionStatus.Failed, refused.ExecutionState?.Status);
        Assert.Contains("refusal category: reasoning_extraction", refused.ExecutionState?.Error);

        WorkflowExecutionLog log = Assert.Single(await LogsAsync(), row => row.BlockName == refused.Name);
        Assert.Equal(BlockExecutionStatus.Failed, log.Status);
        Assert.Equal("reasoning_extraction", log.RefusalCategory);
        Assert.Contains("so the block has no output", log.Error);
    }

    [Fact]
    public async Task ARefusedBlock_HandsItsDependantsNoData_NotTheRefusal()
    {
        (Workflow workflow, _, Block template, _) = BuildWorkflow(REFUSED_PROMPT);

        Block dependant = (await RunAsync(workflow))[template.Name];

        string? merged = dependant.BlockResponse?.RawResponse;
        Assert.NotNull(merged);
        Assert.DoesNotContain("refus", merged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(IChatConnector.ERROR_PREFIX, merged);
        string noData = _serviceProvider.GetRequiredService<IOptions<PlatformSettings>>().Value.IniaAi.NoDataForPlaceHolderText;
        Assert.Contains($"Cash: {noData}", merged);
        Assert.Contains("1,200", merged);
    }

    [Fact]
    public async Task ARefusedBlock_StillRecordsTheTokensItSpent()
    {
        (Workflow workflow, Block template, _, _) = BuildWorkflow(REFUSED_PROMPT);

        Block refused = (await RunAsync(workflow))[template.Name];

        LlmUsageRecord record = Assert.Single(_tokenUsage.LlmRecords, usage => usage.BlockId == refused.Id);
        Assert.Equal(UsageType.LlmBlockExecution, record.UsageType);
        Assert.Contains(record.Components, component => component.ComponentType == PricingComponentType.Input && component.Units > 0);
    }

    [Fact]
    public async Task AWrittenRefusal_FailsTheBlockAsRefused()
    {
        (Workflow workflow, Block refusedTemplate, Block dependantTemplate, _) = BuildWorkflow("Extract the holder's name from this card.");

        Dictionary<string, Block> blocks = await RunAsync(workflow);
        Block refused = blocks[refusedTemplate.Name];
        Block dependant = blocks[dependantTemplate.Name];

        Assert.Equal(ExecutionStatus.Failed, refused.ExecutionState?.Status);
        WorkflowExecutionLog log = Assert.Single(await LogsAsync(), row => row.BlockName == refused.Name);
        Assert.Equal(ModelRefusal.WRITTEN_CATEGORY, log.RefusalCategory);
        Assert.DoesNotContain("identity document", dependant.BlockResponse?.RawResponse);
    }

    [Fact]
    public async Task AnAnsweredBlock_HasNoRefusalOnItsRow()
    {
        (Workflow workflow, _, _, Block template) = BuildWorkflow(REFUSED_PROMPT);

        Block answered = (await RunAsync(workflow))[template.Name];

        Assert.Equal(ExecutionStatus.Completed, answered.ExecutionState?.Status);
        WorkflowExecutionLog log = Assert.Single(await LogsAsync(), row => row.BlockName == answered.Name);
        Assert.Null(log.RefusalCategory);
        Assert.Equal(BlockExecutionStatus.Completed, log.Status);
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    private static (Workflow Workflow, Block Refused, Block Dependant, Block Answered) BuildWorkflow(string refusedPrompt)
    {
        var workflow = new Workflow();
        var extraction = new Section("Extraction", workflow);
        var refused = new Block(BlockTypes.AiQuery, extraction)
        {
            Name = "Extract_Cash",
            ReplacementTag = REFUSED_TAG,
            Properties = new AiQueryProperties { UserPromptTemplate = refusedPrompt, MaxTokens = 100 },
        };
        var answered = new Block(BlockTypes.AiQuery, extraction)
        {
            Name = "PL_Revenue",
            ReplacementTag = ANSWERED_TAG,
            Properties = new AiQueryProperties { UserPromptTemplate = ANSWERED_PROMPT, MaxTokens = 100 },
        };

        var assembly = new Section("Assemble", workflow);
        var dependant = new Block(BlockTypes.Merge, assembly)
        {
            Name = "Assemble",
            ReplacementTag = "assembled",
            Properties = new MergeProperties { TextTemplate = $"Cash: {{{REFUSED_TAG}}}\nRevenue: {{{ANSWERED_TAG}}}" },
        };

        return (workflow, refused, dependant, answered);
    }

    /// <summary>Runs the workflow and returns the blocks as they ran, by name.</summary>
    private async Task<Dictionary<string, Block>> RunAsync(Workflow workflow)
    {
        string userId = Guid.NewGuid().ToString();
        await _workflowHandler.CreateRootOutputAsync(_userData, null, workflow, ORGANIZATION_ID, userId);
        Workflow executed = await _workflowHandler.ExecuteWorkflowAsync(
            workflow, [], ORGANIZATION_ID, userId, _userData, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        return executed.Sections.SelectMany(static section => section.Blocks).ToDictionary(static block => block.Name);
    }

    private async Task<List<WorkflowExecutionLog>> LogsAsync()
    {
        string executionId = _workflowHandler.WorkflowExecutionId
            ?? throw new InvalidOperationException("The run has no execution.");

        return [.. await _userData.Repository<WorkflowExecutionLog>()
            .FindAsync(log => log.WorkflowExecutionId == executionId)];
    }
}
