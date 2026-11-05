using IniaAi.Application.Commands.SharedComponents.ApplySharedComponentReconcile;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetWorkflowSharedComponentReconcile;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class LibraryReconcilePlannerTests
{
    private const string SECTION_COMPONENT_ID = "component-1";
    private const string REF_COMPONENT_ID = "component-2";
    private const string SECTION_HEAD_VERSION = "section-head-v3";
    private const string REF_HEAD_VERSION = "ref-head-v5";
    private const string SECTION_PATH = "Sections[0]";
    private const string SECTION_STABLE_ID = "stable-section-1";

    [Fact]
    public void DefaultActionFor_LinkedHeadAhead_PreselectsAdoptHead()
    {
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.HeadAhead);

        Assert.Equal(SharedComponentReconcileAction.AdoptHead, LibraryReconcilePlanner.DefaultActionFor(row));
    }

    [Fact]
    public void DefaultActionFor_LinkedComponentMissing_PreselectsBreakOut()
    {
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.ComponentMissing);

        Assert.Equal(SharedComponentReconcileAction.BreakOut, LibraryReconcilePlanner.DefaultActionFor(row));
    }

    [Fact]
    public void DefaultEntryAction_HiddenEntry_StaysKeepLocal()
    {
        SharedComponentEntryReconcileRow entry = HiddenEntry("kyc");

        Assert.Equal(SharedComponentReconcileAction.KeepLocal, LibraryReconcilePlanner.DefaultEntryAction(entry));
    }

    [Fact]
    public void DefaultEntryAction_LiveMissingEntry_PreselectsHide()
    {
        SharedComponentEntryReconcileRow entry = LiveMissingEntry("kyc");

        Assert.Equal(SharedComponentReconcileAction.Hide, LibraryReconcilePlanner.DefaultEntryAction(entry));
    }

    [Fact]
    public void DefaultEntryAction_RefEntryHeadAhead_PreselectsAdoptHead()
    {
        SharedComponentEntryReconcileRow entry = RefEntry("kyc", SharedComponentRelation.HeadAhead);

        Assert.Equal(SharedComponentReconcileAction.AdoptHead, LibraryReconcilePlanner.DefaultEntryAction(entry));
    }

    [Fact]
    public void DefaultEntryAction_RefEntryDiverged_StaysKeepLocalDespiteUpdateSharedLeadingTheArms()
    {
        SharedComponentEntryReconcileRow entry = RefEntry("kyc", SharedComponentRelation.Diverged);

        Assert.Equal(SharedComponentReconcileAction.UpdateShared, entry.AvailableActions[0]);
        Assert.Equal(SharedComponentReconcileAction.KeepLocal, LibraryReconcilePlanner.DefaultEntryAction(entry));
    }

    [Fact]
    public void DefaultEntryAction_LinkedInsertComponentMissing_PreselectsBreakOut()
    {
        SharedComponentEntryReconcileRow entry = LinkedInsertEntry(SharedComponentRelation.ComponentMissing);

        Assert.Equal(SharedComponentReconcileAction.BreakOut, LibraryReconcilePlanner.DefaultEntryAction(entry));
    }

    [Fact]
    public void DefaultEntryAction_LinkedInsertInSync_StaysKeepLocalDespiteBreakOutLeadingTheArms()
    {
        SharedComponentEntryReconcileRow entry = LinkedInsertEntry(SharedComponentRelation.InSync);

        Assert.Equal(SharedComponentReconcileAction.BreakOut, entry.AvailableActions[0]);
        Assert.Equal(SharedComponentReconcileAction.KeepLocal, LibraryReconcilePlanner.DefaultEntryAction(entry));
    }

    [Fact]
    public void DefaultEntryAction_BrokenOutEntry_StaysKeepLocal()
    {
        SharedComponentEntryReconcileRow entry = BrokenOutEntry("kyc");

        Assert.Equal(SharedComponentReconcileAction.KeepLocal, LibraryReconcilePlanner.DefaultEntryAction(entry));
    }

    [Fact]
    public void IsQuietEntry_InlineInSync_IsQuiet()
    {
        Assert.True(LibraryReconcilePlanner.IsQuietEntry(InlineEntry("intro", SharedComponentRelation.InSync)));
    }

    [Fact]
    public void IsQuietEntry_HiddenDeletedAndLiveMissing_AreLoud()
    {
        Assert.False(LibraryReconcilePlanner.IsQuietEntry(HiddenEntry("kyc")));
        Assert.False(LibraryReconcilePlanner.IsQuietEntry(DeletedByDefinitionEntry("legacy")));
        Assert.False(LibraryReconcilePlanner.IsQuietEntry(LiveMissingEntry("kyc")));
    }

    [Fact]
    public void RequiresHideConfirmation_VisibleDefinitionProvidedEntries_True()
    {
        Assert.True(LibraryReconcilePlanner.RequiresHideConfirmation(
            InlineEntry("intro", SharedComponentRelation.InSync)));
        Assert.True(LibraryReconcilePlanner.RequiresHideConfirmation(
            RefEntry("kyc", SharedComponentRelation.HeadAhead)));
    }

    [Fact]
    public void RequiresHideConfirmation_MissingLocalInsertOverriddenOrDeleted_False()
    {
        Assert.False(LibraryReconcilePlanner.RequiresHideConfirmation(null));
        Assert.False(LibraryReconcilePlanner.RequiresHideConfirmation(LocalInsertEntry("FX Note")));
        Assert.False(LibraryReconcilePlanner.RequiresHideConfirmation(HiddenEntry("kyc")));
        Assert.False(LibraryReconcilePlanner.RequiresHideConfirmation(BrokenOutEntry("kyc")));
        Assert.False(LibraryReconcilePlanner.RequiresHideConfirmation(DeletedByDefinitionEntry("legacy")));
    }

    [Fact]
    public void ShouldShow_QuietInstanceWithHiddenEntry_Shows()
    {
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked,
            SharedComponentRelation.InSync,
            entries: [InlineEntry("intro", SharedComponentRelation.InSync), HiddenEntry("kyc")]);

        Assert.True(LibraryReconcilePlanner.ShouldShow(row));
    }

    [Fact]
    public void ShouldShow_QuietInstanceWithOnlyQuietEntries_StaysBehindTheSwitch()
    {
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked,
            SharedComponentRelation.InSync,
            entries: [InlineEntry("intro", SharedComponentRelation.InSync)]);

        Assert.False(LibraryReconcilePlanner.ShouldShow(row));
    }

    [Fact]
    public void EntrySelectsEnabled_OnlyWhenWholeSectionArmIsKeepLocal()
    {
        Assert.True(LibraryReconcilePlanner.EntrySelectsEnabled(SharedComponentReconcileAction.KeepLocal));
        Assert.False(LibraryReconcilePlanner.EntrySelectsEnabled(SharedComponentReconcileAction.AdoptHead));
        Assert.False(LibraryReconcilePlanner.EntrySelectsEnabled(SharedComponentReconcileAction.BreakOut));
    }

    [Fact]
    public void BuildDecisions_WholeSectionArmChosen_SuppressesEntryDecisions()
    {
        SharedComponentEntryReconcileRow hidden = HiddenEntry("kyc");
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.HeadAhead, entries: [hidden]);
        Dictionary<string, SharedComponentReconcileAction> instanceActions = new(StringComparer.Ordinal)
        {
            [row.TargetPath] = SharedComponentReconcileAction.AdoptHead,
        };
        Dictionary<string, SharedComponentReconcileAction> entryActions = new(StringComparer.Ordinal)
        {
            [LibraryReconcilePlanner.EntryActionKey(row, hidden)] = SharedComponentReconcileAction.Unhide,
        };

        List<SharedComponentReconcileDecision> decisions = LibraryReconcilePlanner.BuildDecisions(
            [row], instanceActions, entryActions, acknowledgeOrphans: false);

        SharedComponentReconcileDecision decision = Assert.Single(decisions);
        Assert.Equal(SharedComponentReconcileAction.AdoptHead, decision.Action);
        Assert.Null(decision.EntryKey);
        Assert.Null(decision.EntryStableId);
        Assert.Equal(SECTION_HEAD_VERSION, decision.ExpectedHeadVersionId);
        Assert.Equal(SECTION_STABLE_ID, decision.TargetStableId);
        Assert.Null(decision.TargetPath);
    }

    [Fact]
    public void BuildDecisions_EntryUnhide_GoesByEntryKeyWithoutTokens()
    {
        SharedComponentEntryReconcileRow hidden = HiddenEntry("kyc");
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync, entries: [hidden]);
        Dictionary<string, SharedComponentReconcileAction> entryActions = new(StringComparer.Ordinal)
        {
            [LibraryReconcilePlanner.EntryActionKey(row, hidden)] = SharedComponentReconcileAction.Unhide,
        };

        List<SharedComponentReconcileDecision> decisions = LibraryReconcilePlanner.BuildDecisions(
            [row], EmptyActions(), entryActions, acknowledgeOrphans: false);

        SharedComponentReconcileDecision decision = Assert.Single(decisions);
        Assert.Equal(SharedComponentReconcileAction.Unhide, decision.Action);
        Assert.Equal("kyc", decision.EntryKey);
        Assert.Null(decision.EntryStableId);
        Assert.Null(decision.ExpectedHeadVersionId);
        Assert.Null(decision.ExpectedRefHeadVersionId);
    }

    [Fact]
    public void BuildDecisions_RefEntryAdoptHead_CarriesSectionAndRefTokensByStableId()
    {
        SharedComponentEntryReconcileRow refEntry = RefEntry("kyc", SharedComponentRelation.HeadAhead);
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync, entries: [refEntry]);
        Dictionary<string, SharedComponentReconcileAction> entryActions = new(StringComparer.Ordinal)
        {
            [LibraryReconcilePlanner.EntryActionKey(row, refEntry)] = SharedComponentReconcileAction.AdoptHead,
        };

        List<SharedComponentReconcileDecision> decisions = LibraryReconcilePlanner.BuildDecisions(
            [row], EmptyActions(), entryActions, acknowledgeOrphans: false);

        SharedComponentReconcileDecision decision = Assert.Single(decisions);
        Assert.Equal(SECTION_COMPONENT_ID, decision.SharedComponentId);
        Assert.Equal("stable-kyc", decision.EntryStableId);
        Assert.Null(decision.EntryKey);
        Assert.Equal(SECTION_HEAD_VERSION, decision.ExpectedHeadVersionId);
        Assert.Equal(REF_HEAD_VERSION, decision.ExpectedRefHeadVersionId);
    }

    [Fact]
    public void BuildDecisions_HideOnLiveMissingEntry_UsesEntryKeyAndStaysTokenless()
    {
        SharedComponentEntryReconcileRow missing = LiveMissingEntry("kyc");
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync, entries: [missing]);
        Dictionary<string, SharedComponentReconcileAction> entryActions = new(StringComparer.Ordinal)
        {
            [LibraryReconcilePlanner.EntryActionKey(row, missing)] = SharedComponentReconcileAction.Hide,
        };

        List<SharedComponentReconcileDecision> decisions = LibraryReconcilePlanner.BuildDecisions(
            [row], EmptyActions(), entryActions, acknowledgeOrphans: false);

        SharedComponentReconcileDecision decision = Assert.Single(decisions);
        Assert.Equal(SharedComponentReconcileAction.Hide, decision.Action);
        Assert.Equal("kyc", decision.EntryKey);
        Assert.Null(decision.ExpectedHeadVersionId);
        Assert.Null(decision.ExpectedRefHeadVersionId);
    }

    [Fact]
    public void BuildDecisions_MixedInstanceAndEntryPending_EmitsBoth()
    {
        SharedComponentInstanceReconcileRow blockRow = InstanceRow(
            SharedComponentLinkState.Linked,
            SharedComponentRelation.LocalAhead,
            targetPath: "Sections[1].Blocks[0]",
            targetStableId: "stable-block-9",
            kind: SharedComponentKind.Block);
        SharedComponentEntryReconcileRow refEntry = RefEntry("kyc", SharedComponentRelation.HeadAhead);
        SharedComponentInstanceReconcileRow sectionRow = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync, entries: [refEntry]);
        Dictionary<string, SharedComponentReconcileAction> instanceActions = new(StringComparer.Ordinal)
        {
            [blockRow.TargetPath] = SharedComponentReconcileAction.BreakOut,
        };
        Dictionary<string, SharedComponentReconcileAction> entryActions = new(StringComparer.Ordinal)
        {
            [LibraryReconcilePlanner.EntryActionKey(sectionRow, refEntry)] = SharedComponentReconcileAction.AdoptHead,
        };

        List<SharedComponentReconcileDecision> decisions = LibraryReconcilePlanner.BuildDecisions(
            [blockRow, sectionRow], instanceActions, entryActions, acknowledgeOrphans: true);

        Assert.Equal(2, decisions.Count);
        Assert.Equal(SharedComponentReconcileAction.BreakOut, decisions[0].Action);
        Assert.Null(decisions[0].ExpectedHeadVersionId);
        Assert.True(decisions[0].AcknowledgeSignatureOrphans);
        Assert.Equal(SharedComponentReconcileAction.AdoptHead, decisions[1].Action);
        Assert.Equal("stable-kyc", decisions[1].EntryStableId);
        Assert.True(decisions[1].AcknowledgeSignatureOrphans);
    }

    [Fact]
    public void QuietCount_ShownRowWithOnlyQuietEntries_StillCountsThem()
    {
        SharedComponentInstanceReconcileRow row = InstanceRow(
            SharedComponentLinkState.Linked,
            SharedComponentRelation.LocalAhead,
            entries: [InSyncEntry("kyc"), InSyncEntry("fee")]);

        Assert.Equal(2, LibraryReconcilePlanner.QuietCount([row]));
    }

    [Fact]
    public void QuietCount_SumsHiddenRowsAndQuietEntriesInShownRows()
    {
        SharedComponentInstanceReconcileRow quietRow = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync);
        SharedComponentInstanceReconcileRow shownRow = InstanceRow(
            SharedComponentLinkState.Linked,
            SharedComponentRelation.HeadAhead,
            entries: [InSyncEntry("kyc"), HiddenEntry("fee")]);

        Assert.Equal(2, LibraryReconcilePlanner.QuietCount([quietRow, shownRow]));
        Assert.Equal(0, LibraryReconcilePlanner.QuietCount(null));
    }

    [Fact]
    public void RowBadgeLabel_InSyncWithLocalInserts_AppendsAdditionCount()
    {
        SharedComponentInstanceReconcileRow one = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync, localInsertCount: 1);
        SharedComponentInstanceReconcileRow many = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync, localInsertCount: 3);

        Assert.Equal("Up to date · 1 local addition", LibraryReconcilePlanner.RowBadgeLabel(one));
        Assert.Equal("Up to date · 3 local additions", LibraryReconcilePlanner.RowBadgeLabel(many));
    }

    [Theory]
    [InlineData(SharedComponentRelation.LocalAhead, true)]
    [InlineData(SharedComponentRelation.HeadAhead, true)]
    [InlineData(SharedComponentRelation.Diverged, true)]
    [InlineData(SharedComponentRelation.InSync, false)]
    [InlineData(SharedComponentRelation.ComponentMissing, false)]
    [InlineData(SharedComponentRelation.ComponentRetired, false)]
    public void HasContentDifference_OnlyWhereTheCopyAndTheLibraryHeadHoldDifferentContent(
        SharedComponentRelation relation,
        bool expected)
    {
        SharedComponentInstanceReconcileRow row = InstanceRow(SharedComponentLinkState.Linked, relation);

        Assert.Equal(expected, LibraryReconcilePlanner.HasContentDifference(row));
    }

    [Fact]
    public void Listed_LeavesOutBrokenOutCopies()
    {
        SharedComponentInstanceReconcileRow linked = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.LocalAhead);
        SharedComponentInstanceReconcileRow forkedInSync = InstanceRow(
            SharedComponentLinkState.BrokenOut, SharedComponentRelation.InSync);
        SharedComponentInstanceReconcileRow forkedDiverged = InstanceRow(
            SharedComponentLinkState.BrokenOut, SharedComponentRelation.Diverged);
        SharedComponentInstanceReconcileRow forkedOfVanished = InstanceRow(
            SharedComponentLinkState.BrokenOut, SharedComponentRelation.ComponentMissing);

        IReadOnlyList<SharedComponentInstanceReconcileRow> listed =
            LibraryReconcilePlanner.Listed([linked, forkedInSync, forkedDiverged, forkedOfVanished]);

        Assert.Equal([linked], listed);
    }

    [Fact]
    public void Listed_BlockBrokenOutOfASlot_IsLeftOut_SoItsSectionReadsUpToDate()
    {
        SharedComponentEntryReconcileRow intro = InlineEntry("intro", SharedComponentRelation.InSync);
        SharedComponentInstanceReconcileRow section = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync, entries: [intro, BrokenOutEntry("kyc")]);

        SharedComponentInstanceReconcileRow listed = Assert.Single(LibraryReconcilePlanner.Listed([section]));

        Assert.Equal([intro], listed.Entries!);
        Assert.False(LibraryReconcilePlanner.ShouldShow(listed));
    }

    [Fact]
    public void RowBadgeLabel_LinkedWithVanishedComponent_StaysAlarming()
    {
        SharedComponentInstanceReconcileRow linked = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.ComponentMissing);

        Assert.Equal("Missing from Library", LibraryReconcilePlanner.RowBadgeLabel(linked));
    }

    [Theory]
    [InlineData(SharedComponentRelation.LocalAhead)]
    [InlineData(SharedComponentRelation.ComponentMissing)]
    public void Listed_BrokenOutBlockInsertedInALinkedSection_ReadsAsAPlainLocalAddition(SharedComponentRelation relation)
    {
        SharedComponentEntryReconcileRow brokenOut = LinkedInsertEntry(relation, SharedComponentLinkState.BrokenOut);
        SharedComponentInstanceReconcileRow section = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync, entries: [brokenOut]);

        SharedComponentEntryReconcileRow listed =
            Assert.Single(Assert.Single(LibraryReconcilePlanner.Listed([section])).Entries!);

        Assert.Equal(brokenOut.EntryStableId, listed.EntryStableId);
        Assert.Equal(SharedComponentEntryOrigin.LocalInsert, listed.Origin);
        Assert.Null(listed.InsertLinkState);
        Assert.Null(listed.Relation);
        Assert.Null(listed.RefSharedComponentId);
        Assert.Null(listed.RefHeadVersionId);
        Assert.Equal(LocalInsertEntry(listed.DisplayName).AvailableActions, listed.AvailableActions);
        Assert.Null(LibraryReconcilePlanner.EntryBadgeLabel(listed));
    }

    [Fact]
    public void EntryBadge_LinkedInsertWithVanishedComponent_StaysAlarming()
    {
        SharedComponentEntryReconcileRow linked = LinkedInsertEntry(
            SharedComponentRelation.ComponentMissing, SharedComponentLinkState.Linked);

        Assert.Equal("Missing from Library", LibraryReconcilePlanner.EntryBadgeLabel(linked));
    }

    [Fact]
    public void RowBadgeLabel_WithoutInsertsOrOffSync_StaysPlainRelationLabel()
    {
        SharedComponentInstanceReconcileRow clean = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.InSync);
        SharedComponentInstanceReconcileRow headAhead = InstanceRow(
            SharedComponentLinkState.Linked, SharedComponentRelation.HeadAhead, localInsertCount: 2);

        Assert.Equal("Up to date", LibraryReconcilePlanner.RowBadgeLabel(clean));
        Assert.Equal("Update available", LibraryReconcilePlanner.RowBadgeLabel(headAhead));
    }

    private static Dictionary<string, SharedComponentReconcileAction> EmptyActions()
    {
        return new Dictionary<string, SharedComponentReconcileAction>(StringComparer.Ordinal);
    }

    private static SharedComponentInstanceReconcileRow InstanceRow(
        SharedComponentLinkState linkState,
        SharedComponentRelation relation,
        string targetPath = SECTION_PATH,
        string? targetStableId = SECTION_STABLE_ID,
        SharedComponentKind kind = SharedComponentKind.Section,
        IReadOnlyList<SharedComponentEntryReconcileRow>? entries = null,
        int localInsertCount = 0)
    {
        return new SharedComponentInstanceReconcileRow(
            targetPath,
            targetStableId,
            kind,
            SECTION_COMPONENT_ID,
            "KYC Pack",
            SharedComponentState.Active,
            linkState,
            relation,
            InstanceContentHash: "hash-live",
            LinkVersionHash: "hash-link",
            HeadVersionId: SECTION_HEAD_VERSION,
            HeadContentHash: "hash-head",
            LinkHashStale: false,
            OldDefinitionAvailable: true,
            SharedComponentReconciler.AvailableActions(linkState, relation),
            OrderDeviates: false,
            Entries: entries,
            LocalInsertCount: localInsertCount);
    }

    private static SharedComponentEntryReconcileRow InSyncEntry(string slot)
    {
        return new SharedComponentEntryReconcileRow(
            slot, $"stable-{slot}", $"path-{slot}", slot,
            SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
            null, null, null, null, null,
            Relation: SharedComponentRelation.InSync, InstanceContentHash: "hash-entry", DeletedByDefinition: false,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
                SharedComponentRelation.InSync, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
    }

    private static SharedComponentEntryReconcileRow HiddenEntry(string slot)
    {
        return new SharedComponentEntryReconcileRow(
            slot, null, null, slot,
            SharedComponentEntryOrigin.DefinitionRef, SharedComponentEntryOverrideState.Hidden,
            REF_COMPONENT_ID, "Sanctions Check", SharedComponentState.Active,
            REF_HEAD_VERSION, "hash-ref-head",
            Relation: null, InstanceContentHash: null, DeletedByDefinition: false,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionRef, SharedComponentEntryOverrideState.Hidden,
                relation: null, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
    }

    private static SharedComponentEntryReconcileRow LiveMissingEntry(string slot)
    {
        return new SharedComponentEntryReconcileRow(
            slot, null, null, slot,
            SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
            RefSharedComponentId: null, RefComponentName: null, RefComponentState: null,
            RefHeadVersionId: null, RefHeadContentHash: null,
            Relation: null, InstanceContentHash: null, DeletedByDefinition: false,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
                relation: null, insertLinkState: null, deletedByDefinition: false, liveMissing: true));
    }

    private static SharedComponentEntryReconcileRow RefEntry(string slot, SharedComponentRelation relation)
    {
        return new SharedComponentEntryReconcileRow(
            slot, $"stable-{slot}", $"{SECTION_PATH}.Blocks[1]", slot,
            SharedComponentEntryOrigin.DefinitionRef, SharedComponentEntryOverrideState.None,
            REF_COMPONENT_ID, "Sanctions Check", SharedComponentState.Active,
            REF_HEAD_VERSION, "hash-ref-head",
            relation, InstanceContentHash: "hash-entry", DeletedByDefinition: false,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionRef, SharedComponentEntryOverrideState.None,
                relation, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
    }

    private static SharedComponentEntryReconcileRow InlineEntry(string slot, SharedComponentRelation relation)
    {
        return new SharedComponentEntryReconcileRow(
            slot, $"stable-{slot}", $"{SECTION_PATH}.Blocks[0]", slot,
            SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
            RefSharedComponentId: null, RefComponentName: null, RefComponentState: null,
            RefHeadVersionId: null, RefHeadContentHash: null,
            relation, InstanceContentHash: "hash-entry", DeletedByDefinition: false,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
                relation, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
    }

    private static SharedComponentEntryReconcileRow BrokenOutEntry(string slot)
    {
        return new SharedComponentEntryReconcileRow(
            slot, $"stable-{slot}", $"{SECTION_PATH}.Blocks[2]", slot,
            SharedComponentEntryOrigin.DefinitionRef, SharedComponentEntryOverrideState.BrokenOut,
            REF_COMPONENT_ID, "Sanctions Check", SharedComponentState.Active,
            REF_HEAD_VERSION, "hash-ref-head",
            Relation: null, InstanceContentHash: "hash-entry", DeletedByDefinition: false,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionRef, SharedComponentEntryOverrideState.BrokenOut,
                relation: null, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
    }

    private static SharedComponentEntryReconcileRow LocalInsertEntry(string displayName)
    {
        return new SharedComponentEntryReconcileRow(
            EntryKey: null, $"stable-{displayName}", $"{SECTION_PATH}.Blocks[3]", displayName,
            SharedComponentEntryOrigin.LocalInsert, SharedComponentEntryOverrideState.None,
            RefSharedComponentId: null, RefComponentName: null, RefComponentState: null,
            RefHeadVersionId: null, RefHeadContentHash: null,
            Relation: null, InstanceContentHash: "hash-entry", DeletedByDefinition: false,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.LocalInsert, SharedComponentEntryOverrideState.None,
                relation: null, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
    }

    private static SharedComponentEntryReconcileRow LinkedInsertEntry(
        SharedComponentRelation relation,
        SharedComponentLinkState insertLinkState = SharedComponentLinkState.Linked)
    {
        return new SharedComponentEntryReconcileRow(
            EntryKey: null, "stable-insert", $"{SECTION_PATH}.Blocks[4]", "FX Note",
            SharedComponentEntryOrigin.LocalInsert, SharedComponentEntryOverrideState.None,
            REF_COMPONENT_ID, "Sanctions Check", SharedComponentState.Active,
            REF_HEAD_VERSION, "hash-ref-head",
            relation, InstanceContentHash: "hash-entry", DeletedByDefinition: false,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.LocalInsert, SharedComponentEntryOverrideState.None,
                relation, insertLinkState, deletedByDefinition: false, liveMissing: false),
            InsertLinkState: insertLinkState);
    }

    private static SharedComponentEntryReconcileRow DeletedByDefinitionEntry(string slot)
    {
        return new SharedComponentEntryReconcileRow(
            slot, $"stable-{slot}", $"{SECTION_PATH}.Blocks[5]", slot,
            SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
            RefSharedComponentId: null, RefComponentName: null, RefComponentState: null,
            RefHeadVersionId: null, RefHeadContentHash: null,
            Relation: null, InstanceContentHash: "hash-entry", DeletedByDefinition: true,
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
                relation: null, insertLinkState: null, deletedByDefinition: true, liveMissing: false));
    }
}
