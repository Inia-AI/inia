using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowTransfer.ExportWorkflowTemplate;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using DomainKind = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind;
using LinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;
using WireKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.UnitTests.Queries.WorkflowTransfer;

public class ExportWorkflowTemplateQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";
    private const string COMPONENT = "sc-kyc";

    [Fact]
    public async Task Export_BundlesTemplateComponentsAndCategories()
    {
        using var harness = new SharedComponentsTestHarness();

        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (string defJson, string defHash) = harness.Definition(
            SharedComponentLinkTestData.BuildBlockDefinition("kyc?"));
        _ = await harness.AddComponentAsync(DomainKind.Block, "KYC", categoryId: "analysis", id: COMPONENT);
        _ = await harness.AddVersionAsync(COMPONENT, 1, defHash, defJson);

        (string templateJson, string templateHash) = SeedTemplate(harness, defHash);

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        WorkflowTransferBundle bundle = result.Value.Bundle;
        Assert.Equal(1, bundle.BundleFormatVersion);
        Assert.Equal(TYPE, bundle.OriginWorkflowTypeId);
        Assert.Equal(templateJson, bundle.TemplateJson);
        Assert.Equal([templateHash], bundle.TemplateHashTrail);

        TransferComponentDto component = Assert.Single(bundle.Components);
        Assert.Equal(COMPONENT, component.ComponentId);
        Assert.Equal(WireKind.Block, component.Kind);
        Assert.Equal(defJson, component.HeadDefinitionJson);
        Assert.Equal([defHash], component.HashTrail);
        Assert.Equal("analysis", component.CategoryId);

        TransferCategoryDto category = Assert.Single(bundle.Categories);
        Assert.Equal("analysis", category.Id);
        Assert.Equal("Analysis", category.Name);
    }

    [Fact]
    public async Task Export_CarriesWorkflowTypeIdentity()
    {
        using var harness = new SharedComponentsTestHarness();

        _ = harness.Context.Add(new WorkflowType(TYPE, "Report") { Description = "Quarterly credit report" });
        _ = harness.Context.Add(TemplateVersion(harness, "Head", "hash-head", DateTime.UtcNow.AddDays(-1)));
        _ = harness.Context.SaveChanges();

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Report", result.Value.Bundle.WorkflowTypeName);
        Assert.Equal("Quarterly credit report", result.Value.Bundle.WorkflowTypeDescription);
        Assert.NotEmpty(result.Value.SkippedCarriers);
    }

    [Fact]
    public async Task Export_MultiVersionComponent_TrailIsAscendingAndHeadIsLatest()
    {
        using var harness = new SharedComponentsTestHarness();

        (string defJsonV1, string defHashV1) = harness.Definition(
            SharedComponentLinkTestData.BuildBlockDefinition("kyc v1?"));
        (string defJsonV2, string defHashV2) = harness.Definition(
            SharedComponentLinkTestData.BuildBlockDefinition("kyc v2?"));
        _ = await harness.AddComponentAsync(DomainKind.Block, "KYC", id: COMPONENT);
        _ = await harness.AddVersionAsync(COMPONENT, 1, defHashV1, defJsonV1);
        _ = await harness.AddVersionAsync(COMPONENT, 2, defHashV2, defJsonV2);

        _ = SeedTemplate(harness, defHashV2);

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        TransferComponentDto component = Assert.Single(result.Value.Bundle.Components);
        Assert.Equal(defHashV2, component.HeadContentHash);
        Assert.Equal(defJsonV2, component.HeadDefinitionJson);
        Assert.Equal([defHashV1, defHashV2], component.HashTrail);
    }

    [Fact]
    public async Task Export_MultiVersionTemplate_TrailAscendingThroughHead()
    {
        using var harness = new SharedComponentsTestHarness();

        _ = harness.Context.Add(new WorkflowType(TYPE, "Report"));
        _ = harness.Context.Add(TemplateVersion(harness, "Old", "hash-older", DateTime.UtcNow.AddDays(-2)));
        WorkflowVersion newer = TemplateVersion(harness, "New", "hash-newer", DateTime.UtcNow.AddDays(-1));
        _ = harness.Context.Add(newer);
        _ = harness.Context.SaveChanges();

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(["hash-older", "hash-newer"], result.Value.Bundle.TemplateHashTrail);
        Assert.Equal(newer.WorkflowJson, result.Value.Bundle.TemplateJson);
    }

    [Fact]
    public async Task Export_LinkedSectionWithRefTargets_BundlesSectionAndRefTargetBlocks()
    {
        using var harness = new SharedComponentsTestHarness();

        await SeedBlockComponentAsync(harness, "sc-block-a");
        await SeedBlockComponentAsync(harness, "sc-block-b");

        var defSection = new Section("Market", new Workflow("Scratch")) { Title = "Market" };
        LinkBlock(
            new Block(BlockTypes.AiQuery, defSection, "ra")
            {
                Name = "A",
                Properties = new AiQueryProperties { UserPromptTemplate = "a" },
            },
            "sc-block-a");
        LinkBlock(
            new Block(BlockTypes.AiQuery, defSection, "rb")
            {
                Name = "B",
                Properties = new AiQueryProperties { UserPromptTemplate = "b" },
            },
            "sc-block-b");
        (string secJson, string secHash) = harness.Definition(defSection);
        _ = await harness.AddComponentAsync(DomainKind.Section, "Market", id: "sc-section");
        _ = await harness.AddVersionAsync("sc-section", 1, secHash, secJson);

        var workflow = new Workflow("Report") { Title = "Report" };
        var linkedSection = new Section("Body", workflow) { Title = "Body" };
        linkedSection.SharedComponentId = "sc-section";
        linkedSection.SharedComponentLinkState = LinkState.Linked;
        linkedSection.SharedComponentVersionHash = secHash;
        linkedSection.SharedComponentBindings = new Dictionary<string, string>();

        _ = harness.Context.Add(new WorkflowType(TYPE, "Report"));
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = harness.Canonicalizer.ComputeContentHash(workflow),
            CreatedByUserId = USER,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
        };
        version.WorkflowJson = harness.Canonicalizer.ToCanonicalJson(workflow);
        _ = harness.Context.Add(version);
        _ = harness.Context.SaveChanges();

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        List<string> ids = [.. result.Value.Bundle.Components
            .Select(component => component.ComponentId)
            .OrderBy(id => id, StringComparer.Ordinal)];
        Assert.Equal(["sc-block-a", "sc-block-b", "sc-section"], ids);
        Assert.Equal(WireKind.Section, result.Value.Bundle.Components.Single(c => c.ComponentId == "sc-section").Kind);
        Assert.Equal(WireKind.Block, result.Value.Bundle.Components.Single(c => c.ComponentId == "sc-block-a").Kind);
    }

    [Fact]
    public async Task Export_ComponentHeadPayloadAbsent_ReturnsPayloadAbsent()
    {
        using var harness = new SharedComponentsTestHarness();

        _ = await harness.AddComponentAsync(DomainKind.Block, "KYC", id: COMPONENT);
        _ = await harness.AddVersionAsync(COMPONENT, 1, new string('b', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);

        _ = SeedTemplate(harness, new string('b', 64));

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Export_TrimmedTemplateHead_ReturnsPayloadAbsent()
    {
        using var harness = new SharedComponentsTestHarness();

        _ = harness.Context.Add(new WorkflowType(TYPE, "Report"));
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = "trimmed-hash",
            CreatedByUserId = USER,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            PayloadTrimmedAt = DateTime.UtcNow,
        };
        _ = harness.Context.Add(version);
        _ = harness.Context.SaveChanges();

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Export_UnparseableTemplateHead_ReturnsCorruptDefinition()
    {
        using var harness = new SharedComponentsTestHarness();

        _ = harness.Context.Add(new WorkflowType(TYPE, "Report"));
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = "corrupt-hash",
            CreatedByUserId = USER,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
        };
        version.WorkflowJson = "this is not a workflow";
        _ = harness.Context.Add(version);
        _ = harness.Context.SaveChanges();

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.CORRUPT_DEFINITION, result.FirstError.Code);
    }

    [Fact]
    public async Task Export_TypeWithoutHead_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        _ = harness.Context.Add(new WorkflowType(TYPE, "Report"));
        _ = harness.Context.SaveChanges();

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, TYPE), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ExportWorkflowTemplateQueryHandler.NOT_FOUND_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task Export_MissingType_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        ErrorOr<ExportWorkflowTemplateResult> result = await new ExportWorkflowTemplateQueryHandler(harness.UnitOfWork)
            .Handle(new ExportWorkflowTemplateQuery(ORG, USER, "WT-Absent"), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ExportWorkflowTemplateQueryHandler.NOT_FOUND_CODE, result.FirstError.Code);
    }

    private static (string TemplateJson, string ContentHash) SeedTemplate(
        SharedComponentsTestHarness harness,
        string componentVersionHash)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow) { Title = "Body" };
        var block = new Block(BlockTypes.AiQuery, section, "b1")
        {
            Name = "KYC",
            Properties = new AiQueryProperties { UserPromptTemplate = "kyc?" },
        };
        block.SharedComponentId = COMPONENT;
        block.SharedComponentLinkState = LinkState.Linked;
        block.SharedComponentVersionHash = componentVersionHash;
        block.SharedComponentBindings = new Dictionary<string, string>();

        string templateJson = harness.Canonicalizer.ToCanonicalJson(workflow);
        string contentHash = harness.Canonicalizer.ComputeContentHash(workflow);

        _ = harness.Context.Add(new WorkflowType(TYPE, "Report"));
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = contentHash,
            CreatedByUserId = USER,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
        };
        version.WorkflowJson = templateJson;
        _ = harness.Context.Add(version);
        _ = harness.Context.SaveChanges();

        return (templateJson, contentHash);
    }

    private static WorkflowVersion TemplateVersion(
        SharedComponentsTestHarness harness,
        string title,
        string contentHash,
        DateTime createdAt)
    {
        var workflow = new Workflow(title) { Title = title };
        _ = new Section("Body", workflow) { Title = "Body" };
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = contentHash,
            CreatedByUserId = USER,
            CreatedAt = createdAt,
        };
        version.WorkflowJson = harness.Canonicalizer.ToCanonicalJson(workflow);
        return version;
    }

    private static void LinkBlock(Block block, string componentId)
    {
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = LinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        block.SharedComponentBindings = new Dictionary<string, string>();
    }

    private static async Task SeedBlockComponentAsync(SharedComponentsTestHarness harness, string id)
    {
        Block definition = SharedComponentLinkTestData.BuildBlockDefinition($"prompt-{id}");
        (string json, string hash) = harness.Definition(definition);
        _ = await harness.AddComponentAsync(DomainKind.Block, id, id: id);
        _ = await harness.AddVersionAsync(id, 1, hash, json);
    }
}
