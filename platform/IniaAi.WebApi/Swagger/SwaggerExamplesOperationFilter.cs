using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.WebApi.Swagger;

/// <summary>
/// Operation filter that applies request/response examples from
/// <see cref="SwaggerRequestExampleAttribute"/> and <see cref="SwaggerResponseExampleAttribute"/>
/// using Microsoft.OpenApi types.
/// </summary>
public class SwaggerExamplesOperationFilter : IOperationFilter
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SwaggerExamplesOperationFilter"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public SwaggerExamplesOperationFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ApplyRequestExamples(operation, context);
        ApplyResponseExamples(operation, context);
    }

    private void ApplyRequestExamples(OpenApiOperation operation, OperationFilterContext context)
    {
        IEnumerable<SwaggerRequestExampleAttribute> attributes = context.MethodInfo.GetCustomAttributes<SwaggerRequestExampleAttribute>();

        foreach (SwaggerRequestExampleAttribute attr in attributes)
        {
            JsonNode? example = GetExampleValue(attr.ExamplesProviderType);
            if (example is null)
            {
                continue;
            }

            if (operation.RequestBody?.Content?.TryGetValue("application/json", out OpenApiMediaType? mediaType) == true
                && mediaType is OpenApiMediaType mt)
            {
                mt.Example = example;
            }
        }
    }

    private void ApplyResponseExamples(OpenApiOperation operation, OperationFilterContext context)
    {
        IEnumerable<SwaggerResponseExampleAttribute> attributes = context.MethodInfo.GetCustomAttributes<SwaggerResponseExampleAttribute>();

        foreach (SwaggerResponseExampleAttribute attr in attributes)
        {
            string statusCode = attr.StatusCode.ToString();

            if (operation.Responses is null
                || !operation.Responses.TryGetValue(statusCode, out IOpenApiResponse? response))
            {
                continue;
            }

            JsonNode? example = GetExampleValue(attr.ExamplesProviderType);
            if (example is null)
            {
                continue;
            }

            if (response.Content?.TryGetValue("application/json", out OpenApiMediaType? mediaType) == true
                && mediaType is OpenApiMediaType mt)
            {
                mt.Example = example;
            }
        }
    }

    private JsonNode? GetExampleValue(Type examplesProviderType)
    {
        object provider = _serviceProvider.GetService(examplesProviderType)
                       ?? ActivatorUtilities.CreateInstance(_serviceProvider, examplesProviderType);

        // Find the IExamplesProvider<T>.GetExamples() method
        Type? providerInterface = examplesProviderType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType
                                 && i.GetGenericTypeDefinition() == typeof(IExamplesProvider<>));

        if (providerInterface is null)
        {
            return null;
        }

        MethodInfo? getExamplesMethod = providerInterface.GetMethod(nameof(IExamplesProvider<object>.GetExamples));
        object? exampleObject = getExamplesMethod?.Invoke(provider, null);

        return exampleObject is null ? null : JsonSerializer.SerializeToNode(exampleObject);
    }
}
