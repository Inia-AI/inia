using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// The startup seed on a database where someone soft-deleted a row the seed owns. The soft-delete
/// query filter hides that row while the primary key and the unique indexes still hold it, so a
/// seed that looks through the filter inserts a duplicate and aborts every seed step after it.
/// The seed must finish and leave the deleted row deleted.
/// </summary>
public abstract class CoreSeedSoftDeletedRowsTests
{
    private const string MONITORING_CATEGORY_ID = "monitoring-category";
    private const string DEFAULT_ORGANIZATION_ID = "IniaAI";

    private readonly ICoreDatabaseFixture _fixture;
    private readonly string _skipReason;

    protected CoreSeedSoftDeletedRowsTests(ICoreDatabaseFixture fixture, string skipReason)
    {
        _fixture = fixture;
        _skipReason = skipReason;
    }

    [Fact]
    public async Task ASoftDeletedWorkflowTypeCategory_StaysDeleted_AndTheSeedFinishes()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        Initialize();
        await SetDeletedAsync(context => context.WorkflowTypeCategories.Where(c => c.Id == MONITORING_CATEGORY_ID), true, ct);

        try
        {
            Initialize();

            await using CoreDbContext context = _fixture.CreateContext();
            WorkflowTypeCategory category = await context.WorkflowTypeCategories
                .IgnoreQueryFilters()
                .SingleAsync(c => c.Id == MONITORING_CATEGORY_ID, ct);
            Assert.True(category.IsDeleted);
        }
        finally
        {
            await SetDeletedAsync(context => context.WorkflowTypeCategories.Where(c => c.Id == MONITORING_CATEGORY_ID), false, ct);
        }
    }

    [Fact]
    public async Task SoftDeletedDocumentCategories_StayDeleted_AndTheSeedFinishes()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        Initialize();
        await SetDeletedAsync(context => context.DocumentCategories, true, ct);

        try
        {
            Initialize();

            await using CoreDbContext context = _fixture.CreateContext();
            Assert.False(await context.DocumentCategories.AnyAsync(ct));
        }
        finally
        {
            await SetDeletedAsync(context => context.DocumentCategories, false, ct);
        }
    }

    [Fact]
    public async Task ASoftDeletedDefaultAccessGrant_StaysDeleted_AndTheSeedFinishes()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        Initialize();
        await SetDeletedAsync(context => DefaultGenericAccess(context), true, ct);

        try
        {
            Initialize();

            await using CoreDbContext context = _fixture.CreateContext();
            WorkflowTypeOrganizationAccess grant = await DefaultGenericAccess(context)
                .IgnoreQueryFilters()
                .SingleAsync(ct);
            Assert.True(grant.IsDeleted);
        }
        finally
        {
            await SetDeletedAsync(context => DefaultGenericAccess(context), false, ct);
        }
    }

    private static IQueryable<WorkflowTypeOrganizationAccess> DefaultGenericAccess(CoreDbContext context)
    {
        return context.WorkflowTypeOrganizationAccess
            .Where(a => a.WorkflowTypeId == WorkflowType.GENERIC && a.OrganizationId == DEFAULT_ORGANIZATION_ID);
    }

    private void Initialize()
    {
        using CoreDbContext context = _fixture.CreateContext();
        new CoreDbInitializer(context).Initialize();
    }

    private async Task SetDeletedAsync<TEntity>(
        Func<CoreDbContext, IQueryable<TEntity>> rows,
        bool isDeleted,
        CancellationToken ct)
        where TEntity : class, Domain.Abstractions.Entities.IAuditableEntity
    {
        await using CoreDbContext context = _fixture.CreateContext();
        _ = await rows(context)
            .IgnoreQueryFilters()
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, isDeleted), ct);
    }
}

public sealed class CoreSeedSoftDeletedRowsPostgresTests
    : CoreSeedSoftDeletedRowsTests, IClassFixture<PostgresCoreDatabaseFixture>
{
    public CoreSeedSoftDeletedRowsPostgresTests(PostgresCoreDatabaseFixture fixture)
        : base(fixture, "No local PostgreSQL reachable (set INIA_TEST_PG to enable).")
    {
    }
}

public sealed class CoreSeedSoftDeletedRowsSqlServerTests
    : CoreSeedSoftDeletedRowsTests, IClassFixture<SqlServerCoreDatabaseFixture>
{
    public CoreSeedSoftDeletedRowsSqlServerTests(SqlServerCoreDatabaseFixture fixture)
        : base(fixture, "No SQL Server reachable (set INIA_TEST_MSSQL to enable).")
    {
    }
}
