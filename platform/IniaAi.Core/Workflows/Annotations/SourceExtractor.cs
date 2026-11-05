using IniaAi.Common;

namespace IniaAi.Core.Workflows.Annotations;

public interface ISourceExtractor
{
    List<string> ExtractSources(string rawResponse);
}

public class SourceExtractor : ISourceExtractor
{
    public List<string> ExtractSources(string rawResponse)
    {
        if (string.IsNullOrEmpty(rawResponse))
        {
            return [];
        }

        string? sourcesString = Utils.GetStringFromTag(
            rawResponse,
            "source",
            processMultipleTags: true);

        if (string.IsNullOrEmpty(sourcesString))
        {
            return [];
        }

        List<string> sources = [.. sourcesString
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))];

        return sources;
    }
}
