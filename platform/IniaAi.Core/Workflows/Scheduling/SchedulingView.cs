namespace IniaAi.Core.Workflows.Scheduling;

/// <summary>
/// Output of <see cref="SchedulingViewBuilder"/>: an ordered list of layers where every block
/// in layer N must complete before any block in layer N+1 starts. Within a layer, blocks are
/// independent and may run concurrently.
/// </summary>
/// <param name="Layers">
/// Layered (Kahn) topological view used at <c>cap &gt;= 2</c> for parallel batching. Layers may
/// span multiple sections so cross-section independent blocks can run concurrently.
/// </param>
/// <param name="SequentialOrder">
/// Single-threaded fallback order used at <c>cap == 1</c>. This is a priority-aware topological
/// emission of every block ID sorted by <c>(SectionOrder, BlockOrder)</c> while still respecting
/// DAG dependencies. It matches today's sequential <c>foreach</c> over
/// <c>workflow.Sections.OrderBy(s =&gt; s.Order)</c> then <c>section.Blocks.OrderBy(b =&gt; b.Order)</c>
/// whenever the natural section/block order is already a valid topological order &mdash; which is
/// the universal convention for hand-authored workflows. Pins the DOP=1 equivalence contract:
/// cap=1 reproduces sequential foreach byte-for-byte. Empty when there are no scheduled blocks.
/// </param>
public sealed record SchedulingView(
    IReadOnlyList<SchedulingLayer> Layers,
    IReadOnlyList<string> SequentialOrder)
{
    /// <summary>
    /// Nodes whose unsatisfied incoming edges were dropped to break a graph cycle (mutual forward
    /// references). Empty for acyclic graphs. The caller should log these as a warning.
    /// </summary>
    public IReadOnlyList<string> ForcedCycleNodes { get; init; } = [];

    /// <summary>
    /// Back-compat constructor for hand-built test views that only supply layers. The
    /// <see cref="SequentialOrder"/> is synthesised by flattening every layer's block IDs in
    /// layer order (preserving the within-layer ordering chosen by the caller). Tests that
    /// construct views directly do not exercise the DOP=1 sequential-foreach contract, so this
    /// fallback is safe.
    /// </summary>
    public SchedulingView(IReadOnlyList<SchedulingLayer> layers)
        : this(layers, layers.SelectMany(l => l.BlockIds).ToList())
    {
    }
}

/// <summary>
/// A single scheduling layer. Block IDs are sorted by <c>(section.Order, block.Order)</c> so
/// the cap=1 execution order matches today's sequential foreach (DOP=1 equivalence contract).
/// </summary>
public sealed record SchedulingLayer(IReadOnlyList<string> BlockIds);
