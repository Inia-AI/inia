using IniaAi.Domain.Entities.DocumentAnalysis;

namespace IniaAi.Domain.DocumentAnalysis;

/// <summary>
/// The Build Topics prompts a deployment starts with, edited afterwards in Tools Settings. Topic
/// grouping has no system prompt.
/// </summary>
public static class TopicsPromptsDefaults
{
    public static DateTime PromptsUpdateDate { get; } = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    public const string TOPICS_ANALYSIS_SYSTEM_PROMPT =
"""
You are an AI assistant specializing in document segmentation and topic identification.
""";

    public const string TOPICS_ANALYSIS_USER_PROMPT =
"""
You are tasked with segmenting the pages of a document by topic for semantic search.

You will be provided with overviews of each page in the document. Your goal is to identify broad topics, refine them, and create short labels for each topic.

Follow these steps:
1. Read the full page_overviews text and identify broad topics. Use a <scratchpad> to record your thoughts and the list of topics as you read.

2. Read the overviews again and refine the topics, ensuring the segments have common themes. Update your list in the <scratchpad>.

3. Create a short, descriptive label (10 words or less) for each topic. Add these labels to your <scratchpad>.

4. Add one additional topic that would be used later to include all pages with tables.

5. Provide your final output as a list of topics. Each topic should be on a new line and start with a hyphen (-).

6. Use <answer> tags to present the final list of topics.  Keep <scratchpad> and other notes outside the <answer> tags.
""";

    public const string TOPIC_GROUPING_USER_PROMPT =
"""
Map each page to the corresponding topic(s) based on the provided summaries.

<topics>
{0}
</topics>

Follow these steps:
1. Read through each page overview carefully.

2. For each page:
    - Identify which topic(s) best match the content of the page.
    - Consider both primary and secondary topics if applicable.
    - If a page is blank, do not assign it to any topic.
    - If a page belongs to multiple topics, include it under each relevant topic

3. Create a mapping showing which pages belong to each topic.

4. Before providing your final answer, wrap your analysis process in <analysis> tags. In your analysis, make sure to explicitly extract only the numerical page numbers without including the word "page".

5. Critique the mapping of pages you have generated in you analysis and ensure that you have captured the relevant pages accurately.

6. Provide your final response within <answer> tags in the following format:

<answer>
TOPIC: [Topic Label]
Pages: [list of page numbers]

(continue for all topics)
</answer>
""";

    public static TopicsPrompts Create()
    {
        return new TopicsPrompts
        {
            CreatedAt = PromptsUpdateDate,
            TopicsAnalysisSystemPrompt = TOPICS_ANALYSIS_SYSTEM_PROMPT,
            TopicsAnalysisUserPrompt = TOPICS_ANALYSIS_USER_PROMPT,
            TopicGroupingUserPrompt = TOPIC_GROUPING_USER_PROMPT,
        };
    }
}
