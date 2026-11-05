using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Core.Services.AgentRuntime;

/// <summary>One skill as the run facts name it: its invocation identity and asset files.</summary>
public sealed record AgentRunFactSkill(string Name, IReadOnlyList<string> AssetNames);

/// <summary>
/// The system prompt the platform sends for every Agent and Verification run: a
/// <c>&lt;task_setup&gt;</c> section stating what the run carries, then the block's own system
/// prompt inside <c>&lt;instructions&gt;</c>. XML tags rather than headings, because authored prompts
/// use markdown headings. The runtime puts its own <c>&lt;environment&gt;</c> section above
/// and states the staged documents in the first user turn, so they are not repeated here. Each
/// item appears only when it applies, so a run with nothing to state gets no task setup at all.
/// The editor's preview is rendered by the same code.
/// </summary>
public static class AgentRunFacts
{
    public const string TASK_SETUP_OPEN = "<task_setup>";
    public const string TASK_SETUP_CLOSE = "</task_setup>";
    public const string INSTRUCTIONS_OPEN = "<instructions>";
    public const string INSTRUCTIONS_CLOSE = "</instructions>";
    public const string TASK_SETUP_NOTE = "Added automatically by the system.";
    public const string SKILLS_LABEL = "Skills:";
    public const string SKILL_INVOCATION_PREFIX = "skills:";
    public const string SKILL_FILES_LABEL = "Skill files:";
    public const string OUTPUT_SCHEMA_LINE = "The final answer is checked against an enforced output schema.";

    /// <summary>More skill files than this are listed one per sub-bullet instead of on one line.</summary>
    public const int MAX_INLINE_SKILL_FILES = 3;

    private const string SKILL_ASSETS_ROOT = "$SKILL_ASSETS_DIR";
    private const string ITEM_PREFIX = "- ";
    private const string SUB_ITEM_PREFIX = "  - ";
    private const string LIST_SEPARATOR = ", ";
    private const string LABEL_SEPARATOR = " ";
    private const string LINE_SEPARATOR = "\n";
    private const string SECTION_SEPARATOR = "\n\n";

    /// <summary>
    /// The task setup for a run about to start, read off the request itself so it states exactly
    /// what the request carries. Null when there is nothing to state.
    /// </summary>
    public static string? ForRequest(AgentRunStartRequest request)
    {
        return Render(
            [.. request.SkillFiles.Select(static skill => new AgentRunFactSkill(
                skill.Name, [.. skill.Assets.Select(static asset => asset.Name)]))],
            request.OutputSchema is not null);
    }

    /// <summary>
    /// The request with its system prompt composed as task setup then instructions. A request
    /// with neither keeps a null system prompt, because the runtime refuses a blank one.
    /// </summary>
    public static AgentRunStartRequest WithRunFacts(AgentRunStartRequest request)
    {
        return request with { SystemPrompt = ComposeSystemPrompt(ForRequest(request), request.SystemPrompt) };
    }

    /// <summary>The editor's preview for a block, from its authored skills and assets.</summary>
    public static string? Preview(IEnumerable<AgentWorkerSkill> skills, bool hasOutputSchema)
    {
        return Render(
            [.. skills
                .Where(static skill => !string.IsNullOrWhiteSpace(skill.Name))
                .Select(static skill => new AgentRunFactSkill(
                    AgentSkillNames.Normalize(skill.Name) ?? skill.Name,
                    [.. (skill.Assets ?? []).Select(static asset => asset.Name)]))],
            hasOutputSchema);
    }

    private static string? Render(IReadOnlyList<AgentRunFactSkill> skills, bool hasOutputSchema)
    {
        var items = new List<string>();

        if (skills.Count > 0)
        {
            items.Add(ITEM_PREFIX + SKILLS_LABEL + LABEL_SEPARATOR
                + string.Join(LIST_SEPARATOR, skills.Select(static skill => SKILL_INVOCATION_PREFIX + skill.Name)));
        }

        List<string> assetPaths = [.. skills.SelectMany(static skill => skill.AssetNames
            .Select(asset => $"{SKILL_ASSETS_ROOT}/{skill.Name}/{asset}"))];
        if (assetPaths.Count > MAX_INLINE_SKILL_FILES)
        {
            items.Add(ITEM_PREFIX + SKILL_FILES_LABEL
                + string.Concat(assetPaths.Select(static path => LINE_SEPARATOR + SUB_ITEM_PREFIX + path)));
        }
        else if (assetPaths.Count > 0)
        {
            items.Add(ITEM_PREFIX + SKILL_FILES_LABEL + LABEL_SEPARATOR + string.Join(LIST_SEPARATOR, assetPaths));
        }

        if (hasOutputSchema)
        {
            items.Add(ITEM_PREFIX + OUTPUT_SCHEMA_LINE);
        }

        if (items.Count == 0)
        {
            return null;
        }

        return string.Join(LINE_SEPARATOR, [TASK_SETUP_OPEN, TASK_SETUP_NOTE, .. items, TASK_SETUP_CLOSE]);
    }

    private static string? ComposeSystemPrompt(string? taskSetup, string? authoredSystemPrompt)
    {
        var parts = new List<string>();

        if (taskSetup is not null)
        {
            parts.Add(taskSetup);
        }

        if (!string.IsNullOrWhiteSpace(authoredSystemPrompt))
        {
            parts.Add(INSTRUCTIONS_OPEN + LINE_SEPARATOR + authoredSystemPrompt + LINE_SEPARATOR + INSTRUCTIONS_CLOSE);
        }

        return parts.Count == 0 ? null : string.Join(SECTION_SEPARATOR, parts);
    }
}
