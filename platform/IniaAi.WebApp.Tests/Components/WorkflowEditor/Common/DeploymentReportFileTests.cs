using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using System.Text.Json;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class DeploymentReportFileTests
{
    private static readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void AReportThisDeploymentDownloaded_IsRead()
    {
        CurrencyCardDto report = new(
            "this-deploy",
            new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc),
            [new CurrencyCardEntryDto("Corp", "v1", "hash-1", "abc123", DateTime.UtcNow, "user-1", "Editor", ["hash-1"])]);

        CurrencyCardDto? read = DeploymentReportFile.Read(JsonSerializer.Serialize(report, _options), _options);

        Assert.NotNull(read);
        Assert.Equal("this-deploy", read.InstanceName);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"name":"QA schema","fields":[{"key":"amount"}]}""")]
    [InlineData("""{"instanceName":"this-deploy","generatedUtc":"2026-10-19T12:00:00Z"}""")]
    [InlineData("""{"instanceName":"this-deploy","generatedUtc":"2026-10-19T12:00:00Z","workflows":[{"workflowTypeId":"Corp","contentHash":"hash-1"}]}""")]
    [InlineData("null")]
    public void AFileWithoutWhatTheComparisonReads_IsNotAReport(string json)
    {
        Assert.Null(DeploymentReportFile.Read(json, _options));
    }
}
