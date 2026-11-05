using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class WorkflowSeparatorInvariantTests
{
    [Fact]
    public void IsolateBlockWithDependencies_PreservesSeparators()
    {
        var root = new Workflow("root");
        var sectionA = new Section("a", root);
        var keptBlock = new Block(BlockTypes.Merge, sectionA)
        {
            ReplacementTag = "kept",
        };
        var separator = new Section("sep", root)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };
        _ = new Section("b", root);

        root.IsolateBlockWithDependencies(keptBlock);

        Assert.Contains(separator, root.Sections);
        Assert.Contains(sectionA, root.Sections);
    }
}
