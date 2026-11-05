using IniaAi.Domain.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Tools.RagStrategies;

public class UseAutoDetectedTableOfContentsPrompts : Entity<UseAutoDetectedTableOfContentsPrompts>
{
    [JsonIgnore]
    [NotMapped]
    public static DateTime PromptsUpdateDate { get; } = new DateTime(2025, 3, 20, 14, 0, 0).ToUniversalTime();

    [JsonProperty]
    public string RelevantSectionsIdentificationPrompt { get; set; } =
    """
    You are an AI assistant tasked with identifying the most relevant sections from a document to answer a user's query. Follow these instructions carefully:

    1. You will be given a table of contents from a document in the following format:
    <table_of_contents>
    {0}
    </table_of_contents>

    2. You will also be given a user query:
    <user_query>
    {1}
    </user_query>

    3. Analyze the user query and the table of contents. Consider the following:
    - Key words or phrases in the query that match or relate to section names
    - The overall subject matter of the query and how it relates to the sections
    - Any specific information requested in the query (e.g., numerical data, dates, names)

    4. Identify the most relevant sections to answer the query.
        - You may select multiple sections if necessary.

    5. Provide your answer in the following JSON format:

    {{
        "relevantSections": [
            {{
                "sectionName": "string",
                "pageNumbers": [
                    "integer"
                ],
                "relevanceScore": "integer (1-10)",
                "relevanceJustification": "string"
            }}
        ],
        "analysisDetails": {{
            "queryKeywords": [
                "string"
            ],
            "overallReasoning": "string",
            "sectionRelationships": "string (explain how sections work together if multiple)",
            "confidenceLevel": "integer (1-10)"
        }}
    }}

    6. Ensure your response includes:
    - All relevant sections with their respective page numbers from the first page of the relevant section to the last, including the first page of the section that follows it
    - Clear justification for each section's selection
    - How multiple sections complement each other (if applicable)
    - A confidence score for your analysis

    Remember to be thorough yet precise in your explanations while maintaining the JSON structure.
    """;

    [JsonProperty]
    public string RelevanceAssessmentPrompt { get; set; } =
    """
    You will be provided with pages of a document.  Your task is to find the most relevant page(s) from a document to answer a given query.

    Here are the pages of the document:
    <document>
    {0}
    </document>

    Here is the user query you need to consider:
    <user_query>
    {1}
    </user_query>

    Follow these steps to complete the task:

    1. Examine the provided text carefully. Each page is demarcated by Inia labels in the format:
    ""<page[number]>" " at the start of the page and
    ""</page[number]>" " at the end of the page

    2. For each page, assess its content in relation to the user query.

    3. Estimate the probability that each page can be used to answer the query.

    4. Present your findings in a clear, organized list or table format. For each page, include:
    - The Inia label number (X from the page markers)
    - A probability estimate (as a percentage) indicating how likely the page can answer the query

    6. Ensure that your analysis includes ALL pages from the document, even if some have low relevance.

    7. Enclose your final answer, including the list or table of findings, within <answer> tags without including the word "page".

    Remember to focus on the content's relevance to the specific user query when making your assessments.
    """;

    [JsonProperty]
    public string PageSelectionPrompt { get; set; } =
    """
    You are an advanced document analysis system tasked with identifying the most relevant pages from a document based on probability scores. Your input is a structured response containing summaries of various pages with their corresponding probability scores.

    Here is the summaries response:

    <summaries_response>
    {0}
    </summaries_response>

    Your task is to extract the page numbers of the most relevant pages, adhering to the following criteria:
    1. Only consider pages with a probability score greater than 60%.
    2. Limit the selection to a maximum of 20 pages.
    3. If more than 20 pages have scores above 60%, select the top 20 highest-scoring pages.

    Please follow these steps to complete the task:

    1. Analyze the summaries_response.
    2. Create a structured list of pages with their probability scores.
    3. Filter out pages with probability scores of 60% or less.
    4. Sort the remaining pages by their probability scores in descending order.
    5. Select the top 20 pages (or all pages if fewer than 20 remain).
    6. Extract the page numbers of the identified pages.
    7. Format the output as a comma-separated list of page numbers.

    Before providing your final answer, wrap your analysis process in <analysis> tags. In your analysis, make sure to explicitly extract only the numerical page numbers without including the word "page".

    After your analysis, provide your response in the following format:

    <answer>
    [page_number],[page_number],[page_number]
    </answer>

    Example of the desired output format (using generic numbers):
    <answer>
    5,12,18
    </answer>

    Important:
    - Include only the page numbers, separated by commas.
    - Do not include any additional text, explanations, or the probability scores themselves.
    - Ensure that you output only the numerical page numbers (e.g., 1 instead of "page 1").

    Please proceed with your analysis and provide the list of relevant page numbers.
    """;

    public void CopyTo(UseAutoDetectedTableOfContentsPrompts prompts)
    {
        prompts.RelevantSectionsIdentificationPrompt = RelevantSectionsIdentificationPrompt;
        prompts.RelevanceAssessmentPrompt = RelevanceAssessmentPrompt;
        prompts.PageSelectionPrompt = PageSelectionPrompt;
    }
}
