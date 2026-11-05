using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.Workflows;

internal static class AgentTestData
{
    public const string MODEL = "test-model-a";
    public const string SKILL_NAME = "extraction";
    public const string SKILL_DESCRIPTION = "Extracts the things. Use when the task asks for things.";
    public const string SKILL_CONTENT = "Extract the things.";
    public const string PARAMETERIZED_CONFIG_JSON = """{"Variants":[{"Params":{"Region":"EU"}}]}""";

    public static AgentWorkerSkill Skill(
        string name = SKILL_NAME,
        string content = SKILL_CONTENT,
        string description = SKILL_DESCRIPTION)
    {
        return new AgentWorkerSkill { Name = name, Description = description, Content = content };
    }

    public static void ReplaceSkill(
        AgentProperties props,
        string name,
        string content,
        string description = SKILL_DESCRIPTION)
    {
        _ = props.WorkerSkills.RemoveAll(skill => string.Equals(skill.Name, name, StringComparison.Ordinal));
        props.WorkerSkills.Add(Skill(name, content, description));
    }

    public static string SkillContent(AgentProperties props, string name)
    {
        return props.WorkerSkills.Single(skill => string.Equals(skill.Name, name, StringComparison.Ordinal)).Content;
    }

    public const decimal LIMIT_DEADLINE_CEILING_MS = 900_000m;
    public const decimal LIMIT_BUDGET_CEILING_USD = 2.50m;
    public const decimal LIMIT_TURNS_CEILING = 300m;
    public const decimal LIMIT_RETAINED_CEILING_BYTES = 276_824_064m;

    public static AgentRunLimits RuntimeLimits(
        decimal deadlineCeilingMs = LIMIT_DEADLINE_CEILING_MS,
        decimal budgetCeilingUsd = LIMIT_BUDGET_CEILING_USD,
        decimal turnsCeiling = LIMIT_TURNS_CEILING,
        decimal retainedCeilingBytes = LIMIT_RETAINED_CEILING_BYTES)
    {
        return new AgentRunLimits(
            new AgentRunFuseLimit(deadlineCeilingMs, deadlineCeilingMs, "milliseconds"),
            new AgentRunFuseLimit(budgetCeilingUsd / 2, budgetCeilingUsd, "usd"),
            new AgentRunFuseLimit(turnsCeiling, turnsCeiling, "turns"),
            new AgentRunFuseLimit(retainedCeilingBytes, retainedCeilingBytes, "bytes"));
    }

    public sealed class FixedLimitsProvider : IAgentRunLimitsProvider
    {
        private readonly AgentRunLimits? _limits;

        public FixedLimitsProvider(AgentRunLimits? limits)
        {
            _limits = limits;
        }

        public AgentRunLimitsSnapshot? LastKnown =>
            _limits is null ? null : new AgentRunLimitsSnapshot(_limits, DateTimeOffset.UtcNow);

        public AgentRunLimits? Enforceable => _limits;
    }

    public static AgentRuntimeSettings EnabledRuntime(params string[] allowedModels)
    {
        return new AgentRuntimeSettings
        {
            Enabled = true,
            AllowedModels = allowedModels.Length == 0 ? [MODEL] : [.. allowedModels],
        };
    }

    public static Block AddAgent(
        Section section,
        string replacementTag,
        string skillContent = SKILL_CONTENT,
        string? model = MODEL)
    {
        var agent = new Block(BlockTypes.Agent, section, replacementTag);
        var props = (AgentProperties)agent.Properties;
        props.WorkerSkills.Add(Skill(SKILL_NAME, skillContent));
        props.Model = model;

        return agent;
    }

    public static Block AddVerifier(Section section, string tag, params string[] memberTags)
    {
        var verifier = new Block(BlockTypes.Verification, section, tag);
        var props = (VerificationProperties)verifier.Properties;
        props.Members = [.. memberTags.Select(static memberTag => new VerificationMember { MemberTag = memberTag })];
        props.Criteria =
        [
            new VerificationCriterion
            {
                Id = "totals-tie-out",
                Label = "Totals tie out to the source",
                AppliesTo = memberTags.Length == 0 ? [] : [memberTags[0]],
            },
        ];
        props.Model = MODEL;
        props.DeadlineMinutes = 10;
        props.MaxBudgetUsd = 2m;

        return verifier;
    }

    public static Block AgentBlockDefinition(string skillContent = SKILL_CONTENT, string model = MODEL)
    {
        var scratch = new Workflow("Scratch");
        var section = new Section("Scratch", scratch) { Title = "Scratch" };

        return AddAgent(section, "extraction_agent", skillContent, model);
    }

    public static Section AgentSection(bool parameterized)
    {
        var scratch = new Workflow("Scratch");
        var section = new Section("Agents", scratch) { Title = "Agents" };
        if (parameterized)
        {
            section.ParameterizedConfig = PARAMETERIZED_CONFIG_JSON;
        }

        _ = AddAgent(section, "extraction_agent");

        return section;
    }
}
