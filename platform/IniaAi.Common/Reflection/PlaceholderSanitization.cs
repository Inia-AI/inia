namespace IniaAi.Common.Reflection;

/// <summary>
/// Syntax a template property's substituted placeholder values must conform to.
/// Declared per property via <see cref="SanitizePlaceholderValuesAttribute"/> and
/// dispatched by <see cref="PlaceholderReflectionHelper.ReplaceAll"/>.
/// Not a flags enum — exactly one mode per property. Members sit on non-overlapping
/// bit values so accidental flag arithmetic (e.g. <c>Url | Json</c>) produces an
/// undefined value the attribute constructor rejects; keep new members on powers of two.
/// </summary>
public enum PlaceholderSanitization
{
    None = 0,
    Url = 1,
    Json = 2,
    HeaderValue = 4,
}
