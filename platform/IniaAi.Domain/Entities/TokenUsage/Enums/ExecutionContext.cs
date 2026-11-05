using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.TokenUsage.Enums;

/// <summary>
/// Specifies how the operation was triggered.
/// </summary>
public enum ExecutionContext
{
    /// <summary>
    /// Production workflow execution (full report run).
    /// </summary>
    [EnumMember(Value = "WorkflowExecution")]
    WorkflowExecution = 0,

    /// <summary>
    /// Execute Section testing from the workflow editor.
    /// </summary>
    [EnumMember(Value = "SectionTest")]
    SectionTest = 1,

    /// <summary>
    /// Execute Block testing from the workflow editor.
    /// </summary>
    [EnumMember(Value = "BlockTest")]
    BlockTest = 2,

    /// <summary>
    /// Standalone document processing (upload without workflow).
    /// </summary>
    [EnumMember(Value = "DocumentUpload")]
    DocumentUpload = 3,

    /// <summary>
    /// Direct API call (future use).
    /// </summary>
    [EnumMember(Value = "ApiCall")]
    ApiCall = 4,

    /// <summary>
    /// Background system monitoring operations, such as a module's matching of a report to its counterparties.
    /// </summary>
    [EnumMember(Value = "SystemMonitoring")]
    SystemMonitoring = 5,

    /// <summary>
    /// Validation runs (reference extraction, candidate extraction, equivalence judging).
    /// </summary>
    [EnumMember(Value = "Validation")]
    Validation = 6,

    /// <summary>
    /// Screening backtests (the judge re-classifying documents of past runs).
    /// </summary>
    [EnumMember(Value = "ScreeningBacktest")]
    ScreeningBacktest = 7,

    /// <summary>
    /// A conversation with the agent beside the workflow editor.
    /// </summary>
    [EnumMember(Value = "WorkflowEditorAgent")]
    WorkflowEditorAgent = 8,
}
