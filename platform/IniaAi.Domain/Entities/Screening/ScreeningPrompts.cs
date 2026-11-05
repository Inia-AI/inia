using IniaAi.Domain.Common;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Screening;

/// <summary>
/// Prompt set of the Document Intake Screening judge. The newest row wins at read time. The table
/// stays in every build; the defaults and their seeding belong to the screening module.
/// </summary>
public class ScreeningPrompts : Entity<ScreeningPrompts>
{
    [JsonProperty]
    public string? JudgeSystemPrompt { get; set; }

    [JsonProperty]
    public string? JudgePrompt { get; set; }

    public void CopyTo(ScreeningPrompts prompts)
    {
        prompts.JudgeSystemPrompt = JudgeSystemPrompt;
        prompts.JudgePrompt = JudgePrompt;
    }
}
