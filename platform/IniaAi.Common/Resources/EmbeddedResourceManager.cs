using System.Reflection;

namespace IniaAi.Common.Resources;

public static class EmbeddedResourceManager
{
    public static List<string> GetResourcesList(
        this Assembly assembly,
        string? searchPattern = null)
    {
        string[] resources = assembly.GetManifestResourceNames();

        if (searchPattern is not null)
        {
            resources = [.. resources.Where(r => r.Contains(searchPattern))];
        }

        return [.. resources];
    }

    public static async Task<string> ReadResourceAsync(
        this Assembly assembly,
        string name,
        CancellationToken cancellationToken = default)
    {
        string resourcePath = assembly.GetManifestResourceNames()
            .Single(str => str.EndsWith(name));

        using Stream stream = assembly.GetManifestResourceStream(resourcePath)
            ?? throw new InvalidOperationException(
                $"Resource '{name}' not found in assembly '{assembly.FullName}'.");

        using StreamReader reader = new(stream);

        string content = await reader.ReadToEndAsync(cancellationToken);

        return content;
    }

    public static Stream ReadResource(
        this Assembly assembly,
        string name)
    {
        string resourcePath = assembly.GetManifestResourceNames()
            .Single(str => str.EndsWith(name));

        Stream stream = assembly.GetManifestResourceStream(resourcePath)
            ?? throw new InvalidOperationException(
                $"Resource '{name}' not found in assembly '{assembly.FullName}'.");

        return stream;
    }
}
