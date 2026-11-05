using IniaAi.Domain.Common;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.DocumentAnalysis;

/// <summary>
/// Prompts of the Build Topics block: finding the topics a document set covers, then grouping its
/// pages under them. The newest row wins at read time.
/// </summary>
public class TopicsPrompts : Entity<TopicsPrompts>
{
    [JsonProperty]
    public string? TopicsAnalysisSystemPrompt { get; set; }

    [JsonProperty]
    public string? TopicsAnalysisUserPrompt { get; set; }

    [JsonProperty]
    public string? TopicGroupingSystemPrompt { get; set; }

    [JsonProperty]
    public string? TopicGroupingUserPrompt { get; set; }

    public void CopyTo(TopicsPrompts prompts)
    {
        prompts.TopicsAnalysisSystemPrompt = TopicsAnalysisSystemPrompt;
        prompts.TopicsAnalysisUserPrompt = TopicsAnalysisUserPrompt;
        prompts.TopicGroupingSystemPrompt = TopicGroupingSystemPrompt;
        prompts.TopicGroupingUserPrompt = TopicGroupingUserPrompt;
    }
}
