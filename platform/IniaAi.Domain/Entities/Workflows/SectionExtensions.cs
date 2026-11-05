namespace IniaAi.Domain.Entities.Workflows;

public static class SectionExtensions
{
    /// <summary>
    /// Returns only real sections, filtering out visual separator nodes.
    /// Use anywhere the iteration "does work per section" (execution,
    /// validation, signatures, export). Surfaces that need to render every
    /// section (editor sidebar, workflow map) must NOT use this.
    /// </summary>
    public static IEnumerable<Section> NonSeparator(this IEnumerable<Section> sections)
    {
        return sections.Where(s => !s.IsSeparator);
    }
}
