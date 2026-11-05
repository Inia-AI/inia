using IniaAi.WebApi.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>The JSON options the API's controllers serialize with, built the way the host builds them.</summary>
internal static class ApiSerializerOptions
{
    public static JsonSerializerOptions Create()
    {
        ServiceCollection services = new();
        _ = services.AddConfiguredControllers();
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
    }
}
