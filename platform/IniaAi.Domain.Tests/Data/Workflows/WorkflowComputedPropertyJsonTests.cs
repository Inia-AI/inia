using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class WorkflowComputedPropertyJsonTests
{
    [Fact]
    public void Serialize_DoesNotEmitDerivedBlockAndSectionFlags()
    {
        var root = new Workflow("root");
        var section = new Section("s", root, 0) { Title = "S" };
        section.Add(new Block { Type = BlockTypes.LoopStart, IsOutputSource = true });

        string json = root.ToJson(pretty: false);

        Assert.DoesNotContain("IsLoopBoundary", json);
        Assert.DoesNotContain("HasOutputBlocks", json);
    }
}
