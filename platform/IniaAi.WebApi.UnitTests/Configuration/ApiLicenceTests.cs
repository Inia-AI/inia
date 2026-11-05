using Asp.Versioning.ApiExplorer;
using IniaAi.WebApi.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// The API document the server hands out names the licence its source is published under, in every API version.
/// </summary>
public sealed class ApiLicenceTests
{
    private const string TOKEN_URL = "https://localhost/connect/token";

    [Fact]
    public async Task TheApiDocumentOfEveryVersion_NamesTheApacheLicence()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration["Swagger:TokenUrl"] = TOKEN_URL;
        _ = builder.Services.AddConfiguredControllers();
        _ = builder.Services.AddConfiguredApiVersioning();
        _ = builder.Services.AddSwaggerDocs(builder.Configuration);
        await using WebApplication app = builder.Build();
        IReadOnlyList<ApiVersionDescription> versions = app.Services.GetRequiredService<IApiVersionDescriptionProvider>().ApiVersionDescriptions;
        IDictionary<string, OpenApiInfo> documents = app.Services.GetRequiredService<IOptions<SwaggerGenOptions>>().Value.SwaggerGeneratorOptions.SwaggerDocs;

        Assert.NotEmpty(versions);
        Assert.All(versions, version =>
        {
            Assert.True(documents.TryGetValue(version.GroupName, out OpenApiInfo? info), $"No API document for {version.GroupName}");
            Assert.Equal("Apache License 2.0", info.License?.Name);
            Assert.Equal(new Uri("https://www.apache.org/licenses/LICENSE-2.0"), info.License?.Url);
        });
    }
}
