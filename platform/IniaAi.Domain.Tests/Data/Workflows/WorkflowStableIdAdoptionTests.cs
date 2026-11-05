using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// Adoption keeps a template's stable block/section identity alive across a full-replace save:
/// the incoming workflow (whose payload may have carried no stable ids, or freshly minted ones)
/// takes over the ids of the workflow row it replaces, matched by section Order plus block
/// ReplacementTag and Type. Without this, every template save re-mints identity and silently
/// invalidates all regression baselines and replay captures for the type.
/// </summary>
public class WorkflowStableIdAdoptionTests
{
    [Fact]
    public void AdoptStableIdsFrom_MatchingSectionOrderTagAndType_AdoptsIds()
    {
        Workflow source = BuildWorkflow();
        Workflow incoming = BuildWorkflow();

        Assert.NotEqual(
            BlockByTag(source, "alpha").StableBlockId,
            BlockByTag(incoming, "alpha").StableBlockId);

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(
            source.Sections.Single().StableSectionId,
            incoming.Sections.Single().StableSectionId);
        Assert.Equal(
            BlockByTag(source, "alpha").StableBlockId,
            BlockByTag(incoming, "alpha").StableBlockId);
        Assert.Equal(
            BlockByTag(source, "beta").StableBlockId,
            BlockByTag(incoming, "beta").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_NullIncomingIds_AdoptsIds()
    {
        Workflow source = BuildWorkflow();
        Workflow incoming = BuildWorkflow();
        foreach (Block block in incoming.GetAllBlocks())
        {
            block.StableBlockId = null;
        }

        foreach (Section section in incoming.Sections)
        {
            section.StableSectionId = null;
        }

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(
            BlockByTag(source, "alpha").StableBlockId,
            BlockByTag(incoming, "alpha").StableBlockId);
        Assert.Equal(
            source.Sections.Single().StableSectionId,
            incoming.Sections.Single().StableSectionId);
    }

    [Fact]
    public void AdoptStableIdsFrom_UnmatchedTag_KeepsIncomingId()
    {
        Workflow source = BuildWorkflow();
        Workflow incoming = BuildWorkflow();
        Block renamed = BlockByTag(incoming, "beta");
        renamed.ReplacementTag = "gamma";
        string mintedId = renamed.StableBlockId!;

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(mintedId, renamed.StableBlockId);
        Assert.Equal(
            BlockByTag(source, "alpha").StableBlockId,
            BlockByTag(incoming, "alpha").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_TypeChanged_KeepsIncomingId()
    {
        Workflow source = BuildWorkflow();
        Workflow incoming = BuildWorkflow();
        Block changed = BlockByTag(incoming, "beta");
        changed.Type = BlockTypes.AiQuery;
        string mintedId = changed.StableBlockId!;

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(mintedId, changed.StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_DuplicateTagInSource_SkipsThatTag()
    {
        Workflow source = BuildWorkflow();
        Section sourceSection = source.Sections.Single();
        _ = new Block(BlockTypes.Merge, sourceSection, "beta")
        {
            Name = "Duplicate beta",
            Properties = new MergeProperties { TextTemplate = "dup" },
        };

        Workflow incoming = BuildWorkflow();
        string mintedId = BlockByTag(incoming, "beta").StableBlockId!;

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(mintedId, BlockByTag(incoming, "beta").StableBlockId);
        Assert.Equal(
            BlockByTag(source, "alpha").StableBlockId,
            BlockByTag(incoming, "alpha").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_CarriedIdsSurviveSectionReorder()
    {
        Workflow source = BuildTwoSectionWorkflow();
        Workflow incoming = BuildTwoSectionWorkflow();
        CopyStableIds(source, incoming);

        // Move the second section first, as the editor's MoveSection would, then renumber.
        List<Section> reordered = [incoming.Sections.Last(), incoming.Sections.First()];
        for (int i = 0; i < reordered.Count; i++)
        {
            reordered[i].Order = i;
        }

        string bodyId = SectionByTitle(source, "Body").StableSectionId!;
        string annexId = SectionByTitle(source, "Annex").StableSectionId!;

        incoming.AdoptStableIdsFrom(source);

        // Ids follow the sections, not their positions.
        Assert.Equal(bodyId, SectionByTitle(incoming, "Body").StableSectionId);
        Assert.Equal(annexId, SectionByTitle(incoming, "Annex").StableSectionId);
        Assert.Equal(
            BlockByTag(source, "alpha").StableBlockId,
            BlockByTag(incoming, "alpha").StableBlockId);
        Assert.Equal(
            BlockByTag(source, "delta").StableBlockId,
            BlockByTag(incoming, "delta").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_DuplicateTagAcrossSections_ReorderDoesNotSwapIds()
    {
        // The same tag+type in two different sections is validator-endorsed (variant sections).
        // After a section swap, positional matching would silently exchange the two blocks' ids,
        // cross-pairing baselines to the wrong block; carried identity must win instead.
        Workflow source = BuildTwoSectionWorkflow();
        _ = new Block(BlockTypes.Merge, source.Sections.First(), "shared")
        {
            Name = "Shared in Body",
            Properties = new MergeProperties { TextTemplate = "body" },
        };
        _ = new Block(BlockTypes.Merge, source.Sections.Last(), "shared")
        {
            Name = "Shared in Annex",
            Properties = new MergeProperties { TextTemplate = "annex" },
        };

        Workflow incoming = BuildTwoSectionWorkflow();
        _ = new Block(BlockTypes.Merge, incoming.Sections.First(), "shared")
        {
            Name = "Shared in Body",
            Properties = new MergeProperties { TextTemplate = "body" },
        };
        _ = new Block(BlockTypes.Merge, incoming.Sections.Last(), "shared")
        {
            Name = "Shared in Annex",
            Properties = new MergeProperties { TextTemplate = "annex" },
        };
        CopyStableIds(source, incoming);

        List<Section> reordered = [incoming.Sections.Last(), incoming.Sections.First()];
        for (int i = 0; i < reordered.Count; i++)
        {
            reordered[i].Order = i;
        }

        string bodySharedId = SharedBlock(source, "Body").StableBlockId!;
        string annexSharedId = SharedBlock(source, "Annex").StableBlockId!;

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(bodySharedId, SharedBlock(incoming, "Body").StableBlockId);
        Assert.Equal(annexSharedId, SharedBlock(incoming, "Annex").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_NewBlockCannotStealCarriedId()
    {
        Workflow source = BuildWorkflow();
        Workflow incoming = BuildWorkflow();
        CopyStableIds(source, incoming);

        // The carried "beta" moves aside (renamed), and a NEW block takes its structural slot.
        Block carried = BlockByTag(incoming, "beta");
        string carriedId = carried.StableBlockId!;
        carried.ReplacementTag = "beta_moved";
        var thief = new Block(BlockTypes.Merge, incoming.Sections.Single(), "beta")
        {
            Name = "New beta",
            Properties = new MergeProperties { TextTemplate = "new" },
        };
        string thiefMintedId = thief.StableBlockId!;

        incoming.AdoptStableIdsFrom(source);

        // The carried block keeps its identity; the new block keeps its fresh id.
        Assert.Equal(carriedId, BlockByTag(incoming, "beta_moved").StableBlockId);
        Assert.Equal(thiefMintedId, BlockByTag(incoming, "beta").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_IdlessPayload_TwoSections_AdoptsStructurally()
    {
        Workflow source = BuildTwoSectionWorkflow();
        Workflow incoming = BuildTwoSectionWorkflow();
        foreach (Block block in incoming.GetAllBlocks())
        {
            block.StableBlockId = null;
        }

        foreach (Section section in incoming.Sections)
        {
            section.StableSectionId = null;
        }

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(
            SectionByTitle(source, "Annex").StableSectionId,
            SectionByTitle(incoming, "Annex").StableSectionId);
        Assert.Equal(
            BlockByTag(source, "delta").StableBlockId,
            BlockByTag(incoming, "delta").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_DuplicateKeyOnIncomingSide_SkipsThatKey()
    {
        Workflow source = BuildWorkflow();
        Workflow incoming = BuildWorkflow();
        foreach (Block block in incoming.GetAllBlocks())
        {
            block.StableBlockId = null;
        }

        _ = new Block(BlockTypes.Merge, incoming.Sections.Single(), "beta")
        {
            Name = "Duplicate beta",
            Properties = new MergeProperties { TextTemplate = "dup" },
        };
        Block duplicate = incoming.GetAllBlocks().Last();
        duplicate.StableBlockId = null;

        incoming.AdoptStableIdsFrom(source);

        // Ambiguous incoming key adopts nothing; unambiguous sibling still adopts.
        foreach (Block beta in incoming.GetAllBlocks().Where(b => b.ReplacementTag == "beta"))
        {
            Assert.Null(beta.StableBlockId);
        }

        Assert.Equal(
            BlockByTag(source, "alpha").StableBlockId,
            BlockByTag(incoming, "alpha").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_ContestedId_ResolvedByStructure_NotDocumentOrder()
    {
        // An /edit-workflow agent copies S2's "shared" block JSON verbatim into S1, id included.
        // Both incoming "shared" blocks now carry S2's id: neither may keep it by trust alone —
        // structure decides, and the displaced id must not be lost.
        Workflow source = BuildTwoSectionWorkflow();
        _ = new Block(BlockTypes.Merge, source.Sections.First(), "shared")
        {
            Name = "Shared in Body",
            Properties = new MergeProperties { TextTemplate = "body" },
        };
        _ = new Block(BlockTypes.Merge, source.Sections.Last(), "shared")
        {
            Name = "Shared in Annex",
            Properties = new MergeProperties { TextTemplate = "annex" },
        };

        Workflow incoming = BuildTwoSectionWorkflow();
        _ = new Block(BlockTypes.Merge, incoming.Sections.First(), "shared")
        {
            Name = "Shared in Body",
            Properties = new MergeProperties { TextTemplate = "annex" },
        };
        _ = new Block(BlockTypes.Merge, incoming.Sections.Last(), "shared")
        {
            Name = "Shared in Annex",
            Properties = new MergeProperties { TextTemplate = "annex" },
        };
        CopyStableIds(source, incoming);

        string bodySharedId = SharedBlock(source, "Body").StableBlockId!;
        string annexSharedId = SharedBlock(source, "Annex").StableBlockId!;
        SharedBlock(incoming, "Body").StableBlockId = annexSharedId;
        SharedBlock(incoming, "Annex").StableBlockId = annexSharedId;

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(bodySharedId, SharedBlock(incoming, "Body").StableBlockId);
        Assert.Equal(annexSharedId, SharedBlock(incoming, "Annex").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_ContestedId_LoserWithoutStructuralMatch_IsReminted()
    {
        // The copier lands at a NEW slot with no structural counterpart: it must not keep the
        // contested id, and must not beat the true owner by document order. Null -> backfill mints.
        Workflow source = BuildWorkflow();
        Workflow incoming = BuildWorkflow();
        CopyStableIds(source, incoming);

        string ownedId = BlockByTag(incoming, "beta").StableBlockId!;
        var copier = new Block(BlockTypes.Merge, incoming.Sections.Single(), 0, "beta_copy")
        {
            Name = "Pasted copy",
            Properties = new MergeProperties { TextTemplate = "copy" },
        };
        copier.StableBlockId = ownedId;

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(ownedId, BlockByTag(incoming, "beta").StableBlockId);
        Assert.Null(BlockByTag(incoming, "beta_copy").StableBlockId);
    }

    [Fact]
    public void AdoptStableIdsFrom_NewSectionCannotStealCarriedSectionId()
    {
        Workflow source = BuildTwoSectionWorkflow();
        Workflow incoming = BuildTwoSectionWorkflow();
        CopyStableIds(source, incoming);

        // "Annex" moves to the end and a NEW section takes its old Order slot.
        Section carried = SectionByTitle(incoming, "Annex");
        string carriedId = carried.StableSectionId!;
        var inserted = new Section("Inserted", incoming) { Title = "Inserted" };
        inserted.Order = carried.Order;
        carried.Order = inserted.Order + 1;
        string insertedMintedId = inserted.StableSectionId!;

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(carriedId, SectionByTitle(incoming, "Annex").StableSectionId);
        Assert.Equal(insertedMintedId, SectionByTitle(incoming, "Inserted").StableSectionId);
    }

    [Fact]
    public void AdoptStableIdsFrom_KeptEntityAndIdlessAdoptee_Coexist()
    {
        Workflow source = BuildWorkflow();
        Workflow incoming = BuildWorkflow();
        CopyStableIds(source, incoming);

        // One block carries its id (kept); its sibling arrives id-less (structural adoption).
        Block idless = BlockByTag(incoming, "beta");
        idless.StableBlockId = null;
        string carriedId = BlockByTag(incoming, "alpha").StableBlockId!;

        incoming.AdoptStableIdsFrom(source);

        Assert.Equal(carriedId, BlockByTag(incoming, "alpha").StableBlockId);
        Assert.Equal(
            BlockByTag(source, "beta").StableBlockId,
            BlockByTag(incoming, "beta").StableBlockId);
    }

    private static void CopyStableIds(Workflow source, Workflow destination)
    {
        List<Section> sourceSections = [.. source.Sections.OrderBy(s => s.Order)];
        List<Section> destinationSections = [.. destination.Sections.OrderBy(s => s.Order)];
        for (int i = 0; i < sourceSections.Count; i++)
        {
            destinationSections[i].StableSectionId = sourceSections[i].StableSectionId;
            List<Block> sourceBlocks = [.. sourceSections[i].Blocks.OrderBy(b => b.Order)];
            List<Block> destinationBlocks = [.. destinationSections[i].Blocks.OrderBy(b => b.Order)];
            for (int j = 0; j < sourceBlocks.Count; j++)
            {
                destinationBlocks[j].StableBlockId = sourceBlocks[j].StableBlockId;
            }
        }
    }

    private static Section SectionByTitle(Workflow workflow, string title)
    {
        return workflow.Sections.Single(s => s.Title == title);
    }

    private static Block SharedBlock(Workflow workflow, string sectionTitle)
    {
        return SectionByTitle(workflow, sectionTitle).Blocks.Single(b => b.ReplacementTag == "shared");
    }

    private static Workflow BuildTwoSectionWorkflow()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var body = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, body, "alpha")
        {
            Name = "Alpha",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };
        _ = new Block(BlockTypes.Merge, body, "beta")
        {
            Name = "Beta",
            Properties = new MergeProperties { TextTemplate = "{alpha}" },
        };
        var annex = new Section("Annex", workflow) { Title = "Annex" };
        _ = new Block(BlockTypes.AiQuery, annex, "gamma")
        {
            Name = "Gamma",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };
        _ = new Block(BlockTypes.Merge, annex, "delta")
        {
            Name = "Delta",
            Properties = new MergeProperties { TextTemplate = "{gamma}" },
        };

        return workflow;
    }

    private static Block BlockByTag(Workflow workflow, string tag)
    {
        return workflow.GetAllBlocks().Single(b => b.ReplacementTag == tag);
    }

    private static Workflow BuildWorkflow()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "alpha")
        {
            Name = "Alpha",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };
        _ = new Block(BlockTypes.Merge, section, "beta")
        {
            Name = "Beta",
            Properties = new MergeProperties { TextTemplate = "{alpha}" },
        };

        return workflow;
    }
}
