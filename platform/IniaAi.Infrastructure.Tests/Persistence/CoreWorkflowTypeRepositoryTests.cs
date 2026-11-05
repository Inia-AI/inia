using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// A retired workflow type is gone from every list and lookup the repository serves, while its id
/// stays taken until the weekly cleanup removes the row.
/// </summary>
public sealed class CoreWorkflowTypeRepositoryTests : IDisposable
{
    private const string LIVE = "tmw_dev_detect";
    private const string RETIRED = "tmw_test_detect";

    private readonly SqliteCoreDatabase _database = new();
    private readonly CoreWorkflowTypeRepository _repository;

    public CoreWorkflowTypeRepositoryTests()
    {
        SqliteCoreDbContext context = _database.Context;
        context.WorkflowTypes.AddRange(
            new WorkflowType(LIVE, LIVE),
            new WorkflowType(RETIRED, RETIRED) { RetiredAt = DateTime.UtcNow });
        _ = context.SaveChanges();
        context.ChangeTracker.Clear();
        _repository = new CoreWorkflowTypeRepository(context);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task ListsAndLookups_SkipARetiredType()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Equal([LIVE], await _repository.GetAll().Select(type => type.Id).ToListAsync(ct));
        Assert.Equal([LIVE], (await _repository.GetAllAsync()).Select(type => type.Id));
        Assert.Equal([LIVE], (await _repository.GetAllWithDocumentClassesAsync()).Select(type => type.Id));
        Assert.Equal([LIVE], (await _repository.FindAsync(_ => true)).Select(type => type.Id));
        Assert.Null(await _repository.GetByIdAsync(RETIRED));
        Assert.NotNull(await _repository.GetByIdAsync(LIVE));
    }

    [Fact]
    public async Task Exists_StillSeesARetiredType_SoItsIdStaysTaken()
    {
        Assert.True(await _repository.ExistsAsync(RETIRED));
    }
}
