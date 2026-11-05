namespace IniaAi.Common.Reflection;

/// <summary>
/// Excludes a property from <c>PlaceholderReflectionHelper</c>'s replacement scans. For identity
/// and provenance fields whose values can collide with placeholder text by accident (a hex
/// substring inside a GUID or content hash) and must never be rewritten.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class PlaceholderReplacementIgnoreAttribute : Attribute
{
}
