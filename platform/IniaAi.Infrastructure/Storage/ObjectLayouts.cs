using IniaAi.Configuration.Storage;

namespace IniaAi.Infrastructure.Storage;

/// <summary>Where one stored file lives in an object store.</summary>
public sealed record ObjectLocation(string Bucket, string Key);

/// <summary>
/// Maps a stored file to its place in an object store. A file's key is unique on its own, so a
/// layout that leaves the category out of the location can never misplace a file whose caller names
/// the wrong category.
/// </summary>
public interface IObjectLayout
{
    /// <summary>The single bucket every file lives in, or null for a bucket per organization and category, made on the first upload.</summary>
    string? Bucket { get; }

    /// <summary>Where a new file is written.</summary>
    ObjectLocation Locate(string organizationId, string category, string key);

    /// <summary>Every place the file may be, in the order a read tries them; a delete clears them all.</summary>
    IReadOnlyList<ObjectLocation> Candidates(string organizationId, string category, string key)
    {
        return [Locate(organizationId, category, key)];
    }

    /// <summary>The file key an object of <see cref="Bucket"/> was stored under.</summary>
    string KeyOf(string objectKey)
    {
        return objectKey;
    }
}

/// <summary>
/// The layout of a stack's own bucket: one bucket, created by the stack, with every file under its
/// organization's prefix (<c>{organizationId}/{key}</c>).
/// </summary>
public sealed class SharedBucketLayout(string bucket) : IObjectLayout
{
    private const char PREFIX_SEPARATOR = '/';

    /// <inheritdoc />
    public string? Bucket => bucket;

    /// <inheritdoc />
    public ObjectLocation Locate(string organizationId, string category, string key)
    {
        if (organizationId.Contains(PREFIX_SEPARATOR))
        {
            throw new ArgumentException($"An organization id cannot hold '{PREFIX_SEPARATOR}': it separates the organization's prefix.", nameof(organizationId));
        }

        return new ObjectLocation(bucket, $"{organizationId}{PREFIX_SEPARATOR}{key}");
    }

    /// <inheritdoc />
    public string KeyOf(string objectKey)
    {
        int separator = objectKey.IndexOf(PREFIX_SEPARATOR);
        return separator <= 0 ? objectKey : objectKey[(separator + 1)..];
    }
}

/// <summary>
/// The layout of a MinIO store set up with one fixed bucket (<c>FileStorage:MinIO:BucketName</c>):
/// every file at its bare key. A single-tenant deployment uses it, so no organization prefix exists.
/// </summary>
public sealed class FixedBucketLayout(string bucket) : IObjectLayout
{
    /// <inheritdoc />
    public string? Bucket => bucket;

    /// <inheritdoc />
    public ObjectLocation Locate(string organizationId, string category, string key)
    {
        return new ObjectLocation(bucket, key);
    }
}

/// <summary>
/// The layout of a MinIO store without a fixed bucket: a bucket per organization and category
/// (<c>{organizationId}-{category}</c>, lower-cased, <c>_</c> and <c>.</c> as <c>-</c>), created on
/// the first upload. It stays so the files already stored that way remain readable. A caller can
/// name the wrong category, so a read tries every category's bucket, the named one first, and a
/// delete clears them all.
/// </summary>
public sealed class BucketPerOrganizationLayout(IReadOnlyDictionary<string, string> categoryBuckets) : IObjectLayout
{
    private static readonly string[] KnownCategories =
    [
        StorageCategories.Documents,
        StorageCategories.Json2XlsxTemplates,
        StorageCategories.AgentSessionBundles,
    ];

    /// <inheritdoc />
    public string? Bucket => null;

    /// <inheritdoc />
    public ObjectLocation Locate(string organizationId, string category, string key)
    {
        string suffix = categoryBuckets.TryGetValue(category, out string? mapped) ? mapped : category;
        return new ObjectLocation($"{Normalize(organizationId)}-{Normalize(suffix)}", key);
    }

    /// <inheritdoc />
    public IReadOnlyList<ObjectLocation> Candidates(string organizationId, string category, string key)
    {
        return [.. KnownCategories.Prepend(category).Select(each => Locate(organizationId, each, key)).Distinct()];
    }

    private static string Normalize(string input)
    {
        return input.ToLower().Replace("_", "-").Replace(".", "-");
    }
}
