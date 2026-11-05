namespace IniaAi.Common.Reflection;

/// <summary>
/// Marks a string property that is execution-OUTPUT (e.g. an AI-generated preview), not an
/// authored template, so <see cref="PlaceholderReflectionHelper.GetAllTemplateStrings"/> excludes
/// it from editor tag validation. Does not affect runtime placeholder discovery
/// (<see cref="PlaceholderReflectionHelper.GetAllPlaceholders"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAReplacementTemplateAttribute : Attribute;
