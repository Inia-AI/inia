namespace IniaAi.Common.Attributes;

/// <summary>
/// Marks a model on Anthropic's standard vision tier (<see cref="Content.VisionTier.Standard"/>).
/// Only models released before Claude 4.7 carry it, so a new model reads at the high-resolution
/// tier by default.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class StandardVisionTierAttribute : Attribute
{
}
