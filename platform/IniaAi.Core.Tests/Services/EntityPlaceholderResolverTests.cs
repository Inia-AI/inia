using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Services;

public class EntityPlaceholderResolverTests : IClassFixture<WorkflowsFixture<WorkflowHandler>>
{
    private readonly WorkflowsFixture<WorkflowHandler> _fixture;
    private readonly IEntityPlaceholderResolver _resolver;
    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;

    public EntityPlaceholderResolverTests(
        WorkflowsFixture<WorkflowHandler> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testServiceProvider = _fixture.BuildTestServiceProvider(testOutputHelper);
        _resolver = _testServiceProvider.GetRequiredService<IEntityPlaceholderResolver>();
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
    }

    #region Counterparty Tests

    [Fact]
    public async Task ResolveCounterparty_ValidId_ReturnsName()
    {
        // Arrange
        Counterparty counterparty = await GetFirstCounterpartyAsync();

        // Act
        string? result = _resolver.ResolveCounterparty(counterparty.Id, "Name");

        // Assert
        Assert.Equal(counterparty.Name, result);
    }

    [Fact]
    public async Task ResolveCounterparty_ValidId_ReturnsEmail()
    {
        // Arrange
        Counterparty counterparty = await GetFirstCounterpartyAsync();

        // Act
        string? result = _resolver.ResolveCounterparty(counterparty.Id, "Email");

        // Assert
        Assert.Equal(counterparty.Email, result);
    }

    [Fact]
    public async Task ResolveCounterparty_BlankKey_ReturnsName()
    {
        // Arrange
        Counterparty counterparty = await GetFirstCounterpartyAsync();

        // Act
        string? result = _resolver.ResolveCounterparty(counterparty.Id, null);

        // Assert
        Assert.Equal(counterparty.Name, result);
    }

    [Fact]
    public void ResolveCounterparty_UnknownEntity_ReturnsNull()
    {
        // Act — use a made-up ID; the entity is never found
        string? result = _resolver.ResolveCounterparty("some-id", "Name");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveCounterparty_NullId_ReturnsNull()
    {
        // Act
        string? result = _resolver.ResolveCounterparty(null, "Name");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveCounterparty_EmptyId_ReturnsNull()
    {
        // Act
        string? result = _resolver.ResolveCounterparty(string.Empty, "Name");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Counterparty Properties Bag Tests

    private const string FUND_STYLE_JSON = "{\"currency\":\"JPY\",\"units\":\"millions\"}";
    private const string MANAGER_STYLE_JSON = "{\"currency\":\"GBP\"}";

    [Fact]
    public async Task ResolveCounterparty_BagKey_ReturnsRawValueVerbatim()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterparty_BagKey_ReturnsRawValueVerbatim),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON });

        // Act — the resolver returns the stored value untouched; traversal is the caller's job.
        string? result = _resolver.ResolveCounterparty(fund.Id, "reportingStyle");

