using IniaAi.Domain.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Tools.Reconciliation;

public class ReconciliationPrompts : Entity<ReconciliationPrompts>
{
    [JsonIgnore]
    [NotMapped]
    public static DateTime PromptsUpdateDate { get; } = new DateTime(2025, 10, 17, 11, 0, 0).ToUniversalTime();

    [JsonProperty]
    public string? SystemPrompt { get; set; }

    [JsonProperty]
    public string FindMatchPrompt { get; set; } =
    $$"""
    Here is the identified name of a counterparty:

    <identifiedName>
    {0}
    </identifiedName>

    And here is the list of names that could potentially match to the identified name:

    <potentialNames>
    {1}
    </potentialNames>

    Carefully review the list and select the name that closely matches or most resembles the identified counterparty. If none of the names match answer with NA.

    Provide your answer in the following format:

    <answer>
    [Name from the list or NA]
    </answer>
    """;

    [JsonProperty]
    public string HedgeFundsMonitoringFindMatchPrompt { get; set; } = string.Empty;

    [JsonProperty]
    public string CompareListsPrompt { get; set; } =
    """
    Your task is to determine if two lists contain similar items.

    INPUTS:
    <list_A>
    {0}
    </list_A>

    <list_B>
    {1}
    </list_B>

    CONTEXT:
    Lists may be incomplete and names may vary slightly between systems

    MATCHING CRITERIA:
    Consider two items as matching if they share:
    The same core distinctive terms (ignore common words like "Fund", "LP", "Ltd")
    Similar abbreviations (e.g., "Intl" vs "International")
    Minor spelling variations or punctuation differences

    DECISION RULES:
    Answer YES only if:
    At least 2 items match between lists (or 50% if fewer than 4 total items), AND
    The matching items are sufficiently distinctive

    Answer NO if:
    Either list is empty
    No clear matches exist

    OUTPUT:
    <reasoning>
    [List specific item matches found and explain why they are/aren't sufficient]
    </reasoning>

    <answer>
    [Yes or No]
    </answer>
    """;

    [JsonProperty]
    public string HedgeFundsMonitoringCompareListsPrompt { get; set; } = string.Empty;

    public void CopyTo(ReconciliationPrompts prompts)
    {
        prompts.SystemPrompt = SystemPrompt;
        prompts.FindMatchPrompt = FindMatchPrompt;
        prompts.HedgeFundsMonitoringFindMatchPrompt = HedgeFundsMonitoringFindMatchPrompt;
        prompts.CompareListsPrompt = CompareListsPrompt;
        prompts.HedgeFundsMonitoringCompareListsPrompt = HedgeFundsMonitoringCompareListsPrompt;
    }
}
