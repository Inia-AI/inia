using IniaAi.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IniaAi.WebApi.HealthChecks;

/// <summary>
/// Reports whether an object store answers, by one cheap list through the store's own S3 client. It
/// is registered Degraded, which still answers HTTP 200: a slow or failing store shows on the health
/// page without taking the host out of a load balancer, where every request that touches no file
/// still works. A load balancer probes often, so an answer, a store that did not answer in time
/// included, is kept for <see cref="Freshness"/>, and the check gives up before a probe would.
/// </summary>
public sealed class ObjectStoreHealthCheck : IHealthCheck
{
    /// <summary>The S3 store's check name on the health page.</summary>
    public const string S3_NAME = "S3 Storage";

    /// <summary>The MinIO store's check name on the health page.</summary>
    public const string MINIO_NAME = "MinIO Storage";

    /// <summary>How long one answer stands.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(1);

    /// <summary>Shorter than the load balancer's 5-second probe timeout.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly ObjectStorageService _store;
    private readonly TimeProvider _clock;
    private Answer? _last;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectStoreHealthCheck"/> class.
    /// </summary>
    public ObjectStoreHealthCheck(ObjectStorageService store, TimeProvider? clock = null)
    {
        _store = store;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        if (_last is { } last && now - last.At < Freshness)
        {
            return last.Result;
        }

        HealthCheckResult result;
        using var timeout = new CancellationTokenSource(Timeout, _clock);
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await _store.CheckReachableAsync(probe.Token);
            result = HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            result = new HealthCheckResult(context.Registration.FailureStatus, "The file store did not answer.", ex);
        }

        _last = new Answer(now, result);
        return result;
    }

    private sealed record Answer(DateTimeOffset At, HealthCheckResult Result);
}
