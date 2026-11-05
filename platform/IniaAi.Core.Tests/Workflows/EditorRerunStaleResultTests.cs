using IniaAi.Core.Services;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// The editor runs blocks one at a time on a workflow that stays open between runs. After its
/// execution states are cleared, a later block must not read the result an earlier run left on
/// a block that has not run again.
/// </summary>
public class EditorRerunStaleResultTests : IClassFixture<WorkflowsFixture<WorkflowHandler>>
{
    private const string PRODUCER_TAG = "producer";
    private const string PRODUCER_ANSWER = "answer from the first run";

    private readonly WorkflowHandler _workflowHandler;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public EditorRerunStaleResultTests(
        WorkflowsFixture<WorkflowHandler> fixture,
        ITestOutputHelper testOutputHelper)
    {
        IServiceProvider serviceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _workflowHandler = serviceProvider.GetRequiredService<WorkflowHandler>();
        IServiceScope scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [Fact]
    public async Task AfterClearingExecutionStates_ALaterBlockReadsNoDataFromABlockThatHasNotRunAgain()
    {
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var producer = new Block(BlockTypes.Merge, section, replacementTag: PRODUCER_TAG)
        {
            Properties = new MergeProperties { TextTemplate = PRODUCER_ANSWER },
        };
        var consumer = new Block(BlockTypes.Merge, section, replacementTag: "consumer")
        {
            Properties = new MergeProperties { TextTemplate = "<answer>{" + PRODUCER_TAG + "}</answer>" },
        };
        var consumerProperties = (MergeProperties)consumer.Properties;

        _ = await RunBlockAsync(section, producer);
        _ = await RunBlockAsync(section, consumer);
        Assert.Equal($"<answer>{PRODUCER_ANSWER}</answer>", consumerProperties.ExecutionState?.Text);

        workflow.ClearExecutionState();
        _ = await RunBlockAsync(section, consumer);

        Assert.Null(producer.BlockResponse);
        Assert.Equal(
            $"<answer>{JsonStringReplacerStrategy.DefaultNoValue}</answer>",
            consumerProperties.ExecutionState?.Text);
    }

    private Task<string?> RunBlockAsync(Section section, Block block)
    {
        return _workflowHandler.ExecuteBlockAsync(
            section,
            block.Order,
            anonymisationMap: [],
            _userDataUnitOfWork,
            inputs: [],
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
