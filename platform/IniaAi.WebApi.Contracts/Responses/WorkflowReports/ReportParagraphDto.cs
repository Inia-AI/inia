using IniaAi.WebApi.Contracts.Dto.Citations;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowReports;

/// <summary>
/// Leaf-level node of a workflow report.
/// </summary>
public record ReportParagraphDto
{
    /// <summary>
    /// Gets the unique identifier of the paragraph.
    /// </summary>
    public string Id { get; init; } = default!;

    /// <summary>
    /// Gets the kind discriminator (paragraph, chart, divider, table).
    /// </summary>
    public ReportParagraphKindDto Kind { get; init; }

    /// <summary>
    /// Gets the ordinal position of this paragraph within its section.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Gets the title of the paragraph.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the subtitle or secondary title of the paragraph.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// Gets the confidence score associated with the paragraph.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Gets the main content or body of the paragraph.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Gets the replacement tag linking this paragraph to its source block.
    /// </summary>
    public string? ReplacementTag { get; init; }

    /// <summary>
    /// Gets the chart payload, populated when <see cref="Kind"/> is
    /// <see cref="ReportParagraphKindDto.DynamicChart"/>.
    /// </summary>
    public ChartOutputDto? ChartPayload { get; init; }

    /// <summary>
    /// Gets the table payload, populated when <see cref="Kind"/> is
    /// <see cref="ReportParagraphKindDto.Table"/>.
    /// </summary>
    public TableOutputDto? TablePayload { get; init; }

    /// <summary>
    /// Gets the source identifiers or references for the paragraph.
    /// </summary>
    public List<string> Sources { get; init; } = [];

    /// <summary>
    /// Gets the citations with their sources for the paragraph.
    /// </summary>
    public List<CitationDto> Citations { get; init; } = [];

    /// <summary>
    /// Gets, for each block that a block citation in <see cref="Citations"/> reaches, every page
    /// and link that block or its upstream blocks cited. Sent once per block rather than inside
    /// each citation. Empty for a paragraph built before these lists existed.
    /// </summary>
    public List<BlockCitedSourcesDto> CitedSourcesByBlock { get; init; } = [];
}
