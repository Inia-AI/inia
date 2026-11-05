using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Application.UnitTests.Common.Workflows;

internal static class SharedComponentLinkTestData
{
    internal static string LinkedWorkflowJson()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        var block = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = "Who is the counterparty?" },
        };
        block.SharedComponentId = "component-1";
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        block.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "block1" };

        return new WorkflowCanonicalizer().ToCanonicalJson(workflow);
    }

    internal static string SectionLinkedWorkflowJson()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = "Who is the counterparty?" },
        };
        section.SharedComponentId = "component-1";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = new string('a', 64);
        section.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "block1" };

        return new WorkflowCanonicalizer().ToCanonicalJson(workflow);
    }

    internal static Section DefSection(string title, params (string Tag, string Prompt)[] blocks)
    {
        return DefSection(title, new Workflow("Scratch"), blocks);
    }

    internal static Section DefSection(string title, Workflow workflow, params (string Tag, string Prompt)[] blocks)
    {
        var section = new Section(title, workflow) { Title = title };
        foreach ((string tag, string prompt) in blocks)
        {
            _ = new Block(BlockTypes.AiQuery, section, tag)
            {
                Name = "Node",
                Properties = new AiQueryProperties { UserPromptTemplate = prompt },
            };
        }

        return section;
    }

    internal static Block BuildBlockDefinition(string prompt)
    {
        var scratch = new Workflow("Scratch");
        var section = new Section("Scratch", scratch) { Title = "Scratch" };

        return new Block(BlockTypes.AiQuery, section, "definition")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    internal static Block LinkedBlock(
        Section section,
        string tag,
        string prompt,
        string componentId,
        string versionHash,
        SharedComponentLinkState linkState = SharedComponentLinkState.Linked)
    {
        var block = new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = linkState;
        block.SharedComponentVersionHash = versionHash;

        return block;
    }

    internal static Section RefSection(string title, string tag, string refTargetId)
    {
        Section section = DefSection(title, (tag, "a"));
        Block block = section.Blocks.Single();
        block.SharedComponentId = refTargetId;
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        block.SharedComponentBindings = new Dictionary<string, string>();

        return section;
    }
}
