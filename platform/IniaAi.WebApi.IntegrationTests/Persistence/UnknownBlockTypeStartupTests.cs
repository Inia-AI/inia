using IniaAi.Domain;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Startup keeps every stored block, whatever its type. A block of a type this build does not
/// register (a closed module's type, a type an extension adds, or one a later release added) stays
/// with its settings, responses and logs: the editor shows it as unavailable and a run refuses it. Runs
/// against a throwaway migrated PostgreSQL database; skips without one.
/// </summary>
public class UnknownBlockTypeStartupTests : IClassFixture<PostgresUserDataDatabaseFixture>
{
    private const BlockTypes TYPE_NO_REGISTRATION_KNOWS = (BlockTypes)999;
    private const string SETTINGS_OF_A_TYPE_A_LATER_RELEASE_NAMED = """{"type":"aLaterType","threshold":3}""";

    private readonly PostgresUserDataDatabaseFixture _fixture;

    public UnknownBlockTypeStartupTests(PostgresUserDataDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Startup_KeepsEveryBlock_WithItsSettingsResponsesAndLogs_WhateverItsType()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(CleanupServiceTestHarness.SKIP_REASON);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        string org = $"org-{Guid.NewGuid():N}"[..20];

        var execution = new WorkflowExecution { OrganizationId = org, Title = "unknown-block-type-startup" };
        var root = new Workflow { Id = Guid.NewGuid().ToString(), Title = "unknown-block-type-startup" };
        var section = new Section("S", root) { Id = Guid.NewGuid().ToString(), Title = "S" };

        Block module = SeedBlock(section, BlockTypes.Json2Xlsx);
        Block unknown = SeedBlock(section, BlockTypes.Merge);
        unknown.Type = TYPE_NO_REGISTRATION_KNOWS;
        unknown.Properties = new UnavailableBlockProperties(UnavailableBlockProperties.UNNAMED_TYPE, SETTINGS_OF_A_TYPE_A_LATER_RELEASE_NAMED);
        Block[] seeded = [module, unknown];

        await using (UserDataDbContext seed = _fixture.CreateContext())
        {
            _ = seed.WorkflowExecutions.Add(execution);
            _ = seed.Workflows.Add(root);
            seed.WorkflowExecutionLogs.AddRange(seeded.Select(block => Log(execution, block)));
            _ = await seed.SaveChangesAsync(ct);
        }

        await using (UserDataDbContext startup = _fixture.CreateContext())
        {
            new DatabaseMigrationHelper(NullLogger<DatabaseMigrationHelper>.Instance).MigrateDatabase(startup);
        }

        await using UserDataDbContext read = _fixture.CreateContext();
        string[] seededIds = [.. seeded.Select(block => block.Id)];

        Block readUnknown = await read.Blocks.AsNoTracking().SingleAsync(block => block.Id == unknown.Id, ct);
        Assert.Equal(TYPE_NO_REGISTRATION_KNOWS, readUnknown.Type);
        UnavailableBlockProperties settings = Assert.IsType<UnavailableBlockProperties>(readUnknown.Properties);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SETTINGS_OF_A_TYPE_A_LATER_RELEASE_NAMED), JsonNode.Parse(settings.StoredJson())), settings.StoredJson());
        Assert.Equal(
            seededIds.Order(),
            await read.Blocks.Where(block => seededIds.Contains(block.Id)).Select(block => block.Id).OrderBy(id => id).ToListAsync(ct));
        Assert.Equal(
            seededIds.Order(),
            await read.BlockResponses.Where(response => seededIds.Contains(response.BlockId)).Select(response => response.BlockId).OrderBy(id => id).ToListAsync(ct));
        Assert.Equal(
            seededIds.Order(),
            await read.WorkflowExecutionLogs.Where(log => seededIds.Contains(log.BlockId!)).Select(log => log.BlockId!).OrderBy(id => id).ToListAsync(ct));
    }

    private static Block SeedBlock(Section section, BlockTypes type)
    {
        var block = new Block(type, section)
        {
            Id = Guid.NewGuid().ToString(),
            Name = type.ToString(),
        };
        _ = new BlockResponse(block) { RawResponse = $"{type} output" };

        return block;
    }

    private static WorkflowExecutionLog Log(WorkflowExecution execution, Block block)
    {
        return new WorkflowExecutionLog
        {
            WorkflowExecutionId = execution.Id,
            BlockId = block.Id,
            BlockName = block.Name,
            SectionName = "S",
            Output = $"{block.Type} output",
        };
    }
}
