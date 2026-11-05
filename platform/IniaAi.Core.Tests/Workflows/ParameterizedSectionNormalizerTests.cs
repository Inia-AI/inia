using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;

namespace IniaAi.Core.Tests.Workflows;

public class ParameterizedSectionNormalizerTests
{
    #region Helpers

    /// <summary>The Corp shape observed on staging: blocks with mixed junk classes, single-class variants.</summary>
    private static (Workflow parent, Section section) CreateLegacyCorpSection()
    {
        var parent = new Workflow("Corp");
        var section = new Section("", parent) { Title = "Extract - P&L", Order = 1 };
        AddBlock(section, 0, "a", ["Annual Report"]);
        AddBlock(section, 1, "b", ["None"]);
        AddBlock(section, 2, "c", []);
        AddBlock(section, 3, "d", ["Any"]);

        section.ParameterizedConfig = Serialize(
            new SectionVariant("AR", "Annual Report", SectionSupportedDocumentClasses: ["Annual Report"]),
            new SectionVariant("PAR", "Past Annual Report", SectionSupportedDocumentClasses: ["Past Annual Report"]),
            new SectionVariant("QR", "Quarterly Financials", SectionSupportedDocumentClasses: ["Quarterly Financials"]));

        return (parent, section);
    }

    private static void AddBlock(Section section, int order, string tag, List<string> classes)
    {
        _ = new Block(BlockTypes.AiQuery, section, order, tag)
        {
            Name = tag,
            SupportedDocumentClasses = classes,
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
    }

    private static string Serialize(params SectionVariant[] variants) =>
        JsonConvert.SerializeObject(new ParameterizedSectionConfig([.. variants]));

    #endregion

    #region Conversion Tests

    [Fact]
    public void Normalize_CorpShape_RewritesAllBlocksToBaseClass()
    {
        (Workflow _, Section section) = CreateLegacyCorpSection();

        Assert.NotNull(ParameterizedSectionNormalizer.Normalize(section));

        Assert.All(section.Blocks, block =>
            Assert.Equal(["Annual Report"], block.SupportedDocumentClasses));
    }

    [Fact]
    public void Normalize_CorpShape_BuildsIdentityAndTargetMaps()
    {
        (Workflow _, Section section) = CreateLegacyCorpSection();

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);

        Assert.NotNull(config);
        Assert.Empty(config.Variants[0].ClassMap!);
        Assert.Equal(["Past Annual Report"], config.Variants[1].ClassMap!["Annual Report"]);
        Assert.Equal(["Quarterly Financials"], config.Variants[2].ClassMap!["Annual Report"]);
    }

    [Fact]
    public void Normalize_RetainsLegacyListsForRollbackCompatibility()
    {
        (Workflow _, Section section) = CreateLegacyCorpSection();

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);

