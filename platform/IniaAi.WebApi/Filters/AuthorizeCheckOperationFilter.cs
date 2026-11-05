using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// An <see cref="IOperationFilter"/> implementation that adds JWT Bearer authentication requirements
/// to Swagger documentation for endpoints decorated with the <see cref="AuthorizeAttribute"/>.
/// </summary>
public class AuthorizeCheckOperationFilter : IOperationFilter
{
    /// <summary>
    /// Applies the filter to the specified operation using the given context.
    /// </summary>
    /// <param name="operation">The Swagger operation to modify.</param>
    /// <param name="context">The context containing metadata about the API operation.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        bool hasAuthorize = context.MethodInfo.GetCustomAttributes<AuthorizeAttribute>().Any()
            || context.MethodInfo.DeclaringType?.GetCustomAttributes<AuthorizeAttribute>().Any() == true;

        bool hasAllowAnonymous = context.MethodInfo.GetCustomAttribute<AllowAnonymousAttribute>() is not null
            || context.MethodInfo.DeclaringType?.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

        // Only apply security if [Authorize] is present and not overridden by [AllowAnonymous]
        if (hasAuthorize && !hasAllowAnonymous)
        {
            operation.Security ??= [];

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer"),
                    new List<string>()
                }
            });
        }
    }
}
