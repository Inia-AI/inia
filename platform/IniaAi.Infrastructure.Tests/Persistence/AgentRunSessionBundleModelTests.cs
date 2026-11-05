using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the context boundary for session-bundle custody: rows live in UserData beside the
/// executed block graph and die with their block via DB cascade (the zero-code retention path —
/// the soft-delete cleanup schema test deliberately skips Cascade FKs), while CoreDbContext
/// (templates) must not discover the entity.
/// </summary>
public class AgentRunSessionBundleModelTests
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

    /// <summary>
    /// A row belongs to a block or to a conversation, so both links are optional at the schema
    /// level (custody enforces exactly one), and each owner's delete cascades to its rows.
    /// </summary>
    [Fact]
    public void UserDataModel_ContainsAgentRunSessionBundle_WithOptionalCascadeLinksToBlockAndConversation()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType? entityType = context.Model.FindEntityType(typeof(AgentRunSessionBundle));

        Assert.NotNull(entityType);

        List<IForeignKey> foreignKeys = [.. entityType.GetForeignKeys()];
        Assert.Equal(2, foreignKeys.Count);

        IForeignKey toBlock = Assert.Single(foreignKeys, static key => key.PrincipalEntityType.ClrType == typeof(Block));
        Assert.Equal(DeleteBehavior.Cascade, toBlock.DeleteBehavior);
        Assert.False(toBlock.IsRequired);

        IForeignKey toConversation = Assert.Single(
            foreignKeys, static key => key.PrincipalEntityType.ClrType == typeof(AgentConversation));
        Assert.Equal(DeleteBehavior.Cascade, toConversation.DeleteBehavior);
        Assert.False(toConversation.IsRequired);
    }

    [Fact]
    public void UserDataModel_AgentRunSessionBundle_KeysOnConversationAndRoundUniquely()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(AgentRunSessionBundle))!;

        IIndex naturalKey = Assert.Single(entityType.GetIndexes(), index =>
            index.Properties.Count == 2
            && index.Properties[0].Name == nameof(AgentRunSessionBundle.ConversationId)
            && index.Properties[1].Name == nameof(AgentRunSessionBundle.Round));

        Assert.True(naturalKey.IsUnique);
    }

    [Fact]
    public void CoreModel_DoesNotContainAgentConversations()
    {
        using CoreDbContext context = CreateCoreContext();

        Assert.Null(context.Model.FindEntityType(typeof(AgentConversation)));
        Assert.Null(context.Model.FindEntityType(typeof(AgentConversationTurn)));
        Assert.Null(context.Model.FindEntityType(typeof(IniaAi.Domain.Entities.Workflows.EditorAgentDraft)));
    }

    /// <summary>
    /// The (block, round) index is unique because it is the supersede rule: a re-run of the same
    /// leg replaces that round's row rather than adding a second one pointing at other bytes.
    /// </summary>
    [Fact]
    public void UserDataModel_AgentRunSessionBundle_KeysOnBlockAndRoundUniquely()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(AgentRunSessionBundle))!;

        IIndex naturalKey = Assert.Single(entityType.GetIndexes(), index =>
            index.Properties.Count == 2
            && index.Properties[0].Name == nameof(AgentRunSessionBundle.BlockId)
            && index.Properties[1].Name == nameof(AgentRunSessionBundle.Round));

        Assert.True(naturalKey.IsUnique);
    }

    /// <summary>
    /// The run id joins a bundle to its ledger entry and trace rows; the created-at is what the
    /// retention sweep reads, and it reads it on every deployment's whole table.
    /// </summary>
    [Fact]
    public void UserDataModel_AgentRunSessionBundle_IndexesRunExecutionIdAndCreatedAt()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(AgentRunSessionBundle))!;
        List<string> indexedProperties = [.. entityType.GetIndexes()
            .SelectMany(index => index.Properties)
            .Select(property => property.Name)];

        Assert.Contains(nameof(AgentRunSessionBundle.RunExecutionId), indexedProperties);
        Assert.Contains(nameof(AgentRunSessionBundle.CreatedAt), indexedProperties);
    }

    [Fact]
    public void CoreModel_DoesNotContainAgentRunSessionBundle()
    {
        using CoreDbContext context = CreateCoreContext();

        Assert.Null(context.Model.FindEntityType(typeof(AgentRunSessionBundle)));
    }
}
