using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Verification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Isolated tests for <see cref="WorkflowBlockPersistence"/>: the per-block pooled-context writer.
/// Seeds a skeleton block (as BuildExecutedWorkflowAsync would), mutates its in-memory state, then
/// asserts the writer flushes ExecutionState + a BlockResponse upserted on BlockId. The re-execution
/// case proves the upsert keys on BlockId (no duplicate row / 23505) when the strategy mints a
/// fresh-GUID BlockResponse on a second run.
/// </summary>
public class WorkflowBlockPersistenceTests : IClassFixture<WorkflowsFixture<WorkflowBlockPersistenceTests>>
{
    private readonly IServiceProvider _sp;

    public WorkflowBlockPersistenceTests(
        WorkflowsFixture<WorkflowBlockPersistenceTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task PersistBlockResultAsync_PersistsExecutionState_AndUpsertsResponseByBlockId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        // Seed the skeleton row: Workflow -> Section -> Block.
        var workflow = new Workflow { Id = "wbp-wf", Title = "wbp-wf" };
        var section = new Section("S", workflow) { Id = "wbp-sec", Order = 0 };
        var block = new Block(BlockTypes.Merge, section)
        {
            Id = "wbp-blk",
            Name = "Block",
            ReplacementTag = "tag",
            Properties = new MergeProperties { TextTemplate = "x" },
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = await seed.SaveChangesAsync(ct);
        }

        // Mutate in memory (as block execution would), then persist via the writer.
        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = "hello" };

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            Block? persisted = await read.Blocks.AsNoTracking().SingleOrDefaultAsync(b => b.Id == "wbp-blk", ct);
            Assert.NotNull(persisted);
            Assert.NotNull(persisted!.ExecutionState);
            Assert.Equal(ExecutionStatus.Completed, persisted.ExecutionState!.Status);

