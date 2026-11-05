using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

public class WorkflowTypeTenantAssignmentTests
    : IClassFixture<WorkflowsFixture<WorkflowTypeTenantAssignmentTests>>, IAsyncLifetime
{
    private readonly WorkflowsFixture<WorkflowTypeTenantAssignmentTests> _fixture;
    private readonly ICoreUnitOfWork _unitOfWork;
    private readonly CoreDbContext _dbContext;
    private readonly string _workflowTypeId = $"test-wf-type-{Guid.NewGuid()}";

    public WorkflowTypeTenantAssignmentTests(
        WorkflowsFixture<WorkflowTypeTenantAssignmentTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        IServiceProvider sp = _fixture.BuildTestServiceProvider(testOutputHelper);
        _unitOfWork = sp.GetRequiredService<ICoreUnitOfWork>();
        _dbContext = sp.GetRequiredService<CoreDbContext>();
    }

    public async ValueTask InitializeAsync()
    {
        // Seed a WorkflowType for tests
        _ = _dbContext.Set<WorkflowType>().Add(new WorkflowType(_workflowTypeId, "Test Workflow Type"));
        _ = await _dbContext.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Clean up: hard-remove any access rows and the workflow type
        List<WorkflowTypeOrganizationAccess> allAccess = await _dbContext.Set<WorkflowTypeOrganizationAccess>()
            .IgnoreQueryFilters()
            .Where(a => a.WorkflowTypeId == _workflowTypeId)
            .ToListAsync();
        _dbContext.Set<WorkflowTypeOrganizationAccess>().RemoveRange(allAccess);

        WorkflowType? wfType = await _dbContext.Set<WorkflowType>().FindAsync(_workflowTypeId);
        if (wfType is not null)
        {
            _ = _dbContext.Set<WorkflowType>().Remove(wfType);
        }

        _ = await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task SoftRemove_LeavesGhostRow_ThatBlocksReAdd()
    {
        // Arrange — add a tenant assignment
        string orgId = "org-soft-delete-test";
        var access = new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = _workflowTypeId,
            OrganizationId = orgId,
        };
        await _unitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(access);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        // Act — soft-delete it via Remove()
        _unitOfWork.Repository<WorkflowTypeOrganizationAccess>().Remove(access);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert — the row is hidden from normal queries but still exists in DB
        List<WorkflowTypeOrganizationAccess> visibleRows = await _dbContext.Set<WorkflowTypeOrganizationAccess>()
            .Where(a => a.WorkflowTypeId == _workflowTypeId && a.OrganizationId == orgId)
            .ToListAsync(TestContext.Current.CancellationToken);

        List<WorkflowTypeOrganizationAccess> allRows = await _dbContext.Set<WorkflowTypeOrganizationAccess>()
            .IgnoreQueryFilters()
            .Where(a => a.WorkflowTypeId == _workflowTypeId && a.OrganizationId == orgId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(visibleRows);
        _ = Assert.Single(allRows);
        Assert.True(allRows[0].IsDeleted);
    }

    [Fact]
    public async Task HardRemove_DeletesCompletely_AllowsReAdd()
    {
        // Arrange — add a tenant assignment
        string orgId = "org-hard-delete-test";
        var access = new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = _workflowTypeId,
            OrganizationId = orgId,
        };
        await _unitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(access);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        // Act — hard-delete it via HardRemove()
        _unitOfWork.Repository<WorkflowTypeOrganizationAccess>().HardRemove(access);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert — row is completely gone, even with IgnoreQueryFilters
        List<WorkflowTypeOrganizationAccess> allRows = await _dbContext.Set<WorkflowTypeOrganizationAccess>()
            .IgnoreQueryFilters()
            .Where(a => a.WorkflowTypeId == _workflowTypeId && a.OrganizationId == orgId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(allRows);

        // Act 2 — re-add the same tenant (this is what was failing with soft delete)
        var newAccess = new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = _workflowTypeId,
            OrganizationId = orgId,
        };
        await _unitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(newAccess);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert — the new row exists and is active
        List<WorkflowTypeOrganizationAccess> reAddedRows = await _dbContext.Set<WorkflowTypeOrganizationAccess>()
            .Where(a => a.WorkflowTypeId == _workflowTypeId && a.OrganizationId == orgId)
            .ToListAsync(TestContext.Current.CancellationToken);

        _ = Assert.Single(reAddedRows);
        Assert.False(reAddedRows[0].IsDeleted);
    }
}
