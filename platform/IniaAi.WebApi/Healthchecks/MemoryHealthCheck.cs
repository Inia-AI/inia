using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.HealthChecks;

//https://medium.com/@jeslurrahman/implementing-health-checks-in-net-8-c3ba10af83c3
/// <summary>
/// Represents a health check that monitors the application's memory usage.
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<MemoryCheckOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryHealthCheck"/> class with the specified options monitor.
    /// </summary>
    /// <param name="options">The options monitor providing memory check configurations.</param>
    public MemoryHealthCheck(IOptionsMonitor<MemoryCheckOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Gets the name of the memory health check.
    /// </summary>
    public static string Name => "memory_check";

    /// <summary>
    /// Performs the memory health check by evaluating the current allocated memory against the configured threshold.
    /// </summary>
    /// <param name="context">The context in which the health check is executed.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the health check.</param>
    /// <returns>A <see cref="HealthCheckResult"/> indicating the health status.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        MemoryCheckOptions options = _options.Get(context.Registration.Name);

        // Retrieve the total allocated memory without forcing a full garbage collection.
        long allocated = GC.GetTotalMemory(forceFullCollection: false);

        // Gather garbage collection statistics.
        var data = new Dictionary<string, object>
        {
            { "AllocatedBytes", allocated },
            { "Gen0Collections", GC.CollectionCount(0) },
            { "Gen1Collections", GC.CollectionCount(1) },
            { "Gen2Collections", GC.CollectionCount(2) },
        };

        // Determine the health status based on the allocated memory and the configured threshold.
        HealthStatus status = (allocated < options.Threshold) ? HealthStatus.Healthy : HealthStatus.Unhealthy;

        return Task.FromResult(new HealthCheckResult(
            status,
            description: $"Reports degraded status if allocated bytes >= {options.Threshold} bytes.",
            exception: null,
            data: data));
    }
}

/// <summary>
/// Represents configuration options for the <see cref="MemoryHealthCheck"/>.
/// </summary>
public class MemoryCheckOptions
{
    /// <summary>
    /// Gets or sets the memory status description.
    /// </summary>
    public string? MemoryStatus { get; set; }

    /// <summary>
    /// Gets or sets the failure threshold in bytes. If the allocated memory exceeds this value, the health check reports an unhealthy status.
    /// </summary>
    public long Threshold { get; set; } = 7L * 1024L * 1024L * 1024L; // Default to 7 GB
}
