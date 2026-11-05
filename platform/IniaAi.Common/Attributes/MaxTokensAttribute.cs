namespace IniaAi.Common.Attributes;

/// <summary>
/// Specifies the maximum number of tokens allowed for the annotated enum entry.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public class MaxTokensAttribute : Attribute
{
    public int MaxTokens { get; }

    public MaxTokensAttribute(int maxTokens)
    {
        MaxTokens = maxTokens;
    }
}
