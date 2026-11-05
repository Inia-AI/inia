using IniaAi.WebApi.Filters;
using IniaAi.WebApi.Serialization;
using IniaAi.WebApi.Transformers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OData;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Provides extension methods to configure controllers and OData options.
/// </summary>
public static class ControllerConfiguration
{
    private const string JSON_PATH_ROOT = "$";
    private const string JSON_PATH_MEMBER = "$.";
    private const string JSON_PATH_INDEX = "$[";

    /// <summary>
    /// Adds controller configuration with OData, JSON options, and kebab-case routing.
    /// </summary>
    /// <param name="services">The service collection to add controllers to.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddConfiguredControllers(this IServiceCollection services)
    {
        _ = services
             .AddControllers(options =>
             {
                 options.Conventions.Add(new RouteTokenTransformerConvention(new ToKebabParameterTransformer()));
                 _ = options.Filters.Add<ODataQueryValidationFilter>();
             })
             .ConfigureApiBehaviorOptions(options =>
             {
                 Func<ActionContext, IActionResult> defaultResponseFactory = options.InvalidModelStateResponseFactory;
                 options.InvalidModelStateResponseFactory = context =>
                 {
                     DropRequiredErrorOfUnparseableBody(context);
                     return defaultResponseFactory(context);
                 };
             })
             .AddOData(options =>
             {
                 _ = options.EnableQueryFeatures()
                        .Select()
                        .Filter()
                        .OrderBy()
                        .Count()
                        .SetMaxTop(100);

                 options.QueryConfigurations.EnableCount = true;
                 options.QueryConfigurations.EnableExpand = false;
                 options.QueryConfigurations.EnableFilter = true;
                 options.QueryConfigurations.EnableOrderBy = true;
                 options.QueryConfigurations.EnableSelect = false;
                 options.QueryConfigurations.EnableSkipToken = false;
                 options.QueryConfigurations.MaxTop = 100;
             })
             .AddJsonOptions(options => ConfigureJsonSerializer(options.JsonSerializerOptions));

        _ = services.AddEndpointsApiExplorer();

        return services;
    }

    /// <summary>
    /// A body that fails to parse binds its parameter to null, and the implicit required check then
    /// adds a second error ("The request field is required.") that blames a body the caller did send.
    /// The parse error, keyed by its JSON path, is the one to report.
    /// </summary>
    private static void DropRequiredErrorOfUnparseableBody(ActionContext context)
    {
        bool bodyFailedToParse = context.ModelState.Keys.Any(IsJsonPath);

        if (!bodyFailedToParse)
        {
            return;
        }

        IEnumerable<string> bodyParameterNames = context.ActionDescriptor.Parameters
            .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(parameter => parameter.Name);

        foreach (string parameterName in bodyParameterNames)
        {
            _ = context.ModelState.Remove(parameterName);
        }
    }

    private static bool IsJsonPath(string key)
    {
        return key == JSON_PATH_ROOT
            || key.StartsWith(JSON_PATH_MEMBER, StringComparison.Ordinal)
            || key.StartsWith(JSON_PATH_INDEX, StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies the API's wire conventions to a serializer: camelCase names and dictionary
    /// keys, case-insensitive binding, enums as strings, and every timestamp written as UTC.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    public static void ConfigureJsonSerializer(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new VerbatimErrorKeysConverter());
    }
}
