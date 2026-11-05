using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class DocumentClassCascadeTests
{
    [Fact]
    public void RenameDocumentClassKey_UpdatesSupportedDocumentClassReferences()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["invoice", "receipt"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        workflow.RenameDocumentClassKey(documentClass, "invoice_v2");

        Assert.Equal("invoice_v2", documentClass.Key);
        Assert.Equal(["invoice_v2", "receipt"], block.SupportedDocumentClasses);
    }

    [Fact]
    public void RenameDocumentClassKey_UpdatesCreatesDocumentClassReference()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "web-data") { IsOutputDocument = true };
        var producer = new Block(BlockTypes.IntelligentSearch, section)
        {
            ReplacementTag = "p",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" },
        };

        workflow.RenameDocumentClassKey(documentClass, "web-data-v2");

        var props = Assert.IsType<IntelligentSearchProperties>(producer.Properties);
        Assert.Equal("web-data-v2", props.CreatesDocumentClass);
    }

    [Fact]
    public void RenameDocumentClassKey_UpdatesJson2XlsxInputDocumentClassReference()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "model");
        var extractor = new Block(BlockTypes.Json2Xlsx, section)
        {
            ReplacementTag = "e",
            Properties = new Json2XlsxProperties
            {
                Direction = Json2XlsxDirection.Extract,
                InputDocumentClass = "model",
            },
        };

        workflow.RenameDocumentClassKey(documentClass, "model_v2");

        var props = Assert.IsType<Json2XlsxProperties>(extractor.Properties);
        Assert.Equal("model_v2", props.InputDocumentClass);
    }

    [Fact]
    public void RenameDocumentClassKey_LeavesUnrelatedReferencesUntouched()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["receipt"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        workflow.RenameDocumentClassKey(documentClass, "invoice_v2");

        Assert.Equal(["receipt"], block.SupportedDocumentClasses);
    }

    [Fact]
    public void RemoveDocumentClass_RemovesClassAndStripsSupportedReference()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["invoice", "receipt"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        workflow.RemoveDocumentClass(documentClass);

        Assert.DoesNotContain(documentClass, workflow.DocumentClasses);
        Assert.Equal(["receipt"], block.SupportedDocumentClasses);
    }

    [Fact]
    public void RemoveDocumentClass_WhenRemovalEmptiesSupported_FallsBackToAny()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["invoice"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        workflow.RemoveDocumentClass(documentClass);

        Assert.Equal([DocumentClass.KEY_ANY], block.SupportedDocumentClasses);
    }

    [Fact]
    public void RemoveDocumentClass_EmptiedBlockInParameterizedSection_FallsBackToNone()
    {
        // Block classes are the variant maps' pivot; "Any" would feed every variant all documents.
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2"] })]));
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["invoice"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        workflow.RemoveDocumentClass(documentClass);

        Assert.Equal([DocumentClass.KEY_NONE], block.SupportedDocumentClasses);
    }

    [Fact]
    public void RemoveDocumentClass_NullsCreatesDocumentClassReference()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "web-data") { IsOutputDocument = true };
        var producer = new Block(BlockTypes.IntelligentSearch, section)
        {
            ReplacementTag = "p",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" },
        };

        workflow.RemoveDocumentClass(documentClass);

        var props = Assert.IsType<IntelligentSearchProperties>(producer.Properties);
        Assert.Null(props.CreatesDocumentClass);
    }

    [Fact]
    public void RemoveDocumentClass_NullsJson2XlsxInputDocumentClassReference()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "model");
        var extractor = new Block(BlockTypes.Json2Xlsx, section)
        {
            ReplacementTag = "e",
            Properties = new Json2XlsxProperties
            {
                Direction = Json2XlsxDirection.Extract,
                InputDocumentClass = "model",
            },
        };

        workflow.RemoveDocumentClass(documentClass);

        var props = Assert.IsType<Json2XlsxProperties>(extractor.Properties);
        Assert.Null(props.InputDocumentClass);
    }

    [Fact]
    public void RemoveDocumentClass_LeavesAlreadyEmptySupportedUntouched()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = [],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        workflow.RemoveDocumentClass(documentClass);

        Assert.Empty(block.SupportedDocumentClasses);
    }

    [Fact]
    public void RenameDocumentClassKey_UpdatesParameterizedVariantReferences()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("AR", "Annual", SectionSupportedDocumentClasses: ["invoice", "receipt"])]));

        workflow.RenameDocumentClassKey(documentClass, "invoice_v2");

        ParameterizedSectionConfig config = ParseConfig(section);
        Assert.Equal(["invoice_v2", "receipt"], config.Variants[0].SectionSupportedDocumentClasses!);
    }

    [Fact]
    public void RemoveDocumentClass_StripsParameterizedVariantReference()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("AR", "Annual", SectionSupportedDocumentClasses: ["invoice", "receipt"])]));

        workflow.RemoveDocumentClass(documentClass);

        ParameterizedSectionConfig config = ParseConfig(section);
        Assert.Equal(["receipt"], config.Variants[0].SectionSupportedDocumentClasses!);
    }

    [Fact]
    public void RemoveDocumentClass_WhenParameterizedVariantEmpties_FallsBackToAny()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("AR", "Annual", SectionSupportedDocumentClasses: ["invoice"])]));

        workflow.RemoveDocumentClass(documentClass);

        ParameterizedSectionConfig config = ParseConfig(section);
        Assert.Equal([DocumentClass.KEY_ANY], config.Variants[0].SectionSupportedDocumentClasses!);
    }

    [Fact]
    public void RenameDocumentClassKey_WhenNewKeyAlreadyPresentInBlock_DoesNotDuplicate()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "a",
            SupportedDocumentClasses = ["invoice", "receipt"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        workflow.RenameDocumentClassKey(documentClass, "receipt");

        Assert.Equal(["receipt"], block.SupportedDocumentClasses);
    }

    [Fact]
    public void RenameDocumentClassKey_WhenNewKeyAlreadyPresentInVariant_DoesNotDuplicate()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("AR", "Annual", SectionSupportedDocumentClasses: ["invoice", "receipt"])]));

        workflow.RenameDocumentClassKey(documentClass, "receipt");

        ParameterizedSectionConfig config = ParseConfig(section);
        Assert.Equal(["receipt"], config.Variants[0].SectionSupportedDocumentClasses!);
    }

    [Fact]
    public void RenameDocumentClassKey_MalformedParameterizedConfig_LeavesItUntouched()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = "{ not valid json";

        workflow.RenameDocumentClassKey(documentClass, "invoice_v2");

        Assert.Equal("{ not valid json", section.ParameterizedConfig);
    }

    [Fact]
    public void RenameDocumentClassKey_RewritesClassMapKeys()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2"], ["receipt"] = ["receipt_f2"] })]));

        workflow.RenameDocumentClassKey(documentClass, "invoice_v2");

        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Equal(2, map.Count);
        Assert.Equal(["invoice_f2"], map["invoice_v2"]);
        Assert.Equal(["receipt_f2"], map["receipt"]);
    }

    [Fact]
    public void RenameDocumentClassKey_RewritesClassMapTargets()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice_f2");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2", "other"] })]));

        workflow.RenameDocumentClassKey(documentClass, "invoice_fund2");

        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Equal(["invoice_fund2", "other"], map["invoice"]);
    }

    [Fact]
    public void RenameDocumentClassKey_TargetRenameCollidingInList_Deduplicates()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice_f2");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2", "receipt_f2"] })]));

        workflow.RenameDocumentClassKey(documentClass, "receipt_f2");

        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Equal(["receipt_f2"], map["invoice"]);
    }

    [Fact]
    public void RenameDocumentClassKey_ClassMapKeyCollision_KeepsExistingEntry()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "receipt");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2"], ["receipt"] = ["receipt_f2"] })]));

        workflow.RenameDocumentClassKey(documentClass, "invoice");

        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Single(map);
        Assert.Equal(["invoice_f2"], map["invoice"]);
    }

    [Fact]
    public void RenameDocumentClassKey_ClassMapKeyCollision_ExistingEntryWinsRegardlessOfSerializedOrder()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "receipt");

        // The renamed entry is serialized FIRST; the pre-existing "invoice" entry must still win.
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["receipt"] = ["receipt_f2"], ["invoice"] = ["invoice_f2"] })]));

        workflow.RenameDocumentClassKey(documentClass, "invoice");

        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Single(map);
        Assert.Equal(["invoice_f2"], map["invoice"]);
    }

    [Fact]
    public void RenameDocumentClassKey_ClassMapRenameCreatingIdentityEntry_IsKept()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2"] })]));

        workflow.RenameDocumentClassKey(documentClass, "invoice_f2");

        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Equal(["invoice_f2"], map["invoice_f2"]);
    }

    [Fact]
    public void RemoveDocumentClass_RemovesClassMapEntryByKey()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2"], ["receipt"] = ["receipt_f2"] })]));

        workflow.RemoveDocumentClass(documentClass);

        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Single(map);
        Assert.Equal(["receipt_f2"], map["receipt"]);
    }

    [Fact]
    public void RemoveDocumentClass_RemovesClassFromClassMapTargets()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice_f2");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2", "other"] })]));

        workflow.RemoveDocumentClass(documentClass);

        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Equal(["other"], map["invoice"]);
    }

    [Fact]
    public void RemoveDocumentClass_EmptiedClassMapTargets_RemapToNone()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice_f2");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2"], ["receipt"] = ["receipt_f2"] })]));

        workflow.RemoveDocumentClass(documentClass);

        // Starve the blocks rather than silently feed them the source class's documents.
        Dictionary<string, List<string>> map = ParseConfig(section).Variants[0].ClassMap!;
        Assert.Equal([DocumentClass.KEY_NONE], map["invoice"]);
        Assert.Equal(["receipt_f2"], map["receipt"]);
    }

    [Fact]
    public void RemoveDocumentClass_EmptiedClassMap_StaysEmptyNotNull()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2"] })]));

        workflow.RemoveDocumentClass(documentClass);

        ParameterizedSectionConfig config = ParseConfig(section);
        Assert.NotNull(config.Variants[0].ClassMap);
        Assert.Empty(config.Variants[0].ClassMap!);
    }

    [Fact]
    public void RenameDocumentClassKey_VariantWithoutClassMap_ConfigHasNoClassMapMemberAfterCascade()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var documentClass = new DocumentClass(workflow, "invoice");
        section.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("AR", "Annual", SectionSupportedDocumentClasses: ["invoice"])]));

        workflow.RenameDocumentClassKey(documentClass, "invoice_v2");

        Assert.DoesNotContain("ClassMap", section.ParameterizedConfig);
        Assert.Null(ParseConfig(section).Variants[0].ClassMap);
    }

    [Fact]
    public void SectionVariant_LegacyJsonWithoutClassMap_DeserializesWithNullClassMap()
    {
        const string legacyJson =
            """{"Variants":[{"Key":"AR","Label":"Annual","Params":null,"SectionStopGate":null,"SectionSupportedDocumentClasses":["invoice"]}]}""";

        ParameterizedSectionConfig? config = JsonConvert.DeserializeObject<ParameterizedSectionConfig>(legacyJson);

        Assert.NotNull(config);
        Assert.Null(config.Variants[0].ClassMap);
        Assert.Equal(["invoice"], config.Variants[0].SectionSupportedDocumentClasses!);
    }

    [Fact]
    public void SectionVariant_ClassMapSurvivesJsonRoundTrip()
    {
        var original = new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2", ClassMap: new() { ["invoice"] = ["invoice_f2", "invoice_extra"] })]);

        string json = JsonConvert.SerializeObject(original);
        ParameterizedSectionConfig? roundTripped = JsonConvert.DeserializeObject<ParameterizedSectionConfig>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(["invoice_f2", "invoice_extra"], roundTripped.Variants[0].ClassMap!["invoice"]);
    }

    private static ParameterizedSectionConfig ParseConfig(Section section)
    {
        ParameterizedSectionConfig? config =
            JsonConvert.DeserializeObject<ParameterizedSectionConfig>(section.ParameterizedConfig!);
        Assert.NotNull(config);
        return config;
    }
}
