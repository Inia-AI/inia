namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Where an agent-template tag sits: a worker skill (its content or its description) or one of the
/// block's prompt fields. <see cref="Description"/> is the words every save-time message uses for it.
/// </summary>
public sealed record AgentTemplateLocation
{
    private AgentTemplateLocation(string? skillName, string description)
    {
        SkillName = skillName;
        Description = description;
    }

    public string? SkillName { get; }

    public string Description { get; }

    public bool IsSkill => SkillName is not null;

    public static AgentTemplateLocation Skill(string skillName)
    {
        return new AgentTemplateLocation(skillName, $"skill '{skillName}'");
    }

    public static AgentTemplateLocation Prompt(string promptField)
    {
        return new AgentTemplateLocation(null, promptField);
    }
}