            BlockResponse? response = await read.BlockResponses.AsNoTracking().SingleOrDefaultAsync(r => r.BlockId == "wbp-blk", ct);
            Assert.NotNull(response);
            Assert.Equal("hello", response!.RawResponse);
        }

        // Re-execution: a fresh-GUID BlockResponse for the same BlockId must upsert in place,
        // not insert a second row (which would be a 23505 unique violation on real Postgres).
        block.BlockResponse = null;
        _ = new BlockResponse(block) { RawResponse = "world" };
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            List<BlockResponse> responses = await read.BlockResponses.AsNoTracking()
                .Where(r => r.BlockId == "wbp-blk").ToListAsync(ct);
            BlockResponse single = Assert.Single(responses);
            Assert.Equal("world", single.RawResponse);
        }
    }

    [Fact]
    public async Task PersistBlockResultAsync_UpsertsTheCitationTraceByBlockId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        var workflow = new Workflow { Id = "wbp-trace-wf", Title = "wbp-trace-wf" };
        var section = new Section("S", workflow) { Id = "wbp-trace-sec", Order = 0 };
        var block = new Block(BlockTypes.Merge, section)
        {
            Id = "wbp-trace-blk",
            Name = "Block",
            ReplacementTag = "tag",
            Properties = new MergeProperties { TextTemplate = "x" },
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = await seed.SaveChangesAsync(ct);
        }

        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        block.CitationTrace = new BlockCitationTrace { OrganizationId = "org", BlockId = block.Id, Content = "{\"facts\":[]}" };
        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        await writer.PersistBlockResultAsync(block, null, null, ct);

        block.CitationTrace = new BlockCitationTrace { OrganizationId = "org", BlockId = block.Id, Content = "{\"report\":{}}" };
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using UserDataDbContext read = await factory.CreateDbContextAsync(ct);
        BlockCitationTrace single = Assert.Single(await read.BlockCitationTraces.AsNoTracking()
            .Where(trace => trace.BlockId == block.Id).ToListAsync(ct));
        Assert.Equal("{\"report\":{}}", single.Content);
    }

    [Fact]
    public async Task PersistBlockResultAsync_WritesTheCarriedDataOutput_AndAReEntryReplacesIt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        var workflow = new Workflow { Id = "wbp-data-wf", Title = "wbp-data-wf" };
        var section = new Section("S", workflow) { Id = "wbp-data-sec", Order = 0 };
        var block = new Block(BlockTypes.MergeJson, section)
        {
            Id = "wbp-data-blk",
            Name = "Facilities JSON",
            ReplacementTag = "facilities_json",
            DataOutputName = "facilities",
            Properties = new MergeJsonProperties(),
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = await seed.SaveChangesAsync(ct);
        }

        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = """{ "limit": 2 }""" };

        static ReportDataOutput Build(string json)
        {
            return new ReportDataOutput
            {
                WorkflowReportId = "wbp-data-report",
                BlockId = "wbp-data-blk",
                Name = "facilities",
                Json = json,
                Citations =
                [
                    new DataOutputCitation
                    {
                        Id = "c0",
                        Path = "/limit",
                        Value = "2",
                        IsNumerical = true,
                        Sources = [new DocumentSource { CitedText = "Limit 2", PageId = "page-1", PageNumber = 1 }],
                    },
                ],
            };
        }

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        block.DataOutput = Build("""{ "limit": 1 }""");
        await writer.PersistBlockResultAsync(block, null, null, ct);
        block.DataOutput = Build("""{ "limit": 2 }""");
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using UserDataDbContext read = await factory.CreateDbContextAsync(ct);
        ReportDataOutput output = Assert.Single(
            await read.Set<ReportDataOutput>().AsNoTracking()
                .Where(row => row.BlockId == "wbp-data-blk").ToListAsync(ct));
        Assert.Equal("""{ "limit": 2 }""", output.Json);
        DataOutputCitation citation = Assert.Single(output.Citations);
        Assert.Equal("/limit", citation.Path);
        Assert.Equal("page-1", Assert.IsType<DocumentSource>(Assert.Single(citation.Sources)).PageId);
    }

    [Fact]
    public async Task PersistBlockResultAsync_ReEnteringARelease_LeavesOneParagraphAndOneLogRow()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        var workflow = new Workflow { Id = "wbp-idem-wf", Title = "wbp-idem-wf" };
        var section = new Section("S", workflow) { Id = "wbp-idem-sec", Order = 0 };
        var block = new Block(BlockTypes.Merge, section)
        {
            Id = "wbp-idem-blk",
            Name = "Block",
            ReplacementTag = "tag",
            Properties = new MergeProperties { TextTemplate = "x" },
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = await seed.SaveChangesAsync(ct);
        }

        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = "released" };

        // A release re-entry (taint replay, crash-replayed re-execution) rebuilds both rows
        // under fresh GUIDs — the writer must replace on the natural key, never append.
        static (ReportParagraph Paragraph,
            WorkflowExecutionLog Log) BuildRelease(string content)
        {
            var log = new WorkflowExecutionLog
            {
                BlockName = "Block",
                WorkflowExecutionId = "wbp-idem-exec",
                BlockId = "wbp-idem-blk",
                Output = content,
            };

            return (new ReportParagraph
            {
                SectionId = "wbp-idem-repsec",
                BlockId = "wbp-idem-blk",
                Content = content,
                WorkflowExecutionLogs = [log],
            }, log);
        }

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        (ReportParagraph first, _) = BuildRelease("first release");
        await writer.PersistBlockResultAsync(block, first, null, ct);
        (ReportParagraph second, _) = BuildRelease("second release");
        await writer.PersistBlockResultAsync(block, second, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            ReportParagraph paragraph = Assert.Single(
                await read.Set<ReportParagraph>().AsNoTracking()
                    .Where(row => row.BlockId == "wbp-idem-blk").ToListAsync(ct));
            Assert.Equal("second release", paragraph.Content);

            WorkflowExecutionLog log = Assert.Single(
                await read.Set<WorkflowExecutionLog>().AsNoTracking()
                    .Where(row => row.BlockId == "wbp-idem-blk").ToListAsync(ct));
            Assert.Equal("second release", log.Output);
        }
    }

    [Fact]
    public async Task PersistBlockResultAsync_PersistsCollectedAgentRunTraceFiles()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        var workflow = new Workflow { Id = "wbp-tr-wf", Title = "wbp-tr-wf" };
        var section = new Section("S", workflow) { Id = "wbp-tr-sec", Order = 0 };
        var block = new Block(BlockTypes.Agent, section)
        {
            Id = "wbp-tr-blk",
            Name = "Agent",
            ReplacementTag = "tag",
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = await seed.SaveChangesAsync(ct);
        }

        // Mutate in memory exactly as a settled agent run does: state, response, collected trace rows.
        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = "out" };
        block.AgentRunTraceFiles.Add(new AgentRunTraceFile
        {
            OrganizationId = "org-1",
            BlockId = block.Id,
            RunExecutionId = "wbp-tr-wf_wbp-tr-blk_w_1",
            Attempt = 1,
            Path = "trace/trace.ndjson",
            Sha256 = new string('a', 64),
            SizeBytes = 3,
            Content = [1, 2, 3],
        });

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            List<AgentRunTraceFile> rows = await read.AgentRunTraceFiles.AsNoTracking()
                .Where(f => f.BlockId == "wbp-tr-blk").ToListAsync(ct);
            AgentRunTraceFile row = Assert.Single(rows);
            Assert.Equal("trace/trace.ndjson", row.Path);
        }
    }

    [Fact]
    public async Task PersistBlockResultAsync_PersistsRecordedAttemptOutputs()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        var workflow = new Workflow { Id = "wbp-ao-wf", Title = "wbp-ao-wf" };
        var section = new Section("S", workflow) { Id = "wbp-ao-sec", Order = 0 };
        var block = new Block(BlockTypes.Agent, section)
        {
            Id = "wbp-ao-blk",
            Name = "Agent",
            ReplacementTag = "tag",
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = await seed.SaveChangesAsync(ct);
        }

        // Two settled legs recorded their rows in memory; both must land.
        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = "round two output" };
        AddAttemptRow(block, round: 1, "wbp-ao-wf_wbp-ao-blk_w_r1_1", "round one output");
        AddAttemptRow(block, round: 2, "wbp-ao-wf_wbp-ao-blk_w_r2_1", "round two output");

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            List<AgentRunOutput> rows = await read.AgentRunOutputs.AsNoTracking()
                .Where(row => row.BlockId == "wbp-ao-blk").OrderBy(row => row.Round).ToListAsync(ct);
            Assert.Equal(2, rows.Count);
            Assert.Equal("round one output", rows[0].Output);
            Assert.Equal("round two output", rows[1].Output);
        }
    }

    [Fact]
    public async Task PersistBlockResultAsync_AttemptRowWithUnchangedRunId_IsLeftUntouched()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        var workflow = new Workflow { Id = "wbp-as-wf", Title = "wbp-as-wf" };
        var section = new Section("S", workflow) { Id = "wbp-as-sec", Order = 0 };
        var block = new Block(BlockTypes.Agent, section)
        {
            Id = "wbp-as-blk",
            Name = "Agent",
            ReplacementTag = "tag",
        };
        const string RUN_ID = "wbp-as-wf_wbp-as-blk_w_r1_1";
        var persistedRow = new AgentRunOutput
        {
            OrganizationId = "org-1",
            BlockId = block.Id,
            Round = 1,
            RunExecutionId = RUN_ID,
            Output = "already persisted",
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = seed.Add(persistedRow);
            _ = await seed.SaveChangesAsync(ct);
        }

        // A crash-replayed persist re-records the round under a fresh row id but the same run id:
        // the frozen-terminal registry replayed the identical answer, so the row stays untouched.
        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = "already persisted" };
        AddAttemptRow(block, round: 1, RUN_ID, "already persisted");

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            AgentRunOutput row = Assert.Single(await read.AgentRunOutputs.AsNoTracking()
                .Where(candidate => candidate.BlockId == "wbp-as-blk").ToListAsync(ct));
            Assert.Equal(persistedRow.Id, row.Id);
        }
    }

    [Fact]
    public async Task PersistBlockResultAsync_AttemptRowWithChangedRunId_UpdatesInPlace()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        var workflow = new Workflow { Id = "wbp-au-wf", Title = "wbp-au-wf" };
        var section = new Section("S", workflow) { Id = "wbp-au-sec", Order = 0 };
        var block = new Block(BlockTypes.Agent, section)
        {
            Id = "wbp-au-blk",
            Name = "Agent",
            ReplacementTag = "tag",
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = seed.Add(new AgentRunOutput
            {
                OrganizationId = "org-1",
                BlockId = block.Id,
                Round = 1,
                RunExecutionId = "wbp-au-wf_wbp-au-blk_w_r1_1",
                Output = "first materialization",
            });
            _ = await seed.SaveChangesAsync(ct);
        }

        // A re-execution that minted a fresh run id owns its round's row: update in place, no
        // second row at the (BlockId, Round) unique index.
        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = "replayed materialization" };
        AddAttemptRow(block, round: 1, "wbp-au-wf_wbp-au-blk_w_r1_2", "replayed materialization");

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            AgentRunOutput row = Assert.Single(await read.AgentRunOutputs.AsNoTracking()
                .Where(candidate => candidate.BlockId == "wbp-au-blk").ToListAsync(ct));
            Assert.Equal("replayed materialization", row.Output);
            Assert.Equal("wbp-au-wf_wbp-au-blk_w_r1_2", row.RunExecutionId);
        }
    }

    private static void AddAttemptRow(Block block, int round, string runExecutionId, string output)
    {
        block.AgentRunOutputs.Add(new AgentRunOutput
        {
            OrganizationId = "org-1",
            BlockId = block.Id,
            Block = block,
            Round = round,
            RunExecutionId = runExecutionId,
            Output = output,
        });
    }

    [Fact]
    public async Task PersistBlockResultAsync_RowsAlreadyPersistedUnderTheirNaturalKey_AreNotDuplicated()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        var workflow = new Workflow { Id = "wbp-nk-wf", Title = "wbp-nk-wf" };
        var section = new Section("S", workflow) { Id = "wbp-nk-sec", Order = 0 };
        var block = new Block(BlockTypes.Agent, section)
        {
            Id = "wbp-nk-blk",
            Name = "Agent",
            ReplacementTag = "tag",
        };
        AgentRunTraceFile RowWithFreshId()
        {
            return new AgentRunTraceFile
            {
                OrganizationId = "org-1",
                BlockId = block.Id,
                RunExecutionId = "wbp-nk-wf_wbp-nk-blk_w_1",
                Attempt = 1,
                Path = "trace/trace.ndjson",
                Sha256 = new string('a', 64),
                SizeBytes = 3,
                Content = [1, 2, 3],
            };
        }

        // The shared layer already committed this pack once - under different row ids, as a
        // crash-replayed block re-execution re-collects it.
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = seed.Add(RowWithFreshId());
            _ = await seed.SaveChangesAsync(ct);
        }

        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = "out" };
        block.AgentRunTraceFiles.Add(RowWithFreshId());

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            List<AgentRunTraceFile> rows = await read.AgentRunTraceFiles.AsNoTracking()
                .Where(f => f.BlockId == "wbp-nk-blk").ToListAsync(ct);
            _ = Assert.Single(rows);
        }
    }

    [Fact]
    public async Task PersistBlockResultAsync_AppendsFailureRecordRows_SkippingAnAlreadyPersistedPass()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        IUserDataDbContextAccessor accessor = _sp.GetRequiredService<IUserDataDbContextAccessor>();

        Block block = await SeedVerifierBlockAsync(factory, "wbp-vr", ct);

        VerificationCriterionRecord Row(string runId, string criterionId)
        {
            return VerificationRow("wbp-vr-exec", runId, criterionId);
        }

        block.VerificationCriterionRecords.Add(Row("wbp-vr-run-1", "totals"));
        block.VerificationCriterionRecords.Add(Row("wbp-vr-run-1", "members-agree"));

        var writer = new WorkflowBlockPersistence(accessor, NullLogger<WorkflowBlockPersistence>.Instance);
        await writer.PersistBlockResultAsync(block, null, null, ct);

        // A crash-replayed re-execution re-collects the same pass's rows under fresh row ids —
        // the persisted pass is skipped whole, never duplicated.
        block.VerificationCriterionRecords.Clear();
        block.VerificationCriterionRecords.Add(Row("wbp-vr-run-1", "totals"));
        block.VerificationCriterionRecords.Add(Row("wbp-vr-run-1", "members-agree"));
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            Assert.Equal(2, await read.VerificationCriterionRecords.AsNoTracking()
                .CountAsync(row => row.WorkflowExecutionId == "wbp-vr-exec", ct));
        }

        // A later round of the same execution carries its own run id: append-only, both kept.
        block.VerificationCriterionRecords.Add(Row("wbp-vr-run-2", "totals"));
        block.VerificationCriterionRecords.Add(Row("wbp-vr-run-2", "members-agree"));
        await writer.PersistBlockResultAsync(block, null, null, ct);

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            List<VerificationCriterionRecord> rows = await read.VerificationCriterionRecords.AsNoTracking()
                .Where(row => row.WorkflowExecutionId == "wbp-vr-exec").ToListAsync(ct);
            Assert.Equal(4, rows.Count);
            Assert.Equal(2, rows.Count(static row => row.RunExecutionId == "wbp-vr-run-1"));
            Assert.Equal(2, rows.Count(static row => row.RunExecutionId == "wbp-vr-run-2"));
        }
    }

    /// <summary>
    /// A completed verifier block whose parent workflow row is seeded, ready to carry criterion
    /// records through the writer. Ids derive from <paramref name="idPrefix"/>.
    /// </summary>
    private static async Task<Block> SeedVerifierBlockAsync(
        IDbContextFactory<UserDataDbContext> factory, string idPrefix, CancellationToken ct)
    {
        var workflow = new Workflow { Id = $"{idPrefix}-wf", Title = $"{idPrefix}-wf" };
        var section = new Section("S", workflow) { Id = $"{idPrefix}-sec", Order = 0 };
        var block = new Block(BlockTypes.Verification, section)
        {
            Id = $"{idPrefix}-blk",
            Name = "Verifier",
            ReplacementTag = "verifier",
        };
        await using (UserDataDbContext seed = await factory.CreateDbContextAsync(ct))
        {
            _ = seed.Workflows.Add(workflow);
            _ = await seed.SaveChangesAsync(ct);
        }

        block.ExecutionState = new BlockExecutionState(block);
        block.ExecutionState.MarkAsCompleted();
        _ = new BlockResponse(block) { RawResponse = "judgment" };

        return block;
    }

    private static VerificationCriterionRecord VerificationRow(
        string workflowExecutionId,
        string runId,
        string criterionId,
        string met = "met",
        string? memberTag = null,
        string? configuredRedoMode = null)
    {
        return new VerificationCriterionRecord
        {
            OrganizationId = "org-1",
            WorkflowExecutionId = workflowExecutionId,
            WorkflowTypeId = "corp-part-1",
            VerifierBlockId = "stable-verifier",
            MemberTag = memberTag,
            Round = 1,
            RunExecutionId = runId,
            CriterionId = criterionId,
            CriterionLabel = "Totals recompute",
            CriterionDefinitionHash = new string('b', 64),
            Met = met,
            ConfiguredRedoMode = configuredRedoMode,
            JudgedAtUtc = DateTime.UtcNow,
        };
    }
}
