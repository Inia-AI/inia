using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

public class WorkflowTypeLineageMappingTests
{
    private const string SQL_SERVER = "sqlserver";
    private const string NPGSQL = "npgsql";

    [Theory]
    [InlineData(SQL_SERVER)]
    [InlineData(NPGSQL)]
    public void DuplicatedFrom_IsNullable_AndHoldsAnyWorkflowTypeId(string provider)
    {
        using CoreDbContext context = CoreContext(provider);

        IEntityType workflowType = context.Model.FindEntityType(typeof(WorkflowType))!;
        IProperty duplicatedFrom = workflowType.FindProperty(nameof(WorkflowType.DuplicatedFromWorkflowTypeId))!;
        int? idLength = workflowType.FindProperty(nameof(WorkflowType.Id))!.GetMaxLength();

        Assert.True(duplicatedFrom.IsNullable);
        Assert.NotNull(idLength);
        Assert.Equal(idLength, duplicatedFrom.GetMaxLength());
    }

    [Fact]
    public void DuplicatedFrom_IsIndexedNonUnique()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);

        IIndex index = context.Model
            .FindEntityType(typeof(WorkflowType))!
            .GetIndexes()
            .Single(candidate => candidate.Properties.Count == 1
                && candidate.Properties[0].Name == nameof(WorkflowType.DuplicatedFromWorkflowTypeId));

        Assert.False(index.IsUnique);
    }

    [Fact]
    public void DuplicatedFrom_HasNoSelfForeignKey_SoProvenanceOutlivesItsSource()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);

        Assert.DoesNotContain(
            context.Model.FindEntityType(typeof(WorkflowType))!.GetForeignKeys(),
            foreignKey => foreignKey.Properties
                .Any(property => property.Name == nameof(WorkflowType.DuplicatedFromWorkflowTypeId)));
    }

    [Fact]
    public void WorkflowType_StaysAbsentFromUserData()
    {
        using UserDataDbContext context = UserDataContext(SQL_SERVER);

        Assert.Null(context.Model.FindEntityType(typeof(WorkflowType)));
    }

    private static CoreDbContext CoreContext(string provider)
    {
        return new CoreDbContext(BuildOptions<CoreDbContext>(provider));
    }

    private static UserDataDbContext UserDataContext(string provider)
    {
        return new UserDataDbContext(BuildOptions<UserDataDbContext>(provider));
    }

    private static DbContextOptions<TContext> BuildOptions<TContext>(string provider)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        _ = provider == NPGSQL
            ? builder.UseNpgsql("Host=unused")
            : builder.UseSqlServer("Server=unused");

        return builder.Options;
    }
}
