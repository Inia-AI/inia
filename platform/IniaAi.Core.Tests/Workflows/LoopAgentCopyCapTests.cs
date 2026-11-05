using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A loop's list is known only at run time and every Agent copy buys its own run, so a loop that
/// would make more copies than the setting allows fails before any of them starts.
/// </summary>
public class LoopAgentCopyCapTests : IClassFixture<WorkflowsFixture<LoopAgentCopyCapTests>>
{
    private readonly WorkflowsFixture<LoopAgentCopyCapTests> _fixture;
    private readonly IServiceProvider _sp;

    public LoopAgentCopyCapTests(WorkflowsFixture<LoopAgentCopyCapTests> fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    private static Workflow BuildAgentLoop(string items)
    {
        Workflow root = new() { Id = $"agent-loop-{Guid.NewGuid():N}", Title = "agent loop" };
        Section section = new("Per item", root) { Title = "Per item", Order = 0 };
        _ = new Block(BlockTypes.LoopStart, section)
        {
            Properties = new LoopStartProperties { ListTitle = "items", LoopedListTemplate = items },
        };
        var agent = new Block(BlockTypes.Agent, section, replacementTag: "echo");
        ((AgentProperties)agent.Properties).WorkerSkills =
        [
            new AgentWorkerSkill { Name = "echo", Description = "Echoes the item.", Content = "Item: {{items}}" },
        ];
        _ = new Block(BlockTypes.LoopEnd, section);
        return root;
    }

    private async Task<Exception?> RunAsync(Workflow workflow, int cap)
    {
        _sp.GetRequiredService<IOptions<PlatformSettings>>().Value.AgentRuntime.MaxLoopAgentCopies = cap;
        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        try
        {
            _ = await TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1).ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: new(),
                organizationId: _fixture.GetOrganization().Id,
                jobId: Guid.NewGuid().ToString(),
                uow,
                inputs: [],
                cancellationToken: TestContext.Current.CancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception;
        }
    }

    [Fact]
    public async Task ALoopOverTheCap_FailsBeforeAnyAgentRunStarts()
    {
        Workflow workflow = BuildAgentLoop("alpha\nbravo\ncharlie");

        Exception? failure = await RunAsync(workflow, cap: 2);

        Assert.NotNull(failure);
        Assert.Contains(nameof(AgentRuntimeSettings.MaxLoopAgentCopies), failure.Message);
        Assert.Contains("would start 3 agent runs", failure.Message);
        Assert.All(
            workflow.GetAllBlocks().Where(block => block.Type == BlockTypes.Agent),
            block => Assert.Null(((AgentProperties)block.Properties).ExecutionState?.RunPasses));
    }

    [Fact]
    public async Task ALoopAtTheCap_IsNotRefusedByIt()
    {
        Exception? failure = await RunAsync(BuildAgentLoop("alpha\nbravo"), cap: 2);

        Assert.DoesNotContain(nameof(AgentRuntimeSettings.MaxLoopAgentCopies), failure?.Message ?? string.Empty);
    }
}
