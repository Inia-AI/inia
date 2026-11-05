namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How a workflow type consumes a shared component.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Application.Common.Workflows.SharedComponentConsumerChannel</c> —
/// the two are cast between each other by integer value at the application boundary. Drift is
/// guarded by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentConsumerChannel
{
    /// <summary>The workflow type holds linked copies of the component itself.</summary>
    Direct = 1,

    /// <summary>The workflow type reaches the component through a section component that references it.</summary>
    ViaSection = 2,
}
