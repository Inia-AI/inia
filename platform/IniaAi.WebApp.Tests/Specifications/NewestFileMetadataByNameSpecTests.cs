using IniaAi.Configuration;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApp.Tests.Helpers;

namespace IniaAi.WebApp.Tests.Specifications;

/// <summary>
/// A download by file name returns the newest file of that name within the organizations the caller
/// may read, run as SQL on a real database: a newer file of the same name in another organization
/// must not win.
/// </summary>
public sealed class NewestFileMetadataByNameSpecTests : IDisposable
{
    private const string OWN_ORG_ID = "org-own";
    private const string OTHER_ORG_ID = "org-other";
    private const string EMPTY_ORG_ID = "org-empty";
    private const string NAME = "statement";

    private readonly UserDataTestDatabase _database = new(new PlatformSettings());

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task WithinTheCallersOrganizations_TheirOwnFileWins()
    {
        await SeedAsync();

        FileMetadata? found = await FindAsync([OWN_ORG_ID]);

        Assert.Equal(OWN_ORG_ID, found?.OrganizationId);
    }

    [Fact]
    public async Task ForEveryOrganization_TheNewestFileWins()
    {
        await SeedAsync();

        FileMetadata? found = await FindAsync(null);

        Assert.Equal(OTHER_ORG_ID, found?.OrganizationId);
    }

    [Fact]
    public async Task AnOrganizationWithoutTheFile_FindsNothing()
    {
        await SeedAsync();

        Assert.Null(await FindAsync([EMPTY_ORG_ID]));
    }

    private async Task<FileMetadata?> FindAsync(IReadOnlyCollection<string>? organizationIds)
    {
        return await _database.Get<IUserDataUnitOfWork>().FileMetadata
            .GetFirstOrDefaultAsync(new NewestFileMetadataByNameSpec(NAME, organizationIds), TestContext.Current.CancellationToken);
    }

    private async Task SeedAsync()
    {
        await _database.SeedOrganizationsAsync(OWN_ORG_ID, OTHER_ORG_ID, EMPTY_ORG_ID);
        await _database.SeedAsync(
            File(OWN_ORG_ID, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            File(OTHER_ORG_ID, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    private static FileMetadata File(string organizationId, DateTime createdAt)
    {
        return new FileMetadata(organizationId, NAME, 10, StorageProvider.Database, $"test/{organizationId}")
        {
            CreatedAt = createdAt,
        };
    }
}
