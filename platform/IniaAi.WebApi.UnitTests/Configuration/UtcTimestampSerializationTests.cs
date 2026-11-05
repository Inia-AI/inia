using IniaAi.WebApi.UnitTests.Contracts;
using System.Text.Json;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// SQL Server's datetime2 carries no offset, so EF hands the API a DateTime of kind
/// Unspecified, and System.Text.Json then writes it without a zone marker. A browser reads
/// such a value as local time, which is how a viewer at UTC+7 sees seven extra hours of
/// elapsed time. Every timestamp the API writes is UTC, and the wire format must say so
/// whatever database produced it.
/// </summary>
public class UtcTimestampSerializationTests
{
    private sealed record Stamped(DateTime CreatedAt, DateTime? CompletedAt);

    private static readonly JsonSerializerOptions _options = ApiSerializerOptions.Create();

    private static string Serialize(DateTime createdAt, DateTime? completedAt = null)
    {
        return JsonSerializer.Serialize(new Stamped(createdAt, completedAt), _options);
    }

    [Fact]
    public void AnUnspecifiedKindWritesTheSameWallClockWithAUtcMarker()
    {
        var unspecified = new DateTime(2026, 8, 27, 8, 31, 22, 212, DateTimeKind.Unspecified);

        string json = Serialize(unspecified);

        Assert.Contains("\"createdAt\":\"2026-08-27T08:31:22.212Z\"", json);
    }

    [Fact]
    public void ANullableUnspecifiedKindWritesAUtcMarkerToo()
    {
        var unspecified = new DateTime(2026, 8, 27, 8, 31, 22, DateTimeKind.Unspecified);

        string json = Serialize(unspecified, unspecified);

        Assert.Contains("\"completedAt\":\"2026-08-27T08:31:22Z\"", json);
    }

    [Fact]
    public void AUtcKindStaysAsItWas()
    {
        var utc = new DateTime(2026, 9, 15, 12, 42, 47, DateTimeKind.Utc);

        string json = Serialize(utc);

        Assert.Contains("\"createdAt\":\"2026-09-15T12:42:47Z\"", json);
    }

    [Fact]
    public void ALocalKindIsConvertedToUtcBeforeItIsWritten()
    {
        var utc = new DateTime(2026, 9, 15, 12, 42, 47, DateTimeKind.Utc);
        DateTime local = utc.ToLocalTime();

        string json = Serialize(local);

        Assert.Contains("\"createdAt\":\"2026-09-15T12:42:47Z\"", json);
    }
}
