using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.BlockExtensions;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Samples.WordCount;
using System.Reflection;
using System.Runtime.Loader;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;
using static IniaAi.Core.Tests.Workflows.Scheduling.Helpers.VerifiedGroupRuns;

namespace IniaAi.Core.Tests.Workflows.BlockExtensions;

/// <summary>
/// A block type from an assembly outside the platform loads through the extension setting, runs
/// in a real workflow and reads its own service from the block's scope. A block of a type no
/// registration knows fails with a message that says why and what to do.
/// </summary>
public class BlockExtensionTests : IClassFixture<WorkflowsFixture<BlockExtensionTests>>
{
    private const string ID_PREFIX = "bext";
    private const BlockTypes TYPE_NO_REGISTRATION_KNOWS = (BlockTypes)4242;

    private readonly IServiceProvider _sp;

    public BlockExtensionTests(WorkflowsFixture<BlockExtensionTests> fixture, ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(
            testOutputHelper,
            services => services.AddBlockExtensions([typeof(WordCountExtension).Assembly.Location]));
    }

    [Fact]
    public async Task ABlockTypeFromItsOwnAssembly_RunsInAWorkflow()
    {
        Workflow root = new() { Id = $"{ID_PREFIX}-root", Title = ID_PREFIX };
        Section section = new("S", root) { Id = $"{ID_PREFIX}-sec", Order = 0 };
        _ = new Block(BlockTypes.Merge, section, replacementTag: "bext_text")
        {
            Id = $"{ID_PREFIX}-text",
            Name = "Text",
            Properties = new MergeProperties { TextTemplate = "one two three" },
        };
        _ = new Block(WordCountProperties.TYPE, section, replacementTag: "bext_count")
        {
            Id = $"{ID_PREFIX}-count",
            Name = "Count",
            Properties = new WordCountProperties { TextTemplate = "{bext_text} four" },
        };
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        WorkflowExecutionLog log = Assert.Single(ReadLogs(_sp, handler.WorkflowExecutionId!, "Count"));
        Assert.Equal(BlockExecutionStatus.Completed, log.Status);
        Assert.Equal("4", log.Output);
    }

    [Fact]
    public async Task ALoopLeftUnexpanded_DoesNotSendTheReaderToAnExtension()
    {
        Workflow root = new() { Id = $"{ID_PREFIX}-loop-root", Title = ID_PREFIX };
        Section section = new("S", root) { Id = $"{ID_PREFIX}-loop-sec", Order = 0 };
        _ = new Block(BlockTypes.LoopStart, section, replacementTag: "bext_loop_start")
        {
            Id = $"{ID_PREFIX}-loop-start",
            Name = "Loop",
            Properties = new LoopStartProperties(),
        };
        _ = new Block(BlockTypes.Merge, section, replacementTag: "bext_loop_body")
        {
            Id = $"{ID_PREFIX}-loop-body",
            Name = "Body",
            Properties = new MergeProperties { TextTemplate = "body" },
        };
        _ = new Block(BlockTypes.LoopEnd, section, replacementTag: "bext_loop_end") { Id = $"{ID_PREFIX}-loop-end", Name = "Loop end" };
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        WorkflowExecutionLog log = Assert.Single(ReadLogs(_sp, handler.WorkflowExecutionId!, "Loop"));
        Assert.Equal(BlockExecutionStatus.Failed, log.Status);
        Assert.Equal("No strategy found for block type: LoopStart", log.Error);
    }

    [Fact]
    public async Task ABlockOfATypeNoRegistrationKnows_FailsWithTheReason()
    {
        Workflow root = TestWorkflowRuns.BuildSingleBlockWorkflow(ID_PREFIX + "u", BlockTypes.Merge, new MergeProperties());
        Block unknown = root.Sections.Single().Blocks.Single();
        unknown.Type = TYPE_NO_REGISTRATION_KNOWS;
        unknown.Properties = new UnavailableBlockProperties(TYPE_NO_REGISTRATION_KNOWS, """{"Type":4242}""");
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        WorkflowExecutionLog log = Assert.Single(ReadLogs(_sp, handler.WorkflowExecutionId!, "B0"));
        Assert.Equal(BlockExecutionStatus.Failed, log.Status);
        Assert.Equal(UnavailableBlockTypes.RunMessage(TYPE_NO_REGISTRATION_KNOWS), log.Error);
    }

    [Fact]
    public void AnExtensionBuiltAgainstAnotherVersionOfThePlatform_BindsToTheLoadedPlatform()
    {
        Assembly domain = typeof(Block).Assembly;
        AssemblyName reference = new(domain.GetName().Name!) { Version = new Version(99, 0, 0, 0) };

        Assert.Same(domain, BlockTypeServiceCollectionExtensions.LoadedAssembly(AssemblyLoadContext.Default, reference));
    }

    [Fact]
    public void AnAssemblyTheHostHasNotLoaded_IsLeftToTheExtensionFolder()
    {
        Assert.Null(BlockTypeServiceCollectionExtensions.LoadedAssembly(AssemblyLoadContext.Default, new AssemblyName("NoHostLoadsThis.Extension.Dependency")));
    }
}
