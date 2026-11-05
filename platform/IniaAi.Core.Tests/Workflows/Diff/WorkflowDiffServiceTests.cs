using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Core.Tests.Workflows.Diff;

public class WorkflowDiffServiceTests
{
    private readonly WorkflowCanonicalizer _canonicalizer = new();
    private readonly WorkflowDiffService _diffService = new();

    private WorkflowDiff DiffOf(Workflow before, Workflow after)
    {
        return _diffService.Compare(
            _canonicalizer.ToCanonicalJson(before),
            _canonicalizer.ToCanonicalJson(after));
    }

    private static Workflow WorkflowWithSkills(params string[] names)
    {
        var workflow = new Workflow("skills") { Title = "skills", WorkflowTypeId = "skills" };
        var section = new Section("body", workflow);
        var block = new Block(BlockTypes.Agent, section, "agent_block");
        var props = (AgentProperties)block.Properties;
        foreach (string name in names)
        {
            props.WorkerSkills.Add(new AgentWorkerSkill
            {
                Name = name,
                Description = $"Handles {name}.",
                Content = $"Body of {name}.",
            });
        }

        return workflow;
    }

    [Fact]
    public void Compare_AChangedDate_IsShownAsWritten()
    {
        Workflow workflow = SearchWorkflow("2023-01-01T00:00:00Z");
        string before = _canonicalizer.ToCanonicalJson(workflow);
        ((IntelligentSearchProperties)workflow.Sections.Single().Blocks.Single().Properties).StartPublishedDate = "2024-03-12T00:00:00.000Z";

        WorkflowDiff diff = _diffService.Compare(before, _canonicalizer.ToCanonicalJson(workflow));

        FieldDiff date = Assert.Single(Assert.Single(diff.Blocks).FieldDiffs);
        Assert.Equal("2023-01-01T00:00:00Z", date.Before);
        Assert.Equal("2024-03-12T00:00:00.000Z", date.After);
    }

    private static Workflow SearchWorkflow(string startPublishedDate)
    {
        var workflow = new Workflow("search") { Title = "search", WorkflowTypeId = "search" };
        var section = new Section("body", workflow);
        _ = new Block(BlockTypes.IntelligentSearch, section, "search_block")
        {
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "term", StartPublishedDate = startPublishedDate },
        };

        return workflow;
    }

    [Fact]
    public void Compare_RemovingOneSkill_DoesNotReportTheLaterSkillsAsEdited()
    {
        WorkflowDiff diff = DiffOf(
            WorkflowWithSkills("alpha", "beta", "gamma"),
            WorkflowWithSkills("beta", "gamma"));

        Assert.DoesNotContain(diff.ToString() ?? string.Empty, "beta");
        Assert.False(diff.IsEmpty);
    }

    [Fact]
    public void Compare_ReorderingSkills_IsNotAChange()
    {
        Assert.True(DiffOf(
            WorkflowWithSkills("alpha", "beta"),
            WorkflowWithSkills("beta", "alpha")).IsEmpty);
    }

    [Fact]
    public void Compare_IdenticalWorkflows_EmptyDiff()
    {
        Assert.True(DiffOf(BuildBase(), BuildBase()).IsEmpty);
    }

    [Fact]
    public void Compare_PayloadsDifferingOnlyInStableIds_IsEmpty()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        foreach (Block block in after.GetAllBlocks())
        {
            block.StableBlockId = "changed_" + block.StableBlockId;
        }

        foreach (Section section in after.Sections)
        {
            section.StableSectionId = "changed_" + section.StableSectionId;
        }

        Assert.True(DiffOf(before, after).IsEmpty);
    }

    [Fact]
    public void Compare_AddedBlock_DetectedByTag()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        AddBlock(after, "b2");

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff added = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Added);
        Assert.Equal("b2", added.ReplacementTag);
    }

    [Fact]
    public void Compare_RemovedBlock_DetectedByTag()
    {
        Workflow before = BuildBase();
        AddBlock(before, "b2");
        Workflow after = BuildBase();

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff removed = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Removed);
        Assert.Equal("b2", removed.ReplacementTag);
    }

    [Fact]
    public void Compare_ModifiedBlockPrompt_ProducesFieldDiff()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        SetPrompt(after, "b1", "a changed prompt");

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff modified = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        Assert.Equal("b1", modified.ReplacementTag);
        Assert.Contains(modified.FieldDiffs, f => f.FieldName.Contains("UserPromptTemplate"));
    }

    [Fact]
    public void Compare_ReorderedSameTags_NoChange()
    {
        Workflow before = TwoBlockWorkflow("b1", "b2");
        Workflow after = TwoBlockWorkflow("b2", "b1");

        Assert.True(DiffOf(before, after).IsEmpty);
    }

    [Fact]
    public void Compare_RemovedMiddleSection_ReportsSingleRemoval_NotMassUpdates()
    {
        Workflow before = MultiSectionWorkflow(sectionCount: 6);
        Workflow after = MultiSectionWorkflow(sectionCount: 6, removeSectionIndex: 2);

        WorkflowDiff diff = DiffOf(before, after);

        SectionDiff removed = Assert.Single(diff.Sections);
        Assert.Equal(DiffChangeKind.Removed, removed.ChangeKind);
        Assert.DoesNotContain(diff.Sections, section => section.ChangeKind == DiffChangeKind.Modified);
    }

    [Fact]
    public void Compare_ReorderedSections_NoSectionChange()
    {
        Workflow before = MultiSectionWorkflow(sectionCount: 4);
        Workflow after = MultiSectionWorkflow(sectionCount: 4, reversed: true);

        Assert.Empty(DiffOf(before, after).Sections);
    }

    [Fact]
    public void Compare_RemovedBlockFromSection_DoesNotReportSectionAddedOrRemoved()
    {
        Workflow before = TwoBlockWorkflow("b1", "b2");
        Workflow after = SingleBlockWorkflow("b1", BlockTypes.AiQuery);

        WorkflowDiff diff = DiffOf(before, after);

        Assert.Empty(diff.Sections);
        BlockDiff removed = Assert.Single(diff.Blocks, block => block.ChangeKind == DiffChangeKind.Removed);
        Assert.Equal("b2", removed.ReplacementTag);
    }

    [Fact]
    public void Compare_AddedVariable_Detected()
    {
        Workflow after = BuildBase();
        _ = new WorkflowVariable(after, "reportingDate") { DisplayName = "Reporting date" };

        WorkflowDiff diff = DiffOf(BuildBase(), after);

        VariableDiff added = Assert.Single(diff.Variables, v => v.ChangeKind == DiffChangeKind.Added);
        Assert.Equal("reportingDate", added.ReplacementTag);
    }

    [Fact]
    public void Compare_AddedDocumentClass_Detected()
    {
        Workflow after = BuildBase();
        _ = new DocumentClass(after, "contract") { DisplayName = "Contract" };

        WorkflowDiff diff = DiffOf(BuildBase(), after);

        DocumentClassDiff added = Assert.Single(diff.DocumentClasses, d => d.ChangeKind == DiffChangeKind.Added);
        Assert.Equal("contract", added.Key);
    }

    [Fact]
    public void Compare_RagReorderedSameContent_NoChange()
    {
        Assert.True(DiffOf(RagWorkflow(reversed: false), RagWorkflow(reversed: true)).IsEmpty);
    }

    [Fact]
    public void Compare_SwitchCaseCasesReordered_NoChange()
    {
        Workflow before = SwitchCaseWorkflow(new Dictionary<string, string> { ["xray"] = "1", ["alpha"] = "2" });
        Workflow after = SwitchCaseWorkflow(new Dictionary<string, string> { ["alpha"] = "2", ["xray"] = "1" });

        Assert.True(DiffOf(before, after).IsEmpty);
    }

    [Fact]
    public void Compare_BlockTypeChanged_SingleTypeChanged_NotPropertyChurn()
    {
        Workflow before = SingleBlockWorkflow("b1", BlockTypes.AiQuery);
        Workflow after = SingleBlockWorkflow("b1", BlockTypes.IntelligentSearch);

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff changed = Assert.Single(diff.Blocks);
        Assert.Equal(DiffChangeKind.TypeChanged, changed.ChangeKind);
        Assert.Equal("b1", changed.ReplacementTag);
        Assert.All(changed.FieldDiffs, f => Assert.Equal("Type", f.FieldName));
    }

    [Fact]
    public void Compare_ChangedOutputTitle_ProducesWorkflowFieldDiffWithNoBlockChange()
    {
        Workflow before = BuildBase();
        before.Title = "Original title";
        Workflow after = BuildBase();
        after.Title = "New title";

        WorkflowDiff diff = DiffOf(before, after);

        Assert.False(diff.IsEmpty);
        Assert.Empty(diff.Blocks);
        FieldDiff titleDiff = Assert.Single(diff.WorkflowFields, field => field.FieldName == "Title");
        Assert.Equal("New title", titleDiff.After);
    }

    [Fact]
    public void Compare_IdenticalWorkflows_HasNoWorkflowFieldDiff()
    {
        Assert.Empty(DiffOf(BuildBase(), BuildBase()).WorkflowFields);
    }

    [Fact]
    public void Compare_AddedSection_WithEmptyNameAndTitleSet_ReportsTitle()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        var added = new Section(string.Empty, after) { Title = "Risk" };
        AddTypedBlock(added, "r1", BlockTypes.AiQuery);

        WorkflowDiff diff = DiffOf(before, after);

        SectionDiff section = Assert.Single(diff.Sections, s => s.ChangeKind == DiffChangeKind.Added);
        Assert.Equal("Risk", section.Name);
    }

    [Fact]
    public void Compare_RemovedSection_WithEmptyNameAndTitleSet_ReportsTitle()
    {
        Workflow before = BuildBase();
        var removed = new Section(string.Empty, before) { Title = "Risk" };
        AddTypedBlock(removed, "r1", BlockTypes.AiQuery);
        Workflow after = BuildBase();

        WorkflowDiff diff = DiffOf(before, after);

        SectionDiff section = Assert.Single(diff.Sections, s => s.ChangeKind == DiffChangeKind.Removed);
        Assert.Equal("Risk", section.Name);
    }

    [Fact]
    public void Compare_SectionWithNameOnly_FallsBackToName()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        var added = new Section("Legacy", after);
        AddTypedBlock(added, "r1", BlockTypes.AiQuery);

        WorkflowDiff diff = DiffOf(before, after);

        SectionDiff section = Assert.Single(diff.Sections, s => s.ChangeKind == DiffChangeKind.Added);
        Assert.Equal("Legacy", section.Name);
    }

    [Fact]
    public void Compare_SectionWithBothNameAndTitle_PrefersTitle()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        var added = new Section("internal", after) { Title = "Reader title" };
        AddTypedBlock(added, "r1", BlockTypes.AiQuery);

        WorkflowDiff diff = DiffOf(before, after);

        SectionDiff section = Assert.Single(diff.Sections, s => s.ChangeKind == DiffChangeKind.Added);
        Assert.Equal("Reader title", section.Name);
    }

    [Fact]
    public void Compare_RagTypeChanged_ProducesModifiedBlockWithRagFieldDiff()
    {
        Workflow before = RagWorkflow(RagTypes.WholeDocument);
        Workflow after = RagWorkflow(RagTypes.TopNPages);

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff modified = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        Assert.Contains(modified.FieldDiffs, f => f.FieldName.Contains("Rag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_SwitchCaseValueChanged_ProducesFieldDiffWithBeforeAfter()
    {
        Workflow before = SwitchCaseWorkflow(new Dictionary<string, string> { ["alpha"] = "1", ["beta"] = "2" });
        Workflow after = SwitchCaseWorkflow(new Dictionary<string, string> { ["alpha"] = "1", ["beta"] = "99" });

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff modified = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        FieldDiff caseDiff = Assert.Single(modified.FieldDiffs);
        Assert.Equal("2", caseDiff.Before);
        Assert.Equal("99", caseDiff.After);
    }

    [Fact]
    public void Compare_BlockRenamed_ProducesNameFieldDiff_AndReportsNewName()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        after.GetAllBlocks().First(b => b.ReplacementTag == "b1").Name = "Renamed block";

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff modified = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        Assert.Equal("Renamed block", modified.DisplayName);
        FieldDiff nameDiff = Assert.Single(modified.FieldDiffs, f => f.FieldName == "Name");
        Assert.Equal("Block b1", nameDiff.Before);
        Assert.Equal("Renamed block", nameDiff.After);
    }

    [Fact]
    public void Compare_VariableDisplayNameChanged_ReportsModifiedVariable()
    {
        Workflow before = BuildBase();
        _ = new WorkflowVariable(before, "reportingDate") { DisplayName = "Reporting date" };
        Workflow after = BuildBase();
        _ = new WorkflowVariable(after, "reportingDate") { DisplayName = "Valuation date" };

        WorkflowDiff diff = DiffOf(before, after);

        VariableDiff modified = Assert.Single(diff.Variables);
        Assert.Equal(DiffChangeKind.Modified, modified.ChangeKind);
        Assert.Equal("Valuation date", modified.DisplayName);
    }

    [Fact]
    public void Compare_DocumentClassRenamed_ReportsModifiedClass()
    {
        Workflow before = BuildBase();
        _ = new DocumentClass(before, "contract") { DisplayName = "Contract" };
        Workflow after = BuildBase();
        _ = new DocumentClass(after, "contract") { DisplayName = "Agreement" };

        WorkflowDiff diff = DiffOf(before, after);

        DocumentClassDiff modified = Assert.Single(diff.DocumentClasses);
        Assert.Equal(DiffChangeKind.Modified, modified.ChangeKind);
        Assert.Equal("Agreement", modified.DisplayName);
        FieldDiff field = Assert.Single(modified.FieldDiffs);
        Assert.Equal("DisplayName", field.FieldName);
        Assert.Equal("Contract", field.Before);
        Assert.Equal("Agreement", field.After);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compare_RepeatedTagAcrossSections_ReportsTheEditedBlock_NotTheLastOne(bool withStableIds)
    {
        Workflow before = RepeatedTagWorkflow("first prompt", "second prompt", withStableIds);
        Workflow after = RepeatedTagWorkflow("first prompt EDITED", "second prompt", withStableIds);

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff modified = Assert.Single(diff.Blocks);
        Assert.Equal(DiffChangeKind.Modified, modified.ChangeKind);
        Assert.Equal("shared", modified.ReplacementTag);
        Assert.Equal("First", modified.SectionName);
        FieldDiff field = Assert.Single(modified.FieldDiffs);
        Assert.Equal("first prompt", field.Before);
        Assert.Equal("first prompt EDITED", field.After);
    }

    [Fact]
    public void Compare_RepeatedTagWithStableIds_SectionsReordered_IsNotAChange()
    {
        Workflow before = RepeatedTagWorkflow("first prompt", "second prompt", withStableIds: true);
        Workflow after = RepeatedTagWorkflow("first prompt", "second prompt", withStableIds: true, reversed: true);

        Assert.Empty(DiffOf(before, after).Blocks);
    }

    [Fact]
    public void Compare_TagDuplicatedIntoAnotherSection_ReportsOnlyTheNewBlock()
    {
        Workflow before = RepeatedTagWorkflow("first prompt", "second prompt", withStableIds: true);
        Workflow after = RepeatedTagWorkflow("first prompt", "second prompt", withStableIds: true);
        var third = new Section("Third", after) { Title = "Third" };
        AddTypedBlock(third, "shared", BlockTypes.AiQuery);
        third.Blocks.Single().StableBlockId = "stable-third";

        BlockDiff added = Assert.Single(DiffOf(before, after).Blocks);

        Assert.Equal(DiffChangeKind.Added, added.ChangeKind);
        Assert.Equal("Third", added.SectionName);
        Assert.Equal("stable-third", added.StableBlockId);
    }

    [Fact]
    public void Compare_ModifiedVariable_CarriesFieldDiffs()
    {
        Workflow before = BuildBase();
        _ = new WorkflowVariable(before, "currency") { DefaultValue = "USD" };
        Workflow after = BuildBase();
        _ = new WorkflowVariable(after, "currency") { DefaultValue = "EUR" };

        VariableDiff modified = Assert.Single(DiffOf(before, after).Variables);

        Assert.Equal(DiffChangeKind.Modified, modified.ChangeKind);
        FieldDiff field = Assert.Single(modified.FieldDiffs);
        Assert.Equal("DefaultValue", field.FieldName);
        Assert.Equal("USD", field.Before);
        Assert.Equal("EUR", field.After);
    }

    [Fact]
    public void Compare_BlockDiffs_NameTheirSectionAndStableId_RemovedBlockFromTheBeforeSide()
    {
        Workflow before = MultiSectionWorkflow(2);
        Block removedBlock = before.Sections.Single(section => section.Title == "S1").Blocks.Single();
        removedBlock.StableBlockId = "stable-removed";
        Block keptBefore = before.Sections.Single(section => section.Title == "S0").Blocks.Single();
        keptBefore.StableBlockId = "stable-kept";

        Workflow after = MultiSectionWorkflow(2, removeSectionIndex: 1);
        Section keptSection = after.Sections.Single();
        keptSection.Title = "Renamed";
        keptSection.Blocks.Single().StableBlockId = "stable-kept";
        SetPrompt(after, "s0b", "changed prompt");

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff removed = Assert.Single(diff.Blocks, block => block.ChangeKind == DiffChangeKind.Removed);
        Assert.Equal("S1", removed.SectionName);
        Assert.Equal("stable-removed", removed.StableBlockId);

        BlockDiff modified = Assert.Single(diff.Blocks, block => block.ChangeKind == DiffChangeKind.Modified);
        Assert.Equal("Renamed", modified.SectionName);
        Assert.Equal("stable-kept", modified.StableBlockId);
    }

    [Fact]
    public void Compare_SignatureFieldDefinitionChanged_ProducesSignatureFieldDiff()
    {
        Workflow before = SignatureWorkflow(defaultValue: "10 pages");
        Workflow after = SignatureWorkflow(defaultValue: "25 pages");

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff modified = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        FieldDiff signatureDiff = Assert.Single(
            modified.FieldDiffs,
            f => f.FieldName.Contains("SignatureFields.page_limit", StringComparison.Ordinal));
        Assert.Equal("10 pages", signatureDiff.Before);
        Assert.Equal("25 pages", signatureDiff.After);
    }

    [Fact]
    public void Compare_AddedSignaturePlaceholder_ProducesFieldDiffs()
    {
        Workflow before = BuildBase();
        Workflow after = SignatureWorkflow(defaultValue: "10 pages");

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff modified = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        Assert.Contains(
            modified.FieldDiffs,
            f => f.FieldName.Contains("SignatureFields.page_limit", StringComparison.Ordinal) && f.Before is null);
    }

    [Fact]
    public void Compare_ReplacementTagRenamed_ReadsAsRemoveAndAdd()
    {
        // Blocks are matched by ReplacementTag, so a tag rename has no way to read as a
        // modification. This pins the known semantics: one Removed plus one Added.
        Workflow before = SingleBlockWorkflow("oldTag", BlockTypes.AiQuery);
        Workflow after = SingleBlockWorkflow("newTag", BlockTypes.AiQuery);

        WorkflowDiff diff = DiffOf(before, after);

        Assert.Equal(2, diff.Blocks.Count);
        Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Removed && b.ReplacementTag == "oldTag");
        Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Added && b.ReplacementTag == "newTag");
    }

    [Fact]
    public void Compare_AddedLinkedBlock_FlagsLibraryLinked()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        AddBlock(after, "b2");
        LinkBlock(after, "b2");

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff added = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Added);
        Assert.True(added.IsLibraryLinked);
    }

    [Fact]
    public void Compare_AddedPlainBlock_NotLibraryLinked()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        AddBlock(after, "b2");

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff added = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Added);
        Assert.False(added.IsLibraryLinked);
    }

    [Fact]
    public void Compare_LinkOnlyBlockChange_AllFieldDiffsSharedComponentPrefixed_AndFlagged()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        LinkBlock(after, "b1");
        after.GetAllBlocks().First(b => b.ReplacementTag == "b1").SharedComponentBindings =
            new Dictionary<string, string> { ["slot"] = "b1" };

        WorkflowDiff diff = DiffOf(before, after);

        BlockDiff modified = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        Assert.True(modified.IsLibraryLinked);
        Assert.NotEmpty(modified.FieldDiffs);
        Assert.All(
            modified.FieldDiffs,
            f => Assert.StartsWith("SharedComponent", f.FieldName, StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_AddedLinkedSection_FlagsLibraryLinked()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        var added = new Section("Risk", after) { Title = "Risk" };
        AddTypedBlock(added, "r1", BlockTypes.AiQuery);
        LinkSection(added);

        WorkflowDiff diff = DiffOf(before, after);

        SectionDiff section = Assert.Single(diff.Sections, s => s.ChangeKind == DiffChangeKind.Added);
        Assert.True(section.IsLibraryLinked);
    }

    [Fact]
    public void Compare_LinkOnlySectionChange_ProducesPrefixedSectionFieldDiffs()
    {
        Workflow before = BuildBase();
        Workflow after = BuildBase();
        LinkSection(after.Sections.First());

        WorkflowDiff diff = DiffOf(before, after);

        SectionDiff modified = Assert.Single(diff.Sections, s => s.ChangeKind == DiffChangeKind.Modified);
        Assert.True(modified.IsLibraryLinked);
        Assert.NotEmpty(modified.FieldDiffs);
        Assert.All(
            modified.FieldDiffs,
            f => Assert.StartsWith("SharedComponent", f.FieldName, StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_TextInputTemplateChanged_ProducesWorkflowFieldDiff()
    {
        Workflow before = BuildBase();
        before.TextInputTemplate = "Original intro";
        Workflow after = BuildBase();
        after.TextInputTemplate = "Revised intro";

        WorkflowDiff diff = DiffOf(before, after);

        FieldDiff field = Assert.Single(diff.WorkflowFields, f => f.FieldName == "TextInputTemplate");
        Assert.Equal("Original intro", field.Before);
        Assert.Equal("Revised intro", field.After);
    }

    [Fact]
    public void Compare_DropdownVariableOptionsChanged_ReportsModifiedVariable()
    {
        Workflow before = BuildBase();
        _ = new WorkflowVariable(before, "locale")
        {
            DisplayName = "Locale",
            Type = WorkflowVariableTypes.Dropdown,
            Options = new Dictionary<string, string> { ["en"] = "English" },
        };
        Workflow after = BuildBase();
        _ = new WorkflowVariable(after, "locale")
        {
            DisplayName = "Locale",
            Type = WorkflowVariableTypes.Dropdown,
            Options = new Dictionary<string, string> { ["en"] = "English", ["de"] = "German" },
        };

        WorkflowDiff diff = DiffOf(before, after);

        VariableDiff modified = Assert.Single(diff.Variables);
        Assert.Equal(DiffChangeKind.Modified, modified.ChangeKind);
    }

    private static Workflow SignatureWorkflow(string defaultValue)
    {
        Workflow workflow = BuildBase();
        Block block = workflow.GetAllBlocks().First(b => b.ReplacementTag == "b1");
        ((AiQueryProperties)block.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["page_limit"] = new SignatureFieldDefinition("Page limit", "How many pages to read", defaultValue),
        };

        return workflow;
    }

    private static Workflow RagWorkflow(RagTypes ragType)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        var section = new Section("Body", workflow) { Title = "Body" };
        var block = new Block(BlockTypes.AiQuery, section, "b1")
        {
            Name = "Block b1",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt b1" },
        };
        _ = new RagSettings(block) { Type = ragType };

        return workflow;
    }

    private static Workflow RepeatedTagWorkflow(string firstPrompt, string secondPrompt, bool withStableIds, bool reversed = false)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        (string Title, string Prompt, string StableId)[] sections =
        [
            ("First", firstPrompt, "stable-first"),
            ("Second", secondPrompt, "stable-second"),
        ];

        foreach ((string title, string prompt, string stableId) in reversed ? sections.Reverse() : sections)
        {
            var section = new Section(title, workflow) { Title = title };
            AddTypedBlock(section, "shared", BlockTypes.AiQuery);
            Block block = section.Blocks.Single();
            ((AiQueryProperties)block.Properties).UserPromptTemplate = prompt;
            block.StableBlockId = withStableIds ? stableId : null;
        }

        return workflow;
    }

    private static Workflow BuildBase()
    {
        return SingleBlockWorkflow("b1", BlockTypes.AiQuery);
    }

    private static Workflow SingleBlockWorkflow(string tag, BlockTypes type)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        var section = new Section("Body", workflow) { Title = "Body" };
        AddTypedBlock(section, tag, type);

        return workflow;
    }

    private static Workflow MultiSectionWorkflow(int sectionCount, int? removeSectionIndex = null, bool reversed = false)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        IEnumerable<int> indices = Enumerable.Range(0, sectionCount);
        if (reversed)
        {
            indices = indices.Reverse();
        }

        foreach (int i in indices)
        {
            if (i == removeSectionIndex)
            {
                continue;
            }

            var section = new Section($"S{i}", workflow) { Title = $"S{i}" };
            AddTypedBlock(section, $"s{i}b", BlockTypes.AiQuery);
        }

        return workflow;
    }

    private static Workflow TwoBlockWorkflow(string firstTag, string secondTag)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        var section = new Section("Body", workflow) { Title = "Body" };
        AddTypedBlock(section, firstTag, BlockTypes.AiQuery);
        AddTypedBlock(section, secondTag, BlockTypes.AiQuery);

        return workflow;
    }

    private static Workflow RagWorkflow(bool reversed)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        var section = new Section("Body", workflow) { Title = "Body" };
        var block = new Block(BlockTypes.AiQuery, section, "b1")
        {
            Name = "Block b1",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt b1" },
        };

        if (reversed)
        {
            _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        }
        else
        {
            _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        }

        return workflow;
    }

    private static Workflow SwitchCaseWorkflow(Dictionary<string, string> cases)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.SwitchCase, section, "sw")
        {
            Name = "Switch",
            Properties = new SwitchCaseProperties { Cases = cases, InputTemplate = "{in}" },
        };

        return workflow;
    }

    private static void AddBlock(Workflow workflow, string tag)
    {
        AddTypedBlock(workflow.Sections.First(), tag, BlockTypes.AiQuery);
    }

    private static void LinkBlock(Workflow workflow, string tag)
    {
        Block block = workflow.GetAllBlocks().First(b => b.ReplacementTag == tag);
        block.SharedComponentId = "component-1";
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
    }

    private static void LinkSection(Section section)
    {
        section.SharedComponentId = "component-2";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = new string('b', 64);
    }

    private static void AddTypedBlock(Section section, string tag, BlockTypes type)
    {
        var block = new Block(type, section, tag) { Name = $"Block {tag}" };
        if (block.Properties is AiQueryProperties aiProperties)
        {
            aiProperties.UserPromptTemplate = $"Prompt {tag}";
        }
    }

    private static void SetPrompt(Workflow workflow, string tag, string prompt)
    {
        Block block = workflow.GetAllBlocks().First(b => b.ReplacementTag == tag);
        ((AiQueryProperties)block.Properties).UserPromptTemplate = prompt;
    }
}
