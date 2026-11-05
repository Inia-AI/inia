using ErrorOr;
using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.Workflows.Versions;

public class GetWorkflowHeadVersionQueryTests : IDisposable
{
    private const string ORG = "org-test";
    private const string OTHER_ORG = "org-other";
    private const string TYPE = "WT";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _coreContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public GetWorkflowHeadVersionQueryTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _coreContext = _serviceProvider.GetRequiredService<CoreDbContext>();
        _coreUnitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();

        _coreContext.GrantWorkflowTypeAccess(TYPE, ORG);
        _ = _coreContext.SaveChanges();
    }

    public void Dispose()
    {
        _coreContext.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Head_ReturnsLatestVersionId()
    {
        _ = SeedVersion(DateTime.UtcNow.AddMinutes(-5), new string('1', 64));
        WorkflowVersion newest = SeedVersion(DateTime.UtcNow, new string('2', 64));
        await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<WorkflowHeadVersionResult> result = await Handler().Handle(
            new GetWorkflowHeadVersionQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(newest.Id, result.Value.VersionId);
    }

    [Fact]
    public async Task Head_NoVersions_ReturnsNullVersionId()
    {
        ErrorOr<WorkflowHeadVersionResult> result = await Handler().Handle(
            new GetWorkflowHeadVersionQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.VersionId);
    }

    [Fact]
    public async Task Head_OrgWithoutAccess_StillReturnsHead()
    {
        WorkflowVersion head = SeedVersion(DateTime.UtcNow, new string('1', 64));
        await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<WorkflowHeadVersionResult> result = await Handler().Handle(
            new GetWorkflowHeadVersionQuery(OTHER_ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(head.Id, result.Value.VersionId);
    }

    private GetWorkflowHeadVersionQueryHandler Handler()
    {
        return new GetWorkflowHeadVersionQueryHandler(_coreUnitOfWork);
    }

    private WorkflowVersion SeedVersion(DateTime createdAt, string contentHash)
    {
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = contentHash,
            WorkflowJson = "{}",
            CreatedByUserId = "author-1",
            CreatedAt = createdAt,
        };
        _ = _coreContext.WorkflowVersions.Add(version);
        return version;
    }
}
