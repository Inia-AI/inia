using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.TokenUsage.Enums;

/// <summary>
/// Specifies what triggered the token usage.
/// </summary>
public enum UsageType
{
    // ═══════════════════════════════════════════════════════════════
    // LLM Operations (0-19)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// AI query block execution in a workflow.
    /// </summary>
    [EnumMember(Value = "LlmBlockExecution")]
    [Description("Block Execution")]
    LlmBlockExecution = 0,

    /// <summary>
    /// Document overview/summary generation using LLM.
    /// </summary>
    [EnumMember(Value = "LlmOverviewGeneration")]
    [Description("Document Overview")]
    LlmOverviewGeneration = 1,

    /// <summary>
    /// OCR text enhancement - cleaning up raw OCR text using LLM.
    /// Legacy: recorded by the retired Tesseract+enhancement pipeline; kept for historical rows.
    /// </summary>
    [EnumMember(Value = "LlmOcrTextEnhancement")]
    [Description("OCR Text Enhancement")]
    LlmOcrTextEnhancement = 2,

    /// <summary>
    /// OCR markdown formatting - converting enhanced text to markdown using LLM.
    /// Legacy: recorded by the retired Tesseract+enhancement pipeline; kept for historical rows.
    /// </summary>
    [EnumMember(Value = "LlmOcrMarkdownFormatting")]
    [Description("OCR Markdown Formatting")]
    LlmOcrMarkdownFormatting = 3,

    /// <summary>
    /// Document classification using LLM.
    /// </summary>
    [EnumMember(Value = "LlmClassification")]
    [Description("Classification")]
    LlmClassification = 4,

    /// <summary>
    /// AI-powered reconciliation (matching, list comparison).
    /// </summary>
    [EnumMember(Value = "LlmReconciliation")]
    [Description("Reconciliation")]
    LlmReconciliation = 5,

    /// <summary>
    /// RAG filtering operations (page selection, relevance assessment).
    /// </summary>
    [EnumMember(Value = "LlmRagFiltering")]
    [Description("RAG Filtering")]
    LlmRagFiltering = 6,

    /// <summary>
    /// Named entity classification (person, organization, location, etc.).
    /// </summary>
    [EnumMember(Value = "LlmEntityClassification")]
    [Description("Entity Classification")]
    LlmEntityClassification = 8,

    /// <summary>
    /// Spreadsheet/Excel sheet layout analysis and header identification.
    /// </summary>
    [EnumMember(Value = "LlmSpreadsheetAnalysis")]
    [Description("Spreadsheet Analysis")]
    LlmSpreadsheetAnalysis = 9,

    /// <summary>
    /// Graph-based provenance queries.
    /// </summary>
    [EnumMember(Value = "LlmGraphQuery")]
    [Description("Graph Query")]
    LlmGraphQuery = 10,

    /// <summary>
    /// Iterative content refinement (tone adjustment, expansion, simplification, etc.).
    /// </summary>
    [EnumMember(Value = "LlmContentRefinement")]
    [Description("Content Refinement")]
    LlmContentRefinement = 11,

    /// <summary>
    /// OCR oneshot transcription - transcribing a page image using the OCR model.
    /// Recorded per page.
    /// </summary>
    [EnumMember(Value = "LlmOcrTranscription")]
    [Description("OCR Transcription")]
    LlmOcrTranscription = 12,

    /// <summary>
    /// OCR fidelity judge - grading a page transcription good/bad against the page image.
    /// Recorded per page.
    /// </summary>
    [EnumMember(Value = "LlmOcrFidelityJudge")]
    [Description("OCR Fidelity Judge")]
    LlmOcrFidelityJudge = 13,

    /// <summary>
    /// An agent run executed on the agent runtime for an Agent block. Recorded as a spend row
    /// carrying the cost the runtime itself measured, plus one token row per model the run
    /// called, summed from the collected trace pack's usage records.
    /// </summary>
    [EnumMember(Value = "LlmAgentRun")]
    [Description("Agent Run")]
    LlmAgentRun = 14,

    /// <summary>
    /// Validation reference or candidate extraction: a schema filled from one document.
    /// </summary>
    [EnumMember(Value = "LlmValidationExtraction")]
    [Description("Validation Extraction")]
    LlmValidationExtraction = 15,

    /// <summary>
    /// Validation prose judging: one field's reference and candidate texts judged for equivalence.
    /// </summary>
    [EnumMember(Value = "LlmValidationJudge")]
    [Description("Validation Judge")]
    LlmValidationJudge = 16,

    /// <summary>
    /// Intake screening judge: one document of a screened run judged for readability, class and
    /// safety before document processing. Recorded per document.
    /// </summary>
    [EnumMember(Value = "LlmIntakeScreening")]
    [Description("Intake Screening")]
    LlmIntakeScreening = 17,

    // ═══════════════════════════════════════════════════════════════
    // Embedding Operations (20-29)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Page-level embeddings generation.
    /// </summary>
    [EnumMember(Value = "EmbeddingPage")]
    [Description("Page Embedding")]
    EmbeddingPage = 20,

    /// <summary>
    /// Chunk-level embeddings generation.
    /// </summary>
    [EnumMember(Value = "EmbeddingChunk")]
    [Description("Chunk Embedding")]
    EmbeddingChunk = 21,

    /// <summary>
    /// Query embedding for RAG search.
    /// </summary>
    [EnumMember(Value = "EmbeddingQuery")]
    [Description("Query Embedding")]
    EmbeddingQuery = 22,

    // ═══════════════════════════════════════════════════════════════
    // Reranking Operations (30-39)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// RAG result reranking.
    /// </summary>
    [EnumMember(Value = "Reranking")]
    [Description("Reranking")]
    Reranking = 30,

    /// <summary>
    /// Screening backtest judge: one document of a past run judged again to measure how screening
    /// would have classified it. Recorded per document, on no run. An LLM operation, so its value
    /// sits in the LLM range.
    /// </summary>
    [EnumMember(Value = "LlmScreeningBacktest")]
    [Description("Screening Backtest")]
    LlmScreeningBacktest = 18,

    /// <summary>
    /// A message to the agent beside the workflow editor: one conversation turn on the agent
    /// runtime. Valued inside the LLM range (0-19) so the usage dashboard counts it as LLM work.
    /// </summary>
    [EnumMember(Value = "LlmEditorAgentChat")]
    [Description("Workflow Editor Agent")]
    LlmEditorAgentChat = 19,
}
