using System.ComponentModel.DataAnnotations;

namespace IniaAi.Configuration.Platform;

/// <summary>
/// The longest input a refine request may carry, in characters. Refine rewrites one report
/// paragraph, so these bound what one request can cost.
/// </summary>
public sealed class ContentRefinementSettings
{
    public const string SECTION_NAME = "PlatformSettings:ContentRefinement";

    /// <summary>The longest paragraph a request may ask to refine.</summary>
    [Range(1, int.MaxValue)]
    public int MaxContentLength { get; set; } = 100_000;

    /// <summary>The longest paragraph, instruction and conversation history together.</summary>
    [Range(1, int.MaxValue)]
    public int MaxCombinedLength { get; set; } = 500_000;
}
