using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;
using Newtonsoft.Json;

namespace IniaAi.Core.Workflows.Agents;

/// <summary>
/// Serializes the citation <see cref="Fact"/>s an agent leg resolved so a best-attempt restore
/// can re-apply them. <see cref="AgentCitationFacts"/> resolves these at run time
/// against the staged documents and their pages, so they are never present in the leg's output
/// text and a restore that rebuilds annotations from that text alone drops them. Every source an
/// agent citation resolves to is a <see cref="DocumentSource"/>, so the stored shape is concrete
/// and needs no polymorphic source handling. The in-memory-only <c>Quantity</c> is left out and
/// recomputed from <see cref="Fact.IsNumerical"/> on read.
/// </summary>
public static class AgentCitationFactStore
{
    public static string Serialize(IReadOnlyList<Fact> facts)
    {
        return JsonConvert.SerializeObject(facts.Select(static fact => new StoredFact
        {
            Value = fact.Value,
            IsNumerical = fact.IsNumerical,
            Sources = [.. fact.Sources.OfType<DocumentSource>()],
        }));
    }

    public static List<Fact> Deserialize(string json)
    {
        List<StoredFact> stored = JsonConvert.DeserializeObject<List<StoredFact>>(json) ?? [];

        // Every agent citation resolves to exactly one DocumentSource, so a stored fact carries a
        // single source with nothing to merge — set them directly rather than re-run AddSource.
        return [.. stored.Select(static entry => new Fact(entry.Value)
        {
            IsNumerical = entry.IsNumerical,
            Sources = [.. entry.Sources],
        })];
    }

    private sealed class StoredFact
    {
        public string Value { get; set; } = string.Empty;
        public bool IsNumerical { get; set; }
        public List<DocumentSource> Sources { get; set; } = [];
    }
}
