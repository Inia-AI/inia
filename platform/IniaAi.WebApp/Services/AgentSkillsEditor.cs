using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Pure logic for the Agent block's worker-skills editor: deriving the editable rows from
/// <see cref="AgentProperties.WorkerSkills"/> and shaping the rows back into the persisted list.
/// </summary>
public static class AgentSkillsEditor
{
    public class Entry
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public List<AssetEntry> Assets { get; set; } = [];
    }

    public class AssetEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool FillTags { get; set; }
    }

    public static List<Entry> ToEntries(List<AgentWorkerSkill> workerSkills)
    {
        return [.. workerSkills.Select(static skill => new Entry
        {
            Name = skill.Name,
            Description = skill.Description,
            Content = skill.Content,
            Assets = [.. (skill.Assets ?? []).Select(static asset => new AssetEntry
            {
                Name = asset.Name,
                Content = asset.Content,
                FillTags = asset.FillTags,
            })],
        })];
    }

    public static List<AgentWorkerSkill> BuildWorkerSkills(IEnumerable<Entry> entries)
    {
        var positionByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var skills = new List<AgentWorkerSkill>();

        foreach (Entry entry in entries)
        {
            string name = entry.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            var skill = new AgentWorkerSkill
            {
                Name = name,
                Description = entry.Description?.Trim() ?? string.Empty,
                Content = entry.Content ?? string.Empty,
                Assets = BuildAssets(entry.Assets),
            };

            if (positionByName.TryGetValue(name, out int position))
            {
                skills[position] = skill;
                continue;
            }

            positionByName[name] = skills.Count;
            skills.Add(skill);
        }

        return skills;
    }

    /// <summary>
    /// Asset rows whose trimmed name is blank are dropped, like nameless skill rows; content is
    /// kept verbatim — it is file text, never trimmed. The skill's setter normalizes an empty
    /// result to null, so a skill without asset rows persists without an assets field.
    /// </summary>
    private static List<AgentWorkerSkillAsset> BuildAssets(IEnumerable<AssetEntry> assetEntries)
    {
        return [.. assetEntries
            .Select(static asset => new AgentWorkerSkillAsset
            {
                Name = asset.Name?.Trim() ?? string.Empty,
                Content = asset.Content ?? string.Empty,
                FillTags = asset.FillTags,
            })
            .Where(static asset => asset.Name.Length > 0)];
    }
}
