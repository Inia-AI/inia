using ErrorOr;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Domain.Core.Interfaces;
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
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.WorkflowTemplates.GetWorkflowTemplateContent;

public class GetWorkflowTemplateContentQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";

    [Fact]
    public async Task NoOrganizationAccess_StillServesContent()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(TYPE, TYPE));
        _ = harness.Context.Workflows.Add(BuildWorkflow());
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);
        ErrorOr<WorkflowTemplateContentResult> result = await handler.Handle(
            new GetWorkflowTemplateContentQuery(ORG, USER, TYPE),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.WorkflowJson));
    }

    [Fact]
    public async Task UnknownType_NotFound()
    {
        using var harness = new CoreHarness();

        GetWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);
        ErrorOr<WorkflowTemplateContentResult> result = await handler.Handle(
            new GetWorkflowTemplateContentQuery(ORG, USER, TYPE),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("WorkflowTemplate.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HeadVersion_ServedWithMetadata()
    {
        using var harness = new CoreHarness();
        await SeedTypeAsync(harness);
        string json = BuildCanonicalJson();
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = "hash-1",
            WorkflowJson = json,
            CreatedByUserId = USER,
        };
        _ = harness.Context.WorkflowVersions.Add(version);
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);
        ErrorOr<WorkflowTemplateContentResult> result = await handler.Handle(
            new GetWorkflowTemplateContentQuery(ORG, USER, TYPE),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(version.Id, result.Value.VersionId);
        Assert.Equal("hash-1", result.Value.ContentHash);
        Assert.Equal(USER, result.Value.CreatedByUserId);
        Assert.Equal(json, result.Value.WorkflowJson);
    }

    [Fact]
    public async Task TrimmedHead_FallsBackToWorkflowRow_KeepsVersionId()
    {
        using var harness = new CoreHarness();
        await SeedTypeAsync(harness);
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = "hash-1",
            CreatedByUserId = USER,
            PayloadTrimmedAt = DateTime.UtcNow,
        };
        _ = harness.Context.WorkflowVersions.Add(version);
        _ = harness.Context.Workflows.Add(BuildWorkflow());
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);
        ErrorOr<WorkflowTemplateContentResult> result = await handler.Handle(
            new GetWorkflowTemplateContentQuery(ORG, USER, TYPE),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(version.Id, result.Value.VersionId);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.WorkflowJson));
    }

    [Fact]
    public async Task NoVersionNoWorkflow_NotFound()
    {
        using var harness = new CoreHarness();
        await SeedTypeAsync(harness);

        GetWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);
        ErrorOr<WorkflowTemplateContentResult> result = await handler.Handle(
            new GetWorkflowTemplateContentQuery(ORG, USER, TYPE),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("WorkflowTemplate.NoContent", result.FirstError.Code);
    }

    [Fact]
    public async Task NoVersionHistory_ServedFromWorkflowRow_NullVersionId()
    {
        using var harness = new CoreHarness();
        await SeedTypeAsync(harness);
        _ = harness.Context.Workflows.Add(BuildWorkflow());
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);
        ErrorOr<WorkflowTemplateContentResult> result = await handler.Handle(
            new GetWorkflowTemplateContentQuery(ORG, USER, TYPE),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.VersionId);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.WorkflowJson));
    }

    private static GetWorkflowTemplateContentQueryHandler CreateHandler(CoreHarness harness)
    {
        return new GetWorkflowTemplateContentQueryHandler(harness.UnitOfWork, new WorkflowCanonicalizer());
    }

    private static async Task SeedTypeAsync(CoreHarness harness)
    {
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(TYPE, TYPE));
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Workflow BuildWorkflow()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = TYPE, OrganizationId = ORG };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = "Who is the counterparty?" },
        };

        return workflow;
    }

    private static string BuildCanonicalJson()
    {
        return new WorkflowCanonicalizer().ToCanonicalJson(BuildWorkflow());
    }

    private sealed class CoreHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }

        private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

        public CoreHarness()
        {
            DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"getwftc-{Guid.NewGuid()}")
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
