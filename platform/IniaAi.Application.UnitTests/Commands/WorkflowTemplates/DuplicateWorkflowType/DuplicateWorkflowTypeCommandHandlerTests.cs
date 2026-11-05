using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Commands.WorkflowTemplates.DuplicateWorkflowType;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Core;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Commands.WorkflowTemplates.DuplicateWorkflowType;

public class DuplicateWorkflowTypeCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string SOURCE = "WT-Report";
    private const string TARGET = "WT-Report-EU";
    private const string COMPONENT = "sc-counterparty";
    private const string SIGNATURE_FIELD = "analyst_name";

    [Fact]
    public async Task CreatesTypeContentAndRootVersion()
    {
        using var harness = new CoreHarness();
        await SeedSourceAsync(harness);

        ErrorOr<DuplicateWorkflowTypeResult> result = await Handler(harness).Handle(
            Command(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        WorkflowType created = await harness.Context.WorkflowTypes
            .SingleAsync(type => type.Id == TARGET, TestContext.Current.CancellationToken);
        Assert.Equal("Report Workflow (EU)", created.Name);

        Workflow duplicated = await harness.Context.Workflows
            .Include(workflow => workflow.Sections)
            .SingleAsync(workflow => workflow.WorkflowTypeId == TARGET, TestContext.Current.CancellationToken);
        Assert.Single(duplicated.Sections);

        WorkflowVersion version = await harness.Context.WorkflowVersions
            .SingleAsync(candidate => candidate.WorkflowTypeId == TARGET, TestContext.Current.CancellationToken);
        Assert.Equal(result.Value.VersionId, version.Id);
        Assert.Null(version.ParentVersionId);
        Assert.Contains(SOURCE, version.AutoNarrativeSummary);
    }

    [Fact]
    public async Task StampsLineageAtTheImmediateSource()
    {
        using var harness = new CoreHarness();
        await SeedSourceAsync(harness);

        _ = await Handler(harness).Handle(Command(), TestContext.Current.CancellationToken);

        WorkflowType created = await harness.Context.WorkflowTypes
            .SingleAsync(type => type.Id == TARGET, TestContext.Current.CancellationToken);
        Assert.Equal(SOURCE, created.DuplicatedFromWorkflowTypeId);

        WorkflowType source = await harness.Context.WorkflowTypes
            .SingleAsync(type => type.Id == SOURCE, TestContext.Current.CancellationToken);
        Assert.Null(source.DuplicatedFromWorkflowTypeId);
    }

    [Fact]
    public async Task PreservesStableIds()
    {
        using var harness = new CoreHarness();
        Workflow source = await SeedSourceAsync(harness);
        string sourceSectionStableId = source.Sections.Single().StableSectionId!;
        string sourceBlockStableId = source.Sections.Single().Blocks.Single().StableBlockId!;

        _ = await Handler(harness).Handle(Command(), TestContext.Current.CancellationToken);

        Section duplicatedSection = await LoadDuplicatedSectionAsync(harness);
        Assert.Equal(sourceSectionStableId, duplicatedSection.StableSectionId);
        Assert.Equal(sourceBlockStableId, duplicatedSection.Blocks.Single().StableBlockId);
    }

    [Fact]
    public async Task KeepsLinksLive()
    {
        using var harness = new CoreHarness();
        _ = await SeedSourceAsync(harness);

        _ = await Handler(harness).Handle(Command(), TestContext.Current.CancellationToken);

        Block duplicatedBlock = (await LoadDuplicatedSectionAsync(harness)).Blocks.Single();
        Assert.Equal(COMPONENT, duplicatedBlock.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, duplicatedBlock.SharedComponentLinkState);
        Assert.Equal("hash-1", duplicatedBlock.SharedComponentVersionHash);
    }

    [Fact]
    public async Task GrantsNoOrganizationAccess()
    {
        using var harness = new CoreHarness();
        _ = await SeedSourceAsync(harness);

        _ = await Handler(harness).Handle(Command(), TestContext.Current.CancellationToken);

        List<WorkflowTypeOrganizationAccess> access = await harness.Context
            .Set<WorkflowTypeOrganizationAccess>()
            .Where(row => row.WorkflowTypeId == TARGET)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(access);
    }

    [Fact]
    public async Task CarriesSignatureDefinitions_ButNoOverrideValues()
    {
        using var harness = new CoreHarness();
        _ = await SeedSourceAsync(harness);
        _ = harness.Context.Set<SignatureFieldOverride>().Add(new SignatureFieldOverride
        {
            OrganizationId = ORG,
            WorkflowTypeId = SOURCE,
            PlaceholderName = SIGNATURE_FIELD,
            Value = "Approved Analyst",
        });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _ = await Handler(harness).Handle(Command(), TestContext.Current.CancellationToken);

        Workflow duplicated = await harness.Context.Workflows
            .Include(workflow => workflow.SignatureFields)
            .SingleAsync(workflow => workflow.WorkflowTypeId == TARGET, TestContext.Current.CancellationToken);
        Assert.Equal(SIGNATURE_FIELD, duplicated.SignatureFields.Single().Name);

        List<SignatureFieldOverride> overrides = await harness.Context
            .Set<SignatureFieldOverride>()
            .Where(row => row.WorkflowTypeId == TARGET)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(overrides);
    }

    [Fact]
    public async Task TheDuplicate_CarriesTheOverviewPrompts()
    {
        using var harness = new CoreHarness();
        _ = await SeedSourceAsync(harness, source =>
        {
            source.Configuration = new WorkflowConfiguration
            {
                PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Default overview of page {0}." } },
            };
            _ = new DocumentClass(source, "annual-report")
            {
                PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Annual report page {0}." } },
            };
        });

        ErrorOr<DuplicateWorkflowTypeResult> result = await Handler(harness).Handle(
            Command(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow duplicated = await harness.Context.Workflows
            .Include(workflow => workflow.DocumentClasses)
            .SingleAsync(workflow => workflow.WorkflowTypeId == TARGET, TestContext.Current.CancellationToken);
        Assert.Equal("Default overview of page {0}.", duplicated.Configuration!.PageOverviews!.Pages!.UserPrompt);
        Assert.Equal("Annual report page {0}.", Assert.Single(duplicated.DocumentClasses).PageOverviews!.Pages!.UserPrompt);
    }

    [Fact]
    public async Task ReportsWhatDidNotCarry()
    {
        using var harness = new CoreHarness();
        _ = await SeedSourceAsync(harness);

        ErrorOr<DuplicateWorkflowTypeResult> result = await Handler(harness).Handle(
            Command(), TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Value.SkippedCarriers);
        Assert.All(result.Value.SkippedCarriers, carrier =>
        {
            Assert.False(string.IsNullOrWhiteSpace(carrier.Carrier));
            Assert.False(string.IsNullOrWhiteSpace(carrier.Reason));
        });
    }

    [Fact]
    public async Task UnknownSource_ReturnsNotFound()
    {
        using var harness = new CoreHarness();

        ErrorOr<DuplicateWorkflowTypeResult> result = await Handler(harness).Handle(
            Command(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(DuplicateWorkflowTypeCommandHandler.NOT_FOUND_CODE, result.FirstError.Code);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task ExistingTarget_ReturnsAlreadyExists()
    {
        using var harness = new CoreHarness();
        _ = await SeedSourceAsync(harness);
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(TARGET, "Taken"));
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<DuplicateWorkflowTypeResult> result = await Handler(harness).Handle(
            Command(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(DuplicateWorkflowTypeCommandHandler.ALREADY_EXISTS_CODE, result.FirstError.Code);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public async Task SourceWithoutContent_ReturnsNotFound_AndCreatesNothing()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(SOURCE, "Empty"));
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<DuplicateWorkflowTypeResult> result = await Handler(harness).Handle(
            Command(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(DuplicateWorkflowTypeCommandHandler.NOT_FOUND_CODE, result.FirstError.Code);
        Assert.Empty(await harness.Context.WorkflowTypes
            .Where(type => type.Id == TARGET)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static DuplicateWorkflowTypeCommand Command()
    {
        return new DuplicateWorkflowTypeCommand(ORG, USER, SOURCE, TARGET, "Report Workflow (EU)");
    }

    private static async Task<Section> LoadDuplicatedSectionAsync(CoreHarness harness)
    {
        Workflow duplicated = await harness.Context.Workflows
            .Include(workflow => workflow.Sections)
            .ThenInclude(section => section.Blocks)
            .SingleAsync(workflow => workflow.WorkflowTypeId == TARGET, TestContext.Current.CancellationToken);

        return duplicated.Sections.Single();
    }

    private static async Task<Workflow> SeedSourceAsync(CoreHarness harness, Action<Workflow>? configure = null)
    {
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(SOURCE, "Report Workflow")
        {
            CategoryId = "monitoring-category",
        });

        var workflow = new Workflow("Report")
        {
            Title = "Report",
            WorkflowTypeId = SOURCE,
            OrganizationId = ORG,
            UserId = USER,
        };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = "Who is the counterparty?" },
            SharedComponentId = COMPONENT,
            SharedComponentVersionHash = "hash-1",
            SharedComponentLinkState = SharedComponentLinkState.Linked,
        };
        _ = new WorkflowSignatureField(workflow, SIGNATURE_FIELD);
        configure?.Invoke(workflow);

        _ = harness.Context.Workflows.Add(workflow);
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        harness.Context.ChangeTracker.Clear();

        return workflow;
    }

    private static DuplicateWorkflowTypeCommandHandler Handler(CoreHarness harness)
    {
        return new DuplicateWorkflowTypeCommandHandler(
            harness.UnitOfWork,
            harness.Context,
            new WorkflowSavePipeline(
                harness.UnitOfWork,
                harness.Context,
                new WorkflowCanonicalizer(),
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(new AgentRuntimeSettings()),
                NullLogger<WorkflowSavePipeline>.Instance, IniaAi.Testing.DependencyInjection.SuperAdminLookups.OverEmptyUserData()),
            NullLogger<DuplicateWorkflowTypeCommandHandler>.Instance, IniaAi.Testing.DependencyInjection.SuperAdminLookups.OverEmptyUserData());
    }

    private sealed class CoreHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }

        private readonly ServiceProvider _provider;

        public CoreHarness()
        {
            DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"duplicatewft-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Context = new CoreDbContext(options);

            var services = new ServiceCollection();
            _ = services.AddScoped(typeof(ICoreRepository<>), typeof(CoreRepositoryWrapper<>));
            _ = services.AddScoped<CoreDbContext>(_ => Context);
            _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
            _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
            _ = services.AddScoped<IToolsSettingsRepository, ToolsSettingsRepository>();
            _provider = services.BuildServiceProvider();

            UnitOfWork = new CoreUnitOfWork(Context, _provider);
        }

        public void Dispose()
        {
            Context.Dispose();
            _provider.Dispose();
        }
    }
}
