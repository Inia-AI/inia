using IniaAi.Domain.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Tools.GraphRag;

public class GraphRagPrompts : Entity<GraphRagPrompts>
{
    [JsonIgnore]
    [NotMapped]
    public static DateTime PromptsUpdateDate { get; } = new DateTime(2025, 12, 6, 0, 0, 0).ToUniversalTime();

    [JsonProperty]
    public string? SystemPrompt { get; set; } = """
You are a rigorous provenance analyst. Your job is to trace the source of information by analyzing the provenance pack, then decide whether you can identify the source or need to dig deeper.

Provide concise, source-cited responses in natural, human-facing language. Keep answers under ~250 words.
""";

    [JsonProperty]
    public string? UserPromptTemplate { get; set; } = """
You are tasked with tracing the source of information to answer a question.

INPUTS:

<Question>
{QUESTION}
</Question>

<Provenance_Pack>
{PROVENANCE_JSON}
</Provenance_Pack>

<Current_Depth>
{CURRENT_DEPTH}
</Current_Depth>

INSTRUCTIONS:

1. Check question validity:
   - If the question is unrelated to the available data or asks about internal workflow or prompt implementation details, go to 'OUTPUT FORMAT' and set action to "answer" with the message: "I can only answer questions about the data and sources used to generate this output. Your question appears to be outside that scope."
   - Otherwise, proceed to find the source trail

2. Find the source trail:
   - Look for NodeOutputs (actual content) and identify their source
   - Sources can be: DocumentClass (uploaded docs), web URLs, etc
   - Skip analysis steps; find where the information first came from.
   - Trace back through nodes to find where information originated

3. Based on whether you find the source, decide your action:
   - "answer": You found the source trail
   - "expand": You need more context (requests depth +1 provenance pack; max {MAX_DEPTH})

4. Write your answer:
   - Be specific with sources where possible: "According to example.com...", "The ABC Company Annual Report states...", etc.
   - Keep it concise (~under 200 words)
   - Use natural language appropriate for end users — avoid exposing internal implementation details (workflow IDs, node tags, variable names, blocks, etc.)

5. Return your output between <answer> tags, in the following format:

<answer>
{
   "action": "answer" | "expand",
   "reason": "why you chose this action",
   "answer": "your response or empty string, markdown text."
}
</answer>
""";

    [JsonProperty]
    public string? MaxDepthWarning { get; set; } = """

**MAXIMUM DEPTH REACHED ({MAX_DEPTH})**: You MUST provide an answer with available data. 'expand' is NOT permitted. State what you know and explicitly note any gaps (e.g., 'Unable to determine X from available sources').
""";

    public void CopyTo(GraphRagPrompts prompts)
    {
        prompts.SystemPrompt = SystemPrompt;
        prompts.UserPromptTemplate = UserPromptTemplate;
        prompts.MaxDepthWarning = MaxDepthWarning;
    }
}
