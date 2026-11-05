namespace IniaAi.Domain.Enums;

/// <summary>
/// Discriminator for the leaf-level <c>ReportParagraph</c>. Persisted as int,
/// so the enum must only ever be appended to — reordering remaps existing rows.
/// </summary>
public enum ReportParagraphKind
{
    Paragraph = 0,
    DynamicChart = 1,
    Divider = 2,
    Table = 3,
}
