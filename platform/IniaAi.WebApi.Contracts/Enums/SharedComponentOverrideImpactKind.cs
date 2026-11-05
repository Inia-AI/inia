namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// What a definition change does to a client signature-override row it touches.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Application.Common.Workflows.SharedComponentOverrideImpactKind</c> — the
/// two are cast between each other by integer value at the application boundary. Drift is guarded
/// by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentOverrideImpactKind
{
    /// <summary>The row's block drops with a removed slot; the row is deleted with it.</summary>
    Deleted = 1,

    /// <summary>The row survives but its placeholder left the definition, so injection stops consulting it.</summary>
    GoingInert = 2,
}
