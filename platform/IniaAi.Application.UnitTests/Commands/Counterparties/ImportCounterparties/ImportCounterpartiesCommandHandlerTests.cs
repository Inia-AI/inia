using ErrorOr;
using IniaAi.Application.Commands.Counterparties.ImportCounterparties;
using IniaAi.Application.Errors;
using IniaAi.Application.Queries.Counterparties.ExportCounterparties;
using IniaAi.Application.Queries.Counterparties.GetCounterparty;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Services;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Commands.Counterparties.ImportCounterparties;

/// <summary>
/// The bundle import at the handler level against the test database: each disposition, the
/// parents-first landing, the snapshot the matching reads, the dry run, the round trip through
/// the export handler into a second organization, and the shared fields read back through the
/// read model.
/// </summary>
public class ImportCounterpartiesCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-import";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-import";
    private const string INSTANCE = "source.inia.example";
    private const string TYPE_CORPORATE_ID = "type-corporate";
    private const string TYPE_CORPORATE = "Corporate";
    private const string TYPE_BANK = "Bank";
    private const string FINANCIALS = "{\"revenue\":\"12.5\",\"currency\":\"EUR\"}";

    private readonly ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly ImportCounterpartiesCommandHandler _handler;
    private readonly ExportCounterpartiesQueryHandler _exporter;

    public ImportCounterpartiesCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _ = _context.CounterpartyTypes.Add(new CounterpartyType { Id = TYPE_CORPORATE_ID, Name = TYPE_CORPORATE, Description = "A company" });
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new ImportCounterpartiesCommandHandler(
            NullLogger<ImportCounterpartiesCommandHandler>.Instance,
            unitOfWork,
            _context,
            new DbExceptionAnalyzer());
        _exporter = new ExportCounterpartiesQueryHandler(_context);
    }

    public void Dispose()
    {
        _ = _context.Database.EnsureDeleted();
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_FreshOrganization_CreatesEveryRowWithItsSourceIdHierarchyTypesBagAndFinancials()
    {
        CounterpartyBundleDto bundle = Bundle(
            Entry("acme", "Acme Holdings", types: [TYPE_CORPORATE, TYPE_BANK], properties: new() { ["Ticker"] = "ACM" }, financials: FINANCIALS),
            Entry("acme-eu", "Acme Europe", parentId: "acme"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        Assert.True(plan.Applied);
        Assert.Equal(2, plan.CreateCount);
        Assert.Equal(0, plan.SkipCount);
        Assert.Equal([TYPE_BANK], plan.CounterpartyTypesToCreate);

        Counterparty parent = Reload("acme");
        Assert.Equal("Acme Holdings", parent.Name);
        Assert.Null(parent.ParentId);
        Assert.Equal(FINANCIALS, parent.LastFinancialAnalysisResult);
        Assert.Equal("ACM", parent.Properties!["ticker"]);
        Assert.Equal([TYPE_BANK, TYPE_CORPORATE], parent.CounterpartyTypes.Select(t => t.Name).Order(StringComparer.Ordinal));

        Counterparty child = Reload("acme-eu");
        Assert.Equal("acme", child.ParentId);
        Assert.Equal([TYPE_CORPORATE], child.CounterpartyTypes.Select(t => t.Name));
    }

    [Fact]
    public async Task Handle_DryRun_ReportsThePlanAndWritesNothing()
    {
        CounterpartyBundleDto bundle = Bundle(
            Entry("acme", "Acme Holdings", types: [TYPE_BANK]),
            Entry("acme-eu", "Acme Europe", parentId: "acme"));

        CounterpartyImportPlanDto plan = await RunAsync(bundle, dryRun: true);

        Assert.False(plan.Applied);
        Assert.Equal(2, plan.CreateCount);
        Assert.Equal([TYPE_BANK], plan.CounterpartyTypesToCreate);
        _context.ChangeTracker.Clear();
        Assert.Equal(0, await _context.Counterparties.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await _context.CounterpartyTypes.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_SameBundleTwice_ReportsEveryRowUnchangedTheSecondTime()
    {
        CounterpartyBundleDto bundle = Bundle(
            Entry("acme", "Acme Holdings", types: [TYPE_CORPORATE, TYPE_BANK], properties: new() { ["Ticker"] = "ACM" }, financials: FINANCIALS),
            Entry("acme-eu", "Acme Europe", parentId: "acme"));
        _ = await ApplyAsync(bundle);

        CounterpartyImportPlanDto second = await ApplyAsync(bundle);

        Assert.Equal(2, second.UnchangedCount);
        Assert.Equal(0, second.CreateCount + second.UpdateByIdCount + second.UpdateByNameCount + second.SkipCount);
        Assert.Empty(second.CounterpartyTypesToCreate);
    }

    [Fact]
    public async Task Handle_IdHeldByLiveRow_ReplacesEveryFieldIncludingBagAndTypes()
    {
        Seed("cp-1", "Old Name", email: "old@example.com", properties: new() { ["a"] = "1", ["b"] = "2" }, typeIds: [TYPE_CORPORATE_ID]);
        CounterpartyBundleDto bundle = Bundle(
            Entry("cp-1", "New Name", email: "new@example.com", properties: new() { ["a"] = "1" }, types: [TYPE_BANK]));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        Assert.Equal(CounterpartyImportDisposition.UpdateById, plan.Rows.Single().Disposition);
        Counterparty row = Reload("cp-1");
        Assert.Equal("New Name", row.Name);
        Assert.Equal("new@example.com", row.Email);
        Assert.Equal(["a"], row.Properties!.Keys);
        Assert.Equal([TYPE_BANK], row.CounterpartyTypes.Select(t => t.Name));
    }

    [Fact]
    public async Task Handle_IdHeldBySoftDeletedRow_Resurrects()
    {
        Seed("cp-1", "Gone", isDeleted: true);
        CounterpartyBundleDto bundle = Bundle(Entry("cp-1", "Back"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        Assert.Equal(CounterpartyImportDisposition.Resurrect, plan.Rows.Single().Disposition);
        Counterparty row = Reload("cp-1");
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAt);
        Assert.Equal("Back", row.Name);
    }

    [Fact]
    public async Task Handle_UnknownIdButOneLiveNameMatch_UpdatesThatRowKeepsItsIdAndRemapsChildren()
    {
        Seed("target-1", "Acme Holdings");
        CounterpartyBundleDto bundle = Bundle(
            Entry("source-1", "ACME HOLDINGS", email: "info@acme.example"),
            Entry("source-2", "Acme Europe", parentId: "source-1"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        CounterpartyImportRowDto parentRow = plan.Rows.Single(r => r.SourceId == "source-1");
        Assert.Equal(CounterpartyImportDisposition.UpdateByName, parentRow.Disposition);
        Assert.Equal("target-1", parentRow.TargetId);
        Assert.Equal("info@acme.example", Reload("target-1").Email);
        Assert.Equal("target-1", Reload("source-2").ParentId);
        Assert.Null(await _context.Counterparties.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == "source-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_TwoLiveRowsShareTheName_SkipsAsAmbiguous()
    {
        Seed("t-1", "Acme");
        Seed("t-2", "acme");
        CounterpartyBundleDto bundle = Bundle(Entry("s-1", "Acme"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        AssertSkipped(plan.Rows.Single(), CounterpartyImportSkipReason.AmbiguousName);
        Assert.Equal(2, await _context.Counterparties.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_IdHeldByAnotherOrganization_Skips()
    {
        Seed("shared-id", "Theirs", organizationId: OTHER_ORG_ID);
        CounterpartyBundleDto bundle = Bundle(Entry("shared-id", "Mine"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        AssertSkipped(plan.Rows.Single(), CounterpartyImportSkipReason.IdHeldByAnotherOrganization);
        Assert.Equal("Theirs", Reload("shared-id").Name);
        Assert.Equal(OTHER_ORG_ID, Reload("shared-id").OrganizationId);
    }

    [Fact]
    public async Task Handle_RenameIntoAnotherLiveName_SkipsAsNameCollision()
    {
        Seed("cp-1", "Alpha");
        Seed("cp-2", "Beta");
        CounterpartyBundleDto bundle = Bundle(Entry("cp-1", "beta"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        AssertSkipped(plan.Rows.Single(), CounterpartyImportSkipReason.NameCollision);
        Assert.Equal("Alpha", Reload("cp-1").Name);
    }

    [Fact]
    public async Task Handle_TwoRowsLandOnOneTarget_SkipsTheSecond()
    {
        Seed("t-1", "Acme");
        CounterpartyBundleDto bundle = Bundle(
            Entry("s-1", "Acme", email: "first@example.com"),
            Entry("s-2", "acme", email: "second@example.com"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        Assert.Equal(CounterpartyImportDisposition.UpdateByName, plan.Rows[0].Disposition);
        AssertSkipped(plan.Rows[1], CounterpartyImportSkipReason.NameCollision);
        Assert.Equal("first@example.com", Reload("t-1").Email);
    }

    // The snapshot cannot see a name the bundle itself brings in twice: a rename and a new row
    // heading for the same name would otherwise leave two live counterparties with one name.
    [Fact]
    public async Task Handle_TwoRowsCarryOneNewName_SkipsTheSecondAsNameCollision()
    {
        Seed("cp-1", "Alpha");
        CounterpartyBundleDto bundle = Bundle(
            Entry("cp-1", "Gamma"),
            Entry("new-1", "gamma"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        Assert.Equal(CounterpartyImportDisposition.UpdateById, plan.Rows[0].Disposition);
        AssertSkipped(plan.Rows[1], CounterpartyImportSkipReason.NameCollision);
        Assert.Equal("Gamma", Reload("cp-1").Name);
        Assert.False(_context.Counterparties.IgnoreQueryFilters().Any(c => c.Id == "new-1"));
    }

    [Fact]
    public async Task Handle_InvalidParent_SkipsItAndItsWholeSubtree()
    {
        CounterpartyBundleDto bundle = Bundle(
            Entry("child", "Child", parentId: "root"),
            Entry("root", "Bad|Name"),
            Entry("grandchild", "Grandchild", parentId: "child"),
            Entry("other", "Other"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        AssertSkipped(plan.Rows.Single(r => r.SourceId == "root"), CounterpartyImportSkipReason.Invalid);
        AssertSkipped(plan.Rows.Single(r => r.SourceId == "child"), CounterpartyImportSkipReason.ParentNotLanded);
        AssertSkipped(plan.Rows.Single(r => r.SourceId == "grandchild"), CounterpartyImportSkipReason.ParentNotLanded);
        Assert.Equal(CounterpartyImportDisposition.Create, plan.Rows.Single(r => r.SourceId == "other").Disposition);
        Assert.Equal(1, await _context.Counterparties.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_ParentOnlyOnTheTarget_LinksToIt()
    {
        Seed("existing-parent", "Existing Parent");
        CounterpartyBundleDto bundle = Bundle(Entry("child", "Child", parentId: "existing-parent"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        Assert.Equal(CounterpartyImportDisposition.Create, plan.Rows.Single().Disposition);
        Assert.Equal("existing-parent", Reload("child").ParentId);
    }

    [Fact]
    public async Task Handle_ParentNeitherInBundleNorOnTarget_SkipsAsParentNotLanded()
    {
        CounterpartyBundleDto bundle = Bundle(Entry("child", "Child", parentId: "nowhere"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        AssertSkipped(plan.Rows.Single(), CounterpartyImportSkipReason.ParentNotLanded);
    }

    [Fact]
    public async Task Handle_ParentCycle_RefusesTheBundle()
    {
        CounterpartyBundleDto bundle = Bundle(
            Entry("a", "A", parentId: "b"),
            Entry("b", "B", parentId: "a"));

        ErrorOr<CounterpartyImportPlanDto> result = await _handler.Handle(Command(bundle, dryRun: false), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(CounterpartyBundleErrorFactory.CYCLE_CODE, result.FirstError.Code);
        Assert.Contains("'a'", result.FirstError.Description);
        Assert.Contains("'b'", result.FirstError.Description);
    }

    [Fact]
    public async Task Handle_UnsupportedFormatVersion_Refuses()
    {
        CounterpartyBundleDto bundle = Bundle(Entry("a", "A"));
        bundle.BundleFormatVersion = CounterpartyBundleDto.CURRENT_FORMAT_VERSION + 1;

        ErrorOr<CounterpartyImportPlanDto> result = await _handler.Handle(Command(bundle, dryRun: false), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(CounterpartyBundleErrorFactory.VERSION_UNSUPPORTED_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_DuplicateIdInBundle_SkipsTheLaterRow()
    {
        CounterpartyBundleDto bundle = Bundle(
            Entry("dup", "First"),
            Entry("DUP", "Second"));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        Assert.Equal(CounterpartyImportDisposition.Create, plan.Rows[0].Disposition);
        AssertSkipped(plan.Rows[1], CounterpartyImportSkipReason.DuplicateIdInBundle);
        Assert.Equal("First", Reload("dup").Name);
    }

    [Fact]
    public async Task Handle_RowAbsentFromBundle_StaysUntouched()
    {
        Seed("keep-me", "Keep Me");
        CounterpartyBundleDto bundle = Bundle(Entry("new", "New"));

        _ = await ApplyAsync(bundle);

        Assert.False(Reload("keep-me").IsDeleted);
        Assert.Equal(2, await _context.Counterparties.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_RowWithoutTypes_LandsAsTheSourceHeldIt()
    {
        CounterpartyBundleDto bundle = Bundle(Entry("untyped", "Untyped", types: []));

        CounterpartyImportPlanDto plan = await ApplyAsync(bundle);

        Assert.Equal(CounterpartyImportDisposition.Create, plan.Rows.Single().Disposition);
        Assert.Empty(Reload("untyped").CounterpartyTypes);
    }

    /// <summary>
    /// The import and the export both take their fields from <see cref="ICounterpartyFields"/>. This
    /// sets every field the interface names and reads every one back after the round trip, so a
    /// field added to the interface that either side cannot carry fails.
    /// </summary>
    [Fact]
    public async Task EveryCounterpartyField_LeavesOnExport_AndLandsOnImport()
    {
        var row = new Counterparty(ORG_ID, "Every Field") { Id = "every-field" };
        foreach (PropertyInfo field in typeof(ICounterpartyFields).GetProperties())
        {
            PropertyInfo stored = typeof(Counterparty).GetProperty(field.Name)
                ?? throw new InvalidOperationException($"{nameof(Counterparty)} has no {field.Name}, which {nameof(ICounterpartyFields)} names.");
            stored.SetValue(row, SampleValue(field));
        }

        _ = _context.Counterparties.Add(row);
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();

        ErrorOr<CounterpartyBundleDto> exported = await _exporter.Handle(new ExportCounterpartiesQuery(ORG_ID, USER_ID, INSTANCE), TestContext.Current.CancellationToken);
        ErrorOr<CounterpartyImportPlanDto> imported = await _handler.Handle(
            new ImportCounterpartiesCommand(OTHER_ORG_ID, USER_ID, Renamed(exported.Value), DryRun: false),
            TestContext.Current.CancellationToken);
        Assert.False(imported.IsError);
        _context.ChangeTracker.Clear();

        CounterpartyBundleEntryDto exportedEntry = Assert.Single(exported.Value.Counterparties);
        Counterparty landed = Assert.Single(_context.Counterparties.AsNoTracking().Where(counterparty => counterparty.OrganizationId == OTHER_ORG_ID));
        foreach (PropertyInfo field in typeof(ICounterpartyFields).GetProperties())
        {
            string expected = JsonSerializer.Serialize(SampleValue(field));
            Assert.True(expected == JsonSerializer.Serialize(field.GetValue(exportedEntry)), $"{field.Name} did not leave on export.");
            Assert.True(expected == JsonSerializer.Serialize(typeof(Counterparty).GetProperty(field.Name)!.GetValue(landed)), $"{field.Name} did not land on import.");
        }
    }

    [Fact]
    public async Task EveryCounterpartyField_ReadsBackThroughTheReadModel()
    {
        var row = new Counterparty(ORG_ID, "Every Field") { Id = "every-field-read" };
        foreach (PropertyInfo field in typeof(ICounterpartyFields).GetProperties())
        {
            typeof(Counterparty).GetProperty(field.Name)!.SetValue(row, SampleValue(field));
        }

        _ = _context.Counterparties.Add(row);
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();

        ErrorOr<CounterpartyDto> read = await new GetCounterpartyQueryHandler(_serviceProvider.GetRequiredService<IUserDataUnitOfWork>())
            .Handle(new GetCounterpartyQuery(row.Id, ORG_ID), TestContext.Current.CancellationToken);

        Assert.False(read.IsError);
        foreach (PropertyInfo field in typeof(ICounterpartyFields).GetProperties())
        {
            Assert.True(
                JsonSerializer.Serialize(SampleValue(field)) == JsonSerializer.Serialize(field.GetValue(read.Value)),
                $"{field.Name} did not read back.");
        }
    }

    private static object SampleValue(PropertyInfo field)
    {
        return field.Name switch
        {
            nameof(ICounterpartyFields.Email) => "every.field@example.com",
            nameof(ICounterpartyFields.PhoneNumber) => "+44 20 7946 0000",
            nameof(ICounterpartyFields.Website) => "https://every-field.example.com",
            _ when field.PropertyType == typeof(Dictionary<string, string>) => new Dictionary<string, string> { [field.Name] = $"{field.Name} value" },
            _ => $"{field.Name} value",
        };
    }

    private static CounterpartyBundleDto Renamed(CounterpartyBundleDto bundle)
    {
        foreach (CounterpartyBundleEntryDto entry in bundle.Counterparties)
        {
            entry.Id = $"{entry.Id}-copy";
        }

        return bundle;
    }

    [Fact]
    public async Task ExportThenImport_IntoAFreshDeployment_YieldsTheSameGraph()
    {
        Seed("acme", "Acme Holdings", email: "info@acme.example", properties: new() { ["Ticker"] = "ACM" }, financials: FINANCIALS, typeIds: [TYPE_CORPORATE_ID]);
        Seed("acme-eu", "Acme Europe", parentId: "acme", typeIds: [TYPE_CORPORATE_ID]);
        Seed("acme-de", "Acme Germany", parentId: "acme-eu");
        Seed("gone", "Gone", isDeleted: true);

        ErrorOr<CounterpartyBundleDto> exported = await _exporter.Handle(new ExportCounterpartiesQuery(ORG_ID, USER_ID, INSTANCE), TestContext.Current.CancellationToken);
        Assert.False(exported.IsError);
        Assert.Equal(INSTANCE, exported.Value.SourceInstance);
        Assert.Equal(3, exported.Value.Counterparties.Count);

        var otherDeployment = new ServiceCollection();
        _ = otherDeployment.AddTestDatabaseServices();
        using ServiceProvider otherProvider = otherDeployment.BuildServiceProvider();
        using UserDataDbContext otherContext = otherProvider.GetRequiredService<UserDataDbContext>();
        var otherHandler = new ImportCounterpartiesCommandHandler(
            NullLogger<ImportCounterpartiesCommandHandler>.Instance,
            otherProvider.GetRequiredService<IUserDataUnitOfWork>(),
            otherContext,
            new DbExceptionAnalyzer());

        ErrorOr<CounterpartyImportPlanDto> imported = await otherHandler.Handle(
            new ImportCounterpartiesCommand(OTHER_ORG_ID, USER_ID, exported.Value, DryRun: false),
            TestContext.Current.CancellationToken);
        Assert.False(imported.IsError);
        Assert.Equal(3, imported.Value.CreateCount);
        Assert.Equal([TYPE_CORPORATE], imported.Value.CounterpartyTypesToCreate);
        otherContext.ChangeTracker.Clear();

        ErrorOr<CounterpartyBundleDto> reExported = await new ExportCounterpartiesQueryHandler(otherContext)
            .Handle(new ExportCounterpartiesQuery(OTHER_ORG_ID, USER_ID, INSTANCE), TestContext.Current.CancellationToken);
        Assert.False(reExported.IsError);
        Assert.Equal(Describe(exported.Value), Describe(reExported.Value));
        Assert.Equal("A company", otherContext.CounterpartyTypes.Single().Description);
        _ = otherContext.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Handle_BundleFromAnotherOrganizationOfThisDeployment_SkipsEveryRowWhoseIdTheyHold()
    {
        Seed("acme", "Acme Holdings");
        ErrorOr<CounterpartyBundleDto> exported = await _exporter.Handle(new ExportCounterpartiesQuery(ORG_ID, USER_ID, INSTANCE), TestContext.Current.CancellationToken);

        ErrorOr<CounterpartyImportPlanDto> imported = await _handler.Handle(
            new ImportCounterpartiesCommand(OTHER_ORG_ID, USER_ID, exported.Value, DryRun: false),
            TestContext.Current.CancellationToken);

        Assert.False(imported.IsError);
        AssertSkipped(imported.Value.Rows.Single(), CounterpartyImportSkipReason.IdHeldByAnotherOrganization);
    }

    private static string Describe(CounterpartyBundleDto bundle)
    {
        return string.Join("\n", bundle.Counterparties.Select(c =>
            $"{c.Id}|{c.ParentId}|{c.Name}|{c.NameVariants}|{c.Email}|{c.PhoneNumber}|{c.Website}|{c.Address}|{c.LastFinancialAnalysisResult}"
            + $"|{string.Join(",", (c.Properties ?? []).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"{p.Key.ToLowerInvariant()}={p.Value}"))}"
            + $"|{string.Join(",", c.CounterpartyTypeNames)}"));
    }

    private async Task<CounterpartyImportPlanDto> ApplyAsync(CounterpartyBundleDto bundle)
    {
        return await RunAsync(bundle, dryRun: false);
    }

    private async Task<CounterpartyImportPlanDto> RunAsync(CounterpartyBundleDto bundle, bool dryRun)
    {
        ErrorOr<CounterpartyImportPlanDto> result = await _handler.Handle(Command(bundle, dryRun), TestContext.Current.CancellationToken);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
        _context.ChangeTracker.Clear();
        return result.Value;
    }

    private static ImportCounterpartiesCommand Command(CounterpartyBundleDto bundle, bool dryRun)
    {
        return new ImportCounterpartiesCommand(ORG_ID, USER_ID, bundle, dryRun);
    }

    private static void AssertSkipped(CounterpartyImportRowDto row, CounterpartyImportSkipReason reason)
    {
        Assert.Equal(CounterpartyImportDisposition.Skip, row.Disposition);
        Assert.Equal(reason, row.SkipReason);
        Assert.False(string.IsNullOrWhiteSpace(row.Detail));
    }

    private Counterparty Reload(string id)
    {
        _context.ChangeTracker.Clear();
        return _context.Counterparties
            .IgnoreQueryFilters()
            .Include(c => c.CounterpartyTypes)
            .Single(c => c.Id == id);
    }

    private void Seed(
        string id,
        string name,
        string organizationId = ORG_ID,
        string? parentId = null,
        string? email = null,
        Dictionary<string, string>? properties = null,
        string? financials = null,
        IReadOnlyList<string>? typeIds = null,
        bool isDeleted = false)
    {
        var row = new Counterparty(organizationId, name)
        {
            Id = id,
            ParentId = parentId,
            Email = email,
            Properties = properties,
            LastFinancialAnalysisResult = financials,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null,
        };
        foreach (string typeId in typeIds ?? [])
        {
            row.CounterpartyTypes.Add(_context.CounterpartyTypes.Single(t => t.Id == typeId));
        }

        _ = _context.Counterparties.Add(row);
        _ = _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    private static CounterpartyBundleDto Bundle(params CounterpartyBundleEntryDto[] entries)
    {
        return new CounterpartyBundleDto
        {
            SourceInstance = INSTANCE,
            CounterpartyTypes = [new() { Name = TYPE_BANK, Description = "A bank" }],
            Counterparties = [.. entries],
        };
    }

    private static CounterpartyBundleEntryDto Entry(
        string id,
        string name,
        string? parentId = null,
        string? email = null,
        Dictionary<string, string>? properties = null,
        string? financials = null,
        IReadOnlyList<string>? types = null)
    {
        return new CounterpartyBundleEntryDto
        {
            Id = id,
            Name = name,
            ParentId = parentId,
            Email = email,
            Properties = properties,
            LastFinancialAnalysisResult = financials,
            CounterpartyTypeNames = [.. types ?? [TYPE_CORPORATE]],
        };
    }
}
