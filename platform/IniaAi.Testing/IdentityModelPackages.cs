using System.Reflection;
using System.Text.Json;

namespace IniaAi.Testing;

/// <summary>
/// The Microsoft.IdentityModel libraries call each other's internals, so they work only at one
/// shared version. NuGet does not enforce that: each package that needs them pulls its own
/// version, and a mismatch fails token handling at run time with a MissingMethodException. Each
/// host resolves its own set, so the check reads the host's dependency file in the test output.
/// </summary>
public static class IdentityModelPackages
{
    private static readonly string[] _packagePrefixes = ["Microsoft.IdentityModel.", "System.IdentityModel.Tokens.Jwt/"];

    /// <summary>
    /// What is wrong with the identity packages that <paramref name="host"/> resolves, or null when they all share one version.
    /// </summary>
    public static string? VersionMismatch(Assembly host)
    {
        string dependencyFile = Path.Combine(AppContext.BaseDirectory, $"{host.GetName().Name}.deps.json");
        if (!File.Exists(dependencyFile))
        {
            return $"No dependency file at {dependencyFile}";
        }

        using JsonDocument dependencies = JsonDocument.Parse(File.ReadAllText(dependencyFile));
        List<string> packages = [.. dependencies.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name)
            .Where(name => _packagePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))];

        if (packages.Count == 0)
        {
            return $"No identity packages in {dependencyFile}";
        }

        return packages.Select(package => package[(package.IndexOf('/') + 1)..]).Distinct().Count() == 1
            ? null
            : string.Join(", ", packages);
    }
}
