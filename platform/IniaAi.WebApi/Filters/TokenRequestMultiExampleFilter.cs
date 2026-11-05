using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

/// <summary>
/// Swagger operation filter that adds multiple token request examples (password and refresh flows).
/// </summary>
public class TokenRequestMultiExampleFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.ApiDescription.RelativePath?.Contains("connect/token") ?? true)
        {
            return;
        }

        var examples = new Dictionary<string, IOpenApiExample>
        {
            ["Password Flow"] = new OpenApiExample
            {
                Summary = "Password Grant",
                Value = new JsonObject
                {
                    ["grant_type"] = JsonValue.Create("password"),
                    ["username"] = JsonValue.Create("user@example.com"),
                    ["password"] = JsonValue.Create("P@ssw0rd!"),
                    ["scope"] = JsonValue.Create("api offline_access")
                }
            },
            ["Refresh Token Flow"] = new OpenApiExample
            {
                Summary = "Refresh Token Grant",
                Value = new JsonObject
                {
                    ["grant_type"] = JsonValue.Create("refresh_token"),
                    ["refresh_token"] = JsonValue.Create("your-refresh-token")
                }
            }
        };

        string contentType = "application/x-www-form-urlencoded";

        if (operation.RequestBody?.Content?.ContainsKey(contentType) == true)
        {
            if (operation.RequestBody.Content[contentType] is OpenApiMediaType mediaType)
            {
                mediaType.Examples = examples;
            }
        }
    }
}
