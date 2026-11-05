namespace IniaAi.WebApi.Attributes;

/// <summary>
/// Indicates that a controller action supports custom query logic,
/// such as enabling OData or manual query composition.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EnableCustomQueryAttribute : Attribute
{
}
