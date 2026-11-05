using System.Diagnostics.CodeAnalysis;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The hashes stored Library versions have when today's model writes them, found by the stored hash
/// that names each version. A stored content hash and a link stamp are identity tokens: a version
/// stored before the model dropped a field keeps that field in its payload and in its hash, and a
/// live copy of the same content has neither, so a live copy compares against these hashes. A
/// version this deployment holds no payload for keeps its stored hash. The factory names the kind
/// of hash: a copy's content hash compares with the content hashes, its signature-insensitive hash
/// with the signature-insensitive ones.
/// </summary>
public sealed class StoredVersionHashes
{
    public static StoredVersionHashes None { get; } = new([]);

    private readonly Dictionary<(string ComponentId, string ContentHash), string> _hashesByVersion;

    private StoredVersionHashes(Dictionary<(string ComponentId, string ContentHash), string> hashesByVersion)
    {
        _hashesByVersion = hashesByVersion;
    }

    [return: NotNullIfNotNull(nameof(storedHash))]
    public string? Of(string? componentId, string? storedHash)
    {
        return componentId is null || storedHash is null
            ? storedHash
            : _hashesByVersion.GetValueOrDefault((componentId, storedHash), storedHash);
    }

    public static Task<StoredVersionHashes> LoadContentHashesAsync(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponentContentNormalizer normalizer,
        IEnumerable<(string ComponentId, string ContentHash)> versions,
        CancellationToken cancellationToken)
    {
        return LoadAsync(coreUnitOfWork, versions, normalizer.ComputeStoredContentHash, cancellationToken);
    }

    public static Task<StoredVersionHashes> LoadSignatureInsensitiveHashesAsync(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponentContentNormalizer normalizer,
        IEnumerable<(string ComponentId, string ContentHash)> versions,
        CancellationToken cancellationToken)
    {
        return LoadAsync(
            coreUnitOfWork, versions, normalizer.ComputeStoredSignatureInsensitiveContentHash, cancellationToken);
    }

    private static async Task<StoredVersionHashes> LoadAsync(
        ICoreUnitOfWork coreUnitOfWork,
        IEnumerable<(string ComponentId, string ContentHash)> versions,
        Func<string, string> hashOfStoredDefinition,
        CancellationToken cancellationToken)
    {
        HashSet<(string ComponentId, string ContentHash)> wanted = [.. versions
            .Where(v => !string.IsNullOrWhiteSpace(v.ContentHash))];
        if (wanted.Count == 0)
        {
            return None;
        }

        List<string> componentIds = [.. wanted.Select(w => w.ComponentId).Distinct(StringComparer.Ordinal)];
        List<string> contentHashes = [.. wanted.Select(w => w.ContentHash).Distinct(StringComparer.Ordinal)];
        List<SharedComponentVersion> payloadRows = await coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => componentIds.Contains(v.SharedComponentId)
                && contentHashes.Contains(v.ContentHash)
                && v.PayloadAbsentAt == null
                && v.ContentJsonCompressed != null)
            .ToListAsync(cancellationToken);

        Dictionary<(string ComponentId, string ContentHash), string> hashesByVersion = [];
        foreach (SharedComponentVersion row in payloadRows)
        {
            (string, string) key = (row.SharedComponentId, row.ContentHash);
            if (wanted.Contains(key) && !hashesByVersion.ContainsKey(key))
            {
                hashesByVersion[key] = hashOfStoredDefinition(row.ContentJson);
            }
        }

        return new StoredVersionHashes(hashesByVersion);
    }
}
