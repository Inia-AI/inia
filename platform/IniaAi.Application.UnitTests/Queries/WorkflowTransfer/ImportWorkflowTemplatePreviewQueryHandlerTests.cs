using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Application.Queries.WorkflowTransfer.ImportWorkflowTemplatePreview;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using DomainKind = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind;
using DomainState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentState;
using LinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;
using Method = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferCorrelationMethod;
using Relation = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferRelation;
using WireKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.UnitTests.Queries.WorkflowTransfer;

public class ImportWorkflowTemplatePreviewQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";

    [Fact]
    public async Task Preview_IntoARetiredType_ReportsItDeleted()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = harness.Context.Add(new WorkflowType(TYPE, "Deleted") { RetiredAt = DateTime.UtcNow });
        _ = harness.Context.SaveChanges();
        WorkflowTransferBundle bundle = Bundle(harness, []);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task Preview_NewComponent_IsNewViaCreate()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string json, string hash) = BlockDef(harness, "new");
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-new", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.New, row.Relation);
        Assert.Equal(Method.Create, row.CorrelationMethod);
        Assert.Null(row.LocalComponentId);
        Assert.Null(row.LocalHeadVersionId);
    }

    [Fact]
    public async Task Preview_CorrelatedRow_CarriesLocalHeadVersionId()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string json1, string hash1) = BlockDef(harness, "v1");
        (string json2, string hash2) = BlockDef(harness, "v2");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hash1, json1);
        string localHeadVersionId = (await harness.AddVersionAsync("sc-1", 2, hash2, json2)).Id;
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json1, hash1, hash1)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.LocalAhead, row.Relation);
        Assert.Equal(localHeadVersionId, row.LocalHeadVersionId);
    }

    [Fact]
    public async Task Preview_SameHead_IsInSync()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string json, string hash) = BlockDef(harness, "kyc");
        _ = await harness.AddComponentAsync(DomainKind.Block, "KYC", id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hash, json);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.InSync, row.Relation);
        Assert.Equal("sc-1", row.LocalComponentId);
    }

    [Fact]
    public async Task Preview_SameHeadStoredBeforeAFieldWasDropped_IsInSync()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string json, string hash) = harness.StoredBeforeAFieldWasDropped(BlockDef(harness, "kyc").Json);
        _ = await harness.AddComponentAsync(DomainKind.Block, "KYC", id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hash, json);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        Assert.Equal(Relation.InSync, Assert.Single(result.Value.Components).Relation);
    }

    [Fact]
    public async Task Preview_BundleDescendsFromLocalHead_IsBundleAhead()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string json1, string hash1) = BlockDef(harness, "v1");
        (string json2, string hash2) = BlockDef(harness, "v2");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hash1, json1);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json2, hash2, hash1, hash2)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.BundleAhead, row.Relation);
        Assert.Equal(Method.HashTrail, row.CorrelationMethod);
    }

    [Fact]
    public async Task Preview_LocalDescendsFromBundleHead_IsLocalAhead()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string json1, string hash1) = BlockDef(harness, "v1");
        (string json2, string hash2) = BlockDef(harness, "v2");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hash1, json1);
        _ = await harness.AddVersionAsync("sc-1", 2, hash2, json2);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json1, hash1, hash1)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.LocalAhead, row.Relation);
    }

    [Fact]
    public async Task Preview_GuidMatchNoSharedHistory_IsDivergedLineage()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string jsonA, string hashA) = BlockDef(harness, "A");
        (string jsonB, string hashB) = BlockDef(harness, "B");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hashA, jsonA);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", jsonB, hashB, hashB)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.Diverged, row.Relation);
        Assert.Equal(SharedComponentErrorCodes.DIVERGED_LINEAGE, row.ConflictCode);
        Assert.Equal(Method.GuidEquality, row.CorrelationMethod);
        Assert.Equal([hashA], row.LocalHashTrail);
        Assert.Equal([hashB], row.BundleHashTrail);
    }

    [Fact]
    public async Task Preview_CorrelatedLocalRetired_IsLocalRetired()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string json, string hash) = BlockDef(harness, "r");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", state: DomainState.Retired, id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hash, json);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.LocalRetired, row.Relation);
    }

    [Fact]
    public async Task Preview_SectionRefTargetMissingFromBundle_RejectsWholeBundle()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);

        var defSection = new Section("Market", new Workflow("S")) { Title = "Market" };
        var block = new Block(BlockTypes.AiQuery, defSection, "r")
        {
            Name = "A",
            Properties = new AiQueryProperties { UserPromptTemplate = "a" },
        };
        block.SharedComponentId = "sc-missing";
        block.SharedComponentLinkState = LinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        block.SharedComponentBindings = new Dictionary<string, string>();
        (string secJson, string secHash) = harness.Definition(defSection);
        var sectionComp = new TransferComponentDto(
            "sc-section", WireKind.Section, "Market", null, null, secHash, secJson, [secHash]);
        WorkflowTransferBundle bundle = Bundle(harness, [sectionComp]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.CORRUPT_DEFINITION, result.FirstError.Code);
    }

    [Fact]
    public async Task Preview_SectionRefToRetiredLocalTarget_WarnsOnSectionRow()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        (string depJson, string depHash) = BlockDef(harness, "dep");
        _ = await harness.AddComponentAsync(DomainKind.Block, "Dep", state: DomainState.Retired, id: "sc-dep");
        _ = await harness.AddVersionAsync("sc-dep", 1, depHash, depJson);
        (string secJson, string secHash) = harness.Definition(
            SharedComponentLinkTestData.RefSection("Market", "r", "sc-dep"));
        WorkflowTransferBundle bundle = Bundle(
            harness,
            [
                new TransferComponentDto("sc-section", WireKind.Section, "Market", null, null, secHash, secJson, [secHash]),
                BlockComp("sc-dep", depJson, depHash, depHash),
            ]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        ImportComponentPreviewRow sectionRow = Assert.Single(
            result.Value.Components, r => r.BundleComponentId == "sc-section");
        string warning = Assert.Single(sectionRow.RefWarnings ?? []);
        Assert.Contains("sc-dep", warning);
        Assert.Contains(SharedComponentErrorCodes.RETIRED, warning);
        ImportComponentPreviewRow depRow = Assert.Single(
            result.Value.Components, r => r.BundleComponentId == "sc-dep");
        Assert.True(depRow.RefWarnings is null or { Count: 0 });
    }

    [Fact]
    public async Task Preview_NewerBundleFormat_IsRejected()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = SeedLocalTemplate(harness);
        WorkflowTransferBundle bundle = Bundle(harness, [], formatVersion: 2);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.BUNDLE_FORMAT_UNSUPPORTED, result.FirstError.Code);
    }

    [Fact]
    public async Task Preview_TargetTypeMissing_ReturnsCreatePreview()
    {
        using var harness = new SharedComponentsTestHarness();
        (string json, string hash) = BlockDef(harness, "x");
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-x", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        Assert.Null(result.Value.BaseVersionId);
        Assert.Equal(Relation.New, result.Value.Template.Relation);
        Assert.Null(result.Value.Template.ConflictCode);
        Assert.Empty(result.Value.Template.LocalHashTrail);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.New, row.Relation);
    }

    [Fact]
    public async Task Preview_TargetTypeMissing_StillCorrelatesComponentsAgainstLibrary()
    {
        using var harness = new SharedComponentsTestHarness();
        (string json, string hash) = BlockDef(harness, "kyc");
        _ = await harness.AddComponentAsync(DomainKind.Block, "KYC", id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hash, json);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        Assert.Null(result.Value.BaseVersionId);
        ImportComponentPreviewRow row = Assert.Single(result.Value.Components);
        Assert.Equal(Relation.InSync, row.Relation);
        Assert.Equal("sc-1", row.LocalComponentId);
    }

    [Fact]
    public async Task Preview_TargetTypeHeadless_ReturnsCreatePreview()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = harness.Context.Add(new WorkflowType(TYPE, "Report"));
        _ = harness.Context.SaveChanges();
        WorkflowTransferBundle bundle = Bundle(harness, []);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        Assert.Null(result.Value.BaseVersionId);
        Assert.Equal(Relation.New, result.Value.Template.Relation);
    }

    [Fact]
    public async Task Preview_TemplateHeadMatches_TemplateInSyncAndBaseVersionReturned()
    {
        using var harness = new SharedComponentsTestHarness();
        string versionId = SeedLocalTemplate(harness, contentHash: "tpl-head");
        WorkflowTransferBundle bundle = Bundle(harness, [], templateTrail: ["tpl-root", "tpl-head"]);

        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await Preview(harness, bundle);

        Assert.False(result.IsError);
        Assert.Equal(Relation.InSync, result.Value.Template.Relation);
        Assert.Equal(versionId, result.Value.BaseVersionId);
        Assert.NotEmpty(result.Value.SkippedCarriers);
    }

    private static Task<ErrorOr<ImportWorkflowTemplatePreviewResult>> Preview(
        SharedComponentsTestHarness harness,
        WorkflowTransferBundle bundle)
    {
        var handler = new ImportWorkflowTemplatePreviewQueryHandler(
            harness.UnitOfWork,
            new WorkflowTemplateStructuralValidator(),
            harness.CreateFanOutService());

        return handler.Handle(
            new ImportWorkflowTemplatePreviewQuery(ORG, USER, TYPE, bundle),
            TestContext.Current.CancellationToken);
    }

    private static (string Json, string Hash) BlockDef(SharedComponentsTestHarness harness, string prompt)
    {
        return harness.Definition(SharedComponentLinkTestData.BuildBlockDefinition(prompt));
    }

    private static TransferComponentDto BlockComp(string id, string headJson, string headHash, params string[] trail)
    {
        return new TransferComponentDto(id, WireKind.Block, id, null, null, headHash, headJson, [.. trail]);
    }

    private static WorkflowTransferBundle Bundle(
        SharedComponentsTestHarness harness,
        IReadOnlyList<TransferComponentDto> components,
        int formatVersion = 1,
        List<string>? templateTrail = null)
    {
        return new WorkflowTransferBundle(
            formatVersion,
            TYPE,
            "Imported",
            null,
            DateTime.UtcNow,
            USER,
            BundleTemplateJson(harness),
            templateTrail ?? ["tpl-bundle"],
            [.. components],
            []);
    }

    private static string BundleTemplateJson(SharedComponentsTestHarness harness)
    {
        var workflow = new Workflow("Imported") { Title = "Imported" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "b1")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "?" },
        };
        return harness.Canonicalizer.ToCanonicalJson(workflow);
    }

    private static string SeedLocalTemplate(SharedComponentsTestHarness harness, string? contentHash = null)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new Section("Body", workflow) { Title = "Body" };
        _ = harness.Context.Add(new WorkflowType(TYPE, "Report"));
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = contentHash ?? harness.Canonicalizer.ComputeContentHash(workflow),
            CreatedByUserId = USER,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
        };
        version.WorkflowJson = harness.Canonicalizer.ToCanonicalJson(workflow);
        _ = harness.Context.Add(version);
        _ = harness.Context.SaveChanges();

        return version.Id;
    }
}
