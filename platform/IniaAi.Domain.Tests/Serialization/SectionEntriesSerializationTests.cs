using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Serialization;

public class SectionEntriesSerializationTests
{
    private readonly WorkflowCanonicalizer _canonicalizer = new();

    private static Section SectionWithBlocks(params string[] tags)
    {
        var workflow = new Workflow("host");
        var section = new Section("shared", workflow) { Title = "Shared" };
        foreach (string tag in tags)
        {
            _ = new Block(BlockTypes.AiQuery, section, tag)
            {
                Name = $"{tag} name",
                Properties = new AiQueryProperties { UserPromptTemplate = $"{tag} prompt" },
            };
        }

        return section;
    }

    private static SectionEntry RefEntry(string slotTag, string componentId)
    {
        return new SectionEntry
        {
            SlotTag = slotTag,
            Kind = SectionEntryKind.ComponentRef,
            SharedComponentId = componentId,
            Bindings = new Dictionary<string, string> { ["input"] = "value" },
        };
    }

    [Fact]
    public void ComponentCanonicalJson_RefEntry_SurvivesHashProfileWithAllFields()
    {
        Section section = SectionWithBlocks("kyc_check");
        section.SharedComponentId = "section-component";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = new string('a', 64);
        section.Entries =
        [
            new SectionEntry { SlotTag = "kyc_check", Kind = SectionEntryKind.Inline },
            RefEntry("risk_score", "block-component"),
        ];

        string json = _canonicalizer.ToComponentCanonicalJson(section);
        JObject parsed = JObject.Parse(json);

        var entries = (JArray?)parsed[nameof(Section.Entries)];
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.Equal("kyc_check", (string?)entries[0][nameof(SectionEntry.SlotTag)]);
        Assert.Equal(nameof(SectionEntryKind.Inline), (string?)entries[0][nameof(SectionEntry.Kind)]);
        Assert.Null(entries[0][nameof(SectionEntry.SharedComponentId)]);
        Assert.Null(entries[0][nameof(SectionEntry.Bindings)]);
        Assert.Equal("risk_score", (string?)entries[1][nameof(SectionEntry.SlotTag)]);
        Assert.Equal(nameof(SectionEntryKind.ComponentRef), (string?)entries[1][nameof(SectionEntry.Kind)]);
        Assert.Equal("block-component", (string?)entries[1][nameof(SectionEntry.SharedComponentId)]);
        Assert.Equal("value", (string?)entries[1][nameof(SectionEntry.Bindings)]!["input"]);
        Assert.Null(parsed[nameof(Section.SharedComponentId)]);
        Assert.Null(parsed[nameof(Section.SharedComponentVersionHash)]);
    }

    [Fact]
    public void ComponentCanonicalJson_NullOrEmptyEntries_EmitNoEntriesKey()
    {
        Section withNull = SectionWithBlocks("a", "b");
        Section withEmpty = SectionWithBlocks("a", "b");
        withEmpty.Entries = [];

        string nullJson = _canonicalizer.ToComponentCanonicalJson(withNull);
        string emptyJson = _canonicalizer.ToComponentCanonicalJson(withEmpty);

        Assert.DoesNotContain($"\"{nameof(Section.Entries)}\"", nullJson);
        Assert.Equal(nullJson, emptyJson);
    }

    [Fact]
    public void DefinitionJson_RoundTripsEntries_ThroughSectionFromJson()
    {
        Section section = SectionWithBlocks("kyc_check");
        section.Entries =
        [
            RefEntry("risk_score", "block-component"),
            new SectionEntry { SlotTag = "kyc_check", Kind = SectionEntryKind.Inline },
        ];

        Section? restored = Section.FromJson(_canonicalizer.ToComponentCanonicalJson(section));

        Assert.NotNull(restored);
        Assert.NotNull(restored.Entries);
        Assert.Equal(2, restored.Entries.Count);
        Assert.Equal("risk_score", restored.Entries[0].SlotTag);
        Assert.Equal(SectionEntryKind.ComponentRef, restored.Entries[0].Kind);
        Assert.Equal("block-component", restored.Entries[0].SharedComponentId);
        Assert.Equal("value", restored.Entries[0].Bindings!["input"]);
        Assert.Equal("kyc_check", restored.Entries[1].SlotTag);
        Assert.Equal(SectionEntryKind.Inline, restored.Entries[1].Kind);
        Assert.Null(restored.Entries[1].SharedComponentId);
        Assert.Null(restored.Entries[1].Bindings);
    }

    [Fact]
    public void OldPayload_WithoutEntries_DeserializesNullEntries()
    {
        Section legacy = SectionWithBlocks("a", "b");

        Section? restored = Section.FromJson(_canonicalizer.ToComponentCanonicalJson(legacy));

        Assert.NotNull(restored);
        Assert.Null(restored.Entries);
    }

    [Fact]
    public void EffectiveEntries_WithoutEntries_SynthesizesInlineFromBlockOrder()
    {
        Section section = SectionWithBlocks("first", "second", "third");
        section.FindBlockByReplacementTag("first")!.Order = 2;
        section.FindBlockByReplacementTag("third")!.Order = 0;

        List<SectionEntry> effective = section.EffectiveEntries();

        Assert.Equal(["third", "second", "first"], effective.Select(e => e.SlotTag).ToList());
        Assert.All(effective, e => Assert.Equal(SectionEntryKind.Inline, e.Kind));
        Assert.All(effective, e => Assert.Null(e.SharedComponentId));
        Assert.All(effective, e => Assert.Null(e.Bindings));
    }

    [Fact]
    public void EffectiveEntries_EmptyEntriesList_SynthesizesLikeNull()
    {
        Section section = SectionWithBlocks("only");
        section.Entries = [];

        SectionEntry entry = Assert.Single(section.EffectiveEntries());
        Assert.Equal("only", entry.SlotTag);
        Assert.Equal(SectionEntryKind.Inline, entry.Kind);
    }

    [Fact]
    public void EffectiveEntries_WithEntries_ReturnsThemInPosition()
    {
        Section section = SectionWithBlocks("kyc_check");
        section.Entries =
        [
            RefEntry("z_last", "component-z"),
            new SectionEntry { SlotTag = "kyc_check", Kind = SectionEntryKind.Inline },
        ];

        List<SectionEntry> effective = section.EffectiveEntries();

        Assert.Equal(["z_last", "kyc_check"], effective.Select(e => e.SlotTag).ToList());
    }

    [Fact]
    public void Canonicalize_PreservesEntriesArrayPosition()
    {
        Section section = SectionWithBlocks("alpha");
        section.Entries =
        [
            RefEntry("zulu", "component-z"),
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            RefEntry("bravo", "component-b"),
        ];

        string json = _canonicalizer.ToComponentCanonicalJson(section);
        var entries = (JArray)JObject.Parse(json)[nameof(Section.Entries)]!;

        Assert.Equal(
            ["zulu", "alpha", "bravo"],
            entries.Select(e => (string?)e[nameof(SectionEntry.SlotTag)]).ToList());
    }
}
