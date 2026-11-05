namespace IniaAi.Domain.Entities.TokenUsage.Enums;

/// <summary>
/// Defines the unit of measurement for pricing.
/// Supports token-based, time-based, volume-based, and storage-based pricing models.
/// </summary>
public enum PricingUnit
{
    // ═══════════════════════════════════════════════════════════════
    // TOKEN-BASED (0-19)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cost per million tokens (industry standard for LLM/embedding pricing).
    /// </summary>
    PerMillion = 0,

    /// <summary>
    /// Cost per thousand tokens (legacy unit, some older APIs use this).
    /// </summary>
    PerThousand = 1,

    /// <summary>
    /// Cost per individual token (rare, but some specialized models use this).
    /// </summary>
    PerToken = 2,

    /// <summary>
    /// Cost per character (TTS services like Google Cloud TTS).
    /// </summary>
    PerCharacter = 3,

    /// <summary>
    /// Cost per million characters.
    /// </summary>
    PerMillionCharacters = 4,

    // ═══════════════════════════════════════════════════════════════
    // UNIT-BASED (20-39)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cost per unit (generic - images, documents, queries, etc.).
    /// </summary>
    PerUnit = 20,

    /// <summary>
    /// Cost per image (image generation, analysis).
    /// </summary>
    PerImage = 21,

    /// <summary>
    /// Cost per page (document processing, PDF).
    /// </summary>
    PerPage = 22,

    /// <summary>
    /// Cost per request/API call.
    /// </summary>
    PerRequest = 23,

    /// <summary>
    /// Cost per query (search, reranking).
    /// </summary>
    PerQuery = 24,

    /// <summary>
    /// Cost per document (reranking services often charge per doc in result set).
    /// </summary>
    PerDocument = 25,

    /// <summary>
    /// Cost per session (real-time API sessions).
    /// </summary>
    PerSession = 26,

    // ═══════════════════════════════════════════════════════════════
    // TIME-BASED (40-59)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cost per second (real-time audio/video streaming).
    /// </summary>
    PerSecond = 40,

    /// <summary>
    /// Cost per minute (audio transcription, video processing).
    /// </summary>
    PerMinute = 41,

    /// <summary>
    /// Cost per hour (compute time, context caching storage, code interpreter).
    /// </summary>
    PerHour = 42,

    /// <summary>
    /// Cost per day (storage, provisioned capacity).
    /// </summary>
    PerDay = 43,

    /// <summary>
    /// Cost per month (subscriptions, provisioned throughput).
    /// </summary>
    PerMonth = 44,

    // ═══════════════════════════════════════════════════════════════
    // STORAGE-BASED (60-79)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cost per gigabyte (storage).
    /// </summary>
    PerGb = 60,

    /// <summary>
    /// Cost per gigabyte per day (vector storage).
    /// </summary>
    PerGbDay = 61,

    /// <summary>
    /// Cost per gigabyte per month.
    /// </summary>
    PerGbMonth = 62,

    // ═══════════════════════════════════════════════════════════════
    // PROVISIONED/CAPACITY (80-99)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cost per Provisioned Throughput Unit per hour (Azure OpenAI PTU).
    /// </summary>
    PerPtuHour = 80,

    /// <summary>
    /// Cost per Model Unit per hour (AWS Bedrock provisioned).
    /// </summary>
    PerModelUnitHour = 81,

    /// <summary>
    /// Cost per Compute Unit (various providers).
    /// </summary>
    PerComputeUnit = 82
}
