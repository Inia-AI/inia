using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Properties;
using IniaAi.Domain.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class SectionTests
{
    [Fact]
    public void Ctor_WithParentAndIndex_AttachesToWorkflowSectionsAtIndex()
    {
        var workflow = new Workflow("root");

        var first = new Section("first", workflow);
        var second = new Section("second", workflow);
        var inserted = new Section("inserted", workflow, index: 1);

        Section[] ordered = [.. workflow.Sections.OrderBy(s => s.Order)];
        Assert.Equal(3, ordered.Length);
        Assert.Same(first, ordered[0]);
        Assert.Same(inserted, ordered[1]);
        Assert.Same(second, ordered[2]);
    }

    [Fact]
    public void Ctor_WithParent_SetsWorkflowNavigationToParent()
    {
        var workflow = new Workflow("root");

        var section = new Section("s", workflow);

        Assert.Same(workflow, section.Workflow);
        Assert.Equal(workflow.Id, section.WorkflowId);
    }

    [Fact]
    public void Add_BlockToSection_SetsSectionFkAndOrder()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var firstBlock = new Block { Type = BlockTypes.AiQuery };
        var secondBlock = new Block { Type = BlockTypes.AiQuery };

        section.Add(firstBlock);
        section.Add(secondBlock);

        Assert.Same(section, firstBlock.Section);
        Assert.Equal(section.Id, firstBlock.SectionId);
        Assert.Equal(0, firstBlock.Order);
        Assert.Equal(1, secondBlock.Order);
        Assert.Equal(2, section.Blocks.Count);
    }

    [Fact]
    public void Add_BlockToSeparatorSection_Throws()
    {
        var workflow = new Workflow("root");
        var separator = new Section("sep", workflow)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };
        var block = new Block { Type = BlockTypes.AiQuery };

        Assert.Throws<InvalidOperationException>(() => separator.Add(block));
    }

    [Fact]
    public void Insert_BlockIntoSeparatorSection_Throws()
    {
        var workflow = new Workflow("root");
        var separator = new Section("sep", workflow)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };
        var block = new Block { Type = BlockTypes.AiQuery };

        Assert.Throws<InvalidOperationException>(() => separator.Insert(0, block));
    }

    [Fact]
    public void Insert_IndexBeyondBlockCount_AppendsAtEnd()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        section.Add(new Block { Type = BlockTypes.AiQuery });

        var inserted = new Block { Type = BlockTypes.AiQuery };
        section.Insert(5, inserted);

        Assert.Equal(2, section.Blocks.Count);
        Assert.Equal(1, inserted.Order);
    }

    [Fact]
    public void Insert_NegativeIndex_InsertsAtStart()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        section.Add(new Block { Type = BlockTypes.AiQuery });

        var inserted = new Block { Type = BlockTypes.AiQuery };
        section.Insert(-3, inserted);

        Assert.Equal(0, inserted.Order);
    }

    [Fact]
    public void Remove_RemovesBlockAndNormalizesOrder()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var a = new Block { Type = BlockTypes.AiQuery };
        var b = new Block { Type = BlockTypes.AiQuery };
        var c = new Block { Type = BlockTypes.AiQuery };
        section.Add(a);
        section.Add(b);
        section.Add(c);

        section.Remove(b);

        Assert.Equal(2, section.Blocks.Count);
        Assert.Equal(0, a.Order);
        Assert.Equal(1, c.Order);
    }

    [Fact]
    public void Clear_EmptiesBlocks()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        section.Add(new Block { Type = BlockTypes.AiQuery });
        section.Add(new Block { Type = BlockTypes.AiQuery });

        section.Clear();

        Assert.Empty(section.Blocks);
    }

    [Fact]
    public void NormalizeBlockOrdering_ReassignsContiguousOrder()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var a = new Block { Type = BlockTypes.AiQuery, Order = 7 };
        var b = new Block { Type = BlockTypes.AiQuery, Order = 3 };
        var c = new Block { Type = BlockTypes.AiQuery, Order = 5 };
        section.Blocks.Add(a);
        section.Blocks.Add(b);
        section.Blocks.Add(c);

        section.NormalizeBlockOrdering();

        Block[] ordered = [.. section.Blocks.OrderBy(x => x.Order)];
        Assert.Same(b, ordered[0]);
        Assert.Same(c, ordered[1]);
        Assert.Same(a, ordered[2]);
        Assert.Equal(0, ordered[0].Order);
        Assert.Equal(1, ordered[1].Order);
        Assert.Equal(2, ordered[2].Order);
    }

    [Fact]
    public void GetLoopStartAndEnd_ReturnsBothWhenPresent()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var start = new Block { Type = BlockTypes.LoopStart };
        var middle = new Block { Type = BlockTypes.AiQuery };
        var end = new Block { Type = BlockTypes.LoopEnd };
        section.Add(start);
        section.Add(middle);
        section.Add(end);

        section.GetLoopStartAndEnd(out Block? loopStart, out Block? loopEnd);

        Assert.Same(start, loopStart);
        Assert.Same(end, loopEnd);
    }

    [Fact]
    public void GetLoopStartAndEnd_LoopStartWithoutEnd_Throws()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        section.Add(new Block { Type = BlockTypes.LoopStart });

        Assert.Throws<InvalidOperationException>(
            () => section.GetLoopStartAndEnd(out Block? _, out Block? _));
    }

    [Fact]
    public void GetLoopBodyBlockIds_ReturnsInclusiveLoopBody()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var before = new Block { Type = BlockTypes.AiQuery };
        var start = new Block { Type = BlockTypes.LoopStart };
        var middle = new Block { Type = BlockTypes.AiQuery };
        var end = new Block { Type = BlockTypes.LoopEnd };
        var after = new Block { Type = BlockTypes.AiQuery };
        section.Add(before);
        section.Add(start);
        section.Add(middle);
        section.Add(end);
        section.Add(after);

        IReadOnlySet<string> body = section.GetLoopBodyBlockIds();

        Assert.Equal(3, body.Count);
        Assert.Contains(start.Id, body);
        Assert.Contains(middle.Id, body);
        Assert.Contains(end.Id, body);
        Assert.DoesNotContain(before.Id, body);
        Assert.DoesNotContain(after.Id, body);
    }

    [Fact]
    public void GetLoopBodyBlockIds_SectionWithoutLoop_ReturnsEmpty()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        section.Add(new Block { Type = BlockTypes.AiQuery });

        Assert.Empty(section.GetLoopBodyBlockIds());
    }

    [Fact]
    public void GetLoopBodyBlockIds_HalfFormedLoop_ReturnsEmpty()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        section.Add(new Block { Type = BlockTypes.LoopStart });
        section.Add(new Block { Type = BlockTypes.AiQuery });

        Assert.Empty(section.GetLoopBodyBlockIds());
    }

    [Fact]
    public void ReplaceAll_ReplacesPlaceholdersInOwnTextAndBlocks()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow)
        {
            Title = "Hello {name}",
            TextInputTemplate = "Greet {name}",
        };
        var block = new Block { Type = BlockTypes.AiQuery, OutputTitleTemplate = "Output for {name}" };
        section.Add(block);

        section.ReplaceAll(new Dictionary<string, string?> { ["{name}"] = "world" });

        Assert.Equal("Hello world", section.Title);
        Assert.Equal("Greet world", section.TextInputTemplate);
        Assert.Equal("Output for world", block.OutputTitleTemplate);
    }

    [Fact]
    public void HasPlaceholder_FindsPlaceholderInOwnTextOrBlocks()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "Hello {name}" };
        var block = new Block { Type = BlockTypes.AiQuery, Name = "Plain block" };
        section.Add(block);

        Assert.True(section.HasPlaceholder("{name}"));
        Assert.False(section.HasPlaceholder("{nonexistent}"));
    }

    [Fact]
    public void GetAllPlaceholders_ExtractsFromTextInputTemplate()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow)
        {
            TextInputTemplate = "Hi {first} and {second}",
        };

        List<string> placeholders = section.GetAllPlaceholders();

        Assert.Contains("first", placeholders);
        Assert.Contains("second", placeholders);
    }

    [Fact]
    public void ClearExecutionState_NullsOwnStateAndClearsBlockStates()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow)
        {
            ExecutionState = new WorkflowExecutionState { TextInput = "ran" },
        };
        var block = new Block { Type = BlockTypes.AiQuery };
        section.Add(block);
        block.UpdateExecutionState(false, []);

        section.ClearExecutionState();

        Assert.Null(section.ExecutionState);
        Assert.Null(block.ExecutionState);
    }

    [Fact]
    public void UpdateExecutionState_InitializesStateAndAppliesReplacements()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow)
        {
            TextInputTemplate = "Greet {name}",
        };

        section.UpdateExecutionState(true, new Dictionary<string, string?> { ["name"] = "world" });

        Assert.NotNull(section.ExecutionState);
        Assert.Equal("Greet world", section.ExecutionState!.TextInput);
    }

    [Fact]
    public void Indexer_ReturnsBlockAtOrder()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var first = new Block { Type = BlockTypes.AiQuery };
        var second = new Block { Type = BlockTypes.AiQuery };
        section.Add(first);
        section.Add(second);

        Assert.Same(first, section[0]);
        Assert.Same(second, section[1]);
        Assert.Throws<IndexOutOfRangeException>(() => section[99]);
    }

    [Fact]
    public void RemoveBlockAt_RemovesBlockAndReturnsIt()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var first = new Block { Type = BlockTypes.AiQuery };
        var second = new Block { Type = BlockTypes.AiQuery };
        var third = new Block { Type = BlockTypes.AiQuery };
        section.Add(first);
        section.Add(second);
        section.Add(third);

        Block removed = section.RemoveBlockAt(1);

        Assert.Same(second, removed);
        Assert.Equal(2, section.Blocks.Count);
        Assert.Equal(0, first.Order);
        Assert.Equal(1, third.Order);
    }

    [Fact]
    public void ReplacementTagIsInUse_FindsBlockUsingTag()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        section.Add(new Block { Type = BlockTypes.AiQuery, ReplacementTag = "first" });
        section.Add(new Block { Type = BlockTypes.AiQuery, ReplacementTag = "second" });

        Assert.True(section.ReplacementTagIsInUse("first"));
        Assert.True(section.ReplacementTagIsInUse("second"));
        Assert.False(section.ReplacementTagIsInUse("nonexistent"));
    }

    [Fact]
    public void RequiredDocumentsNotProvided_EmptySection_ReturnsFalse()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);

        Assert.False(section.RequiredDocumentsNotProvided());
    }

    [Fact]
    public void RequiredDocumentsNotProvided_NoDocConsumingBlocks_ReturnsFalse()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        section.Add(new Block { Type = BlockTypes.Merge });
        section.Add(new Block { Type = BlockTypes.CalculateNumber });

        Assert.False(section.RequiredDocumentsNotProvided());
    }

    [Fact]
    public void RequiredDocumentsNotProvided_AllRagBlocksMissingDocs_ReturnsTrue()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var rag = new Block { Type = BlockTypes.SimpleRag };
        rag.RagSettings.Add(new RagSettings());
        section.Add(rag);

        Assert.True(section.RequiredDocumentsNotProvided());
    }

    [Fact]
    public void CloneTo_CopiesScalarsPropertiesAndBlocks()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow)
        {
            Title = "Title A",
            Description = "Desc A",
            TextInputTemplate = "Greet {name}",
            Properties = new SectionProperties { IsSeparator = false, ExcludeFromWordExport = true },
        };
        source.Add(new Block { Type = BlockTypes.AiQuery, ReplacementTag = "tag_a" });
        source.Add(new Block { Type = BlockTypes.Merge, ReplacementTag = "tag_b" });

        var destination = new Section("dest", workflow);

        source.CloneTo(destination);

        Assert.Equal("Title A", destination.Title);
        Assert.Equal("Desc A", destination.Description);
        Assert.Equal("Greet {name}", destination.TextInputTemplate);
        Assert.NotSame(source.Properties, destination.Properties);
        Assert.True(destination.Properties.ExcludeFromWordExport);
        Assert.Equal(2, destination.Blocks.Count);
        Block[] destOrdered = [.. destination.Blocks.OrderBy(b => b.Order)];
        Assert.Equal("tag_a", destOrdered[0].ReplacementTag);
        Assert.Equal("tag_b", destOrdered[1].ReplacementTag);
        Assert.Equal(BlockTypes.AiQuery, destOrdered[0].Type);
        Assert.Equal(BlockTypes.Merge, destOrdered[1].Type);
        foreach (Block original in source.Blocks)
        {
            Assert.DoesNotContain(destination.Blocks, b => ReferenceEquals(b, original));
        }
    }

    [Fact]
    public void CloneTo_DeepCopiesConfiguration_SoMutatingCloneDoesNotAffectSource()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow)
        {
            Configuration = new SectionConfiguration
            {
                Version = "1.0",
                GridLayout = new GridLayoutSettings
                {
                    Mode = "grid",
                    Columns = 3,
                    Elements =
                    [
                        new GridElementPosition { BlockOrder = 1, Row = 1, Column = 1, RowSpan = 1, ColumnSpan = 2 },
                    ],
                },
            },
        };

        var destination = new Section("dest", workflow);

        source.CloneTo(destination);

        Assert.NotNull(destination.Configuration);
        Assert.NotSame(source.Configuration, destination.Configuration);
        Assert.NotSame(source.Configuration!.GridLayout, destination.Configuration!.GridLayout);
        Assert.NotSame(source.Configuration.GridLayout!.Elements, destination.Configuration.GridLayout!.Elements);

        destination.Configuration.GridLayout.Columns = 12;
        destination.Configuration.GridLayout.Elements![0].ColumnSpan = 9;

        Assert.Equal(3, source.Configuration.GridLayout.Columns);
        Assert.Equal(2, source.Configuration.GridLayout.Elements![0].ColumnSpan);
    }

    [Fact]
    public void CloneTo_ClearsExistingDestinationBlocks()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow);
        source.Add(new Block { Type = BlockTypes.AiQuery, ReplacementTag = "from_source" });

        var destination = new Section("dest", workflow);
        destination.Add(new Block { Type = BlockTypes.Merge, ReplacementTag = "preexisting" });

        source.CloneTo(destination);

        _ = Assert.Single(destination.Blocks);
        Assert.Equal("from_source", destination.Blocks.Single().ReplacementTag);
    }

    [Fact]
    public void Workflow_DuplicateSectionAt_InsertsCopyAfterOriginal()
    {
        var workflow = new Workflow("root");
        var first = new Section("first", workflow)
        {
            Title = "First section",
        };
        first.Add(new Block { Type = BlockTypes.AiQuery, ReplacementTag = "a" });
        _ = new Section("second", workflow);

        Section duplicate = workflow.DuplicateSectionAt(0);

        Assert.Equal("first - Copy", duplicate.Name);
        Assert.Equal("First section", duplicate.Title);
        Assert.Same(workflow, duplicate.Workflow);
        Assert.Equal(1, duplicate.Order);
        Assert.Equal(3, workflow.Sections.Count);
        _ = Assert.Single(duplicate.Blocks);
        Assert.Equal("a", duplicate.Blocks.Single().ReplacementTag);
    }

    [Fact]
    public void Workflow_DuplicateSectionAt_Separator_Throws()
    {
        var workflow = new Workflow("root");
        _ = new Section("sep", workflow)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };

        Assert.Throws<InvalidOperationException>(() => workflow.DuplicateSectionAt(0));
    }

    [Fact]
    public void Workflow_Remove_Section_RemovesAndRenumbers()
    {
        var workflow = new Workflow("root");
        var a = new Section("a", workflow);
        var b = new Section("b", workflow);
        var c = new Section("c", workflow);

        workflow.Remove(b);

        Assert.Equal(2, workflow.Sections.Count);
        Assert.Equal(0, a.Order);
        Assert.Equal(1, c.Order);
    }

    [Fact]
    public void Workflow_RemoveSectionAt_RemovesAndReturnsSection()
    {
        var workflow = new Workflow("root");
        _ = new Section("a", workflow);
        var b = new Section("b", workflow);
        _ = new Section("c", workflow);

        Section removed = workflow.RemoveSectionAt(1);

        Assert.Same(b, removed);
        Assert.Equal(2, workflow.Sections.Count);
    }

    [Fact]
    public void Workflow_NormalizeSectionOrdering_ReassignsContiguousOrder()
    {
        var workflow = new Workflow("root");
        var a = new Section { Order = 7, Workflow = workflow, WorkflowId = workflow.Id };
        var b = new Section { Order = 3, Workflow = workflow, WorkflowId = workflow.Id };
        var c = new Section { Order = 5, Workflow = workflow, WorkflowId = workflow.Id };
        workflow.Sections.Add(a);
        workflow.Sections.Add(b);
        workflow.Sections.Add(c);

        workflow.NormalizeSectionOrdering();

        Section[] ordered = [.. workflow.Sections.OrderBy(s => s.Order)];
        Assert.Same(b, ordered[0]);
        Assert.Same(c, ordered[1]);
        Assert.Same(a, ordered[2]);
        Assert.Equal(0, ordered[0].Order);
        Assert.Equal(1, ordered[1].Order);
        Assert.Equal(2, ordered[2].Order);
    }

    [Fact]
    public void Workflow_ReplaceSectionWithExpanded_RemovesTemplateAndInsertsExpansions()
    {
        var workflow = new Workflow("root");
        var template = new Section("template", workflow);
        _ = new Section("after", workflow);

        var expanded1 = new Section();
        var expanded2 = new Section();
        var expanded3 = new Section();

        workflow.ReplaceSectionWithExpanded(template, [expanded1, expanded2, expanded3]);

        Section[] ordered = [.. workflow.Sections.OrderBy(s => s.Order)];
        Assert.Equal(4, ordered.Length);
        Assert.Same(expanded1, ordered[0]);
        Assert.Same(expanded2, ordered[1]);
        Assert.Same(expanded3, ordered[2]);
        Assert.Equal("after", ordered[3].Name);
        Assert.DoesNotContain(template, workflow.Sections);
        Assert.Same(workflow, expanded1.Workflow);
        Assert.Equal(workflow.Id, expanded1.WorkflowId);
    }

    [Fact]
    public void Workflow_MoveSection_Reorders()
    {
        var workflow = new Workflow("root");
        var a = new Section("a", workflow);
        var b = new Section("b", workflow);
        var c = new Section("c", workflow);

        workflow.MoveSection(2, 0);

        Section[] ordered = [.. workflow.Sections.OrderBy(s => s.Order)];
        Assert.Same(c, ordered[0]);
        Assert.Same(a, ordered[1]);
        Assert.Same(b, ordered[2]);
    }

    [Fact]
    public void MoveToWorkflow_SameWorkflowDifferentIndex_Reorders()
    {
        var workflow = new Workflow("root");
        var a = new Section("a", workflow);
        var b = new Section("b", workflow);
        var c = new Section("c", workflow);

        c.MoveToWorkflow(workflow, 0);

        Section[] ordered = [.. workflow.Sections.OrderBy(s => s.Order)];
        Assert.Same(c, ordered[0]);
        Assert.Same(a, ordered[1]);
        Assert.Same(b, ordered[2]);
    }

    [Fact]
    public void MoveToWorkflow_DifferentWorkflow_RetargetsFkAndMoves()
    {
        var source = new Workflow("source-root");
        var destination = new Workflow("dest-root");
        var section = new Section("s", source);
        section.Add(new Block { Type = BlockTypes.AiQuery, ReplacementTag = "keep" });

        section.MoveToWorkflow(destination);

        Assert.Same(destination, section.Workflow);
        Assert.Equal(destination.Id, section.WorkflowId);
        Assert.Empty(source.Sections);
        _ = Assert.Single(destination.Sections);
        _ = Assert.Single(section.Blocks);
    }

    [Fact]
    public void CloneToWorkflow_LeavesSourceAndCopiesIntoDestination()
    {
        var source = new Workflow("source-root");
        var destination = new Workflow("dest-root");
        var sourceSection = new Section("src", source)
        {
            Title = "Source title",
        };
        sourceSection.Add(new Block { Type = BlockTypes.AiQuery, ReplacementTag = "tag" });

        Section copied = sourceSection.CloneToWorkflow(destination, 0);

        Assert.Same(destination, copied.Workflow);
        Assert.Equal(destination.Id, copied.WorkflowId);
        Assert.Equal("src", copied.Name);
        Assert.Equal("Source title", copied.Title);
        Assert.Equal(0, copied.Order);
        _ = Assert.Single(source.Sections);
        _ = Assert.Single(destination.Sections);
        Assert.Same(copied, destination.Sections.Single());
        _ = Assert.Single(copied.Blocks);
    }

    [Fact]
    public void CloneToWorkflow_NameOverride_RenamesCopy()
    {
        var source = new Workflow("source-root");
        var destination = new Workflow("dest-root");
        var sourceSection = new Section("original", source);

        Section copied = sourceSection.CloneToWorkflow(destination, 0, nameOverride: "renamed");

        Assert.Equal("renamed", copied.Name);
        Assert.Equal("original", sourceSection.Name);
    }

    [Fact]
    public void FindBlockByReplacementTag_ReturnsFirstMatchOrNull()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var a = new Block { Type = BlockTypes.AiQuery, ReplacementTag = "tag_a" };
        var b = new Block { Type = BlockTypes.AiQuery, ReplacementTag = "tag_b" };
        section.Add(a);
        section.Add(b);

        Assert.Same(a, section.FindBlockByReplacementTag("tag_a"));
        Assert.Same(b, section.FindBlockByReplacementTag("tag_b"));
        Assert.Null(section.FindBlockByReplacementTag("missing"));
    }

    [Fact]
    public void FindBlocksByReplacementTag_ReturnsAllMatches()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var a = new Block { Type = BlockTypes.AiQuery, ReplacementTag = "duplicate" };
        var b = new Block { Type = BlockTypes.AiQuery, ReplacementTag = "unique" };
        var c = new Block { Type = BlockTypes.AiQuery, ReplacementTag = "duplicate" };
        section.Add(a);
        section.Add(b);
        section.Add(c);

        List<Block> dupes = section.FindBlocksByReplacementTag("duplicate");

        Assert.Equal(2, dupes.Count);
        Assert.Contains(a, dupes);
        Assert.Contains(c, dupes);
    }

    [Fact]
    public void Workflow_FromJson_PopulatesSections()
    {
        string json = """
        {
            "Name": "root",
            "Title": "Root title",
            "Sections": [
                {
                    "Name": "first",
                    "Title": "First section",
                    "Order": 0,
                    "GroupColor": "Color1",
                    "Blocks": [
                        { "Type": "AiQuery", "ReplacementTag": "tag_a", "Order": 0 }
                    ]
                },
                {
                    "Name": "sep",
                    "Order": 1,
                    "Properties": { "IsSeparator": true }
                },
                {
                    "Name": "second",
                    "Title": "Second section",
                    "Order": 2,
                    "Blocks": [
                        { "Type": "Merge", "ReplacementTag": "tag_b", "Order": 0 }
                    ]
                }
            ]
        }
        """;

        var workflow = Workflow.FromJson(json);

        Assert.NotNull(workflow);
        Assert.Equal(3, workflow!.Sections.Count);
        Section[] ordered = [.. workflow.Sections.OrderBy(s => s.Order)];
        Assert.Equal("first", ordered[0].Name);
        Assert.Equal("First section", ordered[0].Title);
        Assert.Equal(GroupColor.Color1, ordered[0].GroupColor);
        Assert.False(ordered[0].IsSeparator);
        Assert.True(ordered[1].IsSeparator);
        Assert.Equal("second", ordered[2].Name);
        Assert.All(ordered, s => Assert.False(s.Properties.ExcludeFromWordExport));
        Assert.Same(workflow, ordered[0].Workflow);
        Assert.Equal(workflow.Id, ordered[0].WorkflowId);
    }

    [Fact]
    public void RoundTrip_SectionLeftOutOfWordExport_KeepsTheSetting()
    {
        var root = new Workflow("root");
        _ = new Section("debug", root, 0)
        {
            Properties = new SectionProperties { ExcludeFromWordExport = true },
        };
        _ = new Section("summary", root, 1);

        var roundTripped = Workflow.FromJson(root.ToJson(pretty: false));

        Assert.NotNull(roundTripped);
        List<Section> ordered = [.. roundTripped.Sections.OrderBy(s => s.Order)];
        Assert.True(ordered[0].Properties.ExcludeFromWordExport);
        Assert.False(ordered[1].Properties.ExcludeFromWordExport);
    }

    [Fact]
    public void Workflow_CloneTo_PopulatesDestinationSections()
    {
        string json = """
        {
            "Name": "src",
            "Sections": [
                {
                    "Name": "first",
                    "Title": "First",
                    "Order": 0,
                    "Blocks": [
                        { "Type": "AiQuery", "ReplacementTag": "tag_a", "Order": 0 }
                    ]
                },
                {
                    "Name": "sep",
                    "Order": 1,
                    "Properties": { "IsSeparator": true }
                }
            ]
        }
        """;
        Workflow source = Workflow.FromJson(json)!;
        var destination = new Workflow("dest");

        source.CloneTo(destination);

        Assert.Equal(2, destination.Sections.Count);
        Section[] ordered = [.. destination.Sections.OrderBy(s => s.Order)];
        Assert.Equal("first", ordered[0].Name);
        Assert.True(ordered[1].IsSeparator);
        Assert.Same(destination, ordered[0].Workflow);
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_MarksEachBlockAndSeedsTitle()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var noTitle = new Block { Type = BlockTypes.AiQuery, Name = "Block A", ReplacementTag = "tag_a" };
        var withTitle = new Block { Type = BlockTypes.AiQuery, OutputTitleTemplate = "Explicit title", ReplacementTag = "tag_b" };
        var nameless = new Block { Type = BlockTypes.AiQuery, ReplacementTag = "tag_c" };
        section.Add(noTitle);
        section.Add(withTitle);
        section.Add(nameless);

        section.ExposeAllBlocksAsOutputs();

        Assert.True(noTitle.IsOutputSource);
        Assert.True(withTitle.IsOutputSource);
        Assert.True(nameless.IsOutputSource);
        Assert.Equal("Block A", noTitle.OutputTitleTemplate);
        Assert.Equal("Explicit title", withTitle.OutputTitleTemplate);
        Assert.Equal("tag_c", nameless.OutputTitleTemplate);
    }

    [Fact]
    public void Workflow_FromJson_SectionsFlatBlocks_MatchGetAllBlocks()
    {
        string json = """
        {
            "Name": "root",
            "Children": [
                {
                    "Name": "first",
                    "Order": 0,
                    "Blocks": [
                        { "Type": "AiQuery", "ReplacementTag": "a1", "Order": 0 },
                        { "Type": "Merge", "ReplacementTag": "a2", "Order": 1 }
                    ]
                },
                {
                    "Name": "sep",
                    "Order": 1,
                    "Properties": { "IsSeparator": true }
                },
                {
                    "Name": "second",
                    "Order": 2,
                    "Blocks": [
                        { "Type": "CalculateNumber", "ReplacementTag": "b1", "Order": 0 }
                    ]
                }
            ]
        }
        """;

        Workflow workflow = Workflow.FromJson(json)!;

        List<Block> viaSections = [.. workflow.Sections.SelectMany(s => s.Blocks)];
        List<Block> viaChildren = workflow.GetAllBlocks();

        Assert.Equal(viaChildren.Count, viaSections.Count);
        for (int i = 0; i < viaChildren.Count; i++)
        {
            Assert.Same(viaChildren[i], viaSections[i]);
        }
    }

    [Fact]
    public void MintFreeReplacementTag_SkipsTagsAlreadyHeld()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "ref_1");
        _ = new Block(BlockTypes.AiQuery, section, "ref_2");

        Assert.Equal("ref_3", section.MintFreeReplacementTag("ref_", 1));
    }

    [Fact]
    public void MintFreeReplacementTag_ClampsALongPrefixToTheColumnLimit()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);

        string tag = section.MintFreeReplacementTag(new string('p', Block.MAX_REPLACEMENT_TAG_LENGTH + 20), 1);

        Assert.True(tag.Length <= Block.MAX_REPLACEMENT_TAG_LENGTH);
    }

    [Fact]
    public void MintFreeReplacementTag_WithAnOverlongPrefixThatCollides_StillTerminates()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        string overlongPrefix = new('p', Block.MAX_REPLACEMENT_TAG_LENGTH + 20);
        string firstCandidate = section.MintFreeReplacementTag(overlongPrefix, 1);
        _ = new Block(BlockTypes.AiQuery, section, firstCandidate);

        string second = section.MintFreeReplacementTag(overlongPrefix, 1);

        Assert.NotEqual(firstCandidate, second);
        Assert.True(second.Length <= Block.MAX_REPLACEMENT_TAG_LENGTH);
    }

    [Fact]
    public void FreeReplacementTagFrom_WhenThePreferredTagIsFree_ReturnsItVerbatim()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "alpha");

        Assert.Equal("beta", section.FreeReplacementTagFrom("beta"));
    }

    [Fact]
    public void FreeReplacementTagFrom_WhenThePreferredTagIsTaken_SuffixesFromTwo()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "alpha");

        Assert.Equal("alpha_2", section.FreeReplacementTagFrom("alpha"));
    }

    [Fact]
    public void FreeReplacementTagFrom_WithAnOverlongPreferredTag_ClampsToTheColumnLimit()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);

        string tag = section.FreeReplacementTagFrom(new string('t', Block.MAX_REPLACEMENT_TAG_LENGTH + 40));

        Assert.Equal(Block.MAX_REPLACEMENT_TAG_LENGTH, tag.Length);
    }

    [Fact]
    public void FreeReplacementTagFrom_ExcludingABlock_IgnoresThatBlocksOwnTag()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        var resident = new Block(BlockTypes.AiQuery, section, "alpha");

        Assert.Equal("alpha", section.FreeReplacementTagFrom("alpha", resident));
    }
}
