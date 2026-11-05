using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Constants;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// End-to-end parallel-execution tests through the real <see cref="WorkflowHandler"/> on the Section
/// model. Uses deterministic <see cref="BlockTypes.Merge"/> blocks (no LLM) so cap=1 and cap=4 are
/// exactly comparable, and an instrumented handler that delays + counts inside
/// <see cref="WorkflowHandler.OnBlockExecutedAsync"/> so concurrency is proven by observation, never
/// by wall-clock. Dev mode (no RootOutput) is fine here: blocks execute and set their in-memory
/// BlockResponse without persisting.
/// </summary>
public class ParallelExecutionTests : IClassFixture<WorkflowsFixture<ParallelExecutionTests>>
{
    private const string ORG_ID = "TestOrg";
    private const string USER_ID = "TestUser";
    private const int BLOCK_COUNT = 4;

    private readonly IServiceProvider _sp;

    public ParallelExecutionTests(
        WorkflowsFixture<ParallelExecutionTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task IndependentBlocks_AtCapFour_RunConcurrently()
    {
        SharedCounter counter = new();
        InstrumentedHandler handler = BuildHandler(cap: 4, counter, delayMs: 60);

        await RunAsync(handler, BuildIndependentBlocksWorkflow());

        Assert.True(
            counter.MaxObserved >= 2,
            $"expected overlap at cap=4 but max observed in-flight was {counter.MaxObserved}.");
    }

    [Fact]
    public async Task IndependentBlocks_AtCapOne_NeverOverlap()
    {
        SharedCounter counter = new();
        InstrumentedHandler handler = BuildHandler(cap: 1, counter, delayMs: 10);

        await RunAsync(handler, BuildIndependentBlocksWorkflow());

        Assert.Equal(1, counter.MaxObserved);
    }

    [Fact]
    public async Task Results_AtCapOne_EqualResultsAtCapFour()
    {
        Workflow capOne = BuildChainedWorkflow();
        await RunAsync(BuildHandler(cap: 1, new SharedCounter(), delayMs: 0), capOne);
        Dictionary<string, string?> capOneResults = CollectResponsesByTag(capOne);

        Workflow capFour = BuildChainedWorkflow();
        await RunAsync(BuildHandler(cap: 4, new SharedCounter(), delayMs: 0), capFour);
        Dictionary<string, string?> capFourResults = CollectResponsesByTag(capFour);

        Assert.NotEmpty(capOneResults);
        Assert.Equal(capOneResults, capFourResults);
    }

    /// <summary>
    /// Two sections, each with its own output block, run as one cross-section parallel batch. Each
    /// block's report paragraph must land in ITS OWN section's report section. Regression guard for the
    /// shared-<see cref="IniaAi.Core.Workflows.Outputs.WorkflowOutputContext"/> bug: the parallel
    /// pre-loop set every section's blocks to one shared context whose CurrentReportSection was
    /// last-writer-wins, so both paragraphs collapsed into the last section. Deterministic (independent
    /// of cap) because the mis-pointing happens in the single-threaded prelude, not in the dispatch.
    /// </summary>
    [Fact]
    public async Task TwoEmittingSections_EachParagraph_LandsInItsOwnSection()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        InstrumentedHandler handler = BuildHandler(cap: 2, new SharedCounter(), delayMs: 0);
        Workflow workflow = BuildTwoEmittingSectionsWorkflow();

        string executionId = await RunWithOutputAsync(handler, workflow);

        IDbContextFactory<UserDataDbContext> factory =
            _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        await using UserDataDbContext read = await factory.CreateDbContextAsync(ct);

        Dictionary<int, ReportSection> sectionsByOrder = await read.ReportSections.AsNoTracking()
            .Where(s => s.WorkflowReportId == executionId)
            .ToDictionaryAsync(s => s.Order, ct);

        List<ReportParagraph> paragraphs = await read.ReportParagraphs.AsNoTracking()
            .Where(p => sectionsByOrder.Values.Select(s => s.Id).Contains(p.SectionId))
            .ToListAsync(ct);

        Assert.Equal(2, sectionsByOrder.Count);
        ReportParagraph alpha = Assert.Single(paragraphs, p => p.ReplacementTag == "emit_0");
        ReportParagraph bravo = Assert.Single(paragraphs, p => p.ReplacementTag == "emit_1");

        Assert.Equal(sectionsByOrder[0].Id, alpha.SectionId);
        Assert.Equal(sectionsByOrder[1].Id, bravo.SectionId);
    }

