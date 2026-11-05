using System.Security.Cryptography;
using System.Text.Json;
using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Turns a bundle custody holds into a continuation, for every consumer that resumes a session.
/// Each step answers with the reason the bundle cannot be used, never an exception: a consumer
/// that cannot resume starts the run fresh.
/// </summary>
public static class AgentRunContinuationBuilder
{
    private const string STAMP_MODEL_FIELD = "model";

    /// <summary>
    /// The checks on the held bundle row, before any bytes are read: its version stamp names the
    /// model the run expects, it fits the runtime's cap, and custody kept every out/ file the run
    /// published. A blank <paramref name="expectedModel"/> accepts any stamped model. Null when the
    /// bundle passes.
    /// </summary>
    public static string? DescribeBundleProblem(AgentRunSessionBundle held, string? expectedModel)
    {
        if (held.VersionStamp is null)
        {
            return "the held bundle carries no version stamp";
        }

        if (StampModelMismatch(held.VersionStamp, expectedModel) is string mismatch)
        {
            return mismatch;
        }

        if (held.SizeBytes > AgentRunContinuationCapacity.MAX_SESSION_BUNDLE_BYTES)
        {
            return $"the held bundle is {held.SizeBytes} bytes, past the runtime's "
                + $"{AgentRunContinuationCapacity.MAX_SESSION_BUNDLE_BYTES}-byte continuation cap";
        }

        if (held.DroppedOutFileCount > 0)
        {
            return $"the platform's copy of the leg's out/ is short {held.DroppedOutFileCount} file(s) "
                + "the member published, so resuming would wake the worker in a workspace missing its own files";
        }

        return null;
    }

    /// <summary>The out/ files one attempt of one run published, out/-relative.</summary>
    public static List<AgentRunRecarriedFile> OutFilesOf(IEnumerable<AgentRunTraceFile> rows, string? runExecutionId, int attempt)
    {
        return [.. rows
            .Where(row => string.Equals(row.RunExecutionId, runExecutionId, StringComparison.Ordinal)
                && row.Attempt == attempt
                && row.Path.StartsWith(AgentRunFilePaths.ARTIFACT_PREFIX, StringComparison.Ordinal))
            .Select(static row => new AgentRunRecarriedFile(
                row.Path[AgentRunFilePaths.ARTIFACT_PREFIX.Length..],
                row.Content,
                row.Sha256))];
    }

    /// <summary>
    /// The continuation from a held bundle and its run's out/ files. The re-carried files are
    /// re-proven against their digests here, as custody re-proves the bundle on read, so a drifted
    /// blob degrades to a fresh start instead of a runtime admission refusal.
    /// </summary>
    public static async Task<(AgentRunContinuation? Continuation, string? Problem)> BuildAsync(
        AgentRunSessionBundle held,
        IReadOnlyList<AgentRunRecarriedFile> outFiles,
        IAgentRunSessionBundleCustodian custodian,
        CancellationToken cancellationToken)
    {
        if (AgentRunContinuationCapacity.DescribeFirstRecarryProblem(outFiles) is string recarryProblem)
        {
            return (null, recarryProblem);
        }

        foreach (AgentRunRecarriedFile outFile in outFiles)
        {
            string computedSha256 = Convert.ToHexStringLower(SHA256.HashData(outFile.Content.Span));
            if (!computedSha256.Equals(outFile.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"the platform's copy of the leg's out/{outFile.Path} no longer matches its recorded digest");
            }
        }

        byte[]? bundleBytes = await custodian.ReadBytesAsync(held, cancellationToken);
        if (bundleBytes is null)
        {
            return (null, "the held bundle's bytes could not be read back verbatim from custody");
        }

        return (new AgentRunContinuation(bundleBytes, held.Sha256, outFiles), null);
    }

    /// <summary>
    /// Only the stamp's model is compared: the platform holds no live registry of the deployed
    /// sidecar or its SDK pin, so format and SDK acceptability stay the runtime's parse authority.
    /// A stamp nobody can read certifies nothing.
    /// </summary>
    private static string? StampModelMismatch(string versionStamp, string? expectedModel)
    {
        string? stampModel;
        try
        {
            using JsonDocument stamp = JsonDocument.Parse(versionStamp);
            stampModel = stamp.RootElement.TryGetProperty(STAMP_MODEL_FIELD, out JsonElement model)
                && model.ValueKind == JsonValueKind.String
                    ? model.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return "the held bundle's version stamp is not readable JSON";
        }

        if (string.IsNullOrWhiteSpace(stampModel))
        {
            return "the held bundle's version stamp names no model";
        }

        if (!string.IsNullOrWhiteSpace(expectedModel)
            && !string.Equals(expectedModel, stampModel, StringComparison.Ordinal))
        {
            return $"the held bundle's session ran on model '{stampModel}' "
                + $"but this leg is configured for '{expectedModel}'";
        }

        return null;
    }
}
