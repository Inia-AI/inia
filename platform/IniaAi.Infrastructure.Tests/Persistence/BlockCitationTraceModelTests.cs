using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the context boundary for citation traces: one row per executed block in UserData that
/// dies with its block via DB cascade, while CoreDbContext (templates) must not discover the
/// entity through the Block navigation.
/// </summary>
public class BlockCitationTraceModelTests
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
    public void UserDataModel_ContainsBlockCitationTrace_WithOneCascadeRowPerBlock()
    {
        using UserDataDbContext context = CreateUserDataContext();

        IEntityType? entityType = context.Model.FindEntityType(typeof(BlockCitationTrace));

        Assert.NotNull(entityType);
        IForeignKey foreignKey = Assert.Single(entityType.GetForeignKeys());
        Assert.Equal(typeof(Block), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsUnique);
    }

    [Fact]
    public void CoreModel_DoesNotContainBlockCitationTrace()
    {
        using CoreDbContext context = CreateCoreContext();

        Assert.Null(context.Model.FindEntityType(typeof(BlockCitationTrace)));
    }
}
