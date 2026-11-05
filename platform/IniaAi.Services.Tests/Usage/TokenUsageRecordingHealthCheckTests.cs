using IniaAi.Application.Common.Services;
using IniaAi.Services.Usage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IniaAi.Services.Tests.Usage;

/// <summary>
/// A usage record that fails to save is spend missing from usage and billing, so the host's
/// readiness report says so.
/// </summary>
public class TokenUsageRecordingHealthCheckTests
{
    [Fact]
    public async Task WithNoFailedSave_ItIsHealthy()
    {
        HealthCheckResult result = await new TokenUsageRecordingHealthCheck(new TokenUsageRecordingFailures())
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task AfterAFailedSave_ItIsDegraded_AndNamesTheCountAndTheNewestFailure()
    {
        var failures = new TokenUsageRecordingFailures();
        failures.Record("first failure");
        failures.Record("LlmBlockExecution usage of model claude-sonnet-5: PostgresException");

        HealthCheckResult result = await new TokenUsageRecordingHealthCheck(failures)
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("2 token usage record(s)", result.Description);
        Assert.Contains("PostgresException", result.Description);
        Assert.DoesNotContain("first failure", result.Description);
    }
}
