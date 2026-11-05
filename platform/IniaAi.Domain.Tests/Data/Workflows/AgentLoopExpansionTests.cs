using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class AgentLoopExpansionTests
{
    private const string LIST_TITLE = "items";
    private const string SKILL_NAME = "per-item";
    private const string JSON_LITERAL = """Answer as {"item": "<word>"} and keep {facts} literal.""";

    [Fact]
    public void ProcessLoops_AgentInTheLoopBody_ReadsItsOwnIterationsItemAndSibling()
    {
        (Workflow workflow, _) = BuildLoopWorkflow();

        Section looped = workflow.Sections.First();
        looped.ProcessLoops([]);

        AgentProperties first = AgentProps(looped, $"{LIST_TITLE}_echo_0");
        AgentProperties second = AgentProps(looped, $"{LIST_TITLE}_echo_1");

        Assert.Contains("Item: {{items_0}}", first.WorkerSkills[0].Content);
        Assert.Contains("Facts: {{items_facts_0.answer}}", first.WorkerSkills[0].Content);
        Assert.Contains("Item: {{items_1}}", second.WorkerSkills[0].Content);
        Assert.Contains("Facts: {{items_facts_1.answer}}", second.WorkerSkills[0].Content);
        Assert.Equal("Echo {{items_0}}.", first.PromptTemplate);
        Assert.Equal("Echo {{items_1}}.", second.PromptTemplate);
        Assert.Equal("Item {{items_1}} description.", second.WorkerSkills[0].Description);
        Assert.Equal("System for {{items_1}}.", second.SystemPromptTemplate);
    }

    [Fact]
    public void ProcessLoops_LeavesSingleBraceTextAndStrategyTagsInAgentSkillsAsWritten()
    {
        (Workflow workflow, _) = BuildLoopWorkflow();

        Section looped = workflow.Sections.First();
        looped.ProcessLoops([]);

        string content = AgentProps(looped, $"{LIST_TITLE}_echo_0").WorkerSkills[0].Content;
        Assert.Contains(JSON_LITERAL, content);
        Assert.Contains("{{Verifier.findings}}", content);
        Assert.Contains("{{upstream}}", content);
        Assert.Equal("print('{{items}}')", AgentProps(looped, $"{LIST_TITLE}_echo_0").WorkerSkills[0].Assets![0].Content);
    }

    [Fact]
    public void ProcessLoops_AgentAfterTheLoop_ReceivesEveryIterationOfALoopedProducer()
    {
        (Workflow workflow, Section later) = BuildLoopWorkflow();

        Section looped = workflow.Sections.First();
        looped.ProcessLoops([]);

        AgentProperties sameSection = AgentProps(looped, "collector");
        AgentProperties laterSection = AgentProps(later, "later_collector");

        Assert.Contains("{{items_echo_0}}\n{{items_echo_1}}", sameSection.WorkerSkills[0].Content);
        Assert.Contains("{{items_echo_0}}\n{{items_echo_1}}", laterSection.WorkerSkills[0].Content);
    }

    [Fact]
    public void ProcessLoops_EachAgentCopy_HasItsOwnBlockIdAndRegressionIdentity()
    {
        (Workflow workflow, _) = BuildLoopWorkflow();

        Section looped = workflow.Sections.First();
        looped.ProcessLoops([]);

        Block first = looped.Blocks.Single(b => b.ReplacementTag == $"{LIST_TITLE}_echo_0");
        Block second = looped.Blocks.Single(b => b.ReplacementTag == $"{LIST_TITLE}_echo_1");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal($"echo-stable_{LIST_TITLE}_0", first.StableBlockId);
        Assert.Equal($"echo-stable_{LIST_TITLE}_1", second.StableBlockId);
    }

    [Fact]
    public void ProcessLoops_EachAgentCopy_DependsOnItsOwnIterationsTags()
    {
        (Workflow workflow, _) = BuildLoopWorkflow();

        Section looped = workflow.Sections.First();
        looped.ProcessLoops([]);

        List<string> dependencies = looped.Blocks
            .Single(b => b.ReplacementTag == $"{LIST_TITLE}_echo_1")
            .GetDependencyReplacementTags();

        Assert.Contains("items_1", dependencies);
        Assert.Contains("items_facts_1", dependencies);
        Assert.DoesNotContain("items_0", dependencies);
        Assert.DoesNotContain(LIST_TITLE, dependencies);
    }

    [Fact]
    public void RenameTagReferences_OutsideExpansion_StillLeavesAgentSkillsAlone()
    {
        (Workflow workflow, _) = BuildLoopWorkflow();
        Section looped = workflow.Sections.First();

        looped.RenameTagReferences(new Dictionary<string, string> { ["upstream"] = "renamed" });

        Assert.Contains("{{upstream}}", AgentProps(looped, "echo").WorkerSkills[0].Content);
    }

    private static AgentProperties AgentProps(Section section, string replacementTag)
    {
        return (AgentProperties)section.Blocks.Single(b => b.ReplacementTag == replacementTag).Properties;
    }

    private static Block Agent(Section section, string tag, string content, string prompt = "", string description = "Does the work.", string systemPrompt = "")
    {
        var block = new Block(BlockTypes.Agent, section, tag);
        var props = (AgentProperties)block.Properties;
        props.WorkerSkills = [new AgentWorkerSkill
        {
            Name = SKILL_NAME,
            Description = description,
            Content = content,
            Assets = [new AgentWorkerSkillAsset { Name = "helper.py", Content = "print('{{items}}')" }],
        }];
        props.PromptTemplate = prompt;
        props.SystemPromptTemplate = systemPrompt;
        return block;
    }

    private static (Workflow Workflow, Section Later) BuildLoopWorkflow()
    {
        var workflow = new Workflow("root");
        var section = new Section("Loop", workflow) { Title = "Loop", Order = 0 };

        var loopStart = new Block(BlockTypes.LoopStart, section)
        {
            Properties = new LoopStartProperties { ListTitle = LIST_TITLE, LoopedListTemplate = "alpha\nbravo" },
        };
        loopStart.StableBlockId = "loopstart";

        _ = new Block(BlockTypes.Merge, section, "facts")
        {
            Properties = new MergeProperties { TextTemplate = "Facts about {items}" },
        };

        Block echo = Agent(
            section,
            "echo",
            $"Item: {{{{items}}}}\nFacts: {{{{facts.answer}}}}\nOutside: {{{{upstream}}}}\n{JSON_LITERAL}\nStrategy: {{{{Verifier.findings}}}}",
            prompt: "Echo {{items}}.",
            description: "Item {{items}} description.",
            systemPrompt: "System for {{items}}.");
        echo.StableBlockId = "echo-stable";

        _ = new Block(BlockTypes.LoopEnd, section);

        _ = Agent(section, "collector", "All items:\n{{echo}}");

        var later = new Section("Later", workflow) { Title = "Later", Order = 1 };
        _ = Agent(later, "later_collector", "All items:\n{{echo}}");

        return (workflow, later);
    }
}
