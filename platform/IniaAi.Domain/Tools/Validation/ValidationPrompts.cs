using IniaAi.Domain.Common;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tools.Validation;

/// <summary>
/// Prompt set for the validation engine's two model roles: single-document schema extraction
/// and per-field prose equivalence judging. The newest row wins at read time. The table stays in
/// every build; the defaults and their seeding belong to the validation module.
/// </summary>
public class ValidationPrompts : Entity<ValidationPrompts>
{
    [JsonProperty]
    public string? ExtractionSystemPrompt { get; set; }

    [JsonProperty]
    public string? ExtractionPrompt { get; set; }

    [JsonProperty]
    public string? JudgeSystemPrompt { get; set; }

    [JsonProperty]
    public string? JudgePrompt { get; set; }

    public void CopyTo(ValidationPrompts prompts)
    {
        prompts.ExtractionSystemPrompt = ExtractionSystemPrompt;
        prompts.ExtractionPrompt = ExtractionPrompt;
        prompts.JudgeSystemPrompt = JudgeSystemPrompt;
        prompts.JudgePrompt = JudgePrompt;
    }
}
