using IniaAi.Domain.Entities.Storage;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Storage;

/// <summary>
/// The sweep removes every stored file that no row of <see cref="StoredObjectSweep.ReferencingRows"/>
/// points at. A new table that holds a storage key fails here until the sweep reads it too: without
/// that, the sweep would delete its files a day after they were stored.
/// </summary>
public sealed class StoredObjectSweepSchemaTests
{
    private static readonly string[] _storageKeyColumns = ["ExternalStoragePath", "StorageKey"];

    // A pending delete names a file to remove, not one to keep.
    private static readonly Type[] _filesToRemove = [typeof(StoredObjectDeletion)];

    [Fact]
    public void EveryTableThatHoldsAStorageKey_IsOneTheSweepReads()
    {
        using var userData = new UserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>().UseSqlite("DataSource=:memory:").Options);
        using var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseSqlite("DataSource=:memory:").Options);

        List<string> unread = [.. new[] { userData.Model, core.Model }
            .SelectMany(model => model.GetEntityTypes())
            .Where(entity => entity.GetProperties().Any(property => _storageKeyColumns.Contains(property.Name)))
            .Select(entity => entity.ClrType)
            .Distinct()
            .Where(type => !StoredObjectSweep.ReferencingRows.Contains(type) && !_filesToRemove.Contains(type))
            .Select(type => type.FullName!)
            .Order()];

        Assert.True(unread.Count == 0, "These tables hold a storage key that the sweep does not read: " + string.Join(", ", unread));
    }
}
