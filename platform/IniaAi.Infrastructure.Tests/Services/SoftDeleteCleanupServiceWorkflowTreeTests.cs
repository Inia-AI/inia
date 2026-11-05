using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins that the deleted-workflow query in <see cref="SoftDeleteCleanupService"/>
/// is scoped to root Workflow rows of one organization and never returns Section
/// rows, which cascade via their own FK rather than carrying an OrganizationId.
/// </summary>
public class SoftDeleteCleanupServiceWorkflowTreeTests : IDisposable
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const int RETENTION_DAYS = 7;

    private readonly DbContextOptions<UserDataDbContext> _options;

    public SoftDeleteCleanupServiceWorkflowTreeTests()
    {
        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"SoftDeleteCleanupServiceWorkflowTreeTests-{Guid.NewGuid()}")
            .Options;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DeletedTreeQuery_ExcludesOtherOrganizationsRootsAndSections()
    {
        DateTime pastCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1));
        DateTime cutoff = DateTime.UtcNow.AddDays(-RETENTION_DAYS);

        var orgARoot = new Workflow
        {
            Id = Guid.NewGuid().ToString(),
            Name = "org-a-root",
            OrganizationId = ORG_A,
            IsDeleted = true,
            DeletedAt = pastCutoff,
        };

        var orgBRoot = new Workflow
        {
            Id = Guid.NewGuid().ToString(),
            Name = "org-b-root",
            OrganizationId = ORG_B,
            IsDeleted = true,
            DeletedAt = pastCutoff,
        };

        var orgBSection = new Section("org-b-section", orgBRoot)
        {
            Id = Guid.NewGuid().ToString(),
            IsDeleted = false,
        };

        await using (var seed = new UserDataDbContext(_options))
        {
            await seed.Workflows.AddRangeAsync(orgARoot, orgBRoot);
            _ = await seed.Sections.AddAsync(orgBSection, TestContext.Current.CancellationToken);
            _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = new UserDataDbContext(_options);
        List<string> result = await SoftDeleteCleanupService
            .BuildDeletedWorkflowIdsQuery(ctx.Workflows, ORG_A, cutoff)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(orgBRoot.Id, result);
        Assert.DoesNotContain(orgBSection.Id, result);
        Assert.Contains(orgARoot.Id, result);
    }

    [Fact]
    public async Task DeletedTreeQuery_ExcludesRootDeletedBeforeCutoffBoundary()
    {
        DateTime preCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS - 1));
        DateTime cutoff = DateTime.UtcNow.AddDays(-RETENTION_DAYS);

        var freshlyDeletedRoot = new Workflow
        {
            Id = Guid.NewGuid().ToString(),
            Name = "fresh",
            OrganizationId = ORG_A,
            IsDeleted = true,
            DeletedAt = preCutoff,
        };

        await using (var seed = new UserDataDbContext(_options))
        {
            _ = await seed.Workflows.AddAsync(freshlyDeletedRoot, TestContext.Current.CancellationToken);
            _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = new UserDataDbContext(_options);
        List<string> result = await SoftDeleteCleanupService
            .BuildDeletedWorkflowIdsQuery(ctx.Workflows, ORG_A, cutoff)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(freshlyDeletedRoot.Id, result);
    }

}
