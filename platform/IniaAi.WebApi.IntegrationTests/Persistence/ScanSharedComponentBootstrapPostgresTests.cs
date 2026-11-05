using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.ScanSharedComponentBootstrap;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Runs the bootstrap suggester scan against a real PostgreSQL database with the production
/// relational loader, on templates shaped exactly like GUI-authored ones (empty section name,
/// titled section, single default-tagged block). The InMemory suite proves the grouping logic;
/// this proves the relationally materialized graphs hash identically across workflow types.
/// </summary>
public sealed class ScanSharedComponentBootstrapPostgresTests : IClassFixture<PostgresCoreDatabaseFixture>
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string PROMPT = "What is the total revenue reported in this document?";

    private readonly PostgresCoreDatabaseFixture _fixture;

    public ScanSharedComponentBootstrapPostgresTests(PostgresCoreDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scan_TwoGuiShapedIdenticalTemplates_GroupsSectionAndBlockOnPostgres()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip("No PostgreSQL test server configured (INIA_TEST_PG).");
        }

        using var harness = new ScanHarness(_fixture.Options);
        SaveWorkflowCommandHandler saveHandler = harness.CreateSaveHandler();
        await SaveGuiShapedTemplateAsync(harness, saveHandler, "QA-PG-SCAN-A");
        await SaveGuiShapedTemplateAsync(harness, saveHandler, "QA-PG-SCAN-B");

        var scanHandler = new ScanSharedComponentBootstrapQueryHandler(
            harness.UnitOfWork,
            harness.Normalizer,
            NullLogger<ScanSharedComponentBootstrapQueryHandler>.Instance);
        ErrorOr<SharedComponentBootstrapScanResult> result = await scanHandler.Handle(
            new ScanSharedComponentBootstrapQuery(ORG, USER), Ct);

        Assert.False(result.IsError);
        SharedComponentBootstrapSuggestion sectionGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Section);
        Assert.Equal(2, sectionGroup.Nodes.Count);
        Assert.True(sectionGroup.CrossType);
        Assert.Equal("C8BootstrapSection", sectionGroup.SuggestedName);
        SharedComponentBootstrapSuggestion blockGroup = Assert.Single(
            result.Value.Suggestions, s => s.Kind == SharedComponentKind.Block);
        Assert.Equal(2, blockGroup.Nodes.Count);
    }

    private static async Task SaveGuiShapedTemplateAsync(
        ScanHarness harness,
        SaveWorkflowCommandHandler saveHandler,
        string workflowTypeId)
    {
        var workflow = new Workflow(workflowTypeId) { Title = "" };
        var section = new Section("", workflow) { Title = "C8BootstrapSection" };
        var block = new Block(BlockTypes.AiQuery, section, "block_0")
        {
            Name = "C8BootstrapBlock",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = PROMPT,
                MaxTokens = 8000,
                ShouldUseAdvancedModel = true,
                ShouldUsePageImages = false,
            },
        };
        block.SupportedDocumentClasses.Clear();
        block.SupportedDocumentClasses.Add("Any");

        _ = harness.Context.Add(new WorkflowType(workflowTypeId, workflowTypeId));
        _ = await harness.Context.SaveChangesAsync(Ct);
        ErrorOr<SaveWorkflowResult> saved = await saveHandler.Handle(
            new SaveWorkflowCommand(ORG, USER, workflowTypeId, workflow), Ct);
        Assert.False(saved.IsError);
    }

    private sealed class ScanHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }
        public WorkflowCanonicalizer Canonicalizer { get; } = new();
        public SharedComponentContentNormalizer Normalizer { get; }

        private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

        public ScanHarness(DbContextOptions<CoreDbContext> coreOptions)
        {
            Context = new CoreDbContext(coreOptions);

            var services = new ServiceCollection();
            _ = services.AddScoped(_ => Context);
            _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
            _ = services.AddTestDatabaseServicesWithoutDbContext();
            _provider = services.BuildServiceProvider();

            UnitOfWork = _provider.GetRequiredService<ICoreUnitOfWork>();
            Normalizer = new SharedComponentContentNormalizer(Canonicalizer);
        }

        public SaveWorkflowCommandHandler CreateSaveHandler()
        {
            var pipeline = new WorkflowSavePipeline(
                UnitOfWork,
                Context,
                Canonicalizer,
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(new AgentRuntimeSettings()),
                NullLogger<WorkflowSavePipeline>.Instance, SuperAdminLookups.OverEmptyUserData());

            return new SaveWorkflowCommandHandler(pipeline, Context, NullLogger<SaveWorkflowCommandHandler>.Instance);
        }

        public void Dispose()
        {
            Context.Dispose();
            _provider.Dispose();
        }
    }
}
