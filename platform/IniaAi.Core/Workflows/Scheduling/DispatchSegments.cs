using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Scheduling;

/// <summary>
/// One dispatch segment: the maximal run of sections the handler hands to a single scheduler
/// pass. Blocks in different segments never share a scheduling graph, so no edge between them
/// can hold one back — which is why a verification group split across two segments cannot run.
/// </summary>
public sealed record DispatchSegment(IReadOnlyList<Section> Sections, bool IsLoop);

/// <summary>
/// The single definition of how a workflow's sections divide into dispatch segments. The handler
/// executes these; the save-time guards walk the same result to refuse a shape the dispatcher
/// could not honor. A second copy in a guard would drift from the dispatcher and start passing
/// shapes that fail at run time.
/// </summary>
public static class DispatchSegments
{
    /// <summary>
    /// A loop or parameterized section occupies a segment alone (it expands at run time and
    /// dispatches on its own). Otherwise a run of sections accumulates until a section's skip
    /// gate reads a tag the pending run produces, or the run already holds a fence block.
    /// </summary>
    /// <param name="isExecutionOrderFence">
    /// The fence rule. It is virtual on the handler, so the dispatcher passes its own override;
    /// a save-time caller can only pass <see cref="IsBaseExecutionOrderFence"/>, which is why
    /// the run-time pre-flight repeats the segment checks against the real rule.
    /// </param>
    public static List<DispatchSegment> Split(Workflow workflow, Func<Block, bool> isExecutionOrderFence)
    {
        List<Section> orderedSections =
            [.. workflow.Sections.NonSeparator().OrderBy(static section => section.Order)];

        return [.. Group(
                orderedSections,
                IsLoopSection,
                static section => section.GetAllPlaceholders(),
                static section => section.Blocks.SelectMany(ProducedTags),
                section => section.Blocks.Any(isExecutionOrderFence))
            .Select(static run => new DispatchSegment(run.Units, run.IsLoop))];
    }

    /// <summary>
    /// The segment rule over any ordered sequence of section-like units, so the dependency map can
    /// segment a template's parameterized variants as the run segments the expanded sections.
    /// </summary>
    internal static List<(List<T> Units, bool IsLoop)> Group<T>(
        IReadOnlyList<T> units,
        Func<T, bool> isLoop,
        Func<T, IEnumerable<string>> gatePlaceholders,
        Func<T, IEnumerable<string>> producedTags,
        Func<T, bool> holdsFence)
    {
        List<(List<T> Units, bool IsLoop)> segments = [];
        int index = 0;

        while (index < units.Count)
        {
            if (isLoop(units[index]))
            {
                segments.Add(([units[index]], true));
                index++;
                continue;
            }

            int runStart = index;
            HashSet<string> produced = new(StringComparer.OrdinalIgnoreCase);
            bool runHasFence = false;

            while (index < units.Count && !isLoop(units[index]))
            {
                T candidate = units[index];

                if (index > runStart
                    && (runHasFence || gatePlaceholders(candidate).Any(placeholder => produced.Contains(placeholder.Split('.', 2)[0]))))
                {
                    break;
                }

                produced.UnionWith(producedTags(candidate));
                runHasFence |= holdsFence(candidate);
                index++;
            }

            segments.Add(([.. units.Skip(runStart).Take(index - runStart)], false));
        }

        return segments;
    }

    /// <summary>The segment ordinal of every block, so callers compare membership without re-walking.</summary>
    public static IReadOnlyDictionary<string, int> IndexByBlockId(IReadOnlyList<DispatchSegment> segments)
    {
        Dictionary<string, int> byBlockId = new(StringComparer.Ordinal);

        for (int segment = 0; segment < segments.Count; segment++)
        {
            foreach (Block block in segments[segment].Sections.SelectMany(static section => section.Blocks))
            {
                byBlockId[block.Id] = segment;
            }
        }

        return byBlockId;
    }

    /// <summary>
    /// The fence floor every handler starts from; a handler subclass may declare more, which
    /// save-time callers cannot see.
    /// </summary>
    public static bool IsBaseExecutionOrderFence(Block block)
    {
        return block.Type == BlockTypes.BuildTopicsForAllDocuments;
    }

    internal static bool IsLoopSection(Section section)
    {
        return !string.IsNullOrWhiteSpace(section.ParameterizedConfig) || HoldsLoopStart(section);
    }

    /// <summary>Whether the section loops once its parameterized config has been expanded away.</summary>
    internal static bool HoldsLoopStart(Section section)
    {
        return section.Blocks.Any(static block => block.Type == BlockTypes.LoopStart);
    }

    internal static IEnumerable<string> ProducedTags(Block block)
    {
        yield return block.Id;

        if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
        {
            yield return block.ReplacementTag;
        }
    }
}
