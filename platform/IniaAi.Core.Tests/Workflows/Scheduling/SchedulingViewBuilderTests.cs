using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Behavioral tests for <see cref="SchedulingViewBuilder.Build"/>: Kahn layering with barrier
/// collapse, within-layer tie-break by <c>(SectionOrder, Order)</c>, and the cap=1
/// <see cref="SchedulingView.SequentialOrder"/> authored-order contract.
/// </summary>
public class SchedulingViewBuilderTests
{
    private const string KIND_BLOCK = "block";
    private const string KIND_PARAMETERIZED_SECTION = "parameterized_section";

    [Fact]
    public void Build_ThreeIndependentBlocks_ProducesSingleLayerWithAllThree()
    {
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer layer = Assert.Single(view.Layers);
        Assert.Equal(3, layer.BlockIds.Count);
        Assert.Contains("A", layer.BlockIds);
        Assert.Contains("B", layer.BlockIds);
        Assert.Contains("C", layer.BlockIds);
    }

    [Fact]
    public void Build_SingleEdgeAToB_SplitsAcrossTwoLayers()
    {
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, Order = 2 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "A", TargetId = "B" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Equal(2, view.Layers.Count);
        Assert.Single(view.Layers[0].BlockIds, "A");
        Assert.Single(view.Layers[1].BlockIds, "B");
    }

    [Fact]
    public void Build_ExaSearchBarrier_OccupiesItsOwnLayer()
    {
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "ES", Kind = KIND_BLOCK, BlockType = BlockTypes.IntelligentSearch, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        // Barrier must be alone in a layer. A and C may share layers; ES does not share with anyone.
        SchedulingLayer barrierLayer = Assert.Single(view.Layers, l => l.BlockIds.Contains("ES"));
        _ = Assert.Single(barrierLayer.BlockIds);
    }

    [Fact]
    public void Build_DocumentProducingBlock_OccupiesItsOwnLayer()
    {
        // A block that creates an output document (ProducesDocument) is a barrier regardless of its
        // block type, so two document-producers never write the shared AllPages list concurrently.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "D", Kind = KIND_BLOCK, BlockType = BlockTypes.WebApiCall, ProducesDocument = true, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer barrierLayer = Assert.Single(view.Layers, l => l.BlockIds.Contains("D"));
        _ = Assert.Single(barrierLayer.BlockIds);
    }

    [Fact]
    public void Build_ManualBarrierBlock_OccupiesItsOwnLayer()
    {
        // An author-flagged block (Block.RunsAlone -> ManualBarrier) is a barrier even though it is
        // neither a barrier block type nor a document producer.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "M", Kind = KIND_BLOCK, BlockType = BlockTypes.Merge, ManualBarrier = true, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer barrierLayer = Assert.Single(view.Layers, l => l.BlockIds.Contains("M"));
        _ = Assert.Single(barrierLayer.BlockIds);
    }

    [Fact]
    public void Build_NonDocumentProducingBlock_DoesNotBarrier()
    {
        // The same block type WITHOUT an output document class (e.g. a polling WebApiCall) is not a
        // barrier and shares a layer with other independent blocks.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "W", Kind = KIND_BLOCK, BlockType = BlockTypes.WebApiCall, ProducesDocument = false, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer layer = Assert.Single(view.Layers);
        Assert.Equal(3, layer.BlockIds.Count);
    }

    [Fact]
    public void Build_Json2XlsxBarrier_OccupiesItsOwnLayer()
    {
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "J", Kind = KIND_BLOCK, BlockType = BlockTypes.Json2Xlsx, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer barrierLayer = Assert.Single(view.Layers, l => l.BlockIds.Contains("J"));
        _ = Assert.Single(barrierLayer.BlockIds);
    }

    [Fact]
    public void Build_BuildTopicsForAllDocumentsBarrier_OccupiesItsOwnLayer()
    {
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "T", Kind = KIND_BLOCK, BlockType = BlockTypes.BuildTopicsForAllDocuments, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer barrierLayer = Assert.Single(view.Layers, l => l.BlockIds.Contains("T"));
        _ = Assert.Single(barrierLayer.BlockIds);
    }

