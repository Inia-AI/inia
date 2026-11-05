using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// Specifies how pages should be grouped into chunks for batch processing.
/// </summary>
public enum ChunkingMode
{
    /// <summary>
    /// Chunk by fixed page count (existing behavior).
    /// </summary>
    [EnumMember(Value = nameof(FixedPageCount))]
    [Description("Process a fixed number of pages per batch")]
    FixedPageCount = 0,

    /// <summary>
    /// Chunk optimally by character limit using bin-packing.
    /// </summary>
    [EnumMember(Value = nameof(OptimalCharacters))]
    [Description("Group pages optimally by character limit")]
    OptimalCharacters = 1,
}
