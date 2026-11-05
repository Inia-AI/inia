namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The labels the editor shows for an agent-family block's two prompt fields. Save-time refusals
/// name a field with the same words, so an author finds it by what the screen says.
/// </summary>
public static class AgentPromptFieldLabels
{
    public const string TASK_PROMPT = "Task prompt";
    public const string SYSTEM_PROMPT_OVERRIDE = "System prompt override";
    public const string EXTRA_JUDGING_INSTRUCTIONS = "Extra judging instructions";
    public const string EXTRA_SYSTEM_PROMPT = "Extra system prompt";
}
