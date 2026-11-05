using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the context boundary for agent-run trace evidence: rows live in UserData beside the
/// executed block graph and die with their block via DB cascade (the zero-code retention path —
/// the soft-delete cleanup schema test deliberately skips Cascade FKs), while CoreDbContext
/// (templates) must not discover the entity through the Block navigation.
/// </summary>
public class AgentRunTraceFileModelTests
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
    public void UserDataModel_ContainsAgentRunTraceFile_WithOptionalCascadeLinkToBlock()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType? entityType = context.Model.FindEntityType(typeof(AgentRunTraceFile));

        Assert.NotNull(entityType);

        IForeignKey foreignKey = Assert.Single(entityType.GetForeignKeys());
        Assert.Equal(typeof(Block), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.False(foreignKey.IsRequired);
    }

    [Fact]
    public void UserDataModel_AgentRunTraceFile_IndexesBlockIdAndRunExecutionId()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(AgentRunTraceFile))!;
        List<string> indexedProperties = [.. entityType.GetIndexes()
            .SelectMany(index => index.Properties)
            .Select(property => property.Name)];

        Assert.Contains(nameof(AgentRunTraceFile.BlockId), indexedProperties);
        Assert.Contains(nameof(AgentRunTraceFile.RunExecutionId), indexedProperties);
    }

    [Fact]
    public void CoreModel_DoesNotContainAgentRunTraceFile()
    {
        using CoreDbContext context = CreateCoreContext();

        Assert.Null(context.Model.FindEntityType(typeof(AgentRunTraceFile)));
    }
}
