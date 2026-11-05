using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Domain.Entities.Citations.Traces;

/// <summary>
/// What one executed block's citation steps decided. <see cref="Facts"/> are the facts the block
/// carried when it finished, in order; a fact inherited from an earlier block names that block's
/// fact instead of repeating its sources, so each source is written once, by the block that cited
/// it. <see cref="Sources"/> is the block's source list, which facts and rejections point into by
/// index; a page is named by id and the span its excerpt covers, not copied. <see cref="Report"/>
/// is filled when the block's report paragraph is built.
/// </summary>
public sealed record CitationTraceContent
{
    public const int CURRENT_VERSION = 1;

    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitEmptyLists } },
    };

    public int Version { get; init; } = CURRENT_VERSION;

    public List<TracedFact> Facts { get; init; } = [];

    public List<TracedSource> Sources { get; init; } = [];

    public List<TracedRejection> Rejections { get; init; } = [];

    public TracedReport? Report { get; set; }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, _options);
    }

    /// <summary>
    /// A run writes thousands of facts, most with no expansion and no upstream list, so an empty
    /// list is left out rather than written as <c>[]</c>; it reads back as empty.
    /// </summary>
    private static void OmitEmptyLists(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (typeof(System.Collections.ICollection).IsAssignableFrom(property.PropertyType))
            {
                property.ShouldSerialize = static (_, value) => value is System.Collections.ICollection { Count: > 0 };
            }
        }
    }

    public static CitationTraceContent? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CitationTraceContent>(json, _options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>How a fact came to be on the block.</summary>
public enum TracedFactEntry
{
    /// <summary>The block's own citation: a model citation, a cited page, a search result.</summary>
    Cited,

    /// <summary>A figure inside one of the block's own facts, grounded in that fact's sources.</summary>
    Split,

    /// <summary>An earlier block's fact whose value this block's output restates.</summary>
    Inherited,

    /// <summary>A figure with no fact, cited to the input block whose output states it.</summary>
    Producer,
}

public sealed record TracedFact
{
    /// <summary>The value, cut to <see cref="CitationTraceLimits.MAX_VALUE_LENGTH"/> for a long passage.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The full length of the value when <see cref="Value"/> was cut, else 0.</summary>
    public int CutFromLength { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TracedFactEntry Entry { get; init; }

    public bool Numerical { get; init; }

    /// <summary>The quantity the matcher reads, such as "Currency GBP 2506000"; empty for a broad fact.</summary>
    public string? Quantity { get; init; }

    /// <summary>A bare table cell read at its page's scale: it matches downstream only as that amount.</summary>
    public bool TableCellAtPageScale { get; init; }

    /// <summary>For a <see cref="TracedFactEntry.Split"/> fact, the index of the fact it came from.</summary>
    public int? Parent { get; init; }

    /// <summary>For an <see cref="TracedFactEntry.Inherited"/> fact, the earlier block's fact.</summary>
    public TracedFactOrigin? Origin { get; init; }

    /// <summary>Indexes into <see cref="CitationTraceContent.Sources"/>; empty for an inherited fact.</summary>
    public List<int> Sources { get; init; } = [];

    /// <summary>Earlier blocks' facts whose pages and links this fact gained because it cites their block.</summary>
    public List<TracedFactOrigin> ExpandedFrom { get; init; } = [];

    /// <summary>For a <see cref="TracedFactEntry.Producer"/> fact, the words its claim shares with the producer's sentence.</summary>
    public int? SharedWords { get; init; }
}

/// <summary>A fact of another block's trace: that block's id and the fact's index in its list.</summary>
public sealed record TracedFactOrigin(string BlockId, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int Fact);

public sealed record TracedSource
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public CitationSourceType Type { get; init; }

    public string? PageId { get; init; }

    public int? PageNumber { get; init; }

    public string? DocumentName { get; init; }

    public string? Url { get; init; }

    public string? BlockId { get; init; }

    public string? BlockTitle { get; init; }

    /// <summary>
    /// Where the excerpt sits in the page's text, when it was found there: the reader takes the
    /// text from the page rather than from the trace.
    /// </summary>
    public int? Start { get; init; }

    public int? Length { get; init; }

    /// <summary>The excerpt, cut to <see cref="CitationTraceLimits.MAX_EXCERPT_LENGTH"/>, when it has no place to point at.</summary>
    public string? Text { get; init; }
}

/// <summary>
/// The fixed reasons a citation step turns a figure or a source down. Values are written by name;
/// append a new reason at the end.
/// </summary>
public enum CitationTraceReason
{
    /// <summary>A figure inside a fact was dropped: no source of the fact states it.</summary>
    NoSourceStatesValue,

    /// <summary>A source was removed from a figure: the page or link text does not state it.</summary>
    SourceDoesNotStateValue,

    /// <summary>An earlier block's bare number, with no unit, was not inherited.</summary>
    BareNumberNotInherited,

    /// <summary>An earlier block's bare year was not inherited: its source speaks of other things.</summary>
    YearContextDiffers,

    /// <summary>An earlier block's fact lost to another fact of the same value whose source fits the claim better.</summary>
    OtherMeaningChosen,

    /// <summary>Earlier blocks' facts state one value with different meanings and the claim did not say which.</summary>
    SameValueDifferentMeanings,

    /// <summary>
    /// An input block is not a citation source, so it cannot be cited as a figure's producer. Older
    /// runs recorded this for a block its author set to stay out of citations.
    /// </summary>
    InputExcludedFromCitations,

    /// <summary>A figure with no fact: no input block states it as written.</summary>
    NoProducerStatesFigure,

    /// <summary>A figure with no fact was not cited to a producer: its value is undecided between meanings.</summary>
    UndecidedMeaning,

    /// <summary>A figure's occurrence sits inside an HTML tag, so it gets no marker there.</summary>
    InsideHtmlTag,

    /// <summary>A list item's number is not a figure, so it gets no marker.</summary>
    ListItemNumber,

    /// <summary>An occurrence follows a minus sign the fact does not carry, so it is another figure.</summary>
    NegatedByMinus,

    /// <summary>A citation was dropped from the report: no occurrence of its figure got a marker.</summary>
    NoMarkerPlaced,

    /// <summary>Earlier blocks' facts on different pages tied, so a fact of the value that cites only blocks was kept.</summary>
    TiedPagesKeptBlockFact,
}

public sealed record TracedRejection
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public CitationTraceReason Reason { get; init; }

    public string Value { get; init; } = string.Empty;

    /// <summary>Index into <see cref="CitationTraceContent.Facts"/> when the rejection concerns a kept fact.</summary>
    public int? Fact { get; init; }

    /// <summary>Index into <see cref="CitationTraceContent.Sources"/> when the rejection concerns a source.</summary>
    public int? Source { get; init; }

    /// <summary>The earlier blocks' facts the rejection turned down.</summary>
    public List<TracedFactOrigin> Upstream { get; init; } = [];

    /// <summary>The block a rejection names, such as an input set to stay out of citations.</summary>
    public string? BlockId { get; init; }
}

