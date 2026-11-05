using IniaAi.Core.Workflows;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// Pure serialization guard for the intake skip marker — no database or AWS dependency, so it
/// runs without the DB-backed <see cref="WorkflowsFixture{T}"/>.
/// </summary>
public class SkippedProtectedFileSerializationTests
{
    [Fact]
    public void SerializeDeserialize_RoundTrips()
    {
        List<SkippedProtectedFile> skippedFiles =
        [
            new("q1-report.pdf", "No password available for automatically imported files."),
            new("holdings.xlsx", "Certificate- or DRM-based protection cannot be removed."),
        ];

        List<SkippedProtectedFile> roundTripped =
            SkippedProtectedFile.Deserialize(SkippedProtectedFile.Serialize(skippedFiles));

        Assert.Equal(skippedFiles, roundTripped);
    }

    [Fact]
    public void Deserialize_Garbage_Throws()
    {
        _ = Assert.Throws<System.Text.Json.JsonException>(
            () => SkippedProtectedFile.Deserialize("not json at all"));
    }
}