    [Fact]
    public void Build_LoopRegion_CollapsesIntoSingleBarrierNode()
    {
        // LoopStart and LoopEnd plus any internal blocks must collapse into a single barrier layer.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "LS", Kind = KIND_BLOCK, BlockType = BlockTypes.LoopStart, Order = 2 },
                new WorkflowGraphNode { Id = "Inner", Kind = KIND_BLOCK, Order = 3 },
                new WorkflowGraphNode { Id = "LE", Kind = KIND_BLOCK, BlockType = BlockTypes.LoopEnd, Order = 4 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 5 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "LS", TargetId = "Inner" },
                new WorkflowGraphEdge { SourceId = "Inner", TargetId = "LE" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        // The loop region must contribute exactly one layer (a single barrier node spanning the
        // region). Inner must NOT appear as a peer of A or C.
        Assert.DoesNotContain(view.Layers, l => l.BlockIds.Contains("A") && l.BlockIds.Contains("Inner"));
        Assert.DoesNotContain(view.Layers, l => l.BlockIds.Contains("C") && l.BlockIds.Contains("Inner"));
        // The loop region's barrier layer holds exactly one identifier (the region collapse).
        Assert.Contains(view.Layers, l => l.BlockIds.Count == 1
            && (l.BlockIds[0] == "LS" || l.BlockIds[0] == "LE" || l.BlockIds[0] == "Inner"));
    }

    [Fact]
    public void Build_ParameterizedSection_CollapsesIntoSingleBarrierNode()
    {
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "PS", Kind = KIND_PARAMETERIZED_SECTION, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer barrierLayer = Assert.Single(view.Layers, l => l.BlockIds.Contains("PS"));
        _ = Assert.Single(barrierLayer.BlockIds);
    }

    [Fact]
    public void Build_DuplicateTagReaders_HaveEdgesToAllProducers()
    {
        // Two producers P1 and P2 both emit tag "X" (modelled in the graph as edges with the same
        // SourceId "X" feeding the reader R). The reader R must sit strictly after both producers'
        // layers, regardless of which producer the SchedulingViewBuilder picks as the canonical
        // producer for the (section.Order, block.Order) tie-break rule.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "P1", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "P2", Kind = KIND_BLOCK, Order = 2 },
                new WorkflowGraphNode { Id = "R", Kind = KIND_BLOCK, Order = 3 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "P1", TargetId = "R" },
                new WorkflowGraphEdge { SourceId = "P2", TargetId = "R" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        int p1Layer = IndexOfLayerContaining(view, "P1");
        int p2Layer = IndexOfLayerContaining(view, "P2");
        int rLayer = IndexOfLayerContaining(view, "R");

        Assert.True(rLayer > p1Layer, $"R (layer {rLayer}) must run after P1 (layer {p1Layer}).");
        Assert.True(rLayer > p2Layer, $"R (layer {rLayer}) must run after P2 (layer {p2Layer}).");
    }

    [Fact]
    public void Build_WithinLayer_TieBreaksByBlockOrder()
    {
        // Two independent blocks share the same logical layer. The expected ordering within the
        // BlockIds list is ascending (section.Order, block.Order). Construct them out of natural
        // order in the Nodes list to prove the builder sorts rather than preserving insertion order.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "Z", Kind = KIND_BLOCK, SectionOrder = 0, Order = 5 },
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, SectionOrder = 0, Order = 1 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer layer = Assert.Single(view.Layers);
        Assert.Equal(2, layer.BlockIds.Count);
        Assert.Equal("A", layer.BlockIds[0]);
        Assert.Equal("Z", layer.BlockIds[1]);
    }

    [Fact]
    public void Build_WithinLayer_CrossSectionTieBreaksBySectionOrderThenBlockOrder()
    {
        // Four blocks all in the same logical scheduler layer (no edges between them) split across
        // two sections. Expected ordering: (Section 0, Block 0), (Section 0, Block 1),
        // (Section 1, Block 0), (Section 1, Block 1). Insertion order is deliberately scrambled
        // so the assertion catches both "sort by SectionOrder" and "sort by Order within section".
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "S1B1", Kind = KIND_BLOCK, SectionOrder = 1, Order = 1 },
                new WorkflowGraphNode { Id = "S0B1", Kind = KIND_BLOCK, SectionOrder = 0, Order = 1 },
                new WorkflowGraphNode { Id = "S1B0", Kind = KIND_BLOCK, SectionOrder = 1, Order = 0 },
                new WorkflowGraphNode { Id = "S0B0", Kind = KIND_BLOCK, SectionOrder = 0, Order = 0 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer layer = Assert.Single(view.Layers);
        Assert.Equal(4, layer.BlockIds.Count);
        Assert.Equal("S0B0", layer.BlockIds[0]);
        Assert.Equal("S0B1", layer.BlockIds[1]);
        Assert.Equal("S1B0", layer.BlockIds[2]);
        Assert.Equal("S1B1", layer.BlockIds[3]);
    }

    [Fact]
    public void Build_CyclicGraph_BreaksAtEarliestAuthoredNodeAndReportsIt()
    {
        // A -> B -> C -> A. Develop executed cyclic forward references in authored order (the
        // forward placeholder resolved to no-data), so the cycle breaks at A and order is A, B, C.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "A", TargetId = "B" },
                new WorkflowGraphEdge { SourceId = "B", TargetId = "C" },
                new WorkflowGraphEdge { SourceId = "C", TargetId = "A" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Equal(new[] { "A", "B", "C" }, view.SequentialOrder);
        Assert.Equal(3, view.Layers.Count);
        Assert.Equal("A", Assert.Single(view.Layers[0].BlockIds));
        Assert.Equal("B", Assert.Single(view.Layers[1].BlockIds));
        Assert.Equal("C", Assert.Single(view.Layers[2].BlockIds));
        Assert.Contains("A", view.ForcedCycleNodes);
    }

    [Fact]
    public void Build_AcyclicPreludeThenDownstreamCycle_KeepsPreludeAndBreaksCycle()
    {
        // Prelude: P (no edges). Downstream: A <-> B (mutual forward reference). P runs first,
        // then the cycle breaks at A (earliest authored), then B.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "P", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 2 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, Order = 3 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "A", TargetId = "B" },
                new WorkflowGraphEdge { SourceId = "B", TargetId = "A" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Equal(new[] { "P", "A", "B" }, view.SequentialOrder);
        Assert.Equal("A", Assert.Single(view.ForcedCycleNodes));
    }

    [Fact]
    public void Build_VariableReadingBlock_SharesTheFirstWaveWithIndependentBlocks()
    {
        // Variables are constants by dispatch time, so reading one must not cost the reader a wave:
        // A (reads {Var}) and B (no inputs) both belong in layer 0.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "Var", Kind = WorkflowGraphNodeKinds.INPUT_VARIABLE },
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, SectionOrder = 0, Order = 0 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, SectionOrder = 0, Order = 1 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "Var", TargetId = "A" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        SchedulingLayer layer = Assert.Single(view.Layers);
        Assert.Equal(new[] { "A", "B" }, layer.BlockIds);
    }

    [Fact]
    public void SequentialOrder_VariableReadByFirstBlock_PreservesAuthoredOrder()
    {
        // A (authored first) reads {Var}; B has no inputs. cap=1 must still run A before B.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "Var", Kind = WorkflowGraphNodeKinds.INPUT_VARIABLE },
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, SectionOrder = 0, Order = 0 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, SectionOrder = 0, Order = 1 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "Var", TargetId = "A" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Equal(new[] { "A", "B" }, view.SequentialOrder);
    }

    [Fact]
    public void SequentialOrder_DocumentInputReadByFirstBlock_PreservesAuthoredOrder()
    {
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "InputContext", Kind = WorkflowGraphNodeKinds.DOCUMENT_INPUT },
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, SectionOrder = 0, Order = 0 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, SectionOrder = 0, Order = 1 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "InputContext", TargetId = "A" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Equal(new[] { "A", "B" }, view.SequentialOrder);
    }

    [Fact]
    public void SequentialOrder_IndependentCrossSectionBlocks_FlattensBySectionThenBlockOrder()
    {
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "S1B1", Kind = KIND_BLOCK, SectionOrder = 1, Order = 1 },
                new WorkflowGraphNode { Id = "S0B1", Kind = KIND_BLOCK, SectionOrder = 0, Order = 1 },
                new WorkflowGraphNode { Id = "S1B0", Kind = KIND_BLOCK, SectionOrder = 1, Order = 0 },
                new WorkflowGraphNode { Id = "S0B0", Kind = KIND_BLOCK, SectionOrder = 0, Order = 0 },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Equal(new[] { "S0B0", "S0B1", "S1B0", "S1B1" }, view.SequentialOrder);
    }

    [Fact]
    public void Build_EarliestAuthoredNodeDownstreamOfACycle_IsNotTheOneForced()
    {
        // B <-> C, and A (authored first) reads C. A is blocked but on no cycle: forcing it would
        // run A with no-data and leave the cycle in place, so the break lands on B.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, Order = 2 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 3 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "B", TargetId = "C" },
                new WorkflowGraphEdge { SourceId = "C", TargetId = "B" },
                new WorkflowGraphEdge { SourceId = "C", TargetId = "A" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Equal("B", Assert.Single(view.ForcedCycleNodes));
        Assert.Equal(new[] { "B", "C", "A" }, view.SequentialOrder);
    }

    [Fact]
    public void Build_TwoChainedCycles_BreaksTheUpstreamCycleFirst()
    {
        // A <-> B feeds C <-> D, and D is authored first. Forcing D first would run it before the
        // A/B cycle it reads through C, so each cycle is broken only once its upstream has run.
        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode { Id = "D", Kind = KIND_BLOCK, Order = 1 },
                new WorkflowGraphNode { Id = "A", Kind = KIND_BLOCK, Order = 2 },
                new WorkflowGraphNode { Id = "B", Kind = KIND_BLOCK, Order = 3 },
                new WorkflowGraphNode { Id = "C", Kind = KIND_BLOCK, Order = 4 },
            ],
            Edges =
            [
                new WorkflowGraphEdge { SourceId = "A", TargetId = "B" },
                new WorkflowGraphEdge { SourceId = "B", TargetId = "A" },
                new WorkflowGraphEdge { SourceId = "B", TargetId = "C" },
                new WorkflowGraphEdge { SourceId = "C", TargetId = "D" },
                new WorkflowGraphEdge { SourceId = "D", TargetId = "C" },
            ],
        };

        SchedulingView view = SchedulingViewBuilder.Build(graph);

        Assert.Equal(new[] { "A", "D" }, view.ForcedCycleNodes);
        Assert.Equal(new[] { "A", "B", "D", "C" }, view.SequentialOrder);
    }

    private static int IndexOfLayerContaining(SchedulingView view, string blockId)
    {
        for (int i = 0; i < view.Layers.Count; i++)
        {
            if (view.Layers[i].BlockIds.Contains(blockId))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Block {blockId} not found in any layer.");
    }
}
