using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// Swagger operation filter that enables file upload support by generating appropriate request body schemas.
/// </summary>
public class FileUploadOperationFilter : IOperationFilter
{
    /// <summary>
    /// Applies the file upload schema to Swagger operations with <see cref="IFormFile"/> parameters.
    /// </summary>
    /// <param name="operation">The OpenAPI operation to modify.</param>
    /// <param name="context">The current operation filter context.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        IEnumerable<ParameterInfo> fileTypeParameters = context.MethodInfo
            .GetParameters()
            .Where(p => p.ParameterType == typeof(IFormFile));

        if (fileTypeParameters.Any())
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = fileTypeParameters.ToDictionary(
                                p => p.Name!, // <- null-forgiving operator resolves the warning
                                p => (IOpenApiSchema)new OpenApiSchema
                                {
                                    Type = JsonSchemaType.String,
                                    Format = "binary"
                                }),
                            Required = fileTypeParameters
                                .Select(p => p.Name!)
                                .ToHashSet()
                        }
                    }
                }
            };
        }
    }
}
