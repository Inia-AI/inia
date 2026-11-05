using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// A single find/replace transform applied to each <see cref="MergeJsonProperties.JsonInputs"/> value
/// (and to each extracted snippet) <b>before</b> it is parsed as JSON. Lets workflow authors sanitise
/// inputs that would otherwise be invalid JSON — e.g. stripping digit-group separators with
/// <c>Find = (?&lt;=\d),(?=\d)</c>, <c>ReplaceWith = ""</c>.
///
/// <para><b>Caution:</b> transforms run as plain text replacement over the raw string with no JSON
/// structural awareness. The canonical digit-group strip <c>(?&lt;=\d),(?=\d)</c> removes <i>any</i>
/// comma between two digits, so it will also collapse comma-separated number arrays
/// (<c>[1,234]</c> → <c>[1234]</c>) and digit-grouped numbers inside string values
/// (<c>"ref 1,234"</c> → <c>"ref 1234"</c>). Because the result still parses, such corruption is
/// silent. Only use this recipe when the input is known to contain no JSON number arrays and no
/// comma-grouped digits inside strings; otherwise prefer emitting grouped numbers as quoted strings,
/// or a more tightly scoped pattern.</para>
///
/// Find/replace semantics match the StringManipulation block (see <c>RegexReplacer</c>): the
/// <c>[:newline:]/[:tab:]/[:space:]/[:return:]</c> tokens are honoured, regex supports <c>$1</c>
/// capture groups, and patterns are static workflow configuration — <b>not</b> template-resolved at
/// runtime (so <c>{Year}</c>-style placeholders in <c>Find</c>/<c>ReplaceWith</c> are not substituted).
/// </summary>
public class MergeJsonPreParseTransform
{
    /// <summary>
    /// What to find. A literal substring when <see cref="IsRegex"/> is false, otherwise a .NET regex.
    /// </summary>
    [JsonProperty(nameof(Find))]
    public string Find { get; set; } = string.Empty;

    /// <summary>
    /// Replacement text. Supports <c>$1</c> capture-group references when <see cref="IsRegex"/> is true.
    /// </summary>
    [JsonProperty(nameof(ReplaceWith))]
    public string ReplaceWith { get; set; } = string.Empty;

    /// <summary>
    /// When true (default), <see cref="Find"/> is treated as a regex; otherwise as a literal substring.
    /// </summary>
    [JsonProperty(nameof(IsRegex))]
    public bool IsRegex { get; set; } = true;

    /// <summary>
    /// Whether matching is case-sensitive. Defaults to true.
    /// </summary>
    [JsonProperty(nameof(CaseSensitive))]
    public bool CaseSensitive { get; set; } = true;

    public MergeJsonPreParseTransform Clone()
    {
        return new MergeJsonPreParseTransform
        {
            Find = Find,
            ReplaceWith = ReplaceWith,
            IsRegex = IsRegex,
            CaseSensitive = CaseSensitive,
        };
    }
}
