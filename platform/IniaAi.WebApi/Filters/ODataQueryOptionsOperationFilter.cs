
using IniaAi.WebApi.Attributes;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IniaAi.WebApi.Filters;
/// <summary>
/// Adds OData query options (e.g., $filter, $select) to Swagger for actions that use OData.
/// </summary>
public class ODataQueryOptionsOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Check if the controller or action has [EnableQuery] attribute
        bool hasOData = context.MethodInfo.GetCustomAttributes(true).OfType<EnableCustomQueryAttribute>().Any()
            || context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<EnableCustomQueryAttribute>().Any() == true;

        if (!hasOData)
        {
            return;
        }

        IEnumerable<ApiParameterDescription> odataParams = context.ApiDescription.ParameterDescriptions
           .Where(p => p.Type.Name.StartsWith("ODataQueryOptions"));

        foreach (ApiParameterDescription? param in odataParams)
        {
            if (operation.Parameters is null)
            {
                continue;
            }

            IOpenApiParameter? toRemove = operation.Parameters.FirstOrDefault(p => p.Name == param.Name);

            if (toRemove is not null)
            {
                _ = operation.Parameters.Remove(toRemove);
            }
        }

        operation.Parameters ??= [];

        // Optionally add custom docs for OData-supported params
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "$filter",
            In = ParameterLocation.Query,
            Description = "Filter the results using OData syntax.",
            Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "$orderby",
            In = ParameterLocation.Query,
            Description = "Sort the results using OData syntax.",
            Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "$top",
            In = ParameterLocation.Query,
            Description = "Limit the number of returned items.",
            Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "$skip",
            In = ParameterLocation.Query,
            Description = "Skip the number of returned items.",
            Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "$count",
            In = ParameterLocation.Query,
            Description = "Return count of items.",
            Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean }
        });
    }
}
