namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The runtime's description of its sandbox (<c>GET /v1/environment</c>): the same
/// text it puts at the top of every run's system prompt, and the runtime version that wrote it.
/// </summary>
public sealed record AgentRuntimeEnvironment(string RuntimeVersion, string Text);

public static class AgentRuntimeEnvironmentErrors
{
    /// <summary>The runtime answered 404: it predates the endpoint.</summary>
    public const string NOT_SERVED = "AgentRun.EnvironmentNotServed";
}

/// <summary>
/// The runtime's environment text for display, or why there is none. Never a failure: a
/// runtime without the endpoint, an unreachable one and a host with agents disabled all
/// answer <see cref="IsAvailable"/> false.
/// </summary>
public sealed record AgentRuntimeEnvironmentView(AgentRuntimeEnvironment? Environment)
{
    public static readonly AgentRuntimeEnvironmentView NotAvailable = new(Environment: null);

    public bool IsAvailable => Environment is not null;
}

/// <summary>
/// The cached view of <see cref="IAgentRuntimeClient.GetEnvironmentAsync"/>, for the editor.
/// Concurrent callers share one fetch, and a result is reused for a short window.
/// </summary>
public interface IAgentRuntimeEnvironmentProvider
{
    Task<AgentRuntimeEnvironmentView> GetAsync(CancellationToken cancellationToken = default);
}
