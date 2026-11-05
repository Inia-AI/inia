using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data.Common;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Pins the jsonb query path for the redo-mode stamps on a real PostgreSQL database:
/// <c>"Properties"->'executionState'->'runPasses'</c> with camelCase keys returns the rows, and
/// the PascalCase path silently returns zero rows — the persisted blob is written by
/// System.Text.Json under a camelCase naming policy that ignores the type's Newtonsoft
/// attributes, so a PascalCase path is the query that looks right and finds nothing.
/// </summary>
public class RunPassRedoModeJsonbQueryTests : IClassFixture<PostgresUserDataDatabaseFixture>
{
    private readonly PostgresUserDataDatabaseFixture _fixture;

    public RunPassRedoModeJsonbQueryTests(PostgresUserDataDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunPassRedoModes_AreQueryableThroughTheCamelCaseJsonbPath_AndInvisibleToPascalCase()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(CleanupServiceTestHarness.SKIP_REASON);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        string workflowId = $"jsonb-wf-{Guid.NewGuid():N}"[..24];

        var workflow = new Workflow { Id = workflowId, Title = "jsonb probe" };
        var section = new Section("S", workflow) { Id = $"{workflowId}-sec", Order = 0 };
        var props = new AgentProperties
        {
            WorkerSkills =
            [
                new AgentWorkerSkill { Name = "skill", Description = "Handles the work.", Content = "work" },
            ],
        };
        props.SetExecutionState(false, []);
        props.ExecutionState!.RunPasses.Add(new AgentRunPass
        {
            Round = 2,
            Pass = 1,
            ExecutionId = $"{workflowId}_blk_w_r2_1",
            Status = "Succeeded",
            ConfiguredRedoMode = "Revise",
            ActualRedoMode = "Scratch",
        });
        string blockId = $"{workflowId}-blk";
        _ = new Block(BlockTypes.Agent, section)
        {
            Id = blockId,
            Name = "Member",
            ReplacementTag = "member",
            Properties = props,
        };

        await using (UserDataDbContext seed = _fixture.CreateContext())
        {
            _ = seed.Workflows.Add(workflow);
            _ = await seed.SaveChangesAsync(ct);
        }

        await using UserDataDbContext read = _fixture.CreateContext();
        DbConnection connection = read.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        Assert.Equal(1, await CountAsync(
            connection, blockId, """[{"configuredRedoMode":"Revise","actualRedoMode":"Scratch"}]""", ct));
        Assert.Equal(0, await CountAsync(
            connection, blockId, """[{"ConfiguredRedoMode":"Revise"}]""", ct));
    }

    private static async Task<long> CountAsync(
        DbConnection connection,
        string blockId,
        string containsJson,
        CancellationToken ct)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM "UserData"."Blocks"
            WHERE "Id" = @blockId
              AND "Properties"->'executionState'->'runPasses' @> @contains::jsonb
            """;
        command.Parameters.Add(new NpgsqlParameter("blockId", blockId));
        command.Parameters.Add(new NpgsqlParameter("contains", containsJson));

        object? count = await command.ExecuteScalarAsync(ct);

        return (long)count!;
    }
}
