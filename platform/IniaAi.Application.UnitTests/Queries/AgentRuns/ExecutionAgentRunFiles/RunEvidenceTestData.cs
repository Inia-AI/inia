using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;

namespace IniaAi.Application.UnitTests.Queries.AgentRuns.ExecutionAgentRunFiles;

/// <summary>
/// One executed run with an Agent block and its trace rows, started by a named user, for the
/// run-evidence access tests.
/// </summary>
internal static class RunEvidenceTestData
{
    public const string ORG_ID = "org-evidence";
    public const string OTHER_ORG_ID = "org-evidence-other";
    public const string SERVICE_USER_ID = "service-user";
    public const string ANALYST_ID = "analyst-user";
    public const string OTHER_SERVICE_USER_ID = "other-org-service-user";
    public const string TRACE_PATH = "trace/trace.ndjson";
    public const string ARTIFACT_PATH = "out/result.json";

    public sealed record SeededRun(WorkflowExecution Execution, Block Agent, Block Merge);

    public static async Task SeedUsersAsync(UserDataDbContext context)
    {
        await context.Users.AddRangeAsync(
            [
                new User { Id = SERVICE_USER_ID, UserName = SERVICE_USER_ID, OrganizationId = ORG_ID, IsSystem = true },
                new User { Id = ANALYST_ID, UserName = ANALYST_ID, OrganizationId = ORG_ID, IsSystem = false },
                new User { Id = OTHER_SERVICE_USER_ID, UserName = OTHER_SERVICE_USER_ID, OrganizationId = OTHER_ORG_ID, IsSystem = true },
            ],
            TestContext.Current.CancellationToken);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
    }

    public static async Task<SeededRun> SeedRunAsync(UserDataDbContext context, string organizationId, string startedBy)
    {
        var workflow = new Workflow($"Run {Guid.NewGuid():N}") { OrganizationId = organizationId };
        var section = new Section("Section", workflow);
        var agent = new Block(BlockTypes.Agent, section, "agent_tag") { Name = "Agent" };
        var merge = new Block(BlockTypes.Merge, section, "merge_tag") { Name = "Merge" };
        var execution = new WorkflowExecution
        {
            OrganizationId = organizationId,
            WorkflowId = workflow.Id,
            UserId = startedBy,
            Title = "Run",
        };

        _ = await context.Workflows.AddAsync(workflow, TestContext.Current.CancellationToken);
        _ = await context.WorkflowExecutions.AddAsync(execution, TestContext.Current.CancellationToken);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        return new SeededRun(execution, agent, merge);
    }

    public static async Task SeedTraceAsync(
        UserDataDbContext context,
        string organizationId,
        string blockId,
        string runExecutionId,
        int attempt,
        string path,
        string content,
        DateTime collectedAt)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        _ = await context.AgentRunTraceFiles.AddAsync(
            new AgentRunTraceFile
            {
                OrganizationId = organizationId,
                BlockId = blockId,
                RunExecutionId = runExecutionId,
                Attempt = attempt,
                Path = path,
                Sha256 = new string('a', 64),
                SizeBytes = bytes.Length,
                Content = bytes,
                CollectedAt = collectedAt,
            },
            TestContext.Current.CancellationToken);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
    }
}
