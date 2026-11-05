namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Discriminator for a leaf-level report paragraph. Mirrors the domain
/// <c>ReportParagraphKind</c>.
/// </summary>
public enum ReportParagraphKindDto
{
    /// <summary>
    /// Narrative or descriptive text content.
    /// </summary>
    Paragraph = 0,

    /// <summary>
    /// A dynamic chart visualization; payload carried on <c>ChartPayload</c>.
    /// </summary>
    DynamicChart = 1,

    /// <summary>
    /// A visual divider separating content.
    /// </summary>
    Divider = 2,

    /// <summary>
    /// A tabular representation of data; payload carried on <c>TablePayload</c>.
    /// </summary>
    Table = 3,
}
