using ErrorOr;
using IniaAi.Application.Queries.Counterparties.ExportCounterparties;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.Extensions.DependencyInjection;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.Application.UnitTests.Queries.Counterparties.ExportCounterparties;

public class ExportCounterpartiesQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-export";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-export";
    private const string INSTANCE = "source.inia.example";

    private readonly ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly ExportCounterpartiesQueryHandler _handler;

    public ExportCounterpartiesQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _handler = new ExportCounterpartiesQueryHandler(_context);
    }

    public void Dispose()
    {
        _ = _context.Database.EnsureDeleted();
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_ExportsTheOrganizationsLiveRowsWithTypesBagsAndParentsInNameOrder()
    {
        var corporate = new CounterpartyType { Id = "type-corporate", Name = "Corporate", Description = "A company" };
        var bank = new CounterpartyType { Id = "type-bank", Name = "Bank" };
        var deletedParent = new Counterparty(ORG_ID, "Deleted Parent") { Id = "deleted-parent", IsDeleted = true, DeletedAt = DateTime.UtcNow };
        var zeta = new Counterparty(ORG_ID, "Zeta Corp")
        {
            Id = "zeta",
            Email = "zeta@example.com",
            Properties = new Dictionary<string, string> { ["Ticker"] = "ZTA" },
            LastFinancialAnalysisResult = "{\"revenue\":\"1\"}",
            CounterpartyTypes = { corporate, bank },
        };
        var alpha = new Counterparty(ORG_ID, "Alpha Ltd") { Id = "alpha", ParentId = "zeta", CounterpartyTypes = { corporate } };
        var orphan = new Counterparty(ORG_ID, "Orphan") { Id = "orphan", ParentId = "deleted-parent" };
        var foreign = new Counterparty(OTHER_ORG_ID, "Foreign") { Id = "foreign", CounterpartyTypes = { bank } };
        _context.Counterparties.AddRange(deletedParent, zeta, alpha, orphan, foreign);
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();

        ErrorOr<CounterpartyBundleDto> result = await _handler.Handle(new ExportCounterpartiesQuery(ORG_ID, USER_ID, INSTANCE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        CounterpartyBundleDto bundle = result.Value;
        Assert.Equal(CounterpartyBundleDto.CURRENT_FORMAT_VERSION, bundle.BundleFormatVersion);
        Assert.Equal(INSTANCE, bundle.SourceInstance);
        Assert.Equal(ORG_ID, bundle.SourceOrganizationId);
        Assert.Equal(["alpha", "orphan", "zeta"], bundle.Counterparties.Select(c => c.Id));
        Assert.Equal(["Bank", "Corporate"], bundle.CounterpartyTypes.Select(t => t.Name));
        Assert.Equal("A company", bundle.CounterpartyTypes.Single(t => t.Name == "Corporate").Description);

        CounterpartyBundleEntryDto exportedZeta = bundle.Counterparties.Single(c => c.Id == "zeta");
        Assert.Equal(["Bank", "Corporate"], exportedZeta.CounterpartyTypeNames);
        Assert.Equal("ZTA", exportedZeta.Properties!["ticker"]);
        Assert.Equal("{\"revenue\":\"1\"}", exportedZeta.LastFinancialAnalysisResult);
        Assert.Equal("zeta", bundle.Counterparties.Single(c => c.Id == "alpha").ParentId);
        Assert.Null(bundle.Counterparties.Single(c => c.Id == "orphan").ParentId);
    }
}
