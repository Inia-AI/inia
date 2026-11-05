namespace IniaAi.Common.Attributes;

/// <summary>
/// Indicates that the annotated enum entry represents a thinking model.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public class ThinkingModelAttribute : Attribute
{
    public bool IsThinkingModel { get; }

    public ThinkingModelAttribute(bool isThinkingModel = true)
    {
        IsThinkingModel = isThinkingModel;
    }
}
