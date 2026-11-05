using IniaAi.Application.Common.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IniaAi.Services.Usage;

/// <summary>
/// Degraded once this process has failed to save a token usage record, with the count and the
/// newest failure's usage type, model and kind of error; the log line beside it names the workflow
/// type and the execution. Spend in such a record is missing from usage and billing. Degraded still
/// answers HTTP 200, so no container probe fails on it; it stays until the process restarts.
/// </summary>
public sealed class TokenUsageRecordingHealthCheck : IHealthCheck
{
    public const string NAME = "Token usage recording";

    private const string NONE_FAILED = "Every token usage record this process wrote was saved.";

    private readonly TokenUsageRecordingFailures _failures;

    public TokenUsageRecordingHealthCheck()
        : this(TokenUsageRecordingFailures.Instance)
    {
    }

    internal TokenUsageRecordingHealthCheck(TokenUsageRecordingFailures failures)
    {
        _failures = failures;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        int count = _failures.Count;

        if (count == 0 || _failures.Last is not { } last)
        {
            return Task.FromResult(HealthCheckResult.Healthy(NONE_FAILED));
        }

        return Task.FromResult(HealthCheckResult.Degraded(
            $"{count} token usage record(s) could not be saved since this process started; their spend is missing from usage and billing. "
            + $"Newest at {last.At:O}: {last.Description}"));
    }
}
