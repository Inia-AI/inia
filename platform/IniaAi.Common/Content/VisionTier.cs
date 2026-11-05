using IniaAi.Common.Extensions;

namespace IniaAi.Common.Content;

/// <summary>
/// The largest image an Anthropic model reads as it is, as a long edge and a count of 28 x 28 px
/// visual tokens (platform.claude.com/docs/en/build-with-claude/vision, "Resolution and token
/// cost"). The model downscales an image over either limit before reading it, so pixels beyond the
/// long edge cost memory, render time and request size, and the model never sees them.
/// </summary>
public readonly record struct VisionTier(int LongEdgePx, int MaxVisualTokens)
{
    public const int STANDARD_LONG_EDGE_PX = 1568;

    public const int STANDARD_MAX_VISUAL_TOKENS = 1568;

    public const int HIGH_RESOLUTION_LONG_EDGE_PX = 2576;

    public const int HIGH_RESOLUTION_MAX_VISUAL_TOKENS = 4784;

    /// <summary>Every model released before Claude 4.7.</summary>
    public static VisionTier Standard { get; } = new(STANDARD_LONG_EDGE_PX, STANDARD_MAX_VISUAL_TOKENS);

    /// <summary>Claude 4.7 and later.</summary>
    public static VisionTier HighResolution { get; } = new(HIGH_RESOLUTION_LONG_EDGE_PX, HIGH_RESOLUTION_MAX_VISUAL_TOKENS);

    /// <summary>
    /// The tier <paramref name="model"/> reads at. A model not known to be on the standard tier,
    /// including an id the platform does not recognise, gets the high-resolution tier, so no model
    /// receives a smaller image than it reads.
    /// </summary>
    public static VisionTier ForModel(Enum? model)
    {
        return model?.GetVisionTier() ?? HighResolution;
    }
}
