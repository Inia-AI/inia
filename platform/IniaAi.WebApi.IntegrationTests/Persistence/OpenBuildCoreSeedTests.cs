using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Tools.Reconciliation;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// A clean install without closed modules seeds only the platform's own Core rows: the reserved
/// workflow types, their access rows, the test workflow, and a reconciliation prompt row with the
/// generic prompts and nothing in the columns a module fills.
/// </summary>
public abstract class OpenBuildCoreSeedTests
{
    private const string DEFAULT_ORGANIZATION_ID = "IniaAI";
    private const string TEST_WORKFLOW_NAME = "test-queries";

    private readonly ICoreDatabaseFixture _fixture;
    private readonly string _skipReason;

    protected OpenBuildCoreSeedTests(ICoreDatabaseFixture fixture, string skipReason)
    {
        _fixture = fixture;
        _skipReason = skipReason;
    }

    [Fact]
    public async Task ACleanInstallWithoutModules_SeedsOnlyThePlatformsWorkflowTypesWorkflowsAndAccessRows()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        await using (CoreDbContext context = _fixture.CreateContext())
        {
            new CoreDbInitializer(context).Initialize();
        }

        await using CoreDbContext seeded = _fixture.CreateContext();
        Assert.Equal(
            [WorkflowType.GENERIC, WorkflowType.TEST_WORKFLOW_TYPE],
            (await seeded.WorkflowTypes.IgnoreQueryFilters().Select(type => type.Id).ToListAsync(ct)).Order(StringComparer.Ordinal));
        Assert.Equal(
            [WorkflowType.GENERIC, WorkflowType.TEST_WORKFLOW_TYPE],
            (await seeded.WorkflowTypeOrganizationAccess.IgnoreQueryFilters()
                .Where(access => access.OrganizationId == DEFAULT_ORGANIZATION_ID)
                .Select(access => access.WorkflowTypeId)
                .ToListAsync(ct)).Order(StringComparer.Ordinal));
        Assert.Equal([TEST_WORKFLOW_NAME], await seeded.Workflows.IgnoreQueryFilters().Select(workflow => workflow.Name).ToListAsync(ct));

        ReconciliationPrompts prompts = await seeded.ReconciliationPrompts.OrderByDescending(row => row.CreatedAt).FirstAsync(ct);
        Assert.Equal(new ReconciliationPrompts().FindMatchPrompt, prompts.FindMatchPrompt);
        Assert.Equal(new ReconciliationPrompts().CompareListsPrompt, prompts.CompareListsPrompt);
        Assert.Empty(prompts.HedgeFundsMonitoringFindMatchPrompt);
        Assert.Empty(prompts.HedgeFundsMonitoringCompareListsPrompt);
    }
}

public sealed class OpenBuildCoreSeedPostgresTests
    : OpenBuildCoreSeedTests, IClassFixture<PostgresCoreDatabaseFixture>
{
    public OpenBuildCoreSeedPostgresTests(PostgresCoreDatabaseFixture fixture)
        : base(fixture, "No local PostgreSQL reachable (set INIA_TEST_PG to enable).")
    {
    }
}

public sealed class OpenBuildCoreSeedSqlServerTests
    : OpenBuildCoreSeedTests, IClassFixture<SqlServerCoreDatabaseFixture>
{
    public OpenBuildCoreSeedSqlServerTests(SqlServerCoreDatabaseFixture fixture)
        : base(fixture, "No SQL Server reachable (set INIA_TEST_MSSQL to enable).")
    {
    }
}
