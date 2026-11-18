using IniaAi.Domain.Entities.Tools;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// A conversion type name is unique among an organization's live conversion types. A deleted one
/// keeps its name and blocks nothing, so a name can be deleted, created again and deleted again.
/// </summary>
public class ConversionTypeNameUniquenessTests : IDisposable
{
    private const string ORG_ID = "test-org-conversion-types";
    private const string OTHER_ORG_ID = "test-org-conversion-types-other";
    private const string USER_ID = "test-user-conversion-types";
    private const string NAME = "BrokerDealerTemplate";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<UserDataDbContext> _options;

    public ConversionTypeNameUniquenessTests()
    {
        _connection = SqliteUserDataSchema.OpenInMemoryDatabase();
        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ANameDeletedBefore_IsDeletedAgain()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        string first = await CreateAsync(ORG_ID, NAME, cancellationToken);
        await DeleteAsync(first, cancellationToken);
        string second = await CreateAsync(ORG_ID, NAME, cancellationToken);
        await DeleteAsync(second, cancellationToken);

        await using var db = new SqliteUserDataDbContext(_options);
        List<ConversionType> stored = await db.ConversionTypes.IgnoreQueryFilters()
            .Where(t => t.OrganizationId == ORG_ID)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, t =>
        {
            Assert.True(t.IsDeleted);
            Assert.Equal(NAME, t.Name);
        });
    }

    [Fact]
    public async Task ALiveName_IsRefusedASecondTime_InTheSameOrganization()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        _ = await CreateAsync(ORG_ID, NAME, cancellationToken);

        _ = await Assert.ThrowsAsync<DbUpdateException>(() => CreateAsync(ORG_ID, NAME, cancellationToken));
    }

    [Fact]
    public async Task ALiveName_IsFreeToAnotherOrganization()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        _ = await CreateAsync(ORG_ID, NAME, cancellationToken);
        _ = await CreateAsync(OTHER_ORG_ID, NAME, cancellationToken);

        await using var db = new SqliteUserDataDbContext(_options);
        Assert.Equal(2, await db.ConversionTypes.CountAsync(t => t.Name == NAME, cancellationToken));
    }

    private async Task<string> CreateAsync(string organizationId, string name, CancellationToken cancellationToken)
    {
        await using var db = new SqliteUserDataDbContext(_options);
        var conversionType = new ConversionType { Name = name, DisplayName = name, OrganizationId = organizationId };
        _ = db.ConversionTypes.Add(conversionType);
        _ = await db.SaveChangesAsync(cancellationToken);
        return conversionType.Id;
    }

    private async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = new SqliteUserDataDbContext(_options);
        ConversionType conversionType = await db.ConversionTypes.SingleAsync(t => t.Id == id, cancellationToken);
        conversionType.IsDeleted = true;
        conversionType.DeletedAt = DateTime.UtcNow;
        conversionType.DeletedByUserId = USER_ID;
        _ = await db.SaveChangesAsync(cancellationToken);
    }
}
