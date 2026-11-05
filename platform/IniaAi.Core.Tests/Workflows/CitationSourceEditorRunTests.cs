using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// The editor keeps one workflow open while its author edits it between runs, so whether a block
/// is a citation source is decided again on every run.
/// </summary>
public class CitationSourceEditorRunTests : IClassFixture<WorkflowsFixture<WorkflowHandler>>
{
    private const string NOTES_TAG = "notes";

    private readonly WorkflowHandler _workflowHandler;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public CitationSourceEditorRunTests(
        WorkflowsFixture<WorkflowHandler> fixture,
        ITestOutputHelper testOutputHelper)
    {
        IServiceProvider serviceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _workflowHandler = serviceProvider.GetRequiredService<WorkflowHandler>();
        IServiceScope scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [Fact]
    public async Task BlockThatStartsToReachTheReport_IsTracedOnTheNextRunAsync()
    {
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var notes = new Block(BlockTypes.Merge, section, replacementTag: NOTES_TAG)
        {
            Properties = new MergeProperties { TextTemplate = "Revenue was $5.0m in the year." },
        };
        var paragraph = new Block(BlockTypes.Merge, section, replacementTag: "summary")
        {
            IsOutputSource = true,
            Properties = new MergeProperties { TextTemplate = "The year was steady." },
        };

        _ = await RunBlockAsync(section, notes);
        Assert.Null(notes.CitationTrace);

        ((MergeProperties)paragraph.Properties).TextTemplate = "Summary: {" + NOTES_TAG + "}";
        _ = await RunBlockAsync(section, notes);

        Assert.NotNull(notes.CitationTrace);
    }

    [Fact]
    public async Task BlockThatStopsReachingTheReport_LosesItsTraceOnTheNextRunAsync()
    {
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var notes = new Block(BlockTypes.Merge, section, replacementTag: NOTES_TAG)
        {
            Properties = new MergeProperties { TextTemplate = "Revenue was $5.0m in the year." },
        };
        var paragraph = new Block(BlockTypes.Merge, section, replacementTag: "summary")
        {
            IsOutputSource = true,
            Properties = new MergeProperties { TextTemplate = "Summary: {" + NOTES_TAG + "}" },
        };

        _ = await RunBlockAsync(section, notes);
        Assert.NotNull(notes.CitationTrace);

        ((MergeProperties)paragraph.Properties).TextTemplate = "The year was steady.";
        _ = await RunBlockAsync(section, notes);

        Assert.Null(notes.CitationTrace);
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
