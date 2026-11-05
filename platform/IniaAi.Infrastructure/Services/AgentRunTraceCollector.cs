using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Collects a settled run's trace pack AND its published <c>out/</c> artifacts — a member's
/// machine-check files are staged for the verifier from these rows, and once the run is
/// acknowledged the runtime's copy is released, so collection is the only moment they can be
/// kept. The cap is decided on manifest sizes before any fetch, dropping the largest files
/// first; artifacts get first claim on the budget because they are member work-product, while
/// trace files are diagnostics.
/// </summary>
public class AgentRunTraceCollector : IAgentRunTraceCollector
{
    public const long MAX_TOTAL_TRACE_BYTES = 32L * 1024 * 1024;

    private readonly IAgentRuntimeClient _agentRuntimeClient;
    private readonly ILogger<AgentRunTraceCollector> _logger;
    private readonly long _maxTotalTraceBytes;

    public AgentRunTraceCollector(
        IAgentRuntimeClient agentRuntimeClient,
        ILogger<AgentRunTraceCollector> logger)
        : this(agentRuntimeClient, logger, MAX_TOTAL_TRACE_BYTES)
    {
    }

    internal AgentRunTraceCollector(
        IAgentRuntimeClient agentRuntimeClient,
        ILogger<AgentRunTraceCollector> logger,
        long maxTotalTraceBytes)
    {
        _agentRuntimeClient = agentRuntimeClient;
        _logger = logger;
        _maxTotalTraceBytes = maxTotalTraceBytes;
    }

