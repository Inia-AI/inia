using ErrorOr;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.Workflows.Versions;

public class GenerateCurrencyCardQueryTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly ICoreUnitOfWork _unitOfWork;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public GenerateCurrencyCardQueryTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        _userDataUnitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    private GenerateCurrencyCardQueryHandler CreateHandler(IConfiguration configuration)
    {
        return new GenerateCurrencyCardQueryHandler(
            _unitOfWork, configuration, new UserDisplayNameResolver(_userDataUnitOfWork));
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GenerateCard_LatestPerType_CoversEveryTypeRegardlessOfAccess()
    {
        Seed("A", new string('1', 64), DateTime.UtcNow.AddMinutes(-10));
        Seed("A", new string('2', 64), DateTime.UtcNow.AddMinutes(-1));
        Seed("B", new string('3', 64), DateTime.UtcNow.AddMinutes(-5));
        Seed("C", new string('9', 64), DateTime.UtcNow);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(Config("Acme-Prod"));
        ErrorOr<CurrencyCardDto> result = await handler.Handle(
            new GenerateCurrencyCardQuery(ORG), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Acme-Prod", result.Value.InstanceName);
        Assert.Equal(3, result.Value.Workflows.Count);

        CurrencyCardEntryDto entryA = result.Value.Workflows.Single(w => w.WorkflowTypeId == "A");
        Assert.Equal(new string('2', 64), entryA.ContentHash);
        Assert.Equal(USER, entryA.SavedByName);
        Assert.Contains(result.Value.Workflows, w => w.WorkflowTypeId == "C");
    }

    [Fact]
    public async Task GenerateCard_EachEntry_CarriesItsWorkflowTypeName()
    {
        const string TYPE_ID = "QA-qa-20260917T184538Z-a9bfcb-fixture";
        _ = _context.WorkflowTypes.Add(new WorkflowType(TYPE_ID, "QA fixture qa-20260917T184538Z-a9bfcb"));
        Seed(TYPE_ID, new string('1', 64), DateTime.UtcNow);
        Seed("TypeWithoutRow", new string('2', 64), DateTime.UtcNow);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<CurrencyCardDto> result = await CreateHandler(Config("X")).Handle(
            new GenerateCurrencyCardQuery(ORG), TestContext.Current.CancellationToken);

        Assert.Equal(
            "QA fixture qa-20260917T184538Z-a9bfcb",
            result.Value.Workflows.Single(w => w.WorkflowTypeId == TYPE_ID).WorkflowTypeName);
        Assert.Null(result.Value.Workflows.Single(w => w.WorkflowTypeId == "TypeWithoutRow").WorkflowTypeName);
    }

    [Fact]
    public async Task GenerateCard_NoVersions_EmptyWorkflows()
    {
        var handler = CreateHandler(Config("X"));
        ErrorOr<CurrencyCardDto> result = await handler.Handle(
            new GenerateCurrencyCardQuery(ORG), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Workflows);
    }

    [Fact]
    public async Task GenerateCard_DefaultsToMachineName_WhenConfigMissing()
    {
        var handler = CreateHandler(Config(null));
        ErrorOr<CurrencyCardDto> result = await handler.Handle(
            new GenerateCurrencyCardQuery(ORG), TestContext.Current.CancellationToken);

        Assert.Equal(Environment.MachineName, result.Value.InstanceName);
    }

    [Fact]
    public async Task GenerateCard_CarriesFullLineage_OldestFirstEndingAtHead()
    {
        string oldest = new('1', 64);
        string newest = new('2', 64);
        Seed("A", oldest, DateTime.UtcNow.AddMinutes(-10));
        Seed("A", newest, DateTime.UtcNow.AddMinutes(-1));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler(Config("Acme-Prod"));
        ErrorOr<CurrencyCardDto> result = await handler.Handle(
            new GenerateCurrencyCardQuery(ORG), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        CurrencyCardEntryDto entryA = result.Value.Workflows.Single(w => w.WorkflowTypeId == "A");
        Assert.Equal([oldest, newest], entryA.HistoryHashes);
        Assert.Equal(newest, entryA.ContentHash);
        Assert.Equal(entryA.HistoryHashes[^1], entryA.ContentHash);
    }

    private void Seed(string workflowTypeId, string hash, DateTime createdAt)
    {
        _ = _context.WorkflowVersions.Add(new WorkflowVersion
        {
            WorkflowTypeId = workflowTypeId,
            ContentHash = hash,
            WorkflowJson = "{}",
            CreatedByUserId = USER,
            CreatedAt = createdAt,
        });
    }

    private static IConfiguration Config(string? instanceName)
    {
        Dictionary<string, string?> values = instanceName is null
            ? []
            : new Dictionary<string, string?> { ["PlatformSettings:InstanceName"] = instanceName };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
