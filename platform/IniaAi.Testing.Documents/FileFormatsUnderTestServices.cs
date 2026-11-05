using IniaAi.FileFormats;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Testing.Documents;

public static class FileFormatsUnderTestServices
{
    /// <summary>
    /// Registers the file formats of the build under test as the hosts register them. Every test registration calls
    /// it, so a build that puts another implementation in place changes the tests here once.
    /// </summary>
    public static IServiceCollection AddFileFormatsUnderTest(this IServiceCollection services)
    {
        _ = services.AddOpenFileFormats();
        return services;
    }
}
