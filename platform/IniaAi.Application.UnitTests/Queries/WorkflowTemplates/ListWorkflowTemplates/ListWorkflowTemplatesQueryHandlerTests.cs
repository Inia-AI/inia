using ErrorOr;
using IniaAi.Application.Queries.WorkflowTemplates.ListWorkflowTemplates;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core.UnitOfWork;
using IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.WorkflowTemplates.ListWorkflowTemplates;

public class ListWorkflowTemplatesQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task ReturnsAllTypesRegardlessOfOrgAccess()
    {
        using var harness = new CoreHarness();
        await SeedTypeAsync(harness, "WT-Mine");
        await SeedTypeAsync(harness, "WT-Other");
        await SeedTypeAsync(harness, "WT-NoAccessRows");

        var handler = new ListWorkflowTemplatesQueryHandler(harness.UnitOfWork);
        ErrorOr<List<WorkflowTemplateSummaryDto>> result = await handler.Handle(
            new ListWorkflowTemplatesQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.Count);
        Assert.Contains(result.Value, template => template.Id == "WT-Other");
        Assert.Contains(result.Value, template => template.Id == "WT-NoAccessRows");
    }

    [Fact]
    public async Task ExcludesSpecialTypes()
    {
        using var harness = new CoreHarness();
        await SeedTypeAsync(harness, WorkflowType.GENERIC);
        await SeedTypeAsync(harness, WorkflowType.TEST_WORKFLOW_TYPE);
        await SeedTypeAsync(harness, "WT-Normal");

        var handler = new ListWorkflowTemplatesQueryHandler(harness.UnitOfWork);
        ErrorOr<List<WorkflowTemplateSummaryDto>> result = await handler.Handle(
            new ListWorkflowTemplatesQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.Equal("WT-Normal", Assert.Single(result.Value).Id);
        Assert.DoesNotContain(result.Value, t => t.Id == WorkflowType.GENERIC);
        Assert.DoesNotContain(result.Value, t => t.Id == WorkflowType.TEST_WORKFLOW_TYPE);
    }

    [Fact]
    public async Task ExcludesRetiredTypes()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType("WT-Deleted", "Deleted") { RetiredAt = DateTime.UtcNow });
        await SeedTypeAsync(harness, "WT-Normal");

        var handler = new ListWorkflowTemplatesQueryHandler(harness.UnitOfWork);
        ErrorOr<List<WorkflowTemplateSummaryDto>> result = await handler.Handle(
            new ListWorkflowTemplatesQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.Equal("WT-Normal", Assert.Single(result.Value).Id);
    }

    [Fact]
    public async Task HasContent_ReflectsWorkflowRowPresence()
    {
        using var harness = new CoreHarness();
        await SeedTypeAsync(harness, "WT-Empty");
        await SeedTypeAsync(harness, "WT-Filled");
        _ = harness.Context.Workflows.Add(new Workflow("Filled") { WorkflowTypeId = "WT-Filled" });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListWorkflowTemplatesQueryHandler(harness.UnitOfWork);
        ErrorOr<List<WorkflowTemplateSummaryDto>> result = await handler.Handle(
            new ListWorkflowTemplatesQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.Value.Single(t => t.Id == "WT-Empty").HasContent);
        Assert.True(result.Value.Single(t => t.Id == "WT-Filled").HasContent);
    }

    private static async Task SeedTypeAsync(CoreHarness harness, string typeId)
    {
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(typeId, typeId));
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class CoreHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }

        private readonly ServiceProvider _provider;

        public CoreHarness()
        {
            DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"listwft-{Guid.NewGuid()}")
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
