namespace IniaAi.Application.Queries.Workflows.Versions;

/// <summary>
/// Derives the short, user-facing "version code" / "ID code" shown in the editor from a
/// version's content hash. Purely cosmetic — the full <c>ContentHash</c> stays the identity.
/// </summary>
public static class WorkflowVersionCode
{
    public const int LENGTH = 8;

    public static string Short(string contentHash)
    {
        return contentHash.Length <= LENGTH
            ? contentHash
            : contentHash[..LENGTH];
    }
}
