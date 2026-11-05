using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// An <see cref="IOperationFilter"/> implementation that converts all query parameter names
/// in Swagger documentation to camelCase.
/// </summary>
public class CamelCaseQueryParameterFilter : IOperationFilter
{
    /// <summary>
    /// Applies the filter to the specified operation using the given context.
    /// </summary>
    /// <param name="operation">The Swagger operation to modify.</param>
    /// <param name="context">The context containing metadata about the API operation.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Parameters is null)
        {
            return;
        }

        foreach (IOpenApiParameter parameter in operation.Parameters)
        {
            if (parameter is not OpenApiParameter openApiParameter
                || openApiParameter.Name is null)
            {
                continue;
            }

            openApiParameter.Name = char.ToLowerInvariant(openApiParameter.Name[0]) + openApiParameter.Name[1..];
        }
    }
}
