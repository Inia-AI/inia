using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowReports;

/// <summary>
/// A section of a workflow report, grouping ordered <see cref="ReportParagraphDto"/>
/// leaves.
/// </summary>
public record ReportSectionDto
{
    /// <summary>
    /// Gets the unique identifier of the section.
    /// </summary>
    public string Id { get; init; } = default!;

    /// <summary>
    /// Gets the ordinal position of this section within the report.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Gets the title of the section.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the grid layout for the section's paragraphs, when configured.
    /// </summary>
    public GridLayoutOutputDto? Layout { get; init; }

    /// <summary>
    /// Gets a value indicating whether the Word and Markdown exports leave this section out. The section
    /// still shows in the app report.
    /// </summary>
    public bool ExcludeFromWordExport { get; init; }

    /// <summary>
    /// Gets the ordered paragraphs contained in this section.
    /// </summary>
    public List<ReportParagraphDto> Paragraphs { get; init; } = [];
}
