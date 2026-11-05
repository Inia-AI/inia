using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// Configuration for a parameterized section that gets expanded at runtime
/// into one copy per variant. Enables writing prompts once and reusing
/// across multiple document types, regions, or other dimensions.
/// </summary>
public record ParameterizedSectionConfig(
    List<SectionVariant> Variants
);

/// <summary>
/// Defines a single variant of a parameterized section.
/// Each variant produces one expanded copy of the section at runtime.
/// </summary>
/// <param name="Key">Unique key used for suffixing block ReplacementTags (e.g., "AR", "QR", "Region1")</param>
/// <param name="Label">Human-readable label for display (e.g., "Annual Report")</param>
/// <param name="Params">Meta-variables resolved in prompts before execution.
/// Example: { "_PERIOD_DESC" → "{InterimPeriod} month" }. Use "" for no content.</param>
/// <param name="SectionStopGate">Stop gate template for the expanded section (e.g., "{verify_quarterly_report.stop}")</param>
/// <param name="SectionSupportedDocumentClasses">Legacy input only (the retired replace-all mode).
/// Kept so pre-conversion JSON still deserializes; consumed exclusively by
/// ParameterizedSectionNormalizer and the conversion migration.</param>
/// <param name="ClassMap">Per-block document-class rewrite map (source class → target classes).
/// Blocks keep their own classes mapped through it; unmapped classes and the "Any"/"None"
/// sentinels pass through. Null and empty both mean "no rewrites".</param>
public record SectionVariant(
    string Key,
    string Label,
    Dictionary<string, string>? Params = null,
    string? SectionStopGate = null,
    List<string>? SectionSupportedDocumentClasses = null,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    Dictionary<string, List<string>>? ClassMap = null
);
