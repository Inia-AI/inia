using IniaAi.Testing;
using System.Text.Json;

namespace IniaAi.Core.Tests.CitationTraces;

internal static class CitationTraceWriter
{
    private const string TRACES_RELATIVE_PATH = "artifacts/citation-traces/traces";

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write(string fileName, object trace)
    {
        string targetDir = ResolveTracesDirectory();
        if (!Directory.Exists(targetDir))
        {
            _ = Directory.CreateDirectory(targetDir);
        }

        string targetPath = Path.Combine(targetDir, fileName);
        string json = JsonSerializer.Serialize(trace, _options);
        File.WriteAllText(targetPath, json);
        return targetPath;
    }

    public static string Write(string subDirectory, string fileName, object trace)
    {
        string targetDir = Path.Combine(ResolveTracesDirectory(), subDirectory);
        if (!Directory.Exists(targetDir))
        {
            _ = Directory.CreateDirectory(targetDir);
        }

        string targetPath = Path.Combine(targetDir, fileName);
        string json = JsonSerializer.Serialize(trace, _options);
        File.WriteAllText(targetPath, json);
        return targetPath;
    }

    /// <summary>
    /// Writes a pre-serialised string (e.g. the output of
    /// <c>Workflow.ToJson</c>, which uses Newtonsoft.Json attributes that the
    /// System.Text.Json-based <see cref="Write(string, object)"/> overload does
    /// not honour) into a sibling directory of <c>traces/</c>. Use for saved
    /// workflow definitions, fixture exports, or anything not produced by
    /// our own anonymous-typed traces.
    /// </summary>
    public static string WriteRaw(string siblingSubdirectory, string fileName, string contents)
    {
        string researchRoot = Path.GetDirectoryName(ResolveTracesDirectory())!;
        string targetDir = Path.Combine(researchRoot, siblingSubdirectory);
        if (!Directory.Exists(targetDir))
        {
            _ = Directory.CreateDirectory(targetDir);
        }

        string targetPath = Path.Combine(targetDir, fileName);
        File.WriteAllText(targetPath, contents);
        return targetPath;
    }

    private static string ResolveTracesDirectory()
    {
        string? envOverride = Environment.GetEnvironmentVariable("INIA_CITATION_TRACE_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride;
        }

        string repoRoot = RepositoryRoot.Find();
        return Path.Combine(repoRoot, TRACES_RELATIVE_PATH);
    }
}
