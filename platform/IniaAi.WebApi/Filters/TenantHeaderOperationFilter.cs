using IniaAi.WebApi.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using System.Text.Json.Nodes;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// An <see cref="IOperationFilter"/> implementation that adds the X-Tenant-Id header parameter
/// to Swagger documentation for authenticated API operations that require tenant context.
/// Excludes endpoints marked with [AllowAnonymous] or [TenantOptional].
/// </summary>
public class TenantHeaderOperationFilter : IOperationFilter
{
    /// <summary>
    /// Applies the filter to the specified operation using the given context.
    /// </summary>
    /// <param name="operation">The Swagger operation to modify.</param>
    /// <param name="context">The context containing metadata about the API operation.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Check for [AllowAnonymous] on method or controller
        bool hasAllowAnonymous = context.MethodInfo.GetCustomAttribute<AllowAnonymousAttribute>() is not null
            || context.MethodInfo.DeclaringType?.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

        // Skip if endpoint has [AllowAnonymous] - public endpoints don't need tenant context
        if (hasAllowAnonymous)
        {
            return;
        }

        // Check for [TenantOptional] on method or controller
        bool hasTenantOptional = context.MethodInfo.GetCustomAttribute<TenantOptionalAttribute>() is not null
            || context.MethodInfo.DeclaringType?.GetCustomAttribute<TenantOptionalAttribute>() is not null;

        // Skip if endpoint has [TenantOptional] - authenticated but tenant-agnostic endpoints
        if (hasTenantOptional)
        {
            return;
        }

        // Check if endpoint requires authorization (on method or controller)
        bool hasAuthorize = context.MethodInfo.GetCustomAttributes<AuthorizeAttribute>().Any()
            || context.MethodInfo.DeclaringType?.GetCustomAttributes<AuthorizeAttribute>().Any() == true;

        // Only add X-Tenant-Id header for authenticated endpoints that need tenant context
        if (!hasAuthorize)
        {
            return;
        }

        operation.Parameters ??= [];

        // Insert at the beginning so X-Tenant-Id appears first
        operation.Parameters.Insert(0, new OpenApiParameter
        {
            Name = "X-Tenant-Id",
            In = ParameterLocation.Header,
            Description = "Tenant ID for multi-tenant access (required for this endpoint)",
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Example = JsonValue.Create("org-12345")
            }
        });
    }
}
