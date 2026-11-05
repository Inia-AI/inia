namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The runtime's continuation admission rules (agent-runtime <c>config.ts</c> /
/// <c>run-documents.ts</c> / <c>safe-path.ts</c>), mirrored so the client's pre-HTTP
/// validation and the loop's dispatch planning read one authority. Discovery and pre-flight
/// only — the runtime's own 400 stays the enforcement, and the wire fixtures in the runtime
/// repo pin these numbers, which is the stated re-check when either side moves.
/// </summary>
public static class AgentRunContinuationCapacity
{
    /// <summary>
    /// Entry ceiling for the re-carried <c>out/</c> set. Deliberately between the runtime's
    /// documents cap (out/ paths are not documents) and its publish cap; a member whose
    /// published set exceeds it cannot be re-carried, and the dispatcher degrades that leg to
    /// scratch — a recorded override, never a failure.
    /// </summary>
    public const int MAX_RECARRIED_OUT_FILES = 1_000;

    /// <summary>Per-path length bound; the runtime 400s anything longer at its parse boundary.</summary>
    public const int MAX_RECARRIED_OUT_PATH_CHARS = 512;

    /// <summary>
    /// The runtime's cap on the session bundle it writes and accepts back
    /// (<c>MAX_CHECKPOINT_BUNDLE_BYTES</c>). Custody refuses anything larger before storing,
    /// so an over-cap bundle in a dispatch is a defensive check, not an expected path. The
    /// bundle does not charge the shared input byte budget — re-carried file bytes do.
    /// </summary>
    public const long MAX_SESSION_BUNDLE_BYTES = 64L * 1024 * 1024;

    /// <summary>
    /// The runtime's re-carried path gate: bounded length, and every <c>/</c>-separated
    /// segment non-empty, not <c>.</c> or <c>..</c>, and free of backslashes and NUL — a
    /// dot segment normalizes into the workspace path before staging sees it, and a
    /// backslash smuggles a separator past the split.
    /// </summary>
    public static bool IsRecarriedOutPath(string path)
    {
        if (path.Length == 0
            || path.Length > MAX_RECARRIED_OUT_PATH_CHARS
            || path.Contains('\\')
            || path.Contains('\0'))
        {
            return false;
        }

        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The whole-set rules the runtime enforces over <c>continuation.outFiles</c>, mirrored in
    /// one place so the client's refusal and the dispatcher's degrade agree: the entry cap,
    /// the per-path gate, uniqueness, and prefix nesting — a path that is a directory prefix
    /// of another (<c>a</c> beside <c>a/b</c>) cannot stage, because the first lands as a
    /// regular file exactly where the second needs a directory. Returns the first problem, or
    /// null for a set the runtime will admit.
    /// </summary>
    public static string? DescribeFirstRecarryProblem(IReadOnlyList<AgentRunRecarriedFile> outFiles)
    {
        return DescribeFirstRecarryProblem([.. outFiles.Select(static file => file.Path)]);
    }

    public static string? DescribeFirstRecarryProblem(IReadOnlyList<string> paths)
    {
        if (paths.Count > MAX_RECARRIED_OUT_FILES)
        {
            return $"{paths.Count} re-carried out/ files exceed the runtime's "
                + $"{MAX_RECARRIED_OUT_FILES}-entry continuation ceiling";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var directoryPrefixes = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in paths)
        {
            if (!IsRecarriedOutPath(path))
            {
                return $"Re-carried path '{path}' must be a non-empty out/-relative path of at most "
                    + $"{MAX_RECARRIED_OUT_PATH_CHARS} characters whose segments are non-empty, "
                    + "not '.' or '..', and free of backslashes and NUL";
            }

            if (!seen.Add(path))
            {
                return $"Re-carried path '{path}' is used more than once";
            }

            int separatorIndex = -1;
            while ((separatorIndex = path.IndexOf('/', separatorIndex + 1)) >= 0)
            {
                _ = directoryPrefixes.Add(path[..separatorIndex]);
            }
        }

        foreach (string path in paths)
        {
            if (directoryPrefixes.Contains(path))
            {
                return $"Re-carried path '{path}' is both a file and a directory prefix of another path";
            }
        }

        return null;
    }
}
