namespace IniaAi.WebApi.Extensions;

/// <summary>
/// Provides extension methods for <see cref="IWebHostEnvironment"/> to determine custom environment configurations.
/// </summary>
public static class IWebHostEnvironmentExtensions
{
    /// <summary>
    /// Determines whether the current hosting environment's name ends with "LocalDevelopment".
    /// </summary>
    /// <param name="webHostEnvironment">The <see cref="IWebHostEnvironment"/> instance to evaluate.</param>
    /// <returns><c>true</c> if the environment name ends with "LocalDevelopment"; otherwise, <c>false</c>.</returns>
    public static bool IsLocalDevelopment(this IWebHostEnvironment webHostEnvironment)
    {
        return webHostEnvironment.EnvironmentName.EndsWith("LocalDevelopment");
    }

    /// <summary>
    /// Whether the API serves its OpenAPI document and Swagger UI: only on a developer machine,
    /// because the document hands every signed-in caller the full route map.
    /// </summary>
    public static bool ServesApiDocumentation(this IWebHostEnvironment webHostEnvironment)
    {
        return webHostEnvironment.IsLocalDevelopment();
    }
}
