using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Graphs;

public class WorkflowGraphBuilderTests
{
    [Fact]
    public void Build_Should_Create_Nodes_And_Reference_Edge()
    {
        // Arrange
        var workflow = new Workflow
        {
            Title = "Test Workflow"
        };

        var variable = new WorkflowVariable(workflow, "A");

        var section = new Section("Section 1", workflow);

        var block = new Block(BlockTypes.Merge, section)
        {
            Id = "B",
            Name = "Block B",
            Order = 1,
            ReplacementTag = string.Empty,
            Properties = new MergeProperties
            {
                TextTemplate = "{A.answer}",
            },
        };
        var builder = new WorkflowGraphBuilder();

        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { block } }
        };

        //Act
        WorkflowGraph graph = builder.Build(allBlocks, [variable]);

        //Assert
        Assert.Contains(graph.Nodes, n => n.Kind is "input_variable" && n.Id == "A");
        Assert.Contains(graph.Nodes, n => n.Kind is "block" && n.Id == "B");

        string expectedTarget = string.IsNullOrWhiteSpace(block.ReplacementTag) ? block.Id : block.ReplacementTag;
        Assert.Contains(graph.Edges, e => e.SourceId == "A" && e.TargetId == expectedTarget && e.Label == "A.answer");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_AMembersReader_WaitsForTheVerifier_OnlyWhenVerificationIsInstalled(bool verificationInstalled)
    {
        var workflow = new Workflow { Title = "Verification group" };
        var section = new Section("Section 1", workflow);
        var member = new Block(BlockTypes.Merge, section)
        {
            Id = "MEMBER",
            ReplacementTag = "revenue",
            Properties = new MergeProperties { TextTemplate = "Revenue." },
        };
        var verifier = new Block(BlockTypes.Verification, section)
        {
            Id = "VERIFIER",
            ReplacementTag = "verify",
            Properties = new VerificationProperties { Members = [new VerificationMember { MemberTag = "revenue" }] },
        };
        var reader = new Block(BlockTypes.Merge, section)
        {
            Id = "READER",
            ReplacementTag = "reader",
            Properties = new MergeProperties { TextTemplate = "{revenue.answer}" },
        };
        var installed = new InstalledBlockTypes(verificationInstalled ? [BlockTypes.Verification] : []);

        WorkflowGraph graph = new WorkflowGraphBuilder(installed).Build(
            new Dictionary<int, List<Block>> { { 1, [member, verifier, reader] } },
            []);

        Assert.Contains(graph.Edges, e => e.SourceId == member.Id && e.TargetId == reader.Id);
        Assert.Equal(
            verificationInstalled,
            graph.Edges.Any(e => e.SourceId == verifier.Id && e.TargetId == reader.Id));
    }

    [Fact]
    public void Build_Keys_Block_Nodes_By_Id_And_Preserves_ReplacementTag()
    {
        // Block nodes are keyed by block.Id (not ReplacementTag) so two blocks sharing a tag stay
        // distinct nodes; the tag is preserved on the node and used only for edge resolution.
        // Arrange
        var workflow = new Workflow { Title = "Test Workflow" };
        var variable = new WorkflowVariable(workflow, "A");
        var section = new Section("Section 1", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Id = "B",
            Name = "Block B",
            Order = 1,
            ReplacementTag = "BLOCK_TAG",
            Properties = new MergeProperties
            {
                TextTemplate = "{A.answer}",
            },
        };
        var builder = new WorkflowGraphBuilder();

        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { block } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, [variable]);

        // Assert
        Assert.Contains(graph.Nodes, n => n.Kind is "input_variable" && n.Id == "A");
        Assert.Contains(graph.Nodes, n => n.Kind is "block" && n.Id == "B" && n.ReplacementTag == "BLOCK_TAG");
        Assert.Contains(graph.Edges, e => e.SourceId == "A" && e.TargetId == "B" && e.Label == "A.answer");
    }

    [Fact]
    public void Build_AgentSkillOnlyReference_CreatesEdgeFromProducerToAgentBlock()
    {
        var workflow = new Workflow { Title = "Agent Graph" };
        var section = new Section("Section 1", workflow);
        var upstream = new Block(BlockTypes.Merge, section)
        {
            Id = "UPSTREAM",
            Name = "Upstream",
            Order = 0,
            ReplacementTag = "upstream_tag",
            Properties = new MergeProperties { TextTemplate = "base" },
        };
        var agent = new Block(BlockTypes.Agent, section)
        {
            Id = "AGENT",
            Name = "Agent",
            Order = 1,
            ReplacementTag = "extraction_agent",
        };
        ((AgentProperties)agent.Properties).WorkerSkills.Add(new AgentWorkerSkill
        {
            Name = "extraction",
            Description = "Extracts figures. Use when the task asks for totals.",
            Content = "Read {{upstream_tag.answer}}; single-brace {inert} stays out.",
        });

        WorkflowGraph graph = new WorkflowGraphBuilder().Build(
            new Dictionary<int, List<Block>> { { 1, [upstream, agent] } },
            []);

        Assert.Contains(graph.Edges,
            e => e.SourceId == "UPSTREAM" && e.TargetId == "AGENT" && e.Label == "upstream_tag.answer");
        Assert.DoesNotContain(graph.Nodes, n => n.Id == "inert");
    }

    [Fact]
    public void Build_AgentSkillStrategyNamespace_CreatesNoNodeOrEdge()
    {
        var workflow = new Workflow { Title = "Agent Graph" };
        var section = new Section("Section 1", workflow);
        var agent = new Block(BlockTypes.Agent, section)
        {
            Id = "AGENT",
            Name = "Agent",
            Order = 0,
            ReplacementTag = "extraction_agent",
        };
        ((AgentProperties)agent.Properties).WorkerSkills.Add(new AgentWorkerSkill
        {
            Name = "loop",
            Description = "Loops over upstream answers.",
            Content = "Act, then read {{Validator.decision}} and {{Verifier.verdict}}.",
        });

        WorkflowGraph graph = new WorkflowGraphBuilder().Build(
            new Dictionary<int, List<Block>> { { 1, [agent] } },
            []);

        Assert.DoesNotContain(graph.Nodes, n => n.Id is "Validator" or "Verifier");
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Build_DuplicateReplacementTag_KeepsBothBlocksDistinct_AndReaderDependsOnBoth()
    {
        // The AF11 currency-conversion pattern: two blocks share a ReplacementTag (one base, one
        // converts). They must remain two distinct nodes (so both are scheduled and each runs its
        // own skip gate), and a downstream reader of that tag must depend on BOTH producers.
        // Arrange
        var workflow = new Workflow { Title = "Test Workflow" };
        var section = new Section("Section 1", workflow);

        var baseBlock = new Block(BlockTypes.Merge, section)
        {
            Id = "USD_BASE",
            Name = "Liquidity USD",
            Order = 0,
            ReplacementTag = "liquidity_usd",
            Properties = new MergeProperties { TextTemplate = "base" },
        };
        var convertBlock = new Block(BlockTypes.Merge, section)
        {
            Id = "USD_CONVERT",
            Name = "Liquidity USD (converted)",
            Order = 1,
            ReplacementTag = "liquidity_usd",
            Properties = new MergeProperties { TextTemplate = "convert" },
        };
        var reader = new Block(BlockTypes.Merge, section)
        {
            Id = "READER",
            Name = "Reader",
            Order = 2,
            ReplacementTag = "report",
            Properties = new MergeProperties { TextTemplate = "{liquidity_usd}" },
        };
        var builder = new WorkflowGraphBuilder();

        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { baseBlock, convertBlock, reader } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, []);

        // Assert: both same-tag producers are distinct nodes.
        Assert.Contains(graph.Nodes, n => n.Id == "USD_BASE" && n.ReplacementTag == "liquidity_usd");
        Assert.Contains(graph.Nodes, n => n.Id == "USD_CONVERT" && n.ReplacementTag == "liquidity_usd");

        // The reader depends on BOTH producers of the shared tag.
        Assert.Contains(graph.Edges, e => e.SourceId == "USD_BASE" && e.TargetId == "READER");
        Assert.Contains(graph.Edges, e => e.SourceId == "USD_CONVERT" && e.TargetId == "READER");
    }

    [Fact]
    public void Build_Should_Set_DocumentClass_To_Null_When_SupportedDocumentClasses_Is_Null()
    {
        // Arrange
        var workflow = new Section("Section 1", new Workflow { Title = "Test Workflow" });
        var block = new Block(BlockTypes.Merge, workflow)
        {
            Id = "B",
            Name = "Block B",
            Order = 1,
            SupportedDocumentClasses = null!,
        };
        var builder = new WorkflowGraphBuilder();

        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { block } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, []);

        // Assert
        WorkflowGraphNode node = Assert.Single(graph.Nodes, n => n.Kind is "block");
        Assert.Null(node.DocumentClass);
    }

    [Fact]
    public void Build_Should_Set_DocumentClass_To_Null_When_SupportedDocumentClasses_Is_Empty()
    {
        // Arrange
        var workflow = new Section("Section 1", new Workflow { Title = "Test Workflow" });
        var block = new Block(BlockTypes.Merge, workflow)
        {
            Id = "B",
            Name = "Block B",
            Order = 1,
            SupportedDocumentClasses = [],
        };
        var builder = new WorkflowGraphBuilder();

        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { block } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, []);

        // Assert
        WorkflowGraphNode node = Assert.Single(graph.Nodes, n => n.Kind is "block");
        Assert.Null(node.DocumentClass);
    }

    [Fact]
    public void Build_Should_Set_DocumentClass_To_Null_When_SupportedDocumentClasses_Contains_Only_None()
    {
        // Arrange
        var workflow = new Section("Section 1", new Workflow { Title = "Test Workflow" });
        var block = new Block(BlockTypes.Merge, workflow)
        {
            Id = "B",
            Name = "Block B",
            Order = 1,
            SupportedDocumentClasses = ["None"],
        };
        var builder = new WorkflowGraphBuilder();

        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { block } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, []);

        // Assert
        WorkflowGraphNode node = Assert.Single(graph.Nodes, n => n.Kind is "block");
        Assert.Null(node.DocumentClass);
    }

    [Fact]
    public void Build_Should_Set_DocumentClass_When_Single_Class()
    {
        // Arrange
        var workflow = new Section("Section 1", new Workflow { Title = "Test Workflow" });
        var block = new Block(BlockTypes.Merge, workflow)
        {
            Id = "B",
            Name = "Block B",
            Order = 1,
            SupportedDocumentClasses = ["SEC Filing"],
        };
        var builder = new WorkflowGraphBuilder();
        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { block } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, []);

        // Assert
        WorkflowGraphNode node = Assert.Single(graph.Nodes, n => n.Kind is "block");
        Assert.Equal("SEC Filing", node.DocumentClass);
    }

    [Fact]
    public void Build_Should_Join_Multiple_DocumentClasses()
    {
        // Arrange
        var workflow = new Section("Section 1", new Workflow { Title = "Test Workflow" });
        var block = new Block(BlockTypes.Merge, workflow)
        {
            Id = "B",
            Name = "Block B",
            Order = 1,
            SupportedDocumentClasses = ["SEC Filing", "Prospectus"],
        };
        var builder = new WorkflowGraphBuilder();
        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { block } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, []);

        // Assert
        WorkflowGraphNode node = Assert.Single(graph.Nodes, n => n.Kind is "block");
        Assert.Equal("SEC Filing; Prospectus", node.DocumentClass);
    }

    [Fact]
    public void Build_Should_Filter_None_And_Join_Real_Classes()
    {
        // Arrange
        var workflow = new Section("Section 1", new Workflow { Title = "Test Workflow" });
        var block = new Block(BlockTypes.Merge, workflow)
        {
            Id = "B",
            Name = "Block B",
            Order = 1,
            SupportedDocumentClasses = ["None", "SEC Filing", "Prospectus"],
        };
        var builder = new WorkflowGraphBuilder();
        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { block } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, []);

        // Assert
        WorkflowGraphNode node = Assert.Single(graph.Nodes, n => n.Kind is "block");
        Assert.Equal("SEC Filing; Prospectus", node.DocumentClass);
    }

    [Fact]
    public void Build_Reader_Referencing_Tagged_Block_By_Id_Creates_Edge()
    {
        // Block X has a ReplacementTag, so producersByTag holds "TAG_X" while nodesByKey holds "X".
        // A reader referencing {X} must still resolve to the block instead of throwing.
        // Arrange
        var section = new Section("Section 1", new Workflow { Title = "Test Workflow" });
        var producer = new Block(BlockTypes.Merge, section)
        {
            Id = "X",
            Name = "Producer",
            Order = 1,
            ReplacementTag = "TAG_X",
        };
        var reader = new Block(BlockTypes.Merge, section)
        {
            Id = "R",
            Name = "Reader",
            Order = 2,
            ReplacementTag = string.Empty,
            Properties = new MergeProperties
            {
                TextTemplate = "{X}",
            },
        };
        var builder = new WorkflowGraphBuilder();
        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { producer, reader } }
        };

        // Act
        WorkflowGraph graph = builder.Build(allBlocks, []);

        // Assert
        Assert.Contains(graph.Edges, e => e.SourceId == "X" && e.TargetId == "R");
        Assert.DoesNotContain(graph.Nodes, n => n.Kind is "unknown");
    }

    [Fact]
    public void Build_FenceBlock_PinnedAtAuthoredPositionByBidirectionalEdges()
    {
        // A (order 1), F (order 2, fence), C (order 3), no placeholders. The fence gets edges from
        // every earlier-authored block and to every later-authored block; A and C stay unrelated.
        var section = new Section("Section 1", new Workflow { Title = "Test Workflow" });
        var blockA = new Block(BlockTypes.Merge, section) { Id = "A", Name = "A", Order = 1 };
        var fence = new Block(BlockTypes.Merge, section) { Id = "F", Name = "F", Order = 2 };
        var blockC = new Block(BlockTypes.Merge, section) { Id = "C", Name = "C", Order = 3 };
        var builder = new WorkflowGraphBuilder();
        Dictionary<int, List<Block>> allBlocks = new()
        {
            { 1, new List<Block> { blockA, fence, blockC } }
        };

        WorkflowGraph graph = builder.Build(allBlocks, [], new HashSet<string> { "F" });

        Assert.Contains(graph.Edges, e => e.SourceId == "A" && e.TargetId == "F");
        Assert.Contains(graph.Edges, e => e.SourceId == "F" && e.TargetId == "C");
        Assert.DoesNotContain(graph.Edges, e => e.SourceId == "A" && e.TargetId == "C");
    }
}
