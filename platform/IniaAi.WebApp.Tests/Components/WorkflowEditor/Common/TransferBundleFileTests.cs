using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class TransferBundleFileTests
{
    private const string CAMEL_CASE_BUNDLE_JSON =
        """
        {"bundleFormatVersion":1,"originWorkflowTypeId":"wt-1","exportedAtUtc":"2026-08-03T10:00:00Z","exportedByUserId":"u1","templateJson":"{}","templateHashTrail":["h1"],"components":[{"componentId":"c1","kind":"Block","name":"Block A","description":null,"categoryId":null,"headContentHash":"bh","headDefinitionJson":"{}","hashTrail":["bh"]}],"categories":[]}
        """;

    private const string PASCAL_CASE_BUNDLE_JSON =
        """
        {"BundleFormatVersion":1,"OriginWorkflowTypeId":"wt-1","ExportedAtUtc":"2026-08-03T10:00:00Z","ExportedByUserId":"u1","TemplateJson":"{}","TemplateHashTrail":["h1"],"Components":[{"ComponentId":"c1","Kind":"section","Name":"Section B","Description":"d","CategoryId":"cat","HeadContentHash":"sh","HeadDefinitionJson":"{}","HashTrail":["sh"]}],"Categories":[{"Id":"cat","Name":"Cat","Description":null}]}
        """;

    private const string WORKFLOW_EXPORT_JSON = """{"FormatVersion":6,"Name":"Wf","Sections":[]}""";

    private const string LEGACY_WORKFLOW_JSON = """{"Name":"Old","Children":[]}""";

    private static WorkflowTransferBundle CreateBundle()
    {
        return new WorkflowTransferBundle(
            BundleFormatVersion: 1,
            OriginWorkflowTypeId: "wt-origin",
            WorkflowTypeName: "Origin Type",
            WorkflowTypeDescription: "origin type description",
            ExportedAtUtc: new DateTime(2026, 8, 3, 12, 30, 0, DateTimeKind.Utc),
            ExportedByUserId: "user-1",
            TemplateJson: """{"Name":"T","Sections":[]}""",
            TemplateHashTrail: ["hash-1", "hash-2"],
            Components:
            [
                new TransferComponentDto(
                    "comp-block",
                    SharedComponentKind.Block,
                    "Block A",
                    "block description",
                    "cat-1",
                    "bh-2",
                    """{"BlockType":"Prompt"}""",
                    ["bh-1", "bh-2"]),
                new TransferComponentDto(
                    "comp-section",
                    SharedComponentKind.Section,
                    "Section B",
                    null,
                    null,
                    "sh-1",
                    """{"Blocks":[]}""",
                    ["sh-1"]),
            ],
            Categories: [new TransferCategoryDto("cat-1", "Category One", null)]);
    }

    [Fact]
    public void Serialize_ThenTryDeserialize_RoundTripsLosslessly()
    {
        WorkflowTransferBundle bundle = CreateBundle();

        string json = TransferBundleFile.Serialize(bundle);
        WorkflowTransferBundle? result = TransferBundleFile.TryDeserialize(json);

        Assert.NotNull(result);
        Assert.Equal(json, TransferBundleFile.Serialize(result));
        Assert.Equal(SharedComponentKind.Block, result.Components[0].Kind);
        Assert.Equal(SharedComponentKind.Section, result.Components[1].Kind);
        Assert.Equal(bundle.ExportedAtUtc, result.ExportedAtUtc);
        Assert.Equal(bundle.TemplateHashTrail, result.TemplateHashTrail);
    }

    [Fact]
    public void Serialize_WritesCamelCasePropertiesAndEnumNames()
    {
        string json = TransferBundleFile.Serialize(CreateBundle());

        Assert.Contains("\"bundleFormatVersion\"", json);
        Assert.Contains("\"originWorkflowTypeId\"", json);
        Assert.Contains("\"templateJson\"", json);
        Assert.Contains("\"Block\"", json);
        Assert.Contains("\"Section\"", json);
        Assert.DoesNotContain("\"BundleFormatVersion\"", json);
        Assert.DoesNotContain($"\"kind\": {(int)SharedComponentKind.Block}", json);
    }

    [Fact]
    public void TryDeserialize_ReadsCamelCaseApiOutput()
    {
        WorkflowTransferBundle? result = TransferBundleFile.TryDeserialize(CAMEL_CASE_BUNDLE_JSON);

        Assert.NotNull(result);
        Assert.Equal("wt-1", result.OriginWorkflowTypeId);
        Assert.Equal(SharedComponentKind.Block, result.Components[0].Kind);
        Assert.Equal(new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc), result.ExportedAtUtc);
    }

    [Fact]
    public void TryDeserialize_ReadsPascalCaseAndCaseInsensitiveEnums()
    {
        WorkflowTransferBundle? result = TransferBundleFile.TryDeserialize(PASCAL_CASE_BUNDLE_JSON);

        Assert.NotNull(result);
        Assert.Equal("wt-1", result.OriginWorkflowTypeId);
        Assert.Equal(SharedComponentKind.Section, result.Components[0].Kind);
        Assert.Equal("cat", result.Categories[0].Id);
    }

    [Fact]
    public void TryDeserialize_NormalizesAbsentListsToEmpty()
    {
        string minimal = """{"bundleFormatVersion":1,"originWorkflowTypeId":"wt-1","templateJson":"{}"}""";

        WorkflowTransferBundle? result = TransferBundleFile.TryDeserialize(minimal);

        Assert.NotNull(result);
        Assert.Empty(result.TemplateHashTrail);
        Assert.Empty(result.Components);
        Assert.Empty(result.Categories);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"foo":1}""")]
    [InlineData(WORKFLOW_EXPORT_JSON)]
    public void TryDeserialize_ReturnsNullForNonBundlePayloads(string json)
    {
        Assert.Null(TransferBundleFile.TryDeserialize(json));
    }

    [Fact]
    public void TryDeserialize_ReturnsNullWhenCoreFieldsAreEmpty()
    {
        string emptyOrigin = """{"bundleFormatVersion":1,"originWorkflowTypeId":"","templateJson":"{}"}""";
        string zeroVersion = """{"bundleFormatVersion":0,"originWorkflowTypeId":"wt-1","templateJson":"{}"}""";

        Assert.Null(TransferBundleFile.TryDeserialize(emptyOrigin));
        Assert.Null(TransferBundleFile.TryDeserialize(zeroVersion));
    }

    [Fact]
    public void TryDeserialize_ReturnsNullOnUnknownEnumValue()
    {
        string unknownKind =
            """
            {"bundleFormatVersion":1,"originWorkflowTypeId":"wt-1","templateJson":"{}","components":[{"componentId":"c1","kind":"Widget","name":"X","headContentHash":"h","headDefinitionJson":"{}","hashTrail":[]}]}
            """;

        Assert.Null(TransferBundleFile.TryDeserialize(unknownKind));
    }

    [Theory]
    [InlineData(CAMEL_CASE_BUNDLE_JSON)]
    [InlineData(PASCAL_CASE_BUNDLE_JSON)]
    public void Classify_DetectsTransferBundlesInBothCasings(string json)
    {
        Assert.Equal(TransferPayloadKind.TransferBundle, TransferBundleFile.Classify(json));
    }

    [Fact]
    public void Classify_TreatsUnsupportedFutureVersionAsBundle()
    {
        string future = """{"bundleFormatVersion":99,"originWorkflowTypeId":"wt-1","templateJson":"{}"}""";

        Assert.Equal(TransferPayloadKind.TransferBundle, TransferBundleFile.Classify(future));
    }

    [Theory]
    [InlineData(WORKFLOW_EXPORT_JSON)]
    [InlineData(LEGACY_WORKFLOW_JSON)]
    [InlineData("""{"formatVersion":6,"name":"CamelWf","sections":[]}""")]
    [InlineData("""{"Name":"NoVersion","Sections":[{"Name":"S1"}]}""")]
    public void Classify_DetectsWorkflowJson(string json)
    {
        Assert.Equal(TransferPayloadKind.WorkflowJson, TransferBundleFile.Classify(json));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    [InlineData("""{"foo":1,"bar":"baz"}""")]
    [InlineData("""{"Name":"OnlyAName"}""")]
    public void Classify_ReturnsUnknownForEverythingElse(string json)
    {
        Assert.Equal(TransferPayloadKind.Unknown, TransferBundleFile.Classify(json));
    }

    [Fact]
    public void Classify_ToleratesByteOrderMarkPrefix()
    {
        string withBom = char.ConvertFromUtf32(0xFEFF) + CAMEL_CASE_BUNDLE_JSON;

        Assert.Equal(TransferPayloadKind.TransferBundle, TransferBundleFile.Classify(withBom));
        Assert.NotNull(TransferBundleFile.TryDeserialize(withBom));
    }
}
