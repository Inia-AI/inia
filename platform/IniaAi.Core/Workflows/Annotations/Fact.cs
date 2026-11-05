using IniaAi.Core.Workflows.Annotations.Quantities;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Workflows.Annotations;

public class Fact
{
    public const string MULTIPLE_CITATIONS_PLACEHOLDER = "[Multiple citations used from the source]";

    public string Value { get; set; } = string.Empty;
    public List<BaseSource> Sources { get; set; } = [];

    /// <summary>
    /// Setting this to <c>true</c> also populates <see cref="Quantity"/> from
    /// <see cref="Value"/> when the value parses as a single quantity covering
    /// the whole trimmed string. The invariant lives in the setter so all
    /// construction patterns (object initializer, post-init assignment) get the
    /// quantity for free without each caller having to remember a follow-up step.
    /// </summary>
    public bool IsNumerical
    {
        get;
        set
        {
            field = value;
            if (value && Quantity is null)
            {
                PopulateQuantityFromValue();
            }
        }
    }

    /// <summary>
    /// Structured form of <see cref="Value"/> for numerical facts. Populated
    /// when <see cref="IsNumerical"/> is set to <c>true</c> and <see cref="Value"/>
    /// parses as a single quantity covering the whole string. Null otherwise
    /// (broad facts, or numerical text that isn't a clean single span).
    /// In-memory only — never persisted.
    /// </summary>
    public Quantity? Quantity { get; set; }

    /// <summary>
    /// The value is a bare table cell and <see cref="Quantity"/> the amount it stands for at its
    /// page's scale: "2,506" under £'000 is £2,506,000. Such a fact is found downstream only as
    /// that amount, never as the same digits — "2,506 employees" is not the cell.
    /// </summary>
    public bool IsTableCellAtPageScale { get; set; }

    public Fact(string value)
    {
        Value = value;
    }

    private void PopulateQuantityFromValue()
    {
        Quantity = UnitExtractor.TryExtractSingle(Value);
    }

    /// <summary>
    /// Adds a source to the fact. If a source with the same SourceKey already exists,
    /// keeps the CitedText that mentions the fact's value — literal substring, the same
    /// date in another form, or quantity equivalence when this fact has a structured
    /// <see cref="Quantity"/>. If neither CitedText mentions it, falls back to a
    /// placeholder. If both are identical, skips the duplicate.
    /// <para>
    /// The fold never writes through the instance it holds. Sources are shared by
    /// reference between the facts and blocks that cite them (a provider's excerpt list,
    /// an upstream fact's sources during expansion), so a rewritten entry is a copy that
    /// this fact alone owns; the caller's instance keeps its text for its own grounding.
    /// </para>
    /// </summary>
    public void AddSource(BaseSource source)
    {
        for (int index = 0; index < Sources.Count; index++)
        {
            BaseSource existing = Sources[index];

            if (existing.SourceKey != source.SourceKey)
            {
                continue;
            }

            if (existing.CitedText == source.CitedText)
            {
                return;
            }

            bool sourceMentions = CitedTextMentionsThisFact(source.CitedText);
            bool existingMentions = CitedTextMentionsThisFact(existing.CitedText);

            if (sourceMentions && !existingMentions)
            {
                Sources[index] = existing.WithCitedText(source.CitedText);
            }
            else if (!sourceMentions && !existingMentions)
            {
                Sources[index] = existing.WithCitedText(MULTIPLE_CITATIONS_PLACEHOLDER);
            }

            return;
        }

        Sources.Add(source);
    }

    private bool CitedTextMentionsThisFact(string citedText)
    {
        if (citedText.Contains(Value, StringComparison.OrdinalIgnoreCase)
            || DateEquivalence.TextStatesDate(citedText, Value))
        {
            return true;
        }

        if (Quantity is null)
        {
            return false;
        }

        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(citedText);
        return QuantityMatcher.FindBestMatch(Quantity, quantities) is not null;
    }

    public void AddSources(IEnumerable<BaseSource> sources)
    {
        foreach (BaseSource source in sources)
        {
            AddSource(source);
        }
    }

    public override bool Equals(object? obj)
    {
        return obj is Fact other && Value == other.Value;
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }
}
