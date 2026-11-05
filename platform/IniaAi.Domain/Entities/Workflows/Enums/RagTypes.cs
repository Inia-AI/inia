using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// Represents a type of data retrieval.
/// </summary>
public enum RagTypes
{
    /// <summary>
    /// Use whole document or all available pages.
    /// </summary>
    [EnumMember(Value = nameof(WholeDocument))]
    [Description("Use whole document/all pages")]
    WholeDocument = 0,

    /// <summary>
    /// Use top [N] pages.
    /// </summary>
    [EnumMember(Value = nameof(TopNPages))]
    [Description("Use top [N] semantically closest pages")]
    TopNPages = 1, // no uses (old wfs)

    /// <summary>
    /// Use Top N Semantically closest Pages with their neighbors
    /// </summary>
    [EnumMember(Value = nameof(TopNPagesPerFilterWithTwoNeighbors))]
    [Description("Use top [N] semantically closest pages with their neighbors")]
    TopNPagesPerFilterWithTwoNeighbors = 2,

    /// <summary>
    /// Use top page.
    /// </summary>
    [EnumMember(Value = nameof(TopPage))]
    [Description("Use top page")]
    TopPage = 3, // no uses

    /// <summary>
    /// Use top page plus [N] following pages.
    /// </summary>
    [EnumMember(Value = nameof(TopPageWithNFollowingPages))]
    [Description("Use top page with [N] following pages")]
    TopPageWithNFollowingPages = 4, // no uses (old wfs)

    /// <summary>
    /// Use a N pages starting from index
    /// </summary>
    [EnumMember(Value = nameof(NPagesStartingFromIndex))]
    [Description("Use a N pages starting from index")]
    NPagesStartingFromIndex = 5,

    /// <summary>
    /// Find pages with one of the specified keywords
    /// </summary>
    [EnumMember(Value = nameof(KeywordBased))]
    [Description("Find pages with one of the specified keywords")]
    KeywordBased = 6, // no uses (old wfs)

    /// <summary>
    /// Use the exact pages specified in the dynamic input
    /// </summary>
    [EnumMember(Value = nameof(ExactPages))]
    [Description("Use the exact pages specified in the dynamic input")]
    ExactPages = 7,

    /// <summary>
    /// Use <c>TableOfContentsList</c> object to rebuild the document.
    /// </summary>
    [EnumMember(Value = nameof(UseTableOfContents))]
    [Description("Use table of contents")]
    UseTableOfContents = 8,

    [EnumMember(Value = nameof(UseAutoDetectedTableOfContents))]
    [Description("Use auto-detected table of contents")]
    UseAutoDetectedTableOfContents = 10,

    /// <summary>
    /// Iterative page selection with multiple rounds of evaluation.
    /// </summary>
    [EnumMember(Value = nameof(RagAgent))]
    [Description("Iterative page selection with reasoning")]
    RagAgent = 11,
}
