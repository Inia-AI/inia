using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.DocumentAnalysis;

/// <summary>
/// The standard page overview prompts, offered in the editor as a starting point for a workflow's
/// default. A workflow uses them only when they are copied into it.
/// </summary>
public static class PageOverviewPromptDefaults
{
    public const string PAGES_SYSTEM_PROMPT =
"""
You are a financial document analyst.
""";

    public const string PAGES_USER_PROMPT =
"""
For the provided page {0} content (which includes the main page and surrounding pages for context), generate a concise summary in this exact format:

<answer>
Page {0}:
- **Page Title/Header:** [Extract the main title or header of the page, or "None" if not present.]
- **Page Description:** [1-2 sentences summarizing the key content. For empty/blank pages, use only: empty page]
- **Financial Statement Notes:** [list note headers like "Note 1:" or "Note 2 - Revenue" as "Potential Note Start: [text]" OR omit section]
- **Parent Company Information:** ["Parent Mentioned: Yes/No, Name: [name/Unknown]" OR omit section]
- **Relevant Keywords:** [3-5 key terms separated by commas]
</answer>

Be strictly factual based on the provided content.
""";

    public const string SINGLE_PAGE_SYSTEM_PROMPT =
"""
You are a document analysis assistant. Your task is to generate a concise overview of the provided content.
Focus on extracting key information without assuming any context from surrounding pages.
""";

    public const string SINGLE_PAGE_USER_PROMPT =
"""
Analyze the content below and provide a structured overview.

<content>
{0}
</content>

Respond with a concise overview in the following format:
<answer>Page {0}: [Your overview here - include title, key topics, and important data points]</answer>
""";

    public static PageOverviewPrompts Create()
    {
        return new PageOverviewPrompts
        {
            Pages = new PageOverviewPrompt { SystemPrompt = PAGES_SYSTEM_PROMPT, UserPrompt = PAGES_USER_PROMPT },
            SinglePage = new PageOverviewPrompt { SystemPrompt = SINGLE_PAGE_SYSTEM_PROMPT, UserPrompt = SINGLE_PAGE_USER_PROMPT },
        };
    }
}
