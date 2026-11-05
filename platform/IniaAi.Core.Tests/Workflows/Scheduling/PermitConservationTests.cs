using System.Collections.Concurrent;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Permit accounting end to end through the real handler: the critical-block abort conservation
/// case, the end-to-end upper bound that <c>ParallelExecutionTests</c> does not carry, and the two
/// editor entry points whose capping must not change.
/// </summary>
public class PermitConservationTests : IClassFixture<WorkflowsFixture<PermitConservationTests>>
{
    private const string ID_PREFIX = "permit";
    private const string PARAM_SECTION_ID = $"{ID_PREFIX}-param-sec";
    private const string CRITICAL_BLOCK = "P1";
    private const string BLOCK_FAILURE = "agent start refused";

    private readonly IServiceProvider _sp;

    public PermitConservationTests(
        WorkflowsFixture<PermitConservationTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task CriticalBlockAbort_ReleasesEveryPermit()
    {
        const int CAP = 4;
        TestCap cap = new(CAP);
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(
            _sp,
            writer,
            TestSchedulers.NewSchedulerWithCap(cap),
            onBlockExecuted: block => block.Name == CRITICAL_BLOCK
                ? throw new InvalidOperationException(BLOCK_FAILURE)
                : Task.CompletedTask);

        Workflow workflow = TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, 4, CRITICAL_BLOCK);

        _ = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.True(cap.Acquired > 0, "the run took no permit at all, so conservation proves nothing.");
        Assert.Equal(cap.Acquired, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
        Assert.True(
            cap.MaxConcurrentlyHeld <= CAP,
            $"the abort path exceeded the cap: {cap.MaxConcurrentlyHeld} held at once.");
    }

    /// <summary>
    /// The end-to-end upper bound. <c>ParallelExecutionTests</c> runs exactly four blocks at cap
    /// four and asserts only <c>MaxObserved &gt;= 2</c>, which an entirely uncapped run also
    /// satisfies, so nothing there catches a bracket that raises effective concurrency.
    /// </summary>
    [Fact]
    public async Task SixIndependentBlocks_AtCapTwo_NeverExceedTwoPermits()
    {
        const int CAP = 2;
        const int BLOCK_COUNT = 6;
        TestCap cap = new(CAP);
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(
            _sp,
            writer,
            TestSchedulers.NewSchedulerWithCap(cap),
            onBlockExecuted: _ => SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));

        await TestWorkflowRuns.RunWithOutputAsync(
            _sp,
            handler,
            TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, BLOCK_COUNT),
            TestContext.Current.CancellationToken);

        Assert.Equal(BLOCK_COUNT, cap.Acquired);
        Assert.Equal(CAP, cap.MaxConcurrentlyHeld);
        Assert.Equal(0, cap.CurrentlyHeld);
    }

    /// <summary>
    /// Outcomes 6 and 7 in one test. The scheduler never sees a parameterized section - the engine
    /// pre-expands them before the segment split, and a loop segment expands before dispatch - so
    /// the K-variant recursion in the public <c>ExecuteBlockAsync</c> is reachable only from the
    /// editor's direct block run, which takes no permit at all. Zero permits is the strongest form
    /// of "at most one".
    /// </summary>
    [Fact]
    public async Task EditorSingleBlockRun_OfAParameterizedSection_TakesNoPermit()
    {
        const int VARIANT_COUNT = 3;
        TestCap cap = new(2);
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(
            _sp, TestSchedulers.NewSchedulerWithCap(cap));

        Workflow workflow = BuildParameterizedWorkflow(VARIANT_COUNT);
        Section section = workflow.Sections.First(s => s.Id == PARAM_SECTION_ID);

        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        string? skipReason = await handler.ExecuteBlockAsync(
            section,
            0,
            new ConcurrentDictionary<string, string>(),
            unitOfWork,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(skipReason);
        Assert.Equal(VARIANT_COUNT, workflow.Sections.Count);
        Assert.Equal(0, cap.Acquired);
        Assert.Equal(0, cap.Released);
    }

    [Fact]
    public async Task ParameterizedSection_ExpandedByTheEngine_TakesOnePermitPerBlock()
    {
        const int VARIANT_COUNT = 3;
        TestCap cap = new(4);
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(
            _sp, writer, TestSchedulers.NewSchedulerWithCap(cap));

        await TestWorkflowRuns.RunWithOutputAsync(
            _sp,
            handler,
            BuildParameterizedWorkflow(VARIANT_COUNT),
            TestContext.Current.CancellationToken);

        Assert.Equal(VARIANT_COUNT, cap.Acquired);
        Assert.Equal(cap.Acquired, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
    }

    [Fact]
    public async Task EditorSectionRun_IsStillCapped()
    {
        const int BLOCK_COUNT = 3;
        TestCap cap = new(1);
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(
            _sp, TestSchedulers.NewSchedulerWithCap(cap));

        Workflow workflow = TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, BLOCK_COUNT);

        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        await handler.ExecuteSectionAsync(
            workflow.Sections.First(),
            new ConcurrentDictionary<string, string>(),
            unitOfWork,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(BLOCK_COUNT, cap.Acquired);
        Assert.Equal(cap.Acquired, cap.Released);
        Assert.Equal(1, cap.MaxConcurrentlyHeld);
    }

    private static Workflow BuildParameterizedWorkflow(int variantCount)
    {
        Workflow root = new() { Id = $"{ID_PREFIX}-param-root", Title = $"{ID_PREFIX}-param" };
        Section section = new("S", root) { Id = PARAM_SECTION_ID, Order = 0 };

        _ = new Block(BlockTypes.Merge, section, replacementTag: $"{ID_PREFIX}param_0")
        {
            Id = $"{ID_PREFIX}-param-blk",
            Name = "V0",
            IsOutputSource = true,
            Properties = new MergeProperties { TextTemplate = "variant body" },
        };

        List<SectionVariant> variants = [.. Enumerable.Range(0, variantCount)
            .Select(i => new SectionVariant(Key: $"V{i}", Label: $"Variant {i}"))];

        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(variants));

        return root;
    }
}
