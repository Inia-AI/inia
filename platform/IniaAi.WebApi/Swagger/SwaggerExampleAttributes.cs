namespace IniaAi.WebApi.Swagger;

/// <summary>
/// Specifies a request example provider for Swagger documentation.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SwaggerRequestExampleAttribute : Attribute
{
    /// <summary>The request type this example applies to.</summary>
    public Type RequestType { get; }

    /// <summary>The example provider type (must implement <see cref="IExamplesProvider{T}"/>).</summary>
    public Type ExamplesProviderType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SwaggerRequestExampleAttribute"/> class.
    /// </summary>
    /// <param name="requestType">The request type.</param>
    /// <param name="examplesProviderType">The examples provider type.</param>
    public SwaggerRequestExampleAttribute(Type requestType, Type examplesProviderType)
    {
        RequestType = requestType;
        ExamplesProviderType = examplesProviderType;
    }
}

/// <summary>
/// Specifies a response example provider for Swagger documentation.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SwaggerResponseExampleAttribute : Attribute
{
    /// <summary>The HTTP status code this example applies to.</summary>
    public int StatusCode { get; }

    /// <summary>The example provider type (must implement <see cref="IExamplesProvider{T}"/>).</summary>
    public Type ExamplesProviderType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SwaggerResponseExampleAttribute"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="examplesProviderType">The examples provider type.</param>
    public SwaggerResponseExampleAttribute(int statusCode, Type examplesProviderType)
    {
        StatusCode = statusCode;
        ExamplesProviderType = examplesProviderType;
    }
}
