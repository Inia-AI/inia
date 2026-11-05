using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Dependencies;

public enum DependencyEdgeKind
{
    /// <summary>The target's templates read the source's output or value.</summary>
    Data,

    /// <summary>The target reads documents of a class the source creates.</summary>
    Document,

    /// <summary>The target reads a counterparty value the source writes back.</summary>
    Counterparty,

    /// <summary>The target's section gate reads the source. Shown, never scheduled.</summary>
    Gate,
}

public enum DependencyInputKind
{
    Variable,
    Signature,
    System,
    Loop,
    Unresolved,
}

public enum DependencyIssueKind
{
    /// <summary>No block or input supplies the tag.</summary>
    UnresolvedTag,

    /// <summary>
    /// The blocks read each other. A cycle of data reads is broken by running one block with
    /// no-data for the read that closes it. A document or counterparty edge that would close a
    /// cycle is not scheduled, so its reader runs without what the producer creates.
    /// </summary>
    Cycle,

    /// <summary>
    /// The block reads documents another block creates, but the run dispatches it in an earlier
    /// segment, so it runs before they exist.
    /// </summary>
    ReadsDocumentsBeforeProducer,

    /// <summary>
    /// The block reads a counterparty value another block writes back, but the run dispatches it
    /// in an earlier segment, so it reads the value stored before this run.
    /// </summary>
    ReadsCounterpartyBeforeWriter,
}

/// <summary>
/// One block of the map, keyed by <see cref="Id"/>: the block's own id, which in a template map is
/// the template block every parameterized variant folds onto.
/// </summary>
/// <param name="VariantKeys">The parameterized section's variant keys, empty outside one.</param>
/// <param name="VerificationGroup">The group key of the verifier this block belongs to, or null.</param>
public sealed record DependencyBlock(
    string Id,
    Block Block,
    int SectionOrder,
    IReadOnlyList<string> VariantKeys,
    string? VerificationGroup);

/// <summary>
/// A value a block reads that no block produces. <see cref="Id"/> is unique across kinds (the same
/// tag can be a signature field for one block and unresolved for another); <see cref="Tag"/> is the
/// base tag the templates name.
/// </summary>
public sealed record DependencyInput(string Id, DependencyInputKind Kind, string Tag);

/// <summary>
/// The target depends on the source. Sources are block or input ids, targets are block ids. One
/// edge per (source, target, kind): <see cref="Tags"/> lists every placeholder (or document class)
/// carrying it and <see cref="VariantKeys"/> every parameterized variant it flows through.
/// </summary>
public sealed record DependencyEdge(
    string SourceId,
    string TargetId,
    DependencyEdgeKind Kind,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> VariantKeys);

/// <summary>A verifier and the blocks it judges, drawn as one group.</summary>
public sealed record DependencyVerificationGroup(
    string Key,
    string VerifierBlockId,
    IReadOnlyList<string> MemberTags,
    IReadOnlyList<string> MemberBlockIds);

/// <param name="BlockIds">
/// The readers of an unresolved tag, the blocks on a cycle, or the reader then the producer of an
/// ordering the run cannot keep.
/// </param>
/// <param name="Tag">
/// The unresolved base tag, the document class, or the counterparty placeholder; null for a cycle.
/// </param>
public sealed record DependencyIssue(
    DependencyIssueKind Kind,
    IReadOnlyList<string> BlockIds,
    string? Tag);
