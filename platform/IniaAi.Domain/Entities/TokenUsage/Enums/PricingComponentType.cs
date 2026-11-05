namespace IniaAi.Domain.Entities.TokenUsage.Enums;

/// <summary>
/// Defines the type of pricing component for AI model usage.
/// Covers LLM tokens, multimodal inputs, caching, batch processing,
/// real-time APIs, and specialized model capabilities.
/// </summary>
public enum PricingComponentType
{
    // ═══════════════════════════════════════════════════════════════
    // STANDARD LLM TOKENS (0-19)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Input/prompt tokens for LLM models.
    /// </summary>
    Input = 0,

    /// <summary>
    /// Output/completion tokens for LLM models.
    /// </summary>
    Output = 1,

    /// <summary>
    /// Reasoning tokens for models like OpenAI o1/o3 that charge separately for reasoning.
    /// </summary>
    Reasoning = 2,

    /// <summary>
    /// Training tokens for fine-tuning models.
    /// </summary>
    Training = 3,

    // ═══════════════════════════════════════════════════════════════
    // CACHING (20-39)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cache write tokens with 5-minute TTL (Anthropic).
    /// </summary>
    CacheWrite5m = 20,

    /// <summary>
    /// Cache write tokens with 1-hour TTL (Anthropic).
    /// </summary>
    CacheWrite1h = 21,

    /// <summary>
    /// Cache read/hit tokens (Anthropic, OpenAI cached input).
    /// </summary>
    CacheRead = 22,

    /// <summary>
    /// Context caching input tokens (Google Gemini).
    /// </summary>
    ContextCacheInput = 23,

    /// <summary>
    /// Context caching storage (Google Gemini, charged per hour).
    /// </summary>
    ContextCacheStorage = 24,

    // ═══════════════════════════════════════════════════════════════
    // EMBEDDINGS & SEARCH (40-59)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Embedding tokens for embedding models.
    /// </summary>
    Embedding = 40,

    /// <summary>
    /// Web search requests (general).
    /// </summary>
    Search = 41,

    /// <summary>
    /// Reranking queries (Cohere).
    /// </summary>
    Rerank = 42,

    /// <summary>
    /// Grounding with external search (Google Search grounding for Gemini).
    /// </summary>
    Grounding = 43,

    // ═══════════════════════════════════════════════════════════════
    // MULTIMODAL - VISION (60-79)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Image input/analysis for vision models (tokens or per-image).
    /// </summary>
    ImageInput = 60,

    /// <summary>
    /// Image generation output (DALL-E, Imagen, Stable Diffusion).
    /// </summary>
    ImageGeneration = 61,

    /// <summary>
    /// Image generation - standard quality/resolution.
    /// </summary>
    ImageGenerationStandard = 62,

    /// <summary>
    /// Image generation - HD quality/resolution.
    /// </summary>
    ImageGenerationHd = 63,

    /// <summary>
    /// Video input processing (Google Gemini).
    /// </summary>
    VideoInput = 64,

    // ═══════════════════════════════════════════════════════════════
    // MULTIMODAL - AUDIO (80-99)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Audio input/transcription (Whisper, speech-to-text).
    /// </summary>
    AudioInput = 80,

    /// <summary>
    /// Audio output/generation (TTS, text-to-speech).
    /// </summary>
    AudioOutput = 81,

    /// <summary>
    /// Real-time audio input (OpenAI Realtime API).
    /// </summary>
    RealtimeAudioInput = 82,

    /// <summary>
    /// Real-time audio output (OpenAI Realtime API).
    /// </summary>
    RealtimeAudioOutput = 83,

    /// <summary>
    /// Real-time text input for audio sessions (OpenAI Realtime API).
    /// </summary>
    RealtimeTextInput = 84,

    /// <summary>
    /// Real-time text output for audio sessions (OpenAI Realtime API).
    /// </summary>
    RealtimeTextOutput = 85,

    // ═══════════════════════════════════════════════════════════════
    // BATCH PROCESSING (100-119)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Batch API input tokens (typically 50% discount).
    /// </summary>
    BatchInput = 100,

    /// <summary>
    /// Batch API output tokens (typically 50% discount).
    /// </summary>
    BatchOutput = 101,

    /// <summary>
    /// Batch API cached input tokens.
    /// </summary>
    BatchCacheRead = 102,

    // ═══════════════════════════════════════════════════════════════
    // ASSISTANTS & TOOLS (120-139)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Code interpreter tool usage (OpenAI Assistants).
    /// </summary>
    CodeInterpreter = 120,

    /// <summary>
    /// Code interpreter session time.
    /// </summary>
    CodeInterpreterSession = 121,

    /// <summary>
    /// File search tool usage (OpenAI Assistants).
    /// </summary>
    FileSearch = 122,

    /// <summary>
    /// Vector storage for file search (OpenAI Assistants, per GB/day).
    /// </summary>
    VectorStorage = 123,

    // ═══════════════════════════════════════════════════════════════
    // DOCUMENT PROCESSING (140-159)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Document/PDF page processing.
    /// </summary>
    DocumentPage = 140,

    /// <summary>
    /// OCR character extraction.
    /// </summary>
    OcrCharacter = 141,

    // ═══════════════════════════════════════════════════════════════
    // AGENT RUNS (160-179)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// An agent run's spend record. Units carry the number of passes the run took. The run is
    /// billed on its per-model records, whose <see cref="Input"/>/<see cref="Output"/>/cache
    /// components carry the tokens read out of the collected trace pack, priced request by request
    /// from the pricing table. This component carries only the part of the runtime's own cost
    /// figure that no pricing row covered: the whole figure when the trace was withheld, nothing
    /// when every model was priced. Rows written before per-run billing carry the runtime's whole figure
    /// here and zero on the token components.
    /// </summary>
    AgentRunSpend = 160,

    // ═══════════════════════════════════════════════════════════════
    // LONG PROMPT PRICES (180-199)
    // A model whose price rises with prompt length (ModelPricing.LongPromptThresholdTokens)
    // carries these beside its standard components. They exist on pricing rows only: a recorded
    // call keeps its Input/Output/cache component types and takes the higher cost.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Input tokens of a request whose prompt is over the long-prompt threshold.
    /// </summary>
    LongPromptInput = 180,

    /// <summary>
    /// Output tokens of a request whose prompt is over the long-prompt threshold.
    /// </summary>
    LongPromptOutput = 181,

    /// <summary>
    /// 5-minute cache writes of a request whose prompt is over the long-prompt threshold.
    /// </summary>
    LongPromptCacheWrite5m = 182,

    /// <summary>
    /// 1-hour cache writes of a request whose prompt is over the long-prompt threshold.
    /// </summary>
    LongPromptCacheWrite1h = 183,

    /// <summary>
    /// Cache reads of a request whose prompt is over the long-prompt threshold.
    /// </summary>
    LongPromptCacheRead = 184,
}