        Assert.NotNull(config);
        Assert.Equal(["Annual Report"], config.Variants[0].SectionSupportedDocumentClasses!);
        Assert.Equal(["Past Annual Report"], config.Variants[1].SectionSupportedDocumentClasses!);
        Assert.Contains("SectionSupportedDocumentClasses", section.ParameterizedConfig);
    }

    [Fact]
    public void Normalize_VariantWithAnyList_MapsBaseToAny()
    {
        var parent = new Workflow("HF");
        var section = new Section("", parent) { Title = "MGR Extract", Order = 1 };
        AddBlock(section, 0, "a", ["DDQ"]);
        section.ParameterizedConfig = Serialize(
            new SectionVariant("DDQ", "DDQ", SectionSupportedDocumentClasses: ["DDQ"]),
            new SectionVariant("IP", "Investment Pack", SectionSupportedDocumentClasses: ["Any"]));

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);

        Assert.NotNull(config);
        Assert.Equal(["Any"], config.Variants[1].ClassMap!["DDQ"]);
    }

    [Fact]
    public void Normalize_VariantWithNullList_MapsBaseToAny()
    {
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        AddBlock(section, 0, "a", []);
        section.ParameterizedConfig = Serialize(
            new SectionVariant("V1", "One", SectionSupportedDocumentClasses: ["Invoice"]),
            new SectionVariant("V2", "Two"));

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);

        Assert.NotNull(config);
        Assert.Equal(["Any"], config.Variants[1].ClassMap!["Invoice"]);
    }

    [Fact]
    public void Normalize_MultiClassLegacyList_FansOutFromBase()
    {
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        AddBlock(section, 0, "a", []);
        section.ParameterizedConfig = Serialize(
            new SectionVariant("V1", "One", SectionSupportedDocumentClasses: ["Invoice"]),
            new SectionVariant("V2", "Two", SectionSupportedDocumentClasses: ["Receipt", "Statement"]));

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);

        Assert.NotNull(config);
        Assert.Equal(["Receipt", "Statement"], config.Variants[1].ClassMap!["Invoice"]);
    }

    [Fact]
    public void Normalize_FirstVariantSentinelOnly_BaseComesFromNextVariant()
    {
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        AddBlock(section, 0, "a", []);
        section.ParameterizedConfig = Serialize(
            new SectionVariant("V1", "One", SectionSupportedDocumentClasses: ["Any"]),
            new SectionVariant("V2", "Two", SectionSupportedDocumentClasses: ["DDQ"]));

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);

        Assert.NotNull(config);
        Assert.Equal(["DDQ"], section.Blocks.Single().SupportedDocumentClasses);
        Assert.Equal(["Any"], config.Variants[0].ClassMap!["DDQ"]);
        Assert.Empty(config.Variants[1].ClassMap!);
    }

    [Fact]
    public void Normalize_UniformAnyVariants_ResetsBlocksAndBuildsEmptyMaps()
    {
        // Legacy semantics reset every block to [] (all documents) for these variants;
        // empty block classes plus empty maps reproduce that.
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        AddBlock(section, 0, "a", ["Annual Report"]);
        section.ParameterizedConfig = Serialize(
            new SectionVariant("V1", "One", SectionSupportedDocumentClasses: ["Any"]),
            new SectionVariant("V2", "Two"));

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);

        Assert.NotNull(config);
        Assert.Empty(section.Blocks.Single().SupportedDocumentClasses);
        Assert.All(config.Variants, v =>
        {
            Assert.NotNull(v.ClassMap);
            Assert.Empty(v.ClassMap!);
        });
    }

    #endregion

    #region No-Op Tests

    [Fact]
    public void Normalize_SentinelMixIncludingNone_LeavesConfigUntouched()
    {
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        AddBlock(section, 0, "a", ["Annual Report"]);
        string original = Serialize(
            new SectionVariant("V1", "One", SectionSupportedDocumentClasses: ["Any"]),
            new SectionVariant("V2", "Two", SectionSupportedDocumentClasses: ["None"]));
        section.ParameterizedConfig = original;

        _ = ParameterizedSectionNormalizer.Normalize(section);

        Assert.Equal(original, section.ParameterizedConfig);
        Assert.Equal(["Annual Report"], section.Blocks.Single().SupportedDocumentClasses);
    }

    [Fact]
    public void Normalize_AlreadyConverted_NoOp()
    {
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        AddBlock(section, 0, "a", ["Invoice"]);
        string original = Serialize(
            new SectionVariant("V1", "One", ClassMap: new() { ["Invoice"] = ["Receipt"] }));
        section.ParameterizedConfig = original;

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);

        Assert.NotNull(config);
        Assert.Equal(original, section.ParameterizedConfig);
        Assert.Equal(["Invoice"], section.Blocks.Single().SupportedDocumentClasses);
    }

    [Fact]
    public void Normalize_Idempotent()
    {
        (Workflow _, Section section) = CreateLegacyCorpSection();

        _ = ParameterizedSectionNormalizer.Normalize(section);
        string afterFirst = section.ParameterizedConfig!;

        _ = ParameterizedSectionNormalizer.Normalize(section);
        Assert.Equal(afterFirst, section.ParameterizedConfig);
    }

    [Fact]
    public void Normalize_MalformedJson_ReturnsNullAndLeavesConfig()
    {
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        section.ParameterizedConfig = "{ not valid json";

        Assert.Null(ParameterizedSectionNormalizer.Normalize(section));
        Assert.Equal("{ not valid json", section.ParameterizedConfig);
    }

    [Fact]
    public void Normalize_EmptyConfig_ReturnsNull()
    {
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        section.ParameterizedConfig = null;

        Assert.Null(ParameterizedSectionNormalizer.Normalize(section));
    }

    #endregion

    #region Legacy Equivalence Tests

    [Fact]
    public void NormalizeThenExpand_ReproducesLegacyReplaceSemantics()
    {
        // Legacy semantics: every block in variant V gets exactly V's list, regardless of
        // the block's own classes; a null/empty list resets blocks to "all documents".
        (Workflow parent, Section section) = CreateLegacyCorpSection();

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);
        Assert.NotNull(config);
        List<Section> expanded = ParameterizedSectionExpander.Expand(
            parent.Sections.First(s => s.Id == section.Id), config);

        Assert.All(expanded[0].Blocks, b => Assert.Equal(["Annual Report"], b.SupportedDocumentClasses));
        Assert.All(expanded[1].Blocks, b => Assert.Equal(["Past Annual Report"], b.SupportedDocumentClasses));
        Assert.All(expanded[2].Blocks, b => Assert.Equal(["Quarterly Financials"], b.SupportedDocumentClasses));
    }

    [Fact]
    public void NormalizeThenExpand_UniformAnyVariants_EveryBlockGetsAllDocuments()
    {
        var parent = new Workflow("W");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        AddBlock(section, 0, "a", ["Annual Report"]);
        section.ParameterizedConfig = Serialize(
            new SectionVariant("V1", "One", SectionSupportedDocumentClasses: ["Any"]),
            new SectionVariant("V2", "Two"));

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);
        Assert.NotNull(config);
        List<Section> expanded = ParameterizedSectionExpander.Expand(
            parent.Sections.First(s => s.Id == section.Id), config);

        // Empty classes attach all documents at distribution, matching the legacy reset-to-[].
        Assert.All(expanded, variant =>
            Assert.Empty(variant.Blocks.Single().SupportedDocumentClasses));
    }

    #endregion
}
