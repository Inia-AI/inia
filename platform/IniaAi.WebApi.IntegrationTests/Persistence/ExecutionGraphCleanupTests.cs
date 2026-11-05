using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Graph;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// The execution graph is a cache keyed by the execution id with no foreign key, so nothing cascades
/// it away: the cleanup service deletes the nodes and edges of every execution it purges and leaves
/// the graph of a surviving execution alone. Runs against a throwaway migrated PostgreSQL database;
/// skips without one.
/// </summary>
public class ExecutionGraphCleanupTests : IClassFixture<PostgresUserDataDatabaseFixture>
{
    private const int RETENTION_DAYS = 7;

    private readonly PostgresUserDataDatabaseFixture _fixture;

    public ExecutionGraphCleanupTests(PostgresUserDataDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static GraphNode Node(string runId, string key)
    {
        return new GraphNode { RunId = runId, Key = key, Kind = "Block", Label = $"{key} label" };
    }

    private static GraphEdge Edge(string runId)
    {
        return new GraphEdge { RunId = runId, SourceId = "InputContext", TargetId = "Summary", Label = "InputContext.answer" };
    }

    [Fact]
    public async Task CleanupAsync_HardDeletingTheExecution_DeletesItsGraphAndKeepsTheSurvivorsGraph()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(CleanupServiceTestHarness.SKIP_REASON);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        string org = $"org-{Guid.NewGuid():N}"[..20];
        DateTime pastCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1));

        var purgedExecution = new WorkflowExecution
        {
            OrganizationId = org,
            Title = "purged",
            IsDeleted = true,
            DeletedAt = pastCutoff,
        };
        var survivingExecution = new WorkflowExecution
        {
            OrganizationId = org,
            Title = "surviving",
        };

        await using (UserDataDbContext seed = _fixture.CreateContext())
        {
            seed.WorkflowExecutions.AddRange(purgedExecution, survivingExecution);
            seed.GraphNodes.AddRange(
                Node(purgedExecution.Id, "InputContext"),
                Node(purgedExecution.Id, "Summary"),
                Node(survivingExecution.Id, "InputContext"));
            seed.GraphEdges.AddRange(Edge(purgedExecution.Id), Edge(survivingExecution.Id));
            _ = await seed.SaveChangesAsync(ct);
        }

        CleanupResult result;
        await using (UserDataDbContext context = _fixture.CreateContext())
        {
            result = await CleanupServiceTestHarness.CreateService(context).CleanupAsync(org, RETENTION_DAYS, ct);
        }

        await using UserDataDbContext read = _fixture.CreateContext();

        Assert.Empty(result.Errors);
        Assert.False(await read.GraphNodes.AnyAsync(node => node.RunId == purgedExecution.Id, ct));
        Assert.False(await read.GraphEdges.AnyAsync(edge => edge.RunId == purgedExecution.Id, ct));
        Assert.Equal(1, await read.GraphNodes.CountAsync(node => node.RunId == survivingExecution.Id, ct));
        Assert.Equal(1, await read.GraphEdges.CountAsync(edge => edge.RunId == survivingExecution.Id, ct));
    }
}
