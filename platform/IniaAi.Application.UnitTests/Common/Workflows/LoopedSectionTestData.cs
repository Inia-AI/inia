using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.Workflows;

internal static class LoopedSectionTestData
{
    internal static Section AddLoopedSection(Workflow workflow, string producerTag)
    {
        var looped = new Section("Looped", workflow) { Order = 5 };
        _ = new Block(BlockTypes.LoopStart, looped)
        {
            Properties = new LoopStartProperties
            {
                ListTitle = "Funds",
                LoopedListTemplate = "Fund A\nFund B",
            },
        };
        _ = new Block(BlockTypes.Merge, looped, replacementTag: producerTag)
        {
            Properties = new MergeProperties { TextTemplate = "looped" },
        };
        _ = new Block(BlockTypes.LoopEnd, looped);
        return looped;
    }

    /// <summary>
    /// Inserts an agent block between the producer and the LoopEnd, inside the order-bounded
    /// loop body — appending would land it after LoopEnd, outside the loop.
    /// </summary>
    internal static Block AddAgentInsideLoopBody(Section looped, string replacementTag)
    {
        var agent = new Block(BlockTypes.Agent, looped, index: 2, replacementTag);
        var props = (AgentProperties)agent.Properties;
        props.WorkerSkills.Add(AgentTestData.Skill("extraction", "Extract the things."));
        props.Model = "test-model-a";
        return agent;
    }
}
