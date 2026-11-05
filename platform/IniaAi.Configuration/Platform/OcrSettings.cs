namespace IniaAi.Configuration.Platform;

/// <summary>
/// Provides configuration settings for the <see cref="OcrTranscriptionService"/>: parallelism,
/// the output-token cap and the model that transcribes pages.
/// </summary>
public class OcrSettings
{
    /// <summary>
    /// Gets or sets the maximum degree of parallelism to use for the page render phase.
    /// If not set to a positive integer, the system defaults to 1 (serial rendering).
    /// </summary>
    /// <remarks>
    /// This value controls how many PDF pages can be converted to images concurrently. Raising it
    /// increases peak memory — each concurrent render holds a page bitmap — without improving
    /// throughput, because Aspose serializes rasterization internally and rendering already outpaces
    /// the LLM transcription phase it feeds.
    /// </remarks>
    public string? OcrMaxDegreeOfParallelism { get; set; }

    /// <summary>
    /// Gets or sets the maximum degree of parallelism to use for the LLM transcription phase.
    /// If not set to a positive integer, the system defaults to half the processor count.
    /// </summary>
    /// <remarks>
    /// This value controls how many pages can be transcribed and fidelity-judged concurrently.
    /// </remarks>
    public string? LlmMaxDegreeOfParallelism { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of output tokens for each LLM OCR call (transcription
    /// and fidelity judge). A dense page can exceed a low cap, silently truncating the page
    /// text, so this defaults well above the legacy 4096 value while staying within the
    /// model's output ceiling.
    /// </summary>
    public int OcrMaxOutputTokens { get; set; } = 16384;

    /// <summary>
    /// Gets or sets whether pages are transcribed by the deployment's advanced model
    /// (<c>Anthropic:AdvancedModel</c>) or by its basic model (<c>Anthropic:BasicModel</c>).
    /// </summary>
    public bool ShouldUseAdvancedModel { get; set; } = true;

    /// <summary>
    /// Gets or sets a catalogue model id that transcribes pages instead of the model
    /// <see cref="ShouldUseAdvancedModel"/> picks. The connector resolves it to the id this
    /// deployment calls; OCR fails before its first model call when the deployment cannot call it.
    /// </summary>
    public string? ModelIdOverride { get; set; }
}
