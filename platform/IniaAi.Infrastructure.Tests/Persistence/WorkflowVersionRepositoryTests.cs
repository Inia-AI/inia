using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Tests.Persistence;

public class WorkflowVersionRepositoryTests : IDisposable
{
    private const string TYPE = "WT-Test";
    private const string OTHER_TYPE = "WT-Other";
    private const string USER = "user-test";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly ICoreUnitOfWork _unitOfWork;

    public WorkflowVersionRepositoryTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Repository_AddAndGetById_RoundTrips()
    {
        WorkflowVersion version = NewVersion("h1", DateTime.UtcNow);
        await _unitOfWork.Repository<WorkflowVersion>().AddAsync(version);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        WorkflowVersion? loaded = await _unitOfWork.Repository<WorkflowVersion>()
            .GetByIdAsync(version.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("h1", loaded.ContentHash);
        Assert.Equal(USER, loaded.CreatedByUserId);
    }

    [Fact]
    public async Task LatestByType_ReturnsMostRecentByCreatedAt()
    {
        Seed("old", DateTime.UtcNow.AddMinutes(-10));
        Seed("new", DateTime.UtcNow.AddMinutes(-1));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        WorkflowVersion? latest = await _unitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new LatestWorkflowVersionByTypeSpec(TYPE),
                TestContext.Current.CancellationToken);

        Assert.NotNull(latest);
        Assert.Equal("new", latest.ContentHash);
    }

    [Fact]
    public async Task LatestByType_TiesOnCreatedAt_PicksDeterministicHeadByContentHash()
    {
        DateTime tie = DateTime.UtcNow;
        Seed(new string('a', 64), tie);
        Seed(new string('b', 64), tie);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        WorkflowVersion? latest = await _unitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new LatestWorkflowVersionByTypeSpec(TYPE),
                TestContext.Current.CancellationToken);

        Assert.NotNull(latest);
        Assert.Equal(new string('b', 64), latest.ContentHash);
    }

    [Fact]
    public async Task ByType_OrdersNewestFirst()
    {
        Seed("a", DateTime.UtcNow.AddMinutes(-10));
        Seed("b", DateTime.UtcNow.AddMinutes(-5));
        Seed("c", DateTime.UtcNow.AddMinutes(-1));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IList<WorkflowVersion> all = await _unitOfWork.Repository<WorkflowVersion>()
            .GetAllAsync(new WorkflowVersionsByTypeSpec(TYPE), TestContext.Current.CancellationToken);

        Assert.Equal(["c", "b", "a"], [.. all.Select(v => v.ContentHash)]);
    }

    [Fact]
    public async Task ByType_TiesOnCreatedAt_OrdersByContentHashDescending()
    {
        DateTime tie = DateTime.UtcNow;
        Seed(new string('a', 64), tie);
        Seed(new string('b', 64), tie);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IList<WorkflowVersion> all = await _unitOfWork.Repository<WorkflowVersion>()
            .GetAllAsync(new WorkflowVersionsByTypeSpec(TYPE), TestContext.Current.CancellationToken);

        Assert.Equal([new string('b', 64), new string('a', 64)], [.. all.Select(v => v.ContentHash)]);
    }

    [Fact]
    public async Task ByType_OmitsOtherTypes()
    {
        Seed("ours", DateTime.UtcNow, type: TYPE);
        Seed("theirs", DateTime.UtcNow, type: OTHER_TYPE);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IList<WorkflowVersion> mine = await _unitOfWork.Repository<WorkflowVersion>()
            .GetAllAsync(new WorkflowVersionsByTypeSpec(TYPE), TestContext.Current.CancellationToken);

        Assert.Single(mine);
        Assert.Equal("ours", mine[0].ContentHash);
    }

    [Fact]
    public async Task ParentPointer_RoundTrips()
    {
        WorkflowVersion parent = Seed("parent", DateTime.UtcNow.AddMinutes(-5));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        WorkflowVersion child = Seed("child", DateTime.UtcNow, parentId: parent.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        WorkflowVersion? loaded = await _unitOfWork.Repository<WorkflowVersion>()
            .GetByIdAsync(child.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(parent.Id, loaded.ParentVersionId);
    }

    private WorkflowVersion Seed(string hash, DateTime createdAt, string? type = null, string? parentId = null)
    {
        WorkflowVersion version = NewVersion(hash, createdAt, type, parentId);
        _ = _context.WorkflowVersions.Add(version);

        return version;
    }

    private static WorkflowVersion NewVersion(
        string hash,
        DateTime createdAt,
        string? type = null,
        string? parentId = null)
    {
        return new WorkflowVersion
        {
            WorkflowTypeId = type ?? TYPE,
            ContentHash = hash,
            WorkflowJson = "{}",
            CreatedByUserId = USER,
            ParentVersionId = parentId,
            CreatedAt = createdAt,
        };
    }
}