    /// <summary>
    /// Regression guard for the signature-override load. A client override lives ONLY in the Core DB
    /// (keyed by org + workflow type + field name); the workflow declares the matching signature field with
    /// a different default. On a full run the override must reach the block and beat the default. This
    /// proves the once-per-run prelude load (the single source of truth) feeds per-block injection — so
    /// dropping the redundant per-block reload cannot regress injection.
    /// </summary>
    [Fact]
    public async Task SignatureOverride_FromCoreDb_BeatsDefault_OnFullWorkflowRun()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using (IServiceScope seed = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            ICoreUnitOfWork core = seed.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();
            await core.Repository<SignatureFieldOverride>().AddAsync(new SignatureFieldOverride
            {
                OrganizationId = ORG_ID,
                WorkflowTypeId = "WT-sig",
                PlaceholderName = "tone",
                Value = "cheerful",
            });
            _ = await core.CompleteAsync(ct);
        }

        InstrumentedHandler handler = BuildHandler(cap: 2, new SharedCounter(), delayMs: 0);
        Workflow executed = await RunReturningAsync(handler, BuildSignatureWorkflow());

        Block block = executed.Sections
            .SelectMany(s => s.Blocks)
            .Single(b => b.ReplacementTag == "sig_out");

        // "cheerful" (override) proves the prelude DB load reached injection; "formal" (default)
        // would mean the override never loaded.
        Assert.Equal("Use cheerful please.", block.BlockResponse?.RawResponse);
    }

    /// <summary>
    /// Section 1's skip gate reads section 0's output, which carries the stop term. The segment
    /// split must run section 1's prelude only after section 0's blocks complete, so the gate sees
    /// the resolved value (not no-data) and skips the whole section at section level, exactly like
    /// the sequential engine. Independent of the cap.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task SectionGate_ReadingEarlierSectionStopTerm_SkipsTheSection(int cap)
    {
        InstrumentedHandler handler = BuildHandler(cap, new SharedCounter(), delayMs: 0);
        Workflow executed = await RunReturningAsync(handler, BuildGatedSectionsWorkflow());

        Block victim = executed.Sections
            .SelectMany(s => s.Blocks)
            .Single(b => b.ReplacementTag == "gate_victim");

        Assert.Equal(ExecutionStatus.Skipped, victim.ExecutionState?.Status);
        Assert.Null(victim.BlockResponse?.RawResponse);
    }

    private async Task RunAsync(WorkflowHandler handler, Workflow workflow)
    {
        _ = await RunReturningAsync(handler, workflow);
    }

    private async Task<Workflow> RunReturningAsync(WorkflowHandler handler, Workflow workflow)
    {
        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        return await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: ORG_ID,
            jobId: Guid.NewGuid().ToString(),
            uow,
            inputs: [],
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Runs the handler with the report-output path active (RootOutput set), so report
    /// sections + paragraphs persist to the in-memory UserData store. Returns the execution id.</summary>
    private async Task<string> RunWithOutputAsync(WorkflowHandler handler, Workflow workflow)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        await handler.CreateRootOutputAsync(uow, jobId: Guid.NewGuid().ToString(), workflow, ORG_ID, USER_ID);
        string executionId = handler.WorkflowExecutionId!;

        _ = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: ORG_ID,
            jobId: Guid.NewGuid().ToString(),
            uow,
            inputs: [],
            cancellationToken: ct);

        return executionId;
    }

    /// <summary>Two independent sections, each holding a single output Merge block, so they schedule
    /// into one cross-section parallel batch and each must attribute its paragraph to its own
    /// section.</summary>
    private static Workflow BuildTwoEmittingSectionsWorkflow()
    {
        Workflow root = new() { Id = "emit-root", Title = "emit" };

        Section alpha = new("Alpha", root) { Id = "emit-sec-0", Order = 0 };
        _ = new Block(BlockTypes.Merge, alpha, replacementTag: "emit_0")
        {
            Id = "emit-blk-0",
            Name = "Alpha",
            IsOutputSource = true,
            Properties = new MergeProperties { TextTemplate = "alpha output" },
        };

        Section bravo = new("Bravo", root) { Id = "emit-sec-1", Order = 1 };
        _ = new Block(BlockTypes.Merge, bravo, replacementTag: "emit_1")
        {
            Id = "emit-blk-1",
            Name = "Bravo",
            IsOutputSource = true,
            Properties = new MergeProperties { TextTemplate = "bravo output" },
        };

        return root;
    }

    /// <summary>Workflow declaring one signature field (default "formal") referenced by a single
    /// Merge block's template. The matching override value lives only in the Core DB, so a correct
    /// run substitutes the override into the block's template text.</summary>
    private static Workflow BuildSignatureWorkflow()
    {
        Workflow root = new() { Id = "sig-root", Title = "sig", OrganizationId = ORG_ID, WorkflowTypeId = "WT-sig" };
        _ = new WorkflowSignatureField(root, "tone")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "formal",
        };

        Section section = new("S", root) { Id = "sig-sec", Order = 0 };

        _ = new Block(BlockTypes.Merge, section, replacementTag: "sig_out")
        {
            Id = "sig-blk",
            StableBlockId = "sig-blk",
            Name = "Sig",
            Properties = new MergeProperties
            {
                TextTemplate = "Use {tone} please.",
            },
        };

        return root;
    }

    /// <summary>Section 0 emits the stop term; section 1's TextInputTemplate (skip gate) reads it
    /// and holds a block that must never execute.</summary>
    private static Workflow BuildGatedSectionsWorkflow()
    {
        Workflow root = new() { Id = "gate-root", Title = "gate" };

        Section emitter = new("Emitter", root) { Id = "gate-sec-0", Order = 0 };
        _ = new Block(BlockTypes.Merge, emitter, replacementTag: "gate_src")
        {
            Id = "gate-blk-0",
            Name = "Emitter",
            Properties = new MergeProperties { TextTemplate = StopMarker.VALUE },
        };

        Section gated = new("Gated", root)
        {
            Id = "gate-sec-1",
            Order = 1,
            TextInputTemplate = "{gate_src}",
        };
        _ = new Block(BlockTypes.Merge, gated, replacementTag: "gate_victim")
        {
            Id = "gate-blk-1",
            Name = "Victim",
            Properties = new MergeProperties { TextTemplate = "must not run" },
        };

        return root;
    }

    /// <summary>Four independent Merge blocks in one section (no inter-block placeholders) so they
    /// land in a single scheduler layer and all dispatch together.</summary>
    private static Workflow BuildIndependentBlocksWorkflow()
    {
        Workflow root = new() { Id = "indep-root", Title = "indep" };
        Section section = new("S", root) { Id = "indep-sec", Order = 0 };

        for (int i = 0; i < BLOCK_COUNT; i++)
        {
            _ = new Block(BlockTypes.Merge, section, replacementTag: $"indep_{i}")
            {
                Id = $"indep-blk-{i}",
                Name = $"B{i}",
                Properties = new MergeProperties { TextTemplate = $"out-{i}" },
            };
        }

        return root;
    }

    /// <summary>Linear chain: each Merge block reads the previous block's tag, forcing a multi-layer
    /// DAG so cap=1 and cap=4 traverse the same dependency order.</summary>
    private static Workflow BuildChainedWorkflow()
    {
        Workflow root = new() { Id = "chain-root", Title = "chain" };
        Section section = new("S", root) { Id = "chain-sec", Order = 0 };

        for (int i = 0; i < BLOCK_COUNT; i++)
        {
            string template = i == 0 ? "start" : $"after {{chain_{i - 1}}}";
            _ = new Block(BlockTypes.Merge, section, replacementTag: $"chain_{i}")
            {
                Id = $"chain-blk-{i}",
                Name = $"C{i}",
                Properties = new MergeProperties { TextTemplate = template },
            };
        }

        return root;
    }

    private static Dictionary<string, string?> CollectResponsesByTag(Workflow root)
    {
        Dictionary<string, string?> result = new(StringComparer.Ordinal);

        foreach (Section section in root.Sections.OrderBy(s => s.Order))
        {
            foreach (Block block in section.Blocks.OrderBy(b => b.Order))
            {
                if (!string.IsNullOrEmpty(block.ReplacementTag))
                {
                    result[block.ReplacementTag] = block.BlockResponse?.RawResponse;
                }
            }
        }

        return result;
    }

    private InstrumentedHandler BuildHandler(int cap, SharedCounter counter, int delayMs)
    {
        return new InstrumentedHandler(
            _sp.GetRequiredService<ILogger<WorkflowHandler>>(),
            _sp.GetRequiredService<IOptions<PlatformSettings>>(),
            _sp.GetRequiredService<IServiceScopeFactory>(),
            _sp.GetRequiredService<IWorkflowOutputService>(),
            _sp.GetRequiredService<IDocumentProcessingService>(),
            _sp.GetRequiredService<IFileStorageManager>(),
            _sp.GetRequiredService<IEntityPlaceholderResolver>(),
            _sp.GetRequiredService<IWorkflowGraphBuilder>(),
            TestSchedulers.NewRealScheduler(cap),
            _sp.GetRequiredService<IExecutedBlockWriter>(),
            counter,
            delayMs);
    }

    /// <summary>
    /// Real handler that instruments <see cref="OnBlockExecutedAsync"/> (called once per block after
    /// its strategy) with an in-flight counter + delay, so a parallel layer's overlap is observable.
    /// </summary>
    private sealed class InstrumentedHandler : WorkflowHandler
    {
        private readonly SharedCounter _counter;
        private readonly int _delayMs;

        public InstrumentedHandler(
            ILogger<WorkflowHandler> logger,
            IOptions<PlatformSettings> platformSettings,
            IServiceScopeFactory serviceScopeFactory,
            IWorkflowOutputService workflowOutputService,
            IDocumentProcessingService documentProcessingService,
            IFileStorageManager fileStorageManager,
            IEntityPlaceholderResolver entityPlaceholderResolver,
            IWorkflowGraphBuilder graphBuilder,
            LevelScheduler scheduler,
            IExecutedBlockWriter blockWriter,
            SharedCounter counter,
            int delayMs)
            : base(
                logger,
                platformSettings,
                serviceScopeFactory,
                workflowOutputService,
                documentProcessingService,
                fileStorageManager,
                entityPlaceholderResolver,
                graphBuilder,
                scheduler,
                blockWriter)
        {
            _counter = counter;
            _delayMs = delayMs;
        }

        protected override async Task OnBlockExecutedAsync(
            Block block,
            IUserDataUnitOfWork userDataUnitOfWork,
            CancellationToken cancellationToken)
        {
            _counter.OnEnter(block.Id);
            try
            {
                if (_delayMs > 0)
                {
                    await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(_delayMs), cancellationToken).ConfigureAwait(false);
                }

                await base.OnBlockExecutedAsync(block, userDataUnitOfWork, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _counter.OnExit(block.Id);
            }
        }
    }
}
