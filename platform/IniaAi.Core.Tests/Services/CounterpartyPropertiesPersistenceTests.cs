using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Services;

/// <summary>
/// The properties editor and the workflow write-back mutate a counterparty's freeform bag in place, so
/// change tracking has to notice edits inside the JSON-converted column, not just whole-bag swaps.
/// </summary>
public class CounterpartyPropertiesPersistenceTests : IClassFixture<WorkflowsFixture<WorkflowHandler>>
{
    private readonly WorkflowsFixture<WorkflowHandler> _fixture;
    private readonly IServiceProvider _testServiceProvider;

    public CounterpartyPropertiesPersistenceTests(
        WorkflowsFixture<WorkflowHandler> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testServiceProvider = _fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task MutatingBagValueInPlace_IsPersisted()
    {
        string counterpartyId = await SeedFundAsync(
            nameof(MutatingBagValueInPlace_IsPersisted),
            new Dictionary<string, string> { ["reportingStyle"] = "{\"currency\":\"JPY\"}" });

        IDbContextFactory<UserDataDbContext> dbFactory = _testServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();

        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            Counterparty fund = await db.Counterparties.SingleAsync(
                c => c.Id == counterpartyId,
                TestContext.Current.CancellationToken);

            fund.Properties!["reportingStyle"] = "{\"currency\":\"EUR\"}";

            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            Counterparty reloaded = await db.Counterparties
                .AsNoTracking()
                .SingleAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken);

            Assert.Equal("{\"currency\":\"EUR\"}", reloaded.Properties!["reportingStyle"]);
        }
    }

    [Fact]
    public async Task AddingBagKeyInPlace_IsPersistedAndKeepsExistingKeys()
    {
        string counterpartyId = await SeedFundAsync(
            nameof(AddingBagKeyInPlace_IsPersistedAndKeepsExistingKeys),
            new Dictionary<string, string> { ["existing"] = "keep" });

        IDbContextFactory<UserDataDbContext> dbFactory = _testServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();

        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            Counterparty fund = await db.Counterparties.SingleAsync(
                c => c.Id == counterpartyId,
                TestContext.Current.CancellationToken);

            fund.Properties!["currency"] = "CHF";

            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            Counterparty reloaded = await db.Counterparties
                .AsNoTracking()
                .SingleAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken);

            Assert.Equal("CHF", reloaded.Properties!["currency"]);
            Assert.Equal("keep", reloaded.Properties!["existing"]);
        }
    }

    [Fact]
    public async Task PascalCaseBagKey_SurvivesRoundTripVerbatim()
    {
        string counterpartyId = await SeedFundAsync(
            nameof(PascalCaseBagKey_SurvivesRoundTripVerbatim),
            new Dictionary<string, string> { ["ReportingStyle"] = "quarterly" });

        IDbContextFactory<UserDataDbContext> dbFactory = _testServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();

        using UserDataDbContext db = dbFactory.CreateDbContext();

        Counterparty reloaded = await db.Counterparties
            .AsNoTracking()
            .SingleAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken);

        Assert.True(reloaded.Properties!.ContainsKey("ReportingStyle"));
        Assert.Equal("quarterly", reloaded.Properties!["ReportingStyle"]);
    }

    private async Task<string> SeedFundAsync(
        string uniqueTestName,
        Dictionary<string, string> properties)
    {
        IDbContextFactory<UserDataDbContext> dbFactory = _testServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        var fund = new Counterparty(_fixture.GetOrganization().Id, $"{uniqueTestName} Fund")
        {
            Properties = properties,
        };

        _ = db.Counterparties.Add(fund);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return fund.Id;
    }
}
