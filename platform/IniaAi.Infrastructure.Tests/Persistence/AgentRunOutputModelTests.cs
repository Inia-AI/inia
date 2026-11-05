using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the context boundary for per-attempt output rows: they live in UserData beside the
/// executed block graph and die with their block via DB cascade (the zero-code retention path —
/// the soft-delete cleanup schema test deliberately skips Cascade FKs), while CoreDbContext
/// (templates) must not discover the entity through the Block navigation.
/// </summary>
public class AgentRunOutputModelTests
{
    private static UserDataDbContext CreateUserDataContext()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        return new UserDataDbContext(options);
    }

    private static CoreDbContext CreateCoreContext()
    {
        DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        return new CoreDbContext(options);
    }

    [Fact]
    public void UserDataModel_ContainsAgentRunOutput_WithRequiredCascadeLinkToBlock()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType? entityType = context.Model.FindEntityType(typeof(AgentRunOutput));

        Assert.NotNull(entityType);

        IForeignKey foreignKey = Assert.Single(entityType.GetForeignKeys());
        Assert.Equal(typeof(Block), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsRequired);
    }

    [Fact]
    public void UserDataModel_AgentRunOutput_UniqueOnBlockIdAndRound_AndIndexesRunExecutionId()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(AgentRunOutput))!;
        List<IIndex> indexes = [.. entityType.GetIndexes()];

        IIndex naturalKey = Assert.Single(indexes, index => index.IsUnique);
        Assert.Equal(
            [nameof(AgentRunOutput.BlockId), nameof(AgentRunOutput.Round)],
            naturalKey.Properties.Select(property => property.Name).ToList());

        Assert.Contains(indexes, index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(AgentRunOutput.RunExecutionId)]));
    }

    [Fact]
    public void CoreModel_DoesNotContainAgentRunOutput()
    {
        using CoreDbContext context = CreateCoreContext();

        Assert.Null(context.Model.FindEntityType(typeof(AgentRunOutput)));
    }
}
