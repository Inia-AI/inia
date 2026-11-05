using System.Text;
using ErrorOr;
using IniaAi.Application.Queries.AgentRuns.GetAgentRunActivity;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;
using AgentActivityOutcome = IniaAi.WebApi.Contracts.Enums.AgentActivityOutcome;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.AgentRuns.GetAgentRunActivity;

public class GetAgentRunActivityQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";
    private const string TRACE_PACK_PATH = "trace/trace.ndjson";
    private const string DEFAULT_RUN_EXECUTION_ID = "exec-1";
    private const string LEDGER_WORKFLOW_EXECUTION_ID = "workflow-execution";
    private const string LEDGER_STABLE_BLOCK_ID = "stable-block";

    private static readonly DateTime _firstCollectedAt = new(2026, 8, 13, 13, 0, 0, DateTimeKind.Utc);

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly GetAgentRunActivityQueryHandler _handler;

    public GetAgentRunActivityQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new GetAgentRunActivityQueryHandler(unitOfWork);
    }

    private static byte[] TracePack(string documentName)
    {
        string[] lines =
        [
            """{"uuid":"s1","at":"2026-08-13T13:00:00Z","type":"stage","event":"run.started","executionId":"e","attempt":1,"detail":{}}""",
            $$"""{"uuid":"t1","at":"2026-08-13T13:00:10Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{\"command\":\"pdftotext -layout docs/{{documentName}} text.txt\"}","outputBytes":10,"durationMs":20,"errorClass":null}""",
            """{"uuid":"n1","at":"2026-08-13T13:00:20Z","type":"message","seq":1,"message":{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Reading the statement around page 40 now."}]}}}""",
            """{"uuid":"s2","at":"2026-08-13T13:01:40Z","type":"stage","event":"run.completed","executionId":"e","attempt":1,"detail":{}}""",
        ];
        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    private static byte[] DescribedTracePack()
    {
        string[] lines =
        [
            """{"uuid":"s1","at":"2026-08-13T13:00:00Z","type":"stage","event":"run.started","executionId":"e","attempt":1,"detail":{}}""",
            """{"uuid":"t1","at":"2026-08-13T13:00:10Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{\"command\":\"ls -la out/\",\"description\":\"Check out directory contents\"}","outputBytes":10,"durationMs":20,"errorClass":null}""",
            """{"uuid":"t2","at":"2026-08-13T13:00:30Z","type":"tool_call","toolUseId":"t2","toolName":"Bash","inputTruncated":"{\"command\":\"grep -n 'BALANCE SHEET' text.txt\",\"description\":\"Locate balance sheet statement in 2025 report\"}","outputBytes":10,"durationMs":20,"errorClass":null}""",
            """{"uuid":"s2","at":"2026-08-13T13:01:40Z","type":"stage","event":"run.completed","executionId":"e","attempt":1,"detail":{}}""",
        ];
        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    private sealed class StubTidier : IniaAi.Common.Interfaces.Services.AgentRuntime.Activity.IAgentNarrationTidier
    {
        public Task<IReadOnlyDictionary<string, string?>> TidyAsync(
            IReadOnlyList<IniaAi.Common.Interfaces.Services.AgentRuntime.Activity.AgentNarrationLine> lines,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, string?> tidied =
                lines.ToDictionary(static line => line.Key, static _ => (string?)"Read the statement");
            return Task.FromResult(tidied);
        }
    }

    /// <summary>
    /// Rewrites a tool description that lands on the accounts and suppresses one that does
    /// not — the shape the real prompt is asked for, expressed as the three-state map.
    /// </summary>
    private sealed class ToolActionTidier : IniaAi.Common.Interfaces.Services.AgentRuntime.Activity.IAgentNarrationTidier
    {
        public List<IniaAi.Common.Interfaces.Services.AgentRuntime.Activity.AgentActivityLineKind> Kinds { get; } = [];

        public Task<IReadOnlyDictionary<string, string?>> TidyAsync(
            IReadOnlyList<IniaAi.Common.Interfaces.Services.AgentRuntime.Activity.AgentNarrationLine> lines,
            CancellationToken cancellationToken = default)
        {
            Kinds.AddRange(lines.Select(static line => line.Kind));
            IReadOnlyDictionary<string, string?> tidied = lines.ToDictionary(
                static line => line.Key,
                static line => line.Text.StartsWith("Locate", StringComparison.Ordinal)
                    ? "Finding the 2025 balance sheet"
                    : null);
            return Task.FromResult(tidied);
        }
    }

    [Fact]
    public async Task Handle_ClassifiedOverride_RendersTheSameTraceThroughTheClassifier()
    {
        Block block = await SeedExecutedBlockAsync(ORG_ID);
        await SeedTraceFileAsync(ORG_ID, block.Id, TRACE_PACK_PATH, 1, TracePack("Doc.pdf"));

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id, "classified"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Beats, static beat => beat.Label.StartsWith("Converted"));
    }

    [Fact]
    public async Task Handle_ATidierRewritesNarrationBeatsWhenPresent()
    {
        Block block = await SeedExecutedBlockAsync(ORG_ID);
        await SeedTraceFileAsync(ORG_ID, block.Id, TRACE_PACK_PATH, 1, TracePack("Doc.pdf"));
        var handler = new GetAgentRunActivityQueryHandler(
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(), new StubTidier());

        ErrorOr<AgentActivityDigestDto> result = await handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id, "narrated"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Beats, static beat => beat.Label == "Read the statement");
    }

    [Fact]
    public async Task Handle_ToolDescriptions_ReachTheTidierAndASuppressedOneNeverShows()
    {
        Block block = await SeedExecutedBlockAsync(ORG_ID);
        await SeedTraceFileAsync(ORG_ID, block.Id, TRACE_PACK_PATH, 1, DescribedTracePack());
        var tidier = new ToolActionTidier();
        var handler = new GetAgentRunActivityQueryHandler(
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(), tidier);

        ErrorOr<AgentActivityDigestDto> result = await handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.All(
            tidier.Kinds,
            static kind => Assert.Equal(
                IniaAi.Common.Interfaces.Services.AgentRuntime.Activity.AgentActivityLineKind.ToolAction,
                kind));
        AgentActivityBeatDto beat = Assert.Single(result.Value.Beats);
        Assert.Equal("Finding the 2025 balance sheet", beat.Label);
        Assert.Equal(30, beat.AtSeconds);
    }

    [Fact]
    public async Task Handle_WithoutATidier_KeepsTheRawToolDescriptionsRatherThanEmptyingTheFeed()
    {
        Block block = await SeedExecutedBlockAsync(ORG_ID);
        await SeedTraceFileAsync(ORG_ID, block.Id, TRACE_PACK_PATH, 1, DescribedTracePack());

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(
            ["Check out directory contents", "Locate balance sheet statement in 2025 report"],
            result.Value.Beats.Select(static beat => beat.Label));
    }

    [Fact]
    public async Task Handle_ProjectsTheNewestAttemptsTracePack()
    {
        Block block = await SeedExecutedBlockAsync(ORG_ID);
        await SeedTraceFileAsync(ORG_ID, block.Id, TRACE_PACK_PATH, 1, TracePack("First-Attempt.pdf"));
        await SeedTraceFileAsync(ORG_ID, block.Id, TRACE_PACK_PATH, 2, TracePack("Second-Attempt.pdf"));
        await SeedTraceFileAsync(ORG_ID, block.Id, "trace/sub/agent-1.jsonl", 2, [1, 2, 3]);

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(AgentActivityOutcome.Succeeded, result.Value.Outcome);
        Assert.Equal(100, result.Value.Receipt.DurationSeconds);
        Assert.Contains("Second-Attempt.pdf", result.Value.Receipt.DocumentNames);
        Assert.DoesNotContain("First-Attempt.pdf", result.Value.Receipt.DocumentNames);
        Assert.Equal(2, result.Value.Beats.Count);
        Assert.Equal(10, result.Value.Beats[0].AtSeconds);
    }

    [Fact]
    public async Task Handle_BlockWithoutATrace_AnswersAnEmptyDigestNotAnError()
    {
        Block block = await SeedExecutedBlockAsync(ORG_ID);

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(AgentActivityOutcome.Unknown, result.Value.Outcome);
        Assert.Empty(result.Value.Beats);
        Assert.NotEmpty(result.Value.Phases);
    }

    [Fact]
    public async Task Handle_CrossOrgBlock_IsIndistinguishableFromNoTrace()
    {
        Block block = await SeedExecutedBlockAsync(OTHER_ORG_ID);
        await SeedTraceFileAsync(OTHER_ORG_ID, block.Id, TRACE_PACK_PATH, 1, TracePack("Foreign.pdf"));

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Beats);
        Assert.Empty(result.Value.Receipt.DocumentNames);
    }

    [Fact]
    public async Task Handle_NoSelector_AnswersTheNewestRunEvenWhenTheLedgerNamesEarlierRounds()
    {
        Block block = await SeedExecutedBlockWithLedgerAsync(ORG_ID, 1, 2);
        await SeedRoundTraceAsync(ORG_ID, block.Id, 1, "Round-One.pdf");
        await SeedRoundTraceAsync(ORG_ID, block.Id, 2, "Round-Two.pdf");

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Round-Two.pdf", result.Value.Receipt.DocumentNames);
        Assert.DoesNotContain("Round-One.pdf", result.Value.Receipt.DocumentNames);
    }

    /// <summary>
    /// Newest means collected last, not the highest attempt number. An attempt counts machinery
    /// retries inside one invocation and restarts at one every round, so a round-one leg that
    /// needed a retry carries a higher attempt than the round-two leg that followed it — and an
    /// unselected read that sorted on attempt first answered the older round.
    /// </summary>
    [Fact]
    public async Task Handle_NoSelector_AnswersTheLastCollectedRunEvenWhenAnEarlierRoundRetried()
    {
        Block block = await SeedExecutedBlockWithLedgerAsync(ORG_ID, 1, 2);
        await SeedTraceFileAsync(
            ORG_ID, block.Id, TRACE_PACK_PATH, attempt: 2, TracePack("Round-One-Retry.pdf"),
            RunIdForRound(1), _firstCollectedAt.AddMinutes(1));
        await SeedTraceFileAsync(
            ORG_ID, block.Id, TRACE_PACK_PATH, attempt: 1, TracePack("Round-Two.pdf"),
            RunIdForRound(2), _firstCollectedAt.AddMinutes(2));

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Round-Two.pdf", result.Value.Receipt.DocumentNames);
        Assert.DoesNotContain("Round-One-Retry.pdf", result.Value.Receipt.DocumentNames);
    }

    [Fact]
    public async Task Handle_ARound_AnswersTheRunItsLedgerRecordsNotTheNewest()
    {
        Block block = await SeedExecutedBlockWithLedgerAsync(ORG_ID, 1, 2);
        await SeedRoundTraceAsync(ORG_ID, block.Id, 1, "Round-One.pdf");
        await SeedRoundTraceAsync(ORG_ID, block.Id, 2, "Round-Two.pdf");

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id, Round: 1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Round-One.pdf", result.Value.Receipt.DocumentNames);
        Assert.DoesNotContain("Round-Two.pdf", result.Value.Receipt.DocumentNames);
    }

    [Fact]
    public async Task Handle_ARoundTheBlockBoughtNothingIn_AnswersAnEmptyDigestNotAnotherRoundsRun()
    {
        Block block = await SeedExecutedBlockWithLedgerAsync(ORG_ID, 1);
        await SeedRoundTraceAsync(ORG_ID, block.Id, 1, "Round-One.pdf");

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id, Round: 2),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(AgentActivityOutcome.Unknown, result.Value.Outcome);
        Assert.Empty(result.Value.Beats);
        Assert.Empty(result.Value.Receipt.DocumentNames);
    }

    [Fact]
    public async Task Handle_ARunSelector_TakesPrecedenceOverTheRound()
    {
        Block block = await SeedExecutedBlockWithLedgerAsync(ORG_ID, 1, 2, 3);
        await SeedRoundTraceAsync(ORG_ID, block.Id, 1, "Round-One.pdf");
        await SeedRoundTraceAsync(ORG_ID, block.Id, 2, "Round-Two.pdf");
        await SeedRoundTraceAsync(ORG_ID, block.Id, 3, "Round-Three.pdf");

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(
                ORG_ID,
                USER_ID,
                block.Id,
                Round: 1,
                RunExecutionId: RunIdForRound(2)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Round-Two.pdf", result.Value.Receipt.DocumentNames);
        Assert.DoesNotContain("Round-One.pdf", result.Value.Receipt.DocumentNames);
        Assert.DoesNotContain("Round-Three.pdf", result.Value.Receipt.DocumentNames);
    }

    [Fact]
    public async Task Handle_ARoundOnACrossOrgBlock_IsIndistinguishableFromNoTrace()
    {
        Block block = await SeedExecutedBlockWithLedgerAsync(OTHER_ORG_ID, 1);
        await SeedRoundTraceAsync(OTHER_ORG_ID, block.Id, 1, "Foreign.pdf");

        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, block.Id, Round: 1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Beats);
        Assert.Empty(result.Value.Receipt.DocumentNames);
    }

    [Fact]
    public async Task Handle_ARoundOnABlockThatNeverRan_AnswersAnEmptyDigestNotAnError()
    {
        ErrorOr<AgentActivityDigestDto> result = await _handler.Handle(
            new GetAgentRunActivityQuery(ORG_ID, USER_ID, "block-that-never-ran", Round: 1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(AgentActivityOutcome.Unknown, result.Value.Outcome);
        Assert.Empty(result.Value.Beats);
        Assert.Empty(result.Value.Receipt.DocumentNames);
    }

    private async Task<Block> SeedExecutedBlockAsync(string organizationId)
    {
        var workflow = new Workflow("Executed Agent Workflow") { OrganizationId = organizationId };
        var section = new Section("Section", workflow);
        var block = new Block(BlockTypes.Agent, section) { Name = "Agent" };

        _ = await _context.Workflows.AddAsync(workflow, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return block;
    }

    private async Task SeedTraceFileAsync(
        string organizationId,
        string blockId,
        string path,
        int attempt,
        byte[] content,
        string runExecutionId = DEFAULT_RUN_EXECUTION_ID,
        DateTime? collectedAt = null)
    {
        var file = new AgentRunTraceFile
        {
            OrganizationId = organizationId,
            BlockId = blockId,
            RunExecutionId = runExecutionId,
            Attempt = attempt,
            Path = path,
            Sha256 = new string('a', 64),
            SizeBytes = content.Length,
            Content = content,
            CollectedAt = collectedAt ?? DateTime.UtcNow,
        };

        _ = await _context.AgentRunTraceFiles.AddAsync(file, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task<Block> SeedExecutedBlockWithLedgerAsync(string organizationId, params int[] rounds)
    {
        var workflow = new Workflow("Executed Agent Workflow") { OrganizationId = organizationId };
        var section = new Section("Section", workflow);
        var block = new Block(BlockTypes.Agent, section) { Name = "Agent" };
        AgentProperties properties = (AgentProperties)block.Properties;
        properties.SetExecutionState(true, []);

        foreach (int round in rounds)
        {
            properties.ExecutionState!.RunPasses.Add(new AgentRunPass
            {
                Round = round,
                Pass = AgentRunIds.FIRST_PASS,
                ExecutionId = RunIdForRound(round),
            });
        }

        _ = await _context.Workflows.AddAsync(workflow, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return block;
    }

    /// <summary>
    /// One round's trace pack, collected in round order so the newest pack on the block is the
    /// last round's — what an unselected read answers, and what a round selector must overrule.
    /// </summary>
    private Task SeedRoundTraceAsync(string organizationId, string blockId, int round, string documentName)
    {
        return SeedTraceFileAsync(
            organizationId,
            blockId,
            TRACE_PACK_PATH,
            AgentRunIds.FIRST_PASS,
            TracePack(documentName),
            RunIdForRound(round),
            _firstCollectedAt.AddMinutes(round));
    }

    private static string RunIdForRound(int round)
    {
        return AgentRunIds.For(
            LEDGER_WORKFLOW_EXECUTION_ID,
            LEDGER_STABLE_BLOCK_ID,
            AgentRunIds.WORKER_ROLE,
            round,
            AgentRunIds.FIRST_PASS);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