    public async Task<AgentRunTraceCollectionResult> CollectAsync(
        string executionId,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        var files = new List<AgentRunTraceFile>();
        var diagnostics = new List<string>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ErrorOr<IReadOnlyList<AgentRunFileEntry>> manifest =
                await _agentRuntimeClient.ListRunFilesAsync(executionId, cancellationToken);

            if (manifest.IsError)
            {
                string diagnostic =
                    $"Listing run files for {executionId} failed: {Describe(manifest.Errors)}";
                diagnostics.Add(diagnostic);
                _logger.LogWarning(
                    "Trace collection for run {ExecutionId} could not list files: {Errors}",
                    executionId,
                    Describe(manifest.Errors));

                return new AgentRunTraceCollectionResult(false, files, diagnostics);
            }

            List<AgentRunFileEntry> artifactEntries =
                [.. manifest.Value.Where(entry => entry.Kind == AgentRunFileKind.Artifact
                    && KindMatchesPath(executionId, entry, diagnostics))];
            List<AgentRunFileEntry> traceEntries =
                [.. manifest.Value.Where(entry => entry.Kind == AgentRunFileKind.Trace
                    && KindMatchesPath(executionId, entry, diagnostics))];
            List<AgentRunFileEntry> withinCap = ApplyCap(executionId, artifactEntries, traceEntries, diagnostics);

            bool succeeded = true;
            foreach (AgentRunFileEntry entry in withinCap)
            {
                ErrorOr<byte[]> content =
                    await _agentRuntimeClient.GetRunFileAsync(executionId, entry.Path, cancellationToken);

                if (content.IsError)
                {
                    succeeded = false;
                    diagnostics.Add(
                        $"Fetching file {entry.Path} failed: {Describe(content.Errors)}");
                    _logger.LogWarning(
                        "Trace collection for run {ExecutionId} could not fetch {Path}: {Errors}",
                        executionId,
                        entry.Path,
                        Describe(content.Errors));
                    continue;
                }

                string computedSha256 = Convert.ToHexStringLower(SHA256.HashData(content.Value));
                if (!computedSha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    succeeded = false;
                    diagnostics.Add(
                        $"File {entry.Path} failed sha256 verification against the manifest");
                    _logger.LogWarning(
                        "Trace collection for run {ExecutionId} dropped {Path}: sha256 mismatch",
                        executionId,
                        entry.Path);
                    continue;
                }

                files.Add(new AgentRunTraceFile
                {
                    RunExecutionId = executionId,
                    Attempt = attempt,
                    Path = entry.Path,
                    Sha256 = computedSha256,
                    SizeBytes = content.Value.LongLength,
                    Content = content.Value,
                });
            }

            // Every published out/ row the pack does not hold, whatever kept it out — the cap,
            // a failed fetch, a digest mismatch, or a lying manifest kind. The count is the
            // resume-safety signal: custody records it, and a continuation would wake the
            // worker in a workspace missing any file counted here.
            HashSet<string> keptPaths = [.. files.Select(static file => file.Path)];

            return new AgentRunTraceCollectionResult(succeeded, files, diagnostics)
            {
                DroppedArtifactPaths = [.. manifest.Value
                    .Select(static entry => entry.Path)
                    .Where(path => path.StartsWith(AgentRunFilePaths.ARTIFACT_PREFIX, StringComparison.Ordinal)
                        && !keptPaths.Contains(path))
                    .Distinct(StringComparer.Ordinal)],
            };
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Trace collection for {executionId} failed unexpectedly: {exception.Message}");
            _logger.LogWarning(
                exception,
                "Trace collection for run {ExecutionId} failed unexpectedly",
                executionId);

            return new AgentRunTraceCollectionResult(false, files, diagnostics);
        }
    }

    /// <summary>
    /// The persisted rows carry no kind column — the <c>out/</c> prefix is the kind — so a
    /// manifest row whose declared kind contradicts its path is a lie that would either stage
    /// a reasoning record to the verifier or silently hide a deliverable. Refuse it here, at
    /// the one moment both the kind and the path are in hand.
    /// </summary>
    private bool KindMatchesPath(string executionId, AgentRunFileEntry entry, List<string> diagnostics)
    {
        bool artifactPath = entry.Path.StartsWith(AgentRunFilePaths.ARTIFACT_PREFIX, StringComparison.Ordinal);
        if (entry.Kind == AgentRunFileKind.Artifact == artifactPath)
        {
            return true;
        }

        diagnostics.Add(
            $"Skipped {entry.Path}: its manifest kind '{entry.Kind}' contradicts its path prefix");
        _logger.LogWarning(
            "Collection for run {ExecutionId} skipped {Path}: manifest kind {Kind} contradicts the path prefix",
            executionId,
            entry.Path,
            entry.Kind);

        return false;
    }

    private List<AgentRunFileEntry> ApplyCap(
        string executionId,
        List<AgentRunFileEntry> artifactEntries,
        List<AgentRunFileEntry> traceEntries,
        List<string> diagnostics)
    {
        List<AgentRunFileEntry> prioritized =
        [
            .. artifactEntries.OrderBy(static entry => entry.SizeBytes),
            .. traceEntries.OrderBy(static entry => entry.SizeBytes),
        ];

        if (prioritized.Sum(static entry => entry.SizeBytes) <= _maxTotalTraceBytes)
        {
            return prioritized;
        }

        var kept = new List<AgentRunFileEntry>();
        long keptBytes = 0;
        foreach (AgentRunFileEntry entry in prioritized)
        {
            if (keptBytes + entry.SizeBytes > _maxTotalTraceBytes)
            {
                diagnostics.Add(
                    $"Skipped {entry.Path} ({entry.SizeBytes} bytes): pack exceeds the {_maxTotalTraceBytes}-byte cap");
                _logger.LogWarning(
                    "Collection for run {ExecutionId} skipped {Path} ({SizeBytes} bytes) over the {Cap}-byte pack cap",
                    executionId,
                    entry.Path,
                    entry.SizeBytes,
                    _maxTotalTraceBytes);
                continue;
            }

            keptBytes += entry.SizeBytes;
            kept.Add(entry);
        }

        return kept;
    }

    private static string Describe(List<Error> errors)
    {
        return string.Join("; ", errors.Select(error => $"{error.Code}: {error.Description}"));
    }
}
