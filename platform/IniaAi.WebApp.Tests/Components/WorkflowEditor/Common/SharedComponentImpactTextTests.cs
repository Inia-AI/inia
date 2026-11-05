using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class SharedComponentImpactTextTests
{
    [Fact]
    public void DescribeLinkedCopies_SingleCopySingleType_UsesSingularForms()
    {
        SharedComponentConsumer[] consumers = [new("WT-1", "NS Testing", 1)];

        Assert.Equal("1 linked copy across 1 workflow type", SharedComponentImpactText.DescribeLinkedCopies(consumers));
    }

    [Fact]
    public void DescribeLinkedCopies_ManyCopiesAcrossTypes_UsesPluralForms()
    {
        SharedComponentConsumer[] consumers =
        [
            new("WT-1", "NS Testing", 2),
            new("WT-2", "CIS - Global", 3),
        ];

        Assert.Equal("5 linked copies across 2 workflow types", SharedComponentImpactText.DescribeLinkedCopies(consumers));
    }

    [Fact]
    public void Describe_ActiveDeletedOverride_NamesPlaceholderBlockOrgAndEffect()
    {
        var orphan = new SharedComponentOverrideImpact(
            "org-client", "WT-1", "stable-1", "signer", "director_name", IsPending: false,
            SharedComponentOverrideImpactKind.Deleted);

        Assert.Equal(
            "Client override 'director_name' on block 'signer' (org org-client, WT-1) will be deleted.",
            SharedComponentImpactText.Describe(orphan));
    }

    [Fact]
    public void Describe_PendingGoingInertOverride_SaysPendingAndStopsApplying()
    {
        var orphan = new SharedComponentOverrideImpact(
            "org-client", "WT-1", "stable-1", "signer", "director_name", IsPending: true,
            SharedComponentOverrideImpactKind.GoingInert);

        Assert.Equal(
            "Pending client override 'director_name' on block 'signer' (org org-client, WT-1) will stop being applied.",
            SharedComponentImpactText.Describe(orphan));
    }

    [Fact]
    public void SlotRenames_ReturnsOnlyRealRenames_OrderedBySlotTag()
    {
        Dictionary<string, string> bindings = new(StringComparer.Ordinal)
        {
            ["zulu"] = "zulu_2",
            ["alpha"] = "alpha",
            ["beta"] = "beta_3",
        };

        List<KeyValuePair<string, string>> renames = SharedComponentImpactText.SlotRenames(bindings);

        Assert.Equal(2, renames.Count);
        Assert.Equal(new KeyValuePair<string, string>("beta", "beta_3"), renames[0]);
        Assert.Equal(new KeyValuePair<string, string>("zulu", "zulu_2"), renames[1]);
    }

    [Fact]
    public void SlotRenames_NullBindings_ReturnsEmpty()
    {
        Assert.Empty(SharedComponentImpactText.SlotRenames(null));
    }

    [Fact]
    public void RefEntryPushScope_NamesTheContainingSection()
    {
        Assert.Equal(
            "Pushes to the Library block, including every workflow using section 'KYC Pack'.",
            SharedComponentImpactText.RefEntryPushScope("KYC Pack"));
    }

    [Fact]
    public void RefEntryPushScope_BlankSectionName_FallsBackToThisSection()
    {
        Assert.Equal(
            "Pushes to the Library block, including every workflow using this section.",
            SharedComponentImpactText.RefEntryPushScope(" "));
    }

    [Fact]
    public void SectionPushPointerNote_SaysPointersNotContent()
    {
        Assert.Equal(
            "Referenced Library blocks travel as pointers — pushing the section shares the pointer, not their content.",
            SharedComponentImpactText.SectionPushPointerNote());
    }

    [Fact]
    public void HiddenEntriesPushWarning_SingularAndPlural()
    {
        Assert.Equal(
            "1 entry hidden here will be removed from the Library definition for everyone.",
            SharedComponentImpactText.HiddenEntriesPushWarning(1));
        Assert.Equal(
            "3 entries hidden here will be removed from the Library definition for everyone.",
            SharedComponentImpactText.HiddenEntriesPushWarning(3));
    }

    [Fact]
    public void LocalInsertPushNote_SaysBecomesPartOfTheSection()
    {
        Assert.Equal(
            "This block becomes part of the Library section for everyone.",
            SharedComponentImpactText.LocalInsertPushNote());
    }

    [Fact]
    public void DescribeSectionReconcile_RenamedSlotAndPreservedBlock_ProduceOneLineEach()
    {
        var workflow = new Workflow("Scratch");
        var section = new Section("Risk", workflow) { Title = "Risk" };
        var preserved = new Block(BlockTypes.AiQuery, section, "legacy_note") { Name = "Legacy Note" };
        var result = new SectionReconcileResult(
            RemovedBlocks: [],
            AddedBlocks: [],
            RenamedSlots: new Dictionary<string, string>(StringComparer.Ordinal) { ["kyc"] = "kyc_v2" },
            OldDefinitionAvailable: true,
            PreservedLocalBlocks: [preserved],
            StaleRefSlots: [],
            DroppedRefSlots: [],
            OverwrittenLocalRefSlots: []);

        List<string> lines = SharedComponentImpactText.DescribeSectionReconcile(result);

        Assert.Equal(2, lines.Count);
        Assert.Equal("Slot 'kyc' was renamed to 'kyc_v2' — matched content updated in place.", lines[0]);
        Assert.Equal("Block 'Legacy Note' left the Library definition but stays in this workflow.", lines[1]);
    }

    [Fact]
    public void DescribeSectionReconcile_UnresolvableRefSlots_ProduceOneLineEach()
    {
        var result = new SectionReconcileResult(
            RemovedBlocks: [],
            AddedBlocks: [],
            RenamedSlots: new Dictionary<string, string>(StringComparer.Ordinal),
            OldDefinitionAvailable: true,
            PreservedLocalBlocks: [],
            StaleRefSlots: ["kyc"],
            DroppedRefSlots: ["sanctions"],
            OverwrittenLocalRefSlots: []);

        List<string> lines = SharedComponentImpactText.DescribeSectionReconcile(result);

        Assert.Equal(
            [
                "Slot 'kyc' kept its previous content — its referenced Library block could not be resolved.",
                "Slot 'sanctions' landed empty — its referenced Library block could not be resolved.",
            ],
            lines);
    }

    [Fact]
    public void DescribeSectionReconcile_NothingNoteworthy_ReturnsEmpty()
    {
        var result = new SectionReconcileResult(
            RemovedBlocks: [],
            AddedBlocks: [],
            RenamedSlots: new Dictionary<string, string>(StringComparer.Ordinal),
            OldDefinitionAvailable: true,
            PreservedLocalBlocks: [],
            StaleRefSlots: [],
            DroppedRefSlots: [],
            OverwrittenLocalRefSlots: []);

        Assert.Empty(SharedComponentImpactText.DescribeSectionReconcile(result));
    }
}
