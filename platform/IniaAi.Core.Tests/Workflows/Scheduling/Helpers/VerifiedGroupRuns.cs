using ErrorOr;
using IniaAi.Configuration.Platform;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Scheduling.Helpers;

/// <summary>
/// The shared harness of the verified-group suites: the stub runtime wired for a full
/// verify→redo→re-verify loop, the run-id suffixes the scheduler stamps per leg, and the
/// readers the assertions share.
/// </summary>
public static class VerifiedGroupRuns
{
    public const string MODEL = "claude-sonnet-4-6";

    public const string WORKER_R1 = "_w_r1_1";
    public const string WORKER_R2 = "_w_r2_1";
    public const string WORKER_R2_REPLAY = "_w_r2_2";
    public const string WORKER_R3 = "_w_r3_1";
    public const string VERIFIER_R1 = "_v_r1_1";
    public const string VERIFIER_R1_REPLAY = "_v_r1_2";
    public const string VERIFIER_R2 = "_v_r2_1";
    public const string VERIFIER_R3 = "_v_r3_1";

    /// <summary>
    /// A start that registers, one poll to a settled monitor, an acknowledgeable empty trace
    /// pack. A verifier run answers with the supplied verdict for its round; a worker run
    /// echoes its own run id — or the supplied output — so which leg produced a settled
    /// response is readable off the persisted block.
    /// </summary>
    public static StubAgentRuntimeClient ArrangeGroupRuns(
        IServiceProvider sp,
        Func<string, string> verifierOutput,
        Func<string, string>? workerOutput = null,
        decimal runCostUsd = 0m)
    {
        var stub = Assert.IsType<StubAgentRuntimeClient>(sp.GetRequiredService<IAgentRuntimeClient>());
        stub.StartRequests.Clear();
        stub.CompletedExecutionIds.Clear();
        stub.CancelledExecutionIds.Clear();

        var startedAt = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

        stub.OnStartRun = request => new AgentRunMonitor
        {
            ExecutionId = request.ExecutionId,
            Status = AgentRunStatus.Running,
            Attempt = 1,
            PollInterval = TimeSpan.FromMilliseconds(1),
        };
        stub.OnGetRun = executionId => new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = AgentRunStatus.Succeeded,
            Attempt = 1,
            CreatedAt = startedAt,
            UpdatedAt = startedAt.AddSeconds(45),
            EstimatedCostUsd = runCostUsd,
            Result = new AgentRunResult(
                executionId.Contains("_v_", StringComparison.Ordinal)
                    ? verifierOutput(executionId)
                    : (workerOutput ?? (static id => $"member deliverable of {id}"))(executionId),
                null),
        };
        stub.OnCompleteRun = executionId => new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = AgentRunStatus.Succeeded,
            Attempt = 1,
        };
        stub.OnListRunFiles = _ => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(
            Array.Empty<AgentRunFileEntry>());

        return stub;
    }

    public static AgentProperties WorkerProperties(string prompt)
    {
        return new AgentProperties
        {
            PromptTemplate = prompt,
            WorkerSkills =
            [
                new AgentWorkerSkill
                {
                    Name = "skill",
                    Description = "Handles the work.",
                    Content = "work",
                },
            ],
            Model = MODEL,
        };
    }

    public static List<AgentRunStartRequest> RequestsEndingWith(StubAgentRuntimeClient stub, string suffix)
    {
        return [.. stub.StartRequests.Where(request =>
            request.ExecutionId.EndsWith(suffix, StringComparison.Ordinal))];
    }

    public static AgentRunStartRequest RequestOfWorker(
        StubAgentRuntimeClient stub,
        string roundSuffix,
        string promptContains)
    {
        return Assert.Single(RequestsEndingWith(stub, roundSuffix), request =>
            request.Prompt.Contains(promptContains, StringComparison.Ordinal));
    }

    public static Block ReadPersistedBlockByName(IServiceProvider sp, string blockName)
    {
        IDbContextFactory<UserDataDbContext> factory =
            sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext read = factory.CreateDbContext();

        return read.Blocks.AsNoTracking().Single(block => block.Name == blockName);
    }

    /// <summary>
    /// A document with its bytes in hand and no storage behind it — what a live suite stages on
    /// a block when it wants the model to read something without a document pipeline run.
    /// </summary>
    public static Document CreateInMemoryDocument(string organizationId, string name, string content)
    {
        var metadata = new FileMetadata(organizationId, name, Encoding.UTF8.GetBytes(content));

        return new Document(metadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(name))) { Name = name };
    }

    public static ReportParagraph ReadParagraph(IServiceProvider sp, string blockId)
    {
        IDbContextFactory<UserDataDbContext> factory =
            sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext read = factory.CreateDbContext();

        return read.Set<ReportParagraph>().AsNoTracking().Single(paragraph => paragraph.BlockId == blockId);
    }

    public static List<WorkflowExecutionLog> ReadLogs(IServiceProvider sp, string workflowExecutionId, string blockName)
    {
        IDbContextFactory<UserDataDbContext> factory =
            sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext read = factory.CreateDbContext();

        return [.. read.Set<WorkflowExecutionLog>().AsNoTracking().Where(log =>
            log.WorkflowExecutionId == workflowExecutionId && log.BlockName == blockName)];
    }

    /// <summary>Every leg of the group declares the same deadline, so the window arithmetic is one number.</summary>
    public static void DeclareDeadlines(Workflow workflow, int deadlineMinutes)
    {
        foreach (Block block in workflow.GetAllBlocks())
        {
            if (block.Properties is IAgentRunProperties agentProps)
            {
                agentProps.DeadlineMinutes = deadlineMinutes;
            }
        }
    }

    /// <summary>
    /// The largest per-leg deadline that leaves <paramref name="fillPercent"/> of a
    /// <paramref name="windowHours"/> window to one leg, its machinery retries and its outage wait. One leg fits;
    /// a whole repair round — a redo stage plus the re-verify pass that judges it — does not.
    /// Derived from the deployment's own margin, patience and retry count, so a settings change
    /// moves the figure instead of quietly leaving a hardcoded one that no longer straddles the
    /// window. Each suite asserts or skips on the result as suits it.
    /// </summary>
    public static int DeadlineFillingWindow(AgentRuntimeSettings settings, int windowHours, int fillPercent)
    {
        int overheadMinutes = (int)Math.Ceiling(
            (settings.RunDeadlineMarginSeconds + settings.AdmissionPatienceSeconds) / 60.0);
        int passes = Math.Max(1, settings.MaxRunPasses);
        int outageWaitMinutes = passes > 1
            ? (int)Math.Ceiling(settings.UpstreamErrorRetryDelaySeconds / 60.0)
            : 0;

        return (((windowHours * 60 * fillPercent / 100) - outageWaitMinutes) / passes) - overheadMinutes;
    }

    /// <summary>
    /// A deployment that sweeps aged executions, so the run-time window interlock is live. The
    /// suites register no janitor options otherwise, which leaves the interlock inert by design.
    /// </summary>
    public static Action<IServiceCollection> SweepingJanitor(int maxLifetimeHours)
    {
        return services => services.Replace(
            ServiceDescriptor.Singleton<IOptions<WorkflowExecutionJanitorSettings>>(
                Options.Create(new WorkflowExecutionJanitorSettings
                {
                    Enabled = true,
                    MaxLifetimeHours = maxLifetimeHours,
                })));
    }

    /// <summary>
    /// A terminal bundle the way the runtime writes one: a gzipped JSON manifest whose session
    /// id is the leg's own run id, stamped unless <paramref name="stampModel"/> is null.
    /// </summary>
    public static byte[] StampedBundle(string sessionId, string? stampModel = MODEL)
    {
        string version = stampModel is null
            ? string.Empty
            : $$""", "version": { "bundleFormat": "1", "sidecar": { "build": "test" }, "sdk": "2.1.34", "model": "{{stampModel}}" }""";

        return WorkflowJsonCompression.Compress(
            $$"""{ "sessionId": "{{sessionId}}", "workspacePath": "/workspace", "files": {}{{version}} }""");
    }

    public static string ManifestSessionIdOf(byte[] bundle)
    {
        using JsonDocument manifest = JsonDocument.Parse(WorkflowJsonCompression.Decompress(bundle));

        return manifest.RootElement.GetProperty("sessionId").GetString()!;
    }
}
