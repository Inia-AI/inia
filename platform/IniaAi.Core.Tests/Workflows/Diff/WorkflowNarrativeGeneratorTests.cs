using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Core.Tests.Workflows.Diff;

public class WorkflowNarrativeGeneratorTests
{
    private readonly WorkflowNarrativeGenerator _generator = new();

    [Fact]
    public void Generate_EmptyDiff_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _generator.Generate(EmptyDiff()));
    }

    [Fact]
    public void Generate_AddedBlock_NamesBlock()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff("fx", "FX rate", "AiQuery", DiffChangeKind.Added, []));

        Assert.Equal("Added block 'FX rate'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_RemovedBlock_NamesBlock()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff("fx", "FX rate", "AiQuery", DiffChangeKind.Removed, []));

        Assert.Equal("Removed block 'FX rate'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_ModifiedPrompt_SaysChangedPrompt()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "cp",
            "Counterparty name",
            "AiQuery",
            DiffChangeKind.Modified,
            [new FieldDiff("Properties.UserPromptTemplate", "old", "new")]));

        Assert.Equal("Changed the prompt in 'Counterparty name'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_TypeChanged_SaysSwitchedFromTo()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "lookup",
            "Lookup",
            "IntelligentSearch",
            DiffChangeKind.TypeChanged,
            [new FieldDiff("Type", "AiQuery", "IntelligentSearch")]));

        Assert.Equal("Switched 'Lookup' from AI query to web search", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_TypeChangedToAnExtensionType_NamesItsLabel()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "lk",
            "Lookup",
            "1000",
            DiffChangeKind.TypeChanged,
            [new FieldDiff("Type", "AiQuery", "1000")]));

        Assert.Equal("Switched 'Lookup' from AI query to Count Words", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_AddedSection_NamesSection()
    {
        WorkflowDiff diff = WithSections(new SectionDiff("Summary", 0, DiffChangeKind.Added));

        Assert.Equal("Added section 'Summary'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_RemovedSection_NamesSection()
    {
        WorkflowDiff diff = WithSections(new SectionDiff("Summary", 0, DiffChangeKind.Removed));

        Assert.Equal("Removed section 'Summary'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_ModifiedSection_SaysUpdated()
    {
        WorkflowDiff diff = WithSections(new SectionDiff("Summary", 0, DiffChangeKind.Modified));

        Assert.Equal("Updated section 'Summary'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_UnnamedSection_UsesGenericPhrase()
    {
        WorkflowDiff diff = WithSections(new SectionDiff(string.Empty, 0, DiffChangeKind.Added));

        Assert.Equal("Added a section", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_AddedVariable_NamesVariable()
    {
        var diff = new WorkflowDiff(
            [],
            [],
            [new VariableDiff("reportingDate", "Reporting date", DiffChangeKind.Added)],
            []);

        Assert.Equal("Added input variable 'Reporting date'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_AddedDocumentClass_NamesClass()
    {
        var diff = new WorkflowDiff(
            [],
            [],
            [],
            [new DocumentClassDiff("contract", "Contract", DiffChangeKind.Added)]);

        Assert.Equal("Added document type 'Contract'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_AddedLibraryLinkedBlock_SaysInsertedFromLibrary()
    {
        WorkflowDiff diff = WithBlocks(
            new BlockDiff("fx", "FX rate", "AiQuery", DiffChangeKind.Added, []) { IsLibraryLinked = true });

        Assert.Equal("Inserted block 'FX rate' from the Library", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_LinkOnlyBlockChange_SaysSharedToLibrary()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "fx",
            "FX rate",
            "AiQuery",
            DiffChangeKind.Modified,
            [
                new FieldDiff("SharedComponentId", null, "component-1"),
                new FieldDiff("SharedComponentLinkState", null, "Linked"),
                new FieldDiff("SharedComponentVersionHash", null, new string('a', 64)),
            ])
        { IsLibraryLinked = true });

        Assert.Equal("Shared 'FX rate' to the Library", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_BrokenOutBlockChange_SaysDisconnected()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "fx",
            "FX rate",
            "AiQuery",
            DiffChangeKind.Modified,
            [new FieldDiff("SharedComponentLinkState", "Linked", "BrokenOut")]));

        Assert.Equal("Disconnected 'FX rate' from the Library", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_LinkHashOnlyChange_SaysSyncedWithLibrary()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "fx",
            "FX rate",
            "AiQuery",
            DiffChangeKind.Modified,
            [new FieldDiff("SharedComponentVersionHash", new string('a', 64), new string('b', 64))])
        { IsLibraryLinked = true });

        Assert.Equal("Synced 'FX rate' with the Library", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_HashAndContentBlockChange_SaysUpdatedToLatestLibraryVersion()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "fx",
            "FX rate",
            "AiQuery",
            DiffChangeKind.Modified,
            [
                new FieldDiff("SharedComponentVersionHash", new string('a', 64), new string('b', 64)),
                new FieldDiff("Properties.UserPromptTemplate", "old", "new"),
            ])
        { IsLibraryLinked = true });

        Assert.Equal("Updated 'FX rate' to the latest Library version", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_BreakOutWithContentAndHashChange_DoesNotClaimLibraryUpdate()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "fx",
            "FX rate",
            "AiQuery",
            DiffChangeKind.Modified,
            [
                new FieldDiff("SharedComponentLinkState", "Linked", "BrokenOut"),
                new FieldDiff("SharedComponentVersionHash", new string('a', 64), new string('b', 64)),
                new FieldDiff("Properties.UserPromptTemplate", "old", "new"),
            ]));

        Assert.Equal("Changed the prompt in 'FX rate'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_MixedLinkAndPromptChange_FallsBackToExistingArms()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "cp",
            "Counterparty name",
            "AiQuery",
            DiffChangeKind.Modified,
            [
                new FieldDiff("SharedComponentId", null, "component-1"),
                new FieldDiff("Properties.UserPromptTemplate", "old", "new"),
            ])
        { IsLibraryLinked = true });

        Assert.Equal("Changed the prompt in 'Counterparty name'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_AddedLibraryLinkedSection_SaysInsertedFromLibrary()
    {
        WorkflowDiff diff = WithSections(
            new SectionDiff("Summary", 0, DiffChangeKind.Added) { IsLibraryLinked = true });

        Assert.Equal("Inserted section 'Summary' from the Library", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_LinkOnlySectionChange_SaysSharedToLibrary()
    {
        WorkflowDiff diff = WithSections(new SectionDiff("Summary", 0, DiffChangeKind.Modified)
        {
            IsLibraryLinked = true,
            FieldDiffs =
            [
                new FieldDiff("SharedComponentId", null, "component-2"),
                new FieldDiff("SharedComponentLinkState", null, "Linked"),
            ],
        });

        Assert.Equal("Shared section 'Summary' to the Library", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_SectionWithLinkAndTitleChange_FallsBackToUpdated()
    {
        WorkflowDiff diff = WithSections(new SectionDiff("Summary", 0, DiffChangeKind.Modified)
        {
            IsLibraryLinked = true,
            FieldDiffs =
            [
                new FieldDiff("SharedComponentId", null, "component-2"),
                new FieldDiff("Title", "Old", "Summary"),
            ],
        });

        Assert.Equal("Updated section 'Summary'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_HashAndContentSectionChange_SaysUpdatedToLatestLibraryVersion()
    {
        WorkflowDiff diff = WithSections(new SectionDiff("Summary", 0, DiffChangeKind.Modified)
        {
            IsLibraryLinked = true,
            FieldDiffs =
            [
                new FieldDiff("SharedComponentVersionHash", new string('a', 64), new string('b', 64)),
                new FieldDiff("Title", "Old", "Summary"),
            ],
        });

        Assert.Equal("Updated section 'Summary' to the latest Library version", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_HashOnlySectionChange_SaysSyncedWithLibrary()
    {
        WorkflowDiff diff = WithSections(new SectionDiff("Summary", 0, DiffChangeKind.Modified)
        {
            IsLibraryLinked = true,
            FieldDiffs = [new FieldDiff("SharedComponentVersionHash", new string('a', 64), new string('b', 64))],
        });

        Assert.Equal("Synced section 'Summary' with the Library", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_MultipleChanges_JoinedWithSemicolon()
    {
        var diff = new WorkflowDiff(
            [],
            [new BlockDiff("fx", "FX rate", "AiQuery", DiffChangeKind.Added, [])],
            [new VariableDiff("d", "Date", DiffChangeKind.Added)],
            []);

        string narrative = _generator.Generate(diff);

        Assert.Contains("; ", narrative);
        Assert.Contains("Added input variable 'Date'", narrative);
        Assert.Contains("Added block 'FX rate'", narrative);
    }

    [Fact]
    public void Generate_LongPromptChange_IncludesApproximateLineCount()
    {
        string longPrompt = string.Join("\n", Enumerable.Range(1, 7).Select(i => $"line {i}"));
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "cp",
            "Counterparty name",
            "AiQuery",
            DiffChangeKind.Modified,
            [new FieldDiff("Properties.UserPromptTemplate", "short", longPrompt)]));

        string narrative = _generator.Generate(diff);

        Assert.Contains("Changed the prompt in 'Counterparty name'", narrative);
        Assert.Contains("7 lines", narrative);
    }

    [Fact]
    public void Generate_OneSentenceAddedToALongPrompt_CountsTheChangedLinesNotThePrompt()
    {
        List<string> lines = [.. Enumerable.Range(1, 28).Select(i => $"line {i}")];
        string before = string.Join("\n", lines);
        lines.Insert(20, "Write every amount in full digits.");
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "cp",
            "Counterparty name",
            "AiQuery",
            DiffChangeKind.Modified,
            [new FieldDiff("Properties.UserPromptTemplate", before, string.Join("\n", lines))]));

        Assert.Equal("Changed the prompt in 'Counterparty name'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_OutputTitleChange_DescribesTitle()
    {
        WorkflowDiff diff = WithWorkflowFields(new FieldDiff("Title", "Old title", "New title"));

        Assert.Equal("Changed the output title", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_RenamedWorkflow_SaysRenamed()
    {
        WorkflowDiff diff = WithWorkflowFields(new FieldDiff("Name", "Old", "New"));

        Assert.Equal("Renamed the workflow", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_UnknownWorkflowField_FallsBackToGenericSummary()
    {
        WorkflowDiff diff = WithWorkflowFields(new FieldDiff("ParameterizedConfig", "a", "b"));

        Assert.Equal("Updated workflow settings", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_OutputTitleAndBlockChange_DescribesBoth()
    {
        var diff = new WorkflowDiff(
            [],
            [new BlockDiff("fx", "FX rate", "AiQuery", DiffChangeKind.Added, [])],
            [],
            [])
        {
            WorkflowFields = [new FieldDiff("Title", "Old", "New")],
        };

        string narrative = _generator.Generate(diff);

        Assert.Contains("Added block 'FX rate'", narrative);
        Assert.Contains("Changed the output title", narrative);
    }

    [Fact]
    public void GenerateParts_NameContainingSeparator_StaysOneEntry()
    {
        var diff = new WorkflowDiff(
            [],
            [],
            [new VariableDiff("v", "Maturity; settlement date", DiffChangeKind.Added)],
            []);

        IReadOnlyList<string> parts = _generator.GenerateParts(diff);

        Assert.Single(parts);
        Assert.Equal("Added input variable 'Maturity; settlement date'", parts[0]);
    }

    [Fact]
    public void Generate_EndToEnd_TitleOnlySection_NamesSectionByTitle()
    {
        var canonicalizer = new WorkflowCanonicalizer();
        var diffService = new WorkflowDiffService();
        Workflow before = SingleSectionWorkflow();
        Workflow after = SingleSectionWorkflow();
        var added = new Section(string.Empty, after) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, added, "r1")
        {
            Name = "Block r1",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt r1" },
        };

        WorkflowDiff diff = diffService.Compare(
            canonicalizer.ToCanonicalJson(before),
            canonicalizer.ToCanonicalJson(after));
        string narrative = _generator.Generate(diff);

        Assert.Contains("Added section 'Risk'", narrative);
        Assert.DoesNotContain("Added a section", narrative);
    }

    [Fact]
    public void Generate_RefTransitionToLibrary_SaysNowReferences()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "kyc",
            "kyc",
            "AiQuery",
            DiffChangeKind.Modified,
            [new FieldDiff("SharedComponentId", null, "component-1")])
        { IsLibraryLinked = true });

        Assert.Equal("'kyc' now references Library block 'component-1'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_RefInlined_SaysInlinedLibraryBlock()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "kyc",
            "kyc",
            "AiQuery",
            DiffChangeKind.Modified,
            [new FieldDiff("SharedComponentId", "component-1", null)]));

        Assert.Equal("Inlined Library block 'component-1' into 'kyc'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_RefRetargeted_SaysNowReferencesWithWas()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "kyc",
            "kyc",
            string.Empty,
            DiffChangeKind.Modified,
            [new FieldDiff("SharedComponentId", "component-1", "component-2")])
        { IsLibraryLinked = true });

        Assert.Equal(
            "'kyc' now references Library block 'component-2' (was 'component-1')",
            _generator.Generate(diff));
    }

    [Fact]
    public void Generate_AddedRefSlot_SaysAddedReferencing()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "kyc",
            "kyc",
            string.Empty,
            DiffChangeKind.Added,
            [new FieldDiff("SharedComponentId", null, "component-1")])
        { IsLibraryLinked = true });

        Assert.Equal("Added 'kyc' referencing Library block 'component-1'", _generator.Generate(diff));
    }

    [Fact]
    public void Generate_RemovedRefSlot_SaysRemovedWhichReferenced()
    {
        WorkflowDiff diff = WithBlocks(new BlockDiff(
            "kyc",
            "kyc",
            string.Empty,
            DiffChangeKind.Removed,
            [new FieldDiff("SharedComponentId", "component-1", null)]));

        Assert.Equal("Removed 'kyc', which referenced Library block 'component-1'", _generator.Generate(diff));
    }

    private static Workflow SingleSectionWorkflow()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "b1")
        {
            Name = "Block b1",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt b1" },
        };

        return workflow;
    }

    private static WorkflowDiff EmptyDiff()
    {
        return new WorkflowDiff([], [], [], []);
    }

    private static WorkflowDiff WithBlocks(params BlockDiff[] blocks)
    {
        return new WorkflowDiff([], blocks, [], []);
    }

    private static WorkflowDiff WithSections(params SectionDiff[] sections)
    {
        return new WorkflowDiff(sections, [], [], []);
    }

    private static WorkflowDiff WithWorkflowFields(params FieldDiff[] fields)
    {
        return new WorkflowDiff([], [], [], []) { WorkflowFields = fields };
    }
}