        // Assert
        Assert.Equal(FUND_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterparty_BagKeyAbsentOnFund_FallsBackToManager()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterparty_BagKeyAbsentOnFund_FallsBackToManager),
            managerBag: new Dictionary<string, string> { ["reportingStyle"] = MANAGER_STYLE_JSON },
            fundBag: null);

        // Act
        string? result = _resolver.ResolveCounterparty(fund.Id, "reportingStyle");

        // Assert
        Assert.Equal(MANAGER_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterparty_BagKeyOnBothLevels_PrefersFundOverManager()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterparty_BagKeyOnBothLevels_PrefersFundOverManager),
            managerBag: new Dictionary<string, string> { ["reportingStyle"] = MANAGER_STYLE_JSON },
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON });

        // Act
        string? result = _resolver.ResolveCounterparty(fund.Id, "reportingStyle");

        // Assert
        Assert.Equal(FUND_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterparty_KeyMissingEverywhere_ReturnsNull()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterparty_KeyMissingEverywhere_ReturnsNull),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON });

        // Act
        string? result = _resolver.ResolveCounterparty(fund.Id, "somethingElse");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveCounterparty_BagKeyDifferentCase_ResolvesCaseInsensitively()
    {
        // The key is stored as "reportingStyle"; querying "ReportingStyle" (any casing) still resolves.
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterparty_BagKeyDifferentCase_ResolvesCaseInsensitively),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON });

        // Act
        string? result = _resolver.ResolveCounterparty(fund.Id, "ReportingStyle");

        // Assert
        Assert.Equal(FUND_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterparty_ColumnShadowsBagKeyOfSameName()
    {
        // A real string column wins over a bag key spelled the same, so authors can never mask
        // (or spoof) an entity column with bag content.
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterparty_ColumnShadowsBagKeyOfSameName),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["Name"] = "bag-name" });

        // Act
        string? result = _resolver.ResolveCounterparty(fund.Id, "Name");

        // Assert
        Assert.Equal(fund.Name, result);
        Assert.NotEqual("bag-name", result);
    }

    [Fact]
    public async Task ResolveCounterparty_RelatedEntitySegment_ReturnsNull()
    {
        // A navigation is not a string column, so it falls through to a bag lookup and can never
        // leak a related row (e.g. the organization's API keys) into a prompt.
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterparty_RelatedEntitySegment_ReturnsNull),
            managerBag: new Dictionary<string, string> { ["reportingStyle"] = MANAGER_STYLE_JSON },
            fundBag: null);

        Assert.NotNull(_resolver.GetCounterparty(fund.Id)?.Parent);

        // Act
        string? parent = _resolver.ResolveCounterparty(fund.Id, "Parent");
        string? organization = _resolver.ResolveCounterparty(fund.Id, "Organization");

        // Assert
        Assert.Null(parent);
        Assert.Null(organization);
    }

    #endregion

    #region ResolveCounterpartyByName Tests

    [Fact]
    public async Task ResolveCounterpartyByName_ExactName_ReturnsBagValue()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterpartyByName_ExactName_ReturnsBagValue),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON });

        // Act
        string? result = _resolver.ResolveCounterpartyByName(
            _fixture.GetOrganization().Id,
            fund.Name,
            "reportingStyle");

        // Assert
        Assert.Equal(FUND_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterpartyByName_CaseAndWhitespaceDifferences_StillMatch()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterpartyByName_CaseAndWhitespaceDifferences_StillMatch),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON });

        // Act
        string? result = _resolver.ResolveCounterpartyByName(
            _fixture.GetOrganization().Id,
            $"  {fund.Name.ToUpperInvariant()}  ",
            "reportingStyle");

        // Assert
        Assert.Equal(FUND_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterpartyByName_NameVariantsMatch_ReturnsBagValue()
    {
        // The workflow's fund shortlist is built from GetFundListString(), which emits
        // NameVariants whenever it is populated.
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterpartyByName_NameVariantsMatch_ReturnsBagValue),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON },
            fundNameVariants: $"{nameof(ResolveCounterpartyByName_NameVariantsMatch_ReturnsBagValue)} Variant");

        // Act
        string? result = _resolver.ResolveCounterpartyByName(
            _fixture.GetOrganization().Id,
            fund.NameVariants,
            "reportingStyle");

        // Assert
        Assert.Equal(FUND_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterpartyByName_OneOfSeveralNameVariants_ReturnsBagValue()
    {
        _ = await SeedManagerAndFundAsync(
            nameof(ResolveCounterpartyByName_OneOfSeveralNameVariants_ReturnsBagValue),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON },
            fundNameVariants: $"{nameof(ResolveCounterpartyByName_OneOfSeveralNameVariants_ReturnsBagValue)} First| {nameof(ResolveCounterpartyByName_OneOfSeveralNameVariants_ReturnsBagValue)} Second");

        string? result = _resolver.ResolveCounterpartyByName(
            _fixture.GetOrganization().Id,
            $"{nameof(ResolveCounterpartyByName_OneOfSeveralNameVariants_ReturnsBagValue)} Second",
            "reportingStyle");

        Assert.Equal(FUND_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterpartyByName_FundWithoutOwnBag_FallsBackToManager()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterpartyByName_FundWithoutOwnBag_FallsBackToManager),
            managerBag: new Dictionary<string, string> { ["reportingStyle"] = MANAGER_STYLE_JSON },
            fundBag: null);

        // Act
        string? result = _resolver.ResolveCounterpartyByName(
            _fixture.GetOrganization().Id,
            fund.Name,
            "reportingStyle");

        // Assert
        Assert.Equal(MANAGER_STYLE_JSON, result);
    }

    [Fact]
    public async Task ResolveCounterpartyByName_OtherOrganization_ReturnsNull()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterpartyByName_OtherOrganization_ReturnsNull),
            managerBag: null,
            fundBag: new Dictionary<string, string> { ["reportingStyle"] = FUND_STYLE_JSON });

        // Act
        string? result = _resolver.ResolveCounterpartyByName(
            "a-different-organization",
            fund.Name,
            "reportingStyle");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveCounterpartyByName_UnknownName_ReturnsNull()
    {
        // Act
        string? result = _resolver.ResolveCounterpartyByName(
            _fixture.GetOrganization().Id,
            "No Such Fund At All",
            "reportingStyle");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveCounterpartyByName_BlankName_ReturnsNull()
    {
        // Act
        string? nullName = _resolver.ResolveCounterpartyByName(_fixture.GetOrganization().Id, null, "Name");
        string? blankName = _resolver.ResolveCounterpartyByName(_fixture.GetOrganization().Id, "   ", "Name");

        // Assert
        Assert.Null(nullName);
        Assert.Null(blankName);
    }

    [Fact]
    public void ResolveCounterpartyByName_BlankOrganization_ReturnsNull()
    {
        // Act
        string? result = _resolver.ResolveCounterpartyByName(null, "TestCounterparty", "Name");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveCounterpartyByName_KnownFund_ResolvesPlainEntityColumn()
    {
        // Arrange
        (_, Counterparty fund) = await SeedManagerAndFundAsync(
            nameof(ResolveCounterpartyByName_KnownFund_ResolvesPlainEntityColumn),
            managerBag: null,
            fundBag: null);

        // Act
        string? result = _resolver.ResolveCounterpartyByName(
            _fixture.GetOrganization().Id,
            fund.Name,
            "Name");

        // Assert
        Assert.Equal(fund.Name, result);
    }

    #endregion

    #region Organization Tests

    [Fact]
    public void ResolveOrganization_ValidId_ReturnsName()
    {
        // Arrange
        Organization org = _fixture.GetOrganization();

        // Act
        string? result = _resolver.ResolveOrganization(org.Id, "Name");

        // Assert
        Assert.Equal(org.Name, result);
    }

    [Fact]
    public void ResolveOrganization_InvalidProperty_ReturnsNull()
    {
        // Arrange
        Organization org = _fixture.GetOrganization();

        // Act
        string? result = _resolver.ResolveOrganization(org.Id, "NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOrganization_ParallelApiKey_ReturnsValue()
    {
        // Arrange — set ParallelApiKey on the test organization
        Organization org = _fixture.GetOrganization();
        const string testApiKey = "pk-test-12345";

        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            Organization? entity = await db.Organizations.FindAsync([org.Id], TestContext.Current.CancellationToken);
            Assert.NotNull(entity);
            entity.ParallelApiKey = testApiKey;
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Use a fresh resolver to avoid stale cache
        IEntityPlaceholderResolver freshResolver = _testServiceProvider.GetRequiredService<IEntityPlaceholderResolver>();

        // Act
        string? result = freshResolver.ResolveOrganization(org.Id, "ParallelApiKey");

        // Assert
        Assert.Equal(testApiKey, result);

        // Cleanup — remove the key so other tests aren't affected
        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            Organization? entity = await db.Organizations.FindAsync([org.Id], TestContext.Current.CancellationToken);
            Assert.NotNull(entity);
            entity.ParallelApiKey = null;
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ResolveOrganization_PerplexityApiKey_ReturnsValue()
    {
        // Arrange — set PerplexityApiKey on the test organization
        Organization org = _fixture.GetOrganization();
        const string testApiKey = "pk-test-12345";

        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            Organization? entity = await db.Organizations.FindAsync([org.Id], TestContext.Current.CancellationToken);
            Assert.NotNull(entity);
            entity.PerplexityApiKey = testApiKey;
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Use a fresh resolver to avoid stale cache
        IEntityPlaceholderResolver freshResolver = _testServiceProvider.GetRequiredService<IEntityPlaceholderResolver>();

        // Act
        string? result = freshResolver.ResolveOrganization(org.Id, "PerplexityApiKey");

        // Assert
        Assert.Equal(testApiKey, result);

        // Cleanup — remove the key so other tests aren't affected
        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            Organization? entity = await db.Organizations.FindAsync([org.Id], TestContext.Current.CancellationToken);
            Assert.NotNull(entity);
            entity.PerplexityApiKey = null;
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void ResolveOrganization_ParallelApiKey_WhenNull_ReturnsEmpty()
    {
        // Arrange — seeded org has no ParallelApiKey
        Organization org = _fixture.GetOrganization();

        // Act
        string? result = _resolver.ResolveOrganization(org.Id, "ParallelApiKey");

        // Assert — ReflectionUtils returns string.Empty for null property values
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveOrganization_PerplexityApiKey_WhenNull_ReturnsEmpty()
    {
        // Arrange — seeded org has no PerplexityApiKey
        Organization org = _fixture.GetOrganization();

        // Act
        string? result = _resolver.ResolveOrganization(org.Id, "PerplexityApiKey");

        // Assert — ReflectionUtils returns string.Empty for null property values
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveOrganization_NullId_ReturnsNull()
    {
        // Act
        string? result = _resolver.ResolveOrganization(null, "Name");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region PeerCompanies Tests

    [Fact]
    public async Task ResolvePeerCompanies_ArrayAccess_ReturnsCommaSeparatedNames()
    {
        // Arrange
        List<Counterparty> counterparties = await GetCounterpartiesAsync();
        Assert.True(counterparties.Count >= 2, "Need at least 2 counterparties for this test");

        List<string> peerIds = [.. counterparties.Select(c => c.Id)];
        string[] parts = ["PeerCompanies[]", "Name"];

        // Act
        string? result = _resolver.ResolvePeerCompanies(peerIds, parts);

        // Assert
        string expected = string.Join(", ", counterparties.Select(c => c.Name));
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ResolvePeerCompanies_IndexedAccess_ReturnsCorrectName()
    {
        // Arrange
        List<Counterparty> counterparties = await GetCounterpartiesAsync();
        Assert.True(counterparties.Count >= 1, "Need at least 1 counterparty for this test");

        List<string> peerIds = [.. counterparties.Select(c => c.Id)];
        string[] parts = ["PeerCompanies[1]", "Name"];

        // Act
        string? result = _resolver.ResolvePeerCompanies(peerIds, parts);

        // Assert — [1] is 1-based, so it returns first element
        Assert.Equal(counterparties[0].Name, result);
    }

    [Fact]
    public async Task ResolvePeerCompanies_OutOfRangeIndex_ReturnsNull()
    {
        // Arrange
        List<Counterparty> counterparties = await GetCounterpartiesAsync();
        List<string> peerIds = [.. counterparties.Select(c => c.Id)];
        string[] parts = ["PeerCompanies[10000]", "Name"];

        // Act
        string? result = _resolver.ResolvePeerCompanies(peerIds, parts);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolvePeerCompanies_NullIds_ReturnsNull()
    {
        // Act
        string? result = _resolver.ResolvePeerCompanies(null, ["PeerCompanies[]", "Name"]);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolvePeerCompanies_EmptyIds_ReturnsNull()
    {
        // Act
        string? result = _resolver.ResolvePeerCompanies([], ["PeerCompanies[]", "Name"]);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolvePeerCompanies_PlainAccess_ReturnsNewlineSeparatedNames()
    {
        // Arrange
        List<Counterparty> counterparties = await GetCounterpartiesAsync();
        Assert.True(counterparties.Count >= 2, "Need at least 2 counterparties for this test");

        List<string> peerIds = [.. counterparties.Select(c => c.Id)];
        string[] parts = ["PeerCompanies", "Name"];

        // Act
        string? result = _resolver.ResolvePeerCompanies(peerIds, parts);

        // Assert — plain access returns newline-separated
        string expected = string.Join("\n", counterparties.Select(c => c.Name));
        Assert.Equal(expected, result);
    }

    #endregion

    #region InvalidateCounterparty Tests

    [Fact]
    public async Task InvalidateCounterparty_AfterResolve_NextResolveStillWorks()
    {
        // Arrange
        Counterparty counterparty = await GetFirstCounterpartyAsync();

        // Resolve once to populate cache
        string? firstResult = _resolver.ResolveCounterparty(counterparty.Id, "Name");
        Assert.Equal(counterparty.Name, firstResult);

        // Act — invalidate cache
        _resolver.InvalidateCounterparty(counterparty.Id);

        // Assert — re-resolve still works (fetches from DB again)
        string? secondResult = _resolver.ResolveCounterparty(counterparty.Id, "Name");
        Assert.Equal(counterparty.Name, secondResult);
    }

    [Fact]
    public async Task GetCounterparty_ConcurrentAccess_DoesNotCorruptCache()
    {
        // Many counterparties, so concurrent first-time lookups all miss and write the shared cache
        // at once — the parallel block scheduler resolves counterparties from many blocks concurrently.
        string orgId = _fixture.GetOrganization().Id;
        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        List<string> ids = [];
        using (UserDataDbContext db = dbFactory.CreateDbContext())
        {
            for (int i = 0; i < 300; i++)
            {
                var cp = new Counterparty(orgId, $"Concurrent CP {i}");
                _ = db.Counterparties.Add(cp);
                ids.Add(cp.Id);
            }

            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Regression guard: hammer the shared cache from many concurrent blocks. (A strict red-on-the-old-code
        // demo isn't possible here — the in-memory test DB serialises the lookup that gates each cache write,
        // and the cache is transparent so corruption only ever surfaces as a throw, never a wrong value. The
        // bug is unambiguous by inspection: a plain Dictionary mutated concurrently from the parallel path.)
        for (int round = 0; round < 10; round++)
        {
            IEntityPlaceholderResolver resolver = _testServiceProvider.GetRequiredService<IEntityPlaceholderResolver>();
            await Parallel.ForEachAsync(
                ids,
                new ParallelOptions { MaxDegreeOfParallelism = 96, CancellationToken = TestContext.Current.CancellationToken },
                async (id, ct) =>
                {
                    await Task.Yield();
                    _ = resolver.GetCounterparty(id);      // miss -> TryAdd
                    resolver.InvalidateCounterparty(id);   // Remove
                    _ = resolver.GetCounterparty(id);      // miss -> TryAdd
                });

            // A plain Dictionary corrupts/throws under concurrent writes; a concurrency-safe cache survives.
            Assert.Equal("Concurrent CP 0", resolver.GetCounterparty(ids[0])?.Name);
        }
    }

    #endregion

    #region Helpers

    private async Task<(Counterparty manager, Counterparty fund)> SeedManagerAndFundAsync(
        string uniqueTestName,
        Dictionary<string, string>? managerBag,
        Dictionary<string, string>? fundBag,
        string? fundNameVariants = null)
    {
        string organizationId = _fixture.GetOrganization().Id;

        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        var manager = new Counterparty(organizationId, $"{uniqueTestName} Manager")
        {
            Properties = managerBag,
        };
        _ = db.Counterparties.Add(manager);

        var fund = new Counterparty(organizationId, $"{uniqueTestName} Fund")
        {
            ParentId = manager.Id,
            NameVariants = fundNameVariants ?? string.Empty,
            Properties = fundBag,
        };
        _ = db.Counterparties.Add(fund);

        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (manager, fund);
    }

    private async Task<Counterparty> GetFirstCounterpartyAsync()
    {
        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        Counterparty counterparty = await db.Counterparties
            .Where(c => c.OrganizationId == _fixture.GetOrganization().Id)
            .AsNoTracking()
            .FirstAsync(TestContext.Current.CancellationToken);

        return counterparty;
    }

    private async Task<List<Counterparty>> GetCounterpartiesAsync()
    {
        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        return await db.Counterparties
            .Where(c => c.OrganizationId == _fixture.GetOrganization().Id)
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    #endregion
}
