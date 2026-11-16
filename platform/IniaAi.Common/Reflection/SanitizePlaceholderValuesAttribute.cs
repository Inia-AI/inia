namespace IniaAi.Common.Reflection;

/// <summary>
/// Marks a template property so <see cref="PlaceholderReflectionHelper.ReplaceAll"/> sanitizes
/// each substituted placeholder value for the declared syntax while leaving the template's own
/// literals untouched:
/// <list type="bullet">
/// <item><see cref="PlaceholderSanitization.Url"/> — percent-encodes each value, also when the
/// property is a single placeholder, so no value can carry a whole URL.</item>
/// <item><see cref="PlaceholderSanitization.Json"/> — JSON-escapes each value as string content
/// (assumes the property is JSON-shaped; a non-JSON body would need its own mode). A
/// single-placeholder property substitutes verbatim so a value can carry a complete JSON
/// document, and a value that is already valid escaped JSON string content substitutes verbatim
/// so pre-escaped inputs are not double-escaped.</item>
/// <item><see cref="PlaceholderSanitization.HeaderValue"/> — no transformation; the resolved
/// value is rejected with a descriptive exception when it contains control characters.</item>
/// </list>
/// Exactly one mode must be given; combined values (e.g. <c>Url | Json</c>) are rejected by the
/// constructor.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SanitizePlaceholderValuesAttribute : Attribute
{
    public SanitizePlaceholderValuesAttribute(PlaceholderSanitization mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentException(
                $"'{mode}' is not a single {nameof(PlaceholderSanitization)} mode; modes cannot be combined.",
                nameof(mode));
        }

        Mode = mode;
    }

    public PlaceholderSanitization Mode { get; }
}