/// <summary>How a citation's sources were settled for the report.</summary>
public enum TracedFlattening
{
    /// <summary>The fact cited pages or links itself, so only those stay.</summary>
    Direct,

    /// <summary>The fact cited only blocks, so the pages and links behind those blocks replace them.</summary>
    FromUpstream,

    /// <summary>No page or link stands behind the blocks, so the citation keeps the blocks.</summary>
    BlockFallback,
}

/// <summary>How a citation's marker was placed in the paragraph.</summary>
public enum TracedMarker
{
    /// <summary>The figure as written.</summary>
    Literal,

    /// <summary>The same amount written another way.</summary>
    Quantity,

    /// <summary>The same date written another way.</summary>
    Date,
}

public sealed record TracedReport
{
    public List<TracedCitation> Citations { get; init; } = [];

    public List<TracedRejection> Rejections { get; init; } = [];
}

public sealed record TracedCitation
{
    public string Id { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TracedFlattening Flattening { get; init; }

    /// <summary>Indexes into <see cref="CitationTraceContent.Sources"/>: the citation's sources as the report stores them.</summary>
    public List<int> Sources { get; init; } = [];

    /// <summary>For a block citation, the pages and links of that block whose excerpt states the figure.</summary>
    public int Matching { get; init; }

    /// <summary>Null when no marker was placed; the citation was then dropped.</summary>
    public TracedMarker? Marker { get; init; }
}

public static class CitationTraceLimits
{
    public const int MAX_VALUE_LENGTH = 160;
    public const int MAX_EXCERPT_LENGTH = 400;
}

/// <summary>
/// The text of a page a citation trace points at, carried once per downloaded run so the trace's
/// page spans can be read away from the database the run wrote them in.
/// </summary>
public sealed record CitationTracePage(string Id, string Text);
