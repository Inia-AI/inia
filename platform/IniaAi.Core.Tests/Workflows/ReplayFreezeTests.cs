using IniaAi.Core.Tests.Workflows.Helpers;
using IniaAi.Domain.Replay;
using System.Text.Json;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// Locks in both replay semantics of <see cref="WorkflowHandler"/>. Dormant regression path
/// (<see cref="ReplayUncoveredBlockPolicy.SkipAsInconclusive"/>): every freeze-eligible block
/// short-circuits at execution time and never calls its live strategy; a matched block returns the
/// captured/seeded response, an uncovered one is recorded into RootOutput.ReplayUnmatchedJson and still
/// skips the live call. Freeze submission option (<see cref="ReplayUncoveredBlockPolicy.RunLive"/>):
/// a matched block is seeded the same way, an uncovered one runs live, and the persisted execution
/// discloses coverage as FreezeReplayedCount of FreezeEligibleCount.
/// </summary>
public class ReplayFreezeTests : IClassFixture<WorkflowsFixture<ReplayFreezeTests>>
{
    private readonly WorkflowsFixture<ReplayFreezeTests> _fixture;
    private readonly WorkflowHandler _workflowHandler;
    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public ReplayFreezeTests(
        WorkflowsFixture<ReplayFreezeTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testServiceProvider = _fixture.BuildTestServiceProvider(testOutputHelper);
        _workflowHandler = _testServiceProvider.GetRequiredService<WorkflowHandler>();
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    /// <summary>
    /// A replay run whose capture matches a freeze-eligible block must freeze that block: the live
    /// strategy is never invoked and the block carries the seeded (captured) response.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_ReplayRun_FreezeEligibleBlockMatched_SkipsLiveCallAndUsesSeededResponse()
    {
        const string sectionTitle = "Replay Section";
        const string tag = "api_block";
        const string stableId = "stable-api";
        const string seeded = "SEEDED-CAPTURED-RESPONSE";

        var spy = new RecordingBlockStrategy();
        IBlockStrategy original = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, spy);

        try
        {
            var workflow = new Workflow();
            var section = new Section("", workflow) { Title = sectionTitle };

            var apiBlock = new Block(BlockTypes.WebApiCall, section, tag)
            {
                Name = "Frozen API Call",
                Properties = new WebApiCallProperties
                {
                    UrlTemplate = "https://example.invalid/path",
                    HttpMethod = "GET",
                },
            };
            apiBlock.StableBlockId = stableId;

            var replayOutputs = new List<ReplayOutput>
            {
                new(
                    SectionName: sectionTitle,
                    ReplacementTag: tag,
                    BlockType: BlockTypes.WebApiCall.ToString(),
                    RawResponse: seeded,
                    JsonResponse: null,
                    StableBlockId: stableId),
            };

            // Act
            _ = await _workflowHandler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: [],
                organizationId: "testOrg",
                jobId: Guid.NewGuid().ToString(),
                userDataUnitOfWork: _userDataUnitOfWork,
                inputs: [],
                replayOutputs: replayOutputs,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert: the live strategy was never invoked.
            Assert.Equal(0, spy.InvocationCount);

            // Assert: the frozen block carries the captured response, not a live one.
            Assert.NotNull(apiBlock.BlockResponse);
            Assert.Equal(seeded, apiBlock.BlockResponse!.RawResponse);
            Assert.NotEqual(RecordingBlockStrategy.LIVE_SENTINEL, apiBlock.BlockResponse.RawResponse);
            Assert.Equal(ExecutionStatus.Completed, apiBlock.ExecutionState?.Status);
        }
        finally
        {
            _ = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, original);
        }
    }

    /// <summary>
    /// A freeze-eligible block with NO matching capture in a replay run must NOT call live, and must be
    /// recorded as uncovered in the persisted execution's ReplayUnmatchedJson (making the run inconclusive).
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_ReplayRun_UncoveredFreezeEligibleBlock_RecordsUnmatchedAndSkipsLiveCall()
    {
        const string sectionTitle = "Uncovered Section";
        const string coveredTag = "merge_block";
        const string uncoveredTag = "uncovered_api_block";

        var spy = new RecordingBlockStrategy();
        IBlockStrategy original = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, spy);

        try
        {
            var workflow = new Workflow();
            var section = new Section("", workflow) { Title = sectionTitle };

            // A non-freeze block that the single capture matches, so the run stays a replay run while
            // the freeze-eligible WebApiCall block below is left uncovered.
            var coveredMerge = new Block(BlockTypes.Merge, section, coveredTag)
            {
                Name = "Covered Merge",
                Properties = new MergeProperties { TextTemplate = "hello" },
            };
            coveredMerge.StableBlockId = "covered-merge";

            _ = new Block(BlockTypes.WebApiCall, section, uncoveredTag)
            {
                Name = "Uncovered API Call",
                Properties = new WebApiCallProperties
                {
                    UrlTemplate = "https://example.invalid/path",
                    HttpMethod = "GET",
                },
            };

            string jobId = Guid.NewGuid().ToString();
            await _workflowHandler.CreateRootOutputAsync(
                _userDataUnitOfWork,
                jobId: jobId,
                rootWorkflow: workflow,
                organizationId: "testOrg",
                userId: null);

            string? executionId = _workflowHandler.WorkflowExecutionId;
            Assert.NotNull(executionId);

            var replayOutputs = new List<ReplayOutput>
            {
                new(
                    SectionName: sectionTitle,
                    ReplacementTag: coveredTag,
                    BlockType: BlockTypes.Merge.ToString(),
                    RawResponse: "covered",
                    JsonResponse: null,
                    StableBlockId: "covered-merge"),
            };

            // Act
            _ = await _workflowHandler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: [],
                organizationId: "testOrg",
                jobId: jobId,
                userDataUnitOfWork: _userDataUnitOfWork,
                inputs: [],
                replayOutputs: replayOutputs,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert: the uncovered freeze-eligible block never called its live strategy.
            Assert.Equal(0, spy.InvocationCount);

            WorkflowExecution? persisted = await _userDataUnitOfWork
                .WorkflowExecutions
                .GetByIdAsync(executionId!, TestContext.Current.CancellationToken);

            Assert.NotNull(persisted);
            Assert.False(string.IsNullOrEmpty(persisted!.ReplayUnmatchedJson));

            List<ReplayUnmatched>? unmatched =
                JsonSerializer.Deserialize<List<ReplayUnmatched>>(persisted.ReplayUnmatchedJson!);

            Assert.NotNull(unmatched);
            ReplayUnmatched entry = Assert.Single(
                unmatched!,
                u => u.BlockType == BlockTypes.WebApiCall.ToString()
                    && u.ReplacementTag == uncoveredTag);
            Assert.Contains("freeze-eligible", entry.Reason);

            Assert.Equal(1, persisted.FreezeEligibleCount);
            Assert.Equal(0, persisted.FreezeReplayedCount);
        }
        finally
        {
            _ = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, original);
        }
    }

    /// <summary>
    /// The freeze submission option keeps the seeded short-circuit for a matched block and counts it as
    /// replayed coverage on the persisted execution.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_RunLivePolicy_MatchedBlock_UsesSeededResponseAndCountsReplayed()
    {
        const string sectionTitle = "Freeze Section";
        const string tag = "api_block";
        const string stableId = "stable-frozen-api";
        const string seeded = "SEEDED-FROM-BASELINE";

        var spy = new RecordingBlockStrategy();
        IBlockStrategy original = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, spy);

        try
        {
            var workflow = new Workflow();
            var section = new Section("", workflow) { Title = sectionTitle };

            var apiBlock = new Block(BlockTypes.WebApiCall, section, tag)
            {
                Name = "Frozen API Call",
                Properties = new WebApiCallProperties
                {
                    UrlTemplate = "https://example.invalid/path",
                    HttpMethod = "GET",
                },
            };
            apiBlock.StableBlockId = stableId;

            string jobId = Guid.NewGuid().ToString();
            await _workflowHandler.CreateRootOutputAsync(
                _userDataUnitOfWork,
                jobId: jobId,
                rootWorkflow: workflow,
                organizationId: "testOrg",
                userId: null);
            string? executionId = _workflowHandler.WorkflowExecutionId;
            Assert.NotNull(executionId);

            var replayOutputs = new List<ReplayOutput>
            {
                new(
                    SectionName: sectionTitle,
                    ReplacementTag: tag,
                    BlockType: BlockTypes.WebApiCall.ToString(),
                    RawResponse: seeded,
                    JsonResponse: null,
                    StableBlockId: stableId),
            };

            Workflow executed = await _workflowHandler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: [],
                organizationId: "testOrg",
                jobId: jobId,
                userDataUnitOfWork: _userDataUnitOfWork,
                inputs: [],
                replayOutputs: replayOutputs,
                replayUncoveredBlockPolicy: ReplayUncoveredBlockPolicy.RunLive,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, spy.InvocationCount);
            Block executedApi = Assert.Single(executed.Sections.SelectMany(s => s.Blocks), b => b.Type == BlockTypes.WebApiCall);
            Assert.Equal(seeded, executedApi.BlockResponse?.RawResponse);
            Assert.Equal(ExecutionStatus.Completed, executedApi.ExecutionState?.Status);

            WorkflowExecution? persisted = await _userDataUnitOfWork
                .WorkflowExecutions
                .GetByIdAsync(executionId!, TestContext.Current.CancellationToken);

            Assert.NotNull(persisted);
            Assert.Equal(1, persisted!.FreezeEligibleCount);
            Assert.Equal(1, persisted.FreezeReplayedCount);
            Assert.Null(persisted.ReplayUnmatchedJson);
        }
        finally
        {
            _ = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, original);
        }
    }

    /// <summary>
    /// The freeze submission option is best-effort: an uncovered freeze-eligible block runs live (unlike
    /// the dormant regression path), the live block is still named in ReplayUnmatchedJson, and the
    /// persisted execution discloses the coverage (0 replayed of 1 eligible).
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_RunLivePolicy_UncoveredBlock_RunsLiveAndDisclosesCoverage()
    {
        const string sectionTitle = "Freeze Uncovered Section";
        const string coveredTag = "merge_block";
        const string uncoveredTag = "uncovered_api_block";

        var spy = new RecordingBlockStrategy();
        IBlockStrategy original = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, spy);

        try
        {
            var workflow = new Workflow();
            var section = new Section("", workflow) { Title = sectionTitle };

            var coveredMerge = new Block(BlockTypes.Merge, section, coveredTag)
            {
                Name = "Covered Merge",
                Properties = new MergeProperties { TextTemplate = "hello" },
            };
            coveredMerge.StableBlockId = "covered-merge";

            _ = new Block(BlockTypes.WebApiCall, section, uncoveredTag)
            {
                Name = "Uncovered API Call",
                Properties = new WebApiCallProperties
                {
                    UrlTemplate = "https://example.invalid/path",
                    HttpMethod = "GET",
                },
            };

            string jobId = Guid.NewGuid().ToString();
            await _workflowHandler.CreateRootOutputAsync(
                _userDataUnitOfWork,
                jobId: jobId,
                rootWorkflow: workflow,
                organizationId: "testOrg",
                userId: null);
            string? executionId = _workflowHandler.WorkflowExecutionId;
            Assert.NotNull(executionId);

            var replayOutputs = new List<ReplayOutput>
            {
                new(
                    SectionName: sectionTitle,
                    ReplacementTag: coveredTag,
                    BlockType: BlockTypes.Merge.ToString(),
                    RawResponse: "covered",
                    JsonResponse: null,
                    StableBlockId: "covered-merge"),
            };

            Workflow executed = await _workflowHandler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: [],
                organizationId: "testOrg",
                jobId: jobId,
                userDataUnitOfWork: _userDataUnitOfWork,
                inputs: [],
                replayOutputs: replayOutputs,
                replayUncoveredBlockPolicy: ReplayUncoveredBlockPolicy.RunLive,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, spy.InvocationCount);
            Block executedApi = Assert.Single(executed.Sections.SelectMany(s => s.Blocks), b => b.Type == BlockTypes.WebApiCall);
            Assert.Equal(RecordingBlockStrategy.LIVE_SENTINEL, executedApi.BlockResponse?.RawResponse);

            WorkflowExecution? persisted = await _userDataUnitOfWork
                .WorkflowExecutions
                .GetByIdAsync(executionId!, TestContext.Current.CancellationToken);

            Assert.NotNull(persisted);
            Assert.Equal(1, persisted!.FreezeEligibleCount);
            Assert.Equal(0, persisted.FreezeReplayedCount);

            List<ReplayUnmatched>? unmatched =
                JsonSerializer.Deserialize<List<ReplayUnmatched>>(persisted.ReplayUnmatchedJson!);
            Assert.NotNull(unmatched);
            ReplayUnmatched entry = Assert.Single(unmatched!);
            Assert.Equal(uncoveredTag, entry.ReplacementTag);
            Assert.Contains("ran live", entry.Reason);
        }
        finally
        {
            _ = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, original);
        }
    }

    /// <summary>
    /// Freeze-eligible blocks inside a runtime loop only exist after the section's loop expansion,
    /// which happens during execution, after the initial replay injection. Captures with suffixed
    /// stable ids must be re-resolved against the per-iteration clones once the loop expands, so
    /// each clone carries its own captured response and nothing is recorded as unmatched.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_ReplayRun_FreezeEligibleBlockInsideLoop_SeedsPerIterationClones()
    {
        const string sectionTitle = "Loop Section";
        const string listTitle = "Items";
        const string tag = "api_block";
        const string stableId = "stable-loop-api";
        const string seededFirst = "SEEDED-ITERATION-0";
        const string seededSecond = "SEEDED-ITERATION-1";

        var spy = new RecordingBlockStrategy();
        IBlockStrategy original = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, spy);

        try
        {
            var workflow = new Workflow();
            var section = new Section("", workflow) { Title = sectionTitle };

            _ = new Block(BlockTypes.LoopStart, section)
            {
                Name = "Loop start",
                Properties = new LoopStartProperties
                {
                    ListTitle = listTitle,
                    LoopedListTemplate = "Alpha\nBravo",
                },
            };

            var apiBlock = new Block(BlockTypes.WebApiCall, section, tag)
            {
                Name = "Looped API Call",
                Properties = new WebApiCallProperties
                {
                    UrlTemplate = "https://example.invalid/path",
                    HttpMethod = "GET",
                },
            };
            apiBlock.StableBlockId = stableId;

            _ = new Block(BlockTypes.LoopEnd, section)
            {
                Name = "Loop end",
            };

            // Captures as a baseline export would carry them: the baseline clone was itself
            // loop-expanded, so its ids and tags are per-iteration suffixed.
            var replayOutputs = new List<ReplayOutput>
            {
                new(
                    SectionName: sectionTitle,
                    ReplacementTag: $"{listTitle}_{tag}_0",
                    BlockType: BlockTypes.WebApiCall.ToString(),
                    RawResponse: seededFirst,
                    JsonResponse: null,
                    StableBlockId: $"{stableId}_{listTitle}_0"),
                new(
                    SectionName: sectionTitle,
                    ReplacementTag: $"{listTitle}_{tag}_1",
                    BlockType: BlockTypes.WebApiCall.ToString(),
                    RawResponse: seededSecond,
                    JsonResponse: null,
                    StableBlockId: $"{stableId}_{listTitle}_1"),
            };

            // Act
            _ = await _workflowHandler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: [],
                organizationId: "testOrg",
                jobId: Guid.NewGuid().ToString(),
                userDataUnitOfWork: _userDataUnitOfWork,
                inputs: [],
                replayOutputs: replayOutputs,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert: no looped clone ever called the live strategy.
            Assert.Equal(0, spy.InvocationCount);

            List<Block> clones = [.. section.Blocks
                .Where(b => b.Type is BlockTypes.WebApiCall)
                .OrderBy(b => b.Order)];
            Assert.Equal(2, clones.Count);

            Assert.Equal($"{stableId}_{listTitle}_0", clones[0].StableBlockId);
            Assert.Equal(seededFirst, clones[0].BlockResponse?.RawResponse);
            Assert.Equal(ExecutionStatus.Completed, clones[0].ExecutionState?.Status);

            Assert.Equal($"{stableId}_{listTitle}_1", clones[1].StableBlockId);
            Assert.Equal(seededSecond, clones[1].BlockResponse?.RawResponse);
            Assert.Equal(ExecutionStatus.Completed, clones[1].ExecutionState?.Status);
        }
        finally
        {
            _ = TestBlockStrategies.Swap(_workflowHandler, BlockTypes.WebApiCall, original);
        }
    }

    /// <summary>
    /// Spy strategy: records whether it was invoked and, if so, writes a distinctive sentinel response.
    /// The freeze short-circuit must prevent this from running for freeze-eligible blocks in a replay run.
    /// </summary>
    private sealed class RecordingBlockStrategy : IBlockStrategy
    {
        public const string LIVE_SENTINEL = "LIVE-STRATEGY-INVOKED";

        public int InvocationCount;

        public Task<BlockStrategyOutput> ExecuteBlockAsync(
            Block block,
            BlockExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _ = Interlocked.Increment(ref InvocationCount);
            _ = new BlockResponse(block) { RawResponse = LIVE_SENTINEL };
            return Task.FromResult(new BlockStrategyOutput());
        }
    }
}
