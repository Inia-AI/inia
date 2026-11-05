using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Services;
using System.Text;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// The stamp reader against real bundles, composed the way the runtime composes them: a gzipped
/// JSON manifest whose bulk is a base64 file map.
/// </summary>
public class AgentRunSessionBundleContentTests
{
    private const string STAMP =
        """{"bundleFormat":"1","sidecar":{"version":"0.3.1005","revision":"eng339gate"},"sdk":"2.0.44","model":"claude-opus-5"}""";

    public static byte[] Bundle(string manifestJson, bool compressed = true)
    {
        return compressed
            ? WorkflowJsonCompression.Compress(manifestJson)
            : Encoding.UTF8.GetBytes(manifestJson);
    }

    public static string ManifestJson(string? stamp = STAMP, int fileCount = 2)
    {
        string files = string.Join(
            ",",
            Enumerable.Range(0, fileCount).Select(index =>
                $"\"workspace/file-{index}.md\":\"{Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('x', 64)))}\""));

        string version = stamp is null ? string.Empty : $",\"version\":{stamp}";

        return $$"""{"sessionId":"sess-abc","workspacePath":"/workspaces/run","files":{{{files}}}{{version}}}""";
    }

    [Fact]
    public void TryReadVersionStamp_CompressedManifest_ReadsTheFourPartStamp()
    {
        string? stamp = AgentRunSessionBundleContent.TryReadVersionStamp(Bundle(ManifestJson()));

        Assert.Equal(STAMP, stamp);
    }

    [Fact]
    public void TryReadVersionStamp_UncompressedManifest_StillReads()
    {
        string? stamp = AgentRunSessionBundleContent.TryReadVersionStamp(
            Bundle(ManifestJson(), compressed: false));

        Assert.Equal(STAMP, stamp);
    }

    /// <summary>
    /// The stamp sits after the file map in the manifest the runtime writes, so reading it out
    /// of a manifest carrying hundreds of files is the case that matters.
    /// </summary>
    [Fact]
    public void TryReadVersionStamp_ManifestCarryingManyFiles_StillReachesTheStamp()
    {
        string? stamp = AgentRunSessionBundleContent.TryReadVersionStamp(
            Bundle(ManifestJson(fileCount: 500)));

        Assert.Equal(STAMP, stamp);
    }

    [Fact]
    public void TryReadVersionStamp_BundleWrittenBeforeStampsExisted_ReadsAsUnstamped()
    {
        string? stamp = AgentRunSessionBundleContent.TryReadVersionStamp(
            Bundle(ManifestJson(stamp: null)));

        Assert.Null(stamp);
    }

    /// <summary>
    /// A stamp past the column is refused whole rather than cut down: a truncated stamp names a
    /// build nobody shipped, and every later comparison against it would be a lie.
    /// </summary>
    [Fact]
    public void TryReadVersionStamp_StampPastTheColumn_ReadsAsUnstamped()
    {
        string oversized = $$"""{"bundleFormat":"{{new string('1', AgentRunSessionBundleContent.MAX_VERSION_STAMP_CHARS)}}"}""";

        Assert.Null(AgentRunSessionBundleContent.TryReadVersionStamp(Bundle(ManifestJson(stamp: oversized))));
    }

    [Fact]
    public void TryReadVersionStamp_BytesThatAreNotAManifest_ReadsAsUnstamped()
    {
        Assert.Null(AgentRunSessionBundleContent.TryReadVersionStamp("not json at all"u8.ToArray()));
        Assert.Null(AgentRunSessionBundleContent.TryReadVersionStamp([]));
    }

    /// <summary>
    /// A payload that inflates past the cap is refused instead of deciding how much memory the
    /// reader takes — the bundle arrives compressed, so its declared size proves nothing.
    /// </summary>
    [Fact]
    public void TryReadVersionStamp_PayloadInflatingPastTheCap_ReadsAsUnstamped()
    {
        byte[] bomb = Bundle(new string('a', (int)AgentRunSessionBundleContent.MAX_BUNDLE_BYTES + 1024));

        Assert.True(bomb.LongLength < AgentRunSessionBundleContent.MAX_BUNDLE_BYTES);
        Assert.Null(AgentRunSessionBundleContent.TryReadVersionStamp(bomb));
    }
}
