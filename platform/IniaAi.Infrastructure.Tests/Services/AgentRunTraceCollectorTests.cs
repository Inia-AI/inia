using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunTraceCollectorTests
{
    private const string EXECUTION_ID = "exec-1";
    private const int ATTEMPT = 2;

    private readonly StubAgentRuntimeClient _stub = new();

    private AgentRunTraceCollector CreateCollector(long? maxTotalTraceBytes = null)
    {
        return maxTotalTraceBytes is null
            ? new AgentRunTraceCollector(_stub, NullLogger<AgentRunTraceCollector>.Instance)
            : new AgentRunTraceCollector(_stub, NullLogger<AgentRunTraceCollector>.Instance, maxTotalTraceBytes.Value);
    }

    private void ArrangeManifest(params AgentRunFileEntry[] entries)
    {
        _stub.OnListRunFiles = _ => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>([.. entries]);
    }

    private void ArrangeFileContents(Dictionary<string, byte[]> contentsByPath)
    {
        _stub.OnGetRunFile = (_, path) => contentsByPath.TryGetValue(path, out byte[]? content)
            ? content
            : Error.NotFound("AgentRun.NotFound", $"No such file: {path}");
    }

    private static AgentRunFileEntry Entry(
        string path,
        byte[] content,
        AgentRunFileKind kind = AgentRunFileKind.Trace,
        string? sha256Override = null)
    {
        return new AgentRunFileEntry
        {
            Path = path,
            SizeBytes = content.LongLength,
            Sha256 = sha256Override ?? Sha256Hex(content),
            Kind = kind,
        };
    }

    private static string Sha256Hex(byte[] content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    [Fact]
    public async Task Collect_FetchesTraceAndArtifactFiles_VerifiesAndSucceeds()
    {
        byte[] envelope = "envelope"u8.ToArray();
        byte[] transcript = "transcript"u8.ToArray();
        byte[] deliverable = "deliverable"u8.ToArray();
        ArrangeManifest(
            Entry("trace/trace.ndjson", envelope),
            Entry("trace/sub/agent-1.jsonl", transcript),
            Entry("out/checks/tie-out.txt", deliverable, AgentRunFileKind.Artifact));
        ArrangeFileContents(new Dictionary<string, byte[]>
        {
            ["trace/trace.ndjson"] = envelope,
            ["trace/sub/agent-1.jsonl"] = transcript,
            ["out/checks/tie-out.txt"] = deliverable,
        });

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Files.Count);

        AgentRunTraceFile envelopeRow = Assert.Single(result.Files, f => f.Path == "trace/trace.ndjson");
        Assert.Equal(EXECUTION_ID, envelopeRow.RunExecutionId);
        Assert.Equal(ATTEMPT, envelopeRow.Attempt);
        Assert.Equal(Sha256Hex(envelope), envelopeRow.Sha256);
        Assert.Equal(envelope.LongLength, envelopeRow.SizeBytes);
        Assert.Equal(envelope, envelopeRow.Content);
        Assert.Null(envelopeRow.BlockId);

        AgentRunTraceFile artifactRow = Assert.Single(result.Files, f => f.Path == "out/checks/tie-out.txt");
        Assert.Equal(Sha256Hex(deliverable), artifactRow.Sha256);
        Assert.Equal(deliverable, artifactRow.Content);
    }

    [Fact]
    public async Task Collect_AManifestKindContradictingItsPath_IsRefusedWithADiagnostic()
    {
        byte[] steps = "steps"u8.ToArray();
        byte[] masquerading = "secret chain of thought"u8.ToArray();
        ArrangeManifest(
            Entry("trace/trace.ndjson", steps),
            Entry("out/masquerading.txt", masquerading, AgentRunFileKind.Trace));
        ArrangeFileContents(new Dictionary<string, byte[]>
        {
            ["trace/trace.ndjson"] = steps,
            ["out/masquerading.txt"] = masquerading,
        });

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(
            EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result.Files, static file => file.Path == "out/masquerading.txt");
        Assert.Contains(result.Files, static file => file.Path == "trace/trace.ndjson");
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Contains("contradicts its path prefix", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collect_WithEmptyManifest_SucceedsWithoutFetching()
    {
        ArrangeManifest();

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Files);
        Assert.Empty(_stub.FetchedFilePaths);
    }

    [Fact]
    public async Task Collect_OverCap_ArtifactsGetFirstClaimOnTheBudget()
    {
        byte[] artifact = new byte[8];
        byte[] trace = new byte[5];
        ArrangeManifest(
            Entry("trace/trace.ndjson", trace),
            Entry("out/checks/tie-out.txt", artifact, AgentRunFileKind.Artifact));
        ArrangeFileContents(new Dictionary<string, byte[]>
        {
            ["out/checks/tie-out.txt"] = artifact,
        });

        AgentRunTraceCollectionResult result = await CreateCollector(maxTotalTraceBytes: 10)
            .CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        AgentRunTraceFile kept = Assert.Single(result.Files);
        Assert.Equal("out/checks/tie-out.txt", kept.Path);
        Assert.DoesNotContain("trace/trace.ndjson", _stub.FetchedFilePaths);
        Assert.Contains(result.Diagnostics, d => d.Contains("trace/trace.ndjson"));
    }

    [Fact]
    public async Task Collect_WhenListingFails_FailsWithNoRows()
    {
        _stub.OnListRunFiles = _ => Error.Unexpected("AgentRun.Unavailable", "Sidecar unreachable");

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Files);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task Collect_WhenOneFetchFails_KeepsVerifiedRowsButFails()
    {
        byte[] envelope = "envelope"u8.ToArray();
        ArrangeManifest(
            Entry("trace/trace.ndjson", envelope),
            Entry("trace/sub/agent-1.jsonl", "transcript"u8.ToArray()));
        ArrangeFileContents(new Dictionary<string, byte[]>
        {
            ["trace/trace.ndjson"] = envelope,
        });

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        AgentRunTraceFile row = Assert.Single(result.Files);
        Assert.Equal("trace/trace.ndjson", row.Path);
        Assert.Contains(result.Diagnostics, d => d.Contains("trace/sub/agent-1.jsonl"));
    }

    [Fact]
    public async Task Collect_WhenShaMismatches_ExcludesRowAndFails()
    {
        byte[] envelope = "envelope"u8.ToArray();
        byte[] tampered = "tampered"u8.ToArray();
        ArrangeManifest(
            Entry("trace/trace.ndjson", envelope),
            Entry("trace/sub/agent-1.jsonl", tampered, sha256Override: Sha256Hex("original"u8.ToArray())));
        ArrangeFileContents(new Dictionary<string, byte[]>
        {
            ["trace/trace.ndjson"] = envelope,
            ["trace/sub/agent-1.jsonl"] = tampered,
        });

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        AgentRunTraceFile row = Assert.Single(result.Files);
        Assert.Equal("trace/trace.ndjson", row.Path);
        Assert.Contains(result.Diagnostics, d => d.Contains("trace/sub/agent-1.jsonl"));
    }

    [Fact]
    public async Task Collect_OverTotalCap_SkipsLargestFilesFirstAndSucceeds()
    {
        byte[] largest = new byte[60];
        byte[] middle = new byte[50];
        byte[] smallest = new byte[30];
        ArrangeManifest(
            Entry("trace/trace.ndjson", smallest),
            Entry("trace/sub/agent-big.jsonl", largest),
            Entry("trace/sub/agent-small.jsonl", middle));
        ArrangeFileContents(new Dictionary<string, byte[]>
        {
            ["trace/trace.ndjson"] = smallest,
            ["trace/sub/agent-small.jsonl"] = middle,
        });

        AgentRunTraceCollectionResult result = await CreateCollector(maxTotalTraceBytes: 100)
            .CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Files.Count);
        Assert.DoesNotContain("trace/sub/agent-big.jsonl", _stub.FetchedFilePaths);
        Assert.Contains(result.Diagnostics, d => d.Contains("trace/sub/agent-big.jsonl"));
    }

    [Fact]
    public async Task Collect_ADroppedArtifact_RidesTheTypedShortfallSignal_TracesDoNot()
    {
        byte[] smallArtifact = new byte[8];
        byte[] bigArtifact = new byte[20];
        byte[] trace = new byte[5];
        ArrangeManifest(
            Entry("trace/trace.ndjson", trace),
            Entry("out/report.md", smallArtifact, AgentRunFileKind.Artifact),
            Entry("out/checks/model.py", bigArtifact, AgentRunFileKind.Artifact));
        ArrangeFileContents(new Dictionary<string, byte[]>
        {
            ["out/report.md"] = smallArtifact,
        });

        AgentRunTraceCollectionResult result = await CreateCollector(maxTotalTraceBytes: 10)
            .CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        // The dropped deliverable is a typed signal — a continuation must know the platform's
        // copy of out/ is short — while the dropped trace file stays diagnostics only.
        Assert.True(result.Succeeded);
        string droppedArtifact = Assert.Single(result.DroppedArtifactPaths);
        Assert.Equal("out/checks/model.py", droppedArtifact);
        Assert.Contains(result.Diagnostics, d => d.Contains("trace/trace.ndjson"));
    }

    /// <summary>
    /// The shortfall signal covers every way an out/ row goes missing, not just the cap: a
    /// deliverable the platform failed to fetch or refused on a digest mismatch is exactly as
    /// absent from a resumed workspace as a capped one.
    /// </summary>
    [Fact]
    public async Task Collect_AnUnfetchableOrTamperedArtifact_RidesTheShortfallSignalToo()
    {
        byte[] kept = "kept"u8.ToArray();
        byte[] tampered = "tampered"u8.ToArray();
        ArrangeManifest(
            Entry("out/report.md", kept, AgentRunFileKind.Artifact),
            Entry("out/unfetchable.bin", "gone"u8.ToArray(), AgentRunFileKind.Artifact),
            Entry("out/tampered.txt", tampered, AgentRunFileKind.Artifact,
                sha256Override: Sha256Hex("original"u8.ToArray())));
        ArrangeFileContents(new Dictionary<string, byte[]>
        {
            ["out/report.md"] = kept,
            ["out/tampered.txt"] = tampered,
        });

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(
            EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("out/report.md", Assert.Single(result.Files).Path);
        Assert.Equal(
            ["out/tampered.txt", "out/unfetchable.bin"],
            result.DroppedArtifactPaths.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// An out/-pathed row hidden behind a lying manifest kind is refused from the pack — and is
    /// therefore also a deliverable the platform holds no copy of, so it counts as dropped.
    /// </summary>
    [Fact]
    public async Task Collect_AnOutRowRefusedForItsLyingKind_CountsAsDropped()
    {
        byte[] masquerading = "deliverable"u8.ToArray();
        ArrangeManifest(Entry("out/hidden.txt", masquerading, AgentRunFileKind.Trace));
        ArrangeFileContents(new Dictionary<string, byte[]> { ["out/hidden.txt"] = masquerading });

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(
            EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.Equal("out/hidden.txt", Assert.Single(result.DroppedArtifactPaths));
    }

    [Fact]
    public async Task Collect_WhenClientThrows_ReturnsFailureInsteadOfThrowing()
    {
        _stub.OnListRunFiles = _ => throw new InvalidOperationException("Boom");

        AgentRunTraceCollectionResult result = await CreateCollector().CollectAsync(EXECUTION_ID, ATTEMPT, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Files);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task Collect_WithCancelledToken_ReturnsFailureInsteadOfThrowing()
    {
        ArrangeManifest(Entry("trace/trace.ndjson", "envelope"u8.ToArray()));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        AgentRunTraceCollectionResult result = await CreateCollector()
            .CollectAsync(EXECUTION_ID, ATTEMPT, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Files);
    }
}
