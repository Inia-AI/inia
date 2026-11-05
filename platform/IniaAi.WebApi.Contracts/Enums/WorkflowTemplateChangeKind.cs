namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How one element of a workflow template differs between two versions.
/// </summary>
public enum WorkflowTemplateChangeKind
{
    /// <summary>Present only in the later version.</summary>
    Added,

    /// <summary>Present only in the earlier version.</summary>
    Removed,

    /// <summary>Present in both versions with differing fields.</summary>
    Modified,

    /// <summary>A block present in both versions whose block type changed.</summary>
    TypeChanged,
}
