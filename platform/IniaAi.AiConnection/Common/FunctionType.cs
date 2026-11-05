using System.ComponentModel;

namespace IniaAi.AiConnection.Common;

/// <summary>
/// Represents different functional categories for AI operations.
/// Used for function-specific model family routing.
/// </summary>
public enum FunctionType
{
    [Description("General AI reasoning and chat responses")]
    GeneralReasoning,

    [Description("Workflow block AI queries and complex reasoning")]
    WorkflowReasoning,

    [Description("Document OCR and vision-based text extraction")]
    DocumentOCR,

    [Description("Financial analysis and metrics extraction")]
    FinancialAnalysis,

    [Description("Search summarization and content analysis")]
    SearchSummarization,

    [Description("Text proofreading and correction")]
    Proofreading,

    [Description("Background job processing")]
    BackgroundProcessing,

    [Description("System monitoring and health checks")]
    SystemMonitoring,

    [Description("Document processing and analysis")]
    DocumentProcessing,

    [Description("Bulk data processing operations")]
    BulkProcessing,

    [Description("Excel and spreadsheet analysis")]
    SpreadsheetAnalysis,

    [Description("Validation extraction and prose equivalence judging")]
    Validation
}
