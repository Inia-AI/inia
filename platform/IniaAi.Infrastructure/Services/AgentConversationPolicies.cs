using IniaAi.Common.Interfaces.Services.AgentRuntime;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Each purpose's <see cref="AgentConversationPolicy"/>, read from the registered consumers once
/// per process. A consumer may depend on scoped services, so it is resolved in a scope of its own;
/// a fault while reading is not cached, so the next caller retries.
/// </summary>
public sealed class AgentConversationPolicies
{
    private readonly Lazy<IReadOnlyDictionary<string, AgentConversationPolicy>> _byPurpose;

    public AgentConversationPolicies(IServiceScopeFactory scopeFactory)
    {
        _byPurpose = new(() => Read(scopeFactory), LazyThreadSafetyMode.PublicationOnly);
    }

    public IReadOnlyDictionary<string, AgentConversationPolicy> All => _byPurpose.Value;

    public AgentConversationPolicy Of(string purpose)
    {
        return All.TryGetValue(purpose, out AgentConversationPolicy? policy)
            ? policy
            : throw new InvalidOperationException($"No agent conversation consumer is registered for purpose '{purpose}'");
    }

    private static IReadOnlyDictionary<string, AgentConversationPolicy> Read(IServiceScopeFactory scopeFactory)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetServices<IAgentConversationConsumer>()
            .GroupBy(static consumer => consumer.Purpose, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Policy, StringComparer.Ordinal);
    }
}
