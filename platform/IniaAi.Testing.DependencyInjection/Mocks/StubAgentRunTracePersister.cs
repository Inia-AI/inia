using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// In-memory stand-in for the durable trace store: records every batch the executor commits and
/// succeeds unless told otherwise, so strategy-level tests exercise the real
/// persist-then-acknowledge order without a database.
/// </summary>
public class StubAgentRunTracePersister : IAgentRunTracePersister
{
    public List<IReadOnlyList<AgentRunTraceFile>> Persisted { get; } = [];

    public Func<IReadOnlyList<AgentRunTraceFile>, bool> OnPersist { get; set; } = _ => true;

    public Task<bool> PersistAsync(
        IReadOnlyList<AgentRunTraceFile> files,
        CancellationToken cancellationToken = default)
    {
        Persisted.Add(files);

        return Task.FromResult(OnPersist(files));
    }
}
