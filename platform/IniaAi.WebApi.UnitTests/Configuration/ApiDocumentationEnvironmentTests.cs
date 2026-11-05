using IniaAi.WebApi.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// Only a developer machine serves the OpenAPI document and Swagger UI. A server that runs under
/// any other environment name, Development included, does not hand out the route map.
/// </summary>
public sealed class ApiDocumentationEnvironmentTests
{
    [Theory]
    [InlineData("LocalDevelopment", true)]
    [InlineData("Development", false)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    [InlineData("Tests", false)]
    public void OnlyADeveloperMachine_ServesTheApiDocumentation(string environmentName, bool serves)
    {
        IWebHostEnvironment environment = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName }).Environment;

        Assert.Equal(serves, environment.ServesApiDocumentation());
    }
}
