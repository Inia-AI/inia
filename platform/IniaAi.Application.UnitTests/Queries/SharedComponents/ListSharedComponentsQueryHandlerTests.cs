using ErrorOr;
using FluentValidation.Results;
using IniaAi.Application.Queries.SharedComponents.ListSharedComponents;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class ListSharedComponentsQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task List_ProjectsHeadTokensAndConsumerCounts_LinkedOnly()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent alpha = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        SharedComponent beta = await harness.AddComponentAsync(SharedComponentKind.Section, "Beta");
        _ = await harness.AddVersionAsync(alpha.Id, 1, new string('1', 64), """{"v":1}""");
        SharedComponentVersion alphaHead = await harness.AddVersionAsync(alpha.Id, 2, new string('2', 64), """{"v":2}""");
        _ = await harness.AddVersionAsync(beta.Id, 1, new string('3', 64), """{"v":3}""");
        await SeedConsumersAsync(harness, alpha.Id);

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        SharedComponentSummaryDto alphaDto = result.Value[0];
        Assert.Equal("Alpha", alphaDto.Name);
        Assert.Equal(alphaHead.Id, alphaDto.HeadVersionId);
        Assert.Equal(new string('2', 64), alphaDto.HeadContentHash);
        Assert.Equal(alphaHead.CreatedAt, alphaDto.HeadCreatedAtUtc);
        Assert.Equal(2, alphaDto.ConsumerCount);
        SharedComponentSummaryDto betaDto = result.Value[1];
        Assert.Equal(0, betaDto.ConsumerCount);
        Assert.Equal(ContractsKind.Section, betaDto.Kind);
    }

    [Fact]
    public async Task List_HeadlessComponent_HasNullHeadCreatedAt()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Headless");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto only = Assert.Single(result.Value);
        Assert.Null(only.HeadVersionId);
        Assert.Null(only.HeadCreatedAtUtc);
    }

    [Fact]
    public async Task List_ContentHashFilter_NarrowsToMatchingHead()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent alpha = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        SharedComponent beta = await harness.AddComponentAsync(SharedComponentKind.Block, "Beta");
        _ = await harness.AddVersionAsync(alpha.Id, 1, new string('1', 64), """{"v":1}""");
        _ = await harness.AddVersionAsync(beta.Id, 1, new string('2', 64), """{"v":2}""");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER, new string('2', 64)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto match = Assert.Single(result.Value);
        Assert.Equal(beta.Id, match.Id);
    }

    [Fact]
    public async Task List_ContentHashFilter_FindsAHeadStoredBeforeAFieldWasDropped()
    {
        using var harness = new SharedComponentsTestHarness();
        (string todayJson, string todayHash) = harness.Definition(SharedComponentLinkTestData.BuildBlockDefinition("Who signs?"));
        (string storedJson, string storedHash) = harness.StoredBeforeAFieldWasDropped(todayJson);
        SharedComponent signer = await harness.AddComponentAsync(SharedComponentKind.Block, "Signer");
        _ = await harness.AddVersionAsync(signer.Id, 1, storedHash, storedJson);

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER, todayHash),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(signer.Id, Assert.Single(result.Value).Id);
    }

    [Fact]
    public async Task List_KindFilter_NarrowsToOneKind()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent block = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        _ = await harness.AddComponentAsync(SharedComponentKind.Section, "Beta");
        _ = await harness.AddVersionAsync(block.Id, 1, new string('1', 64), """{"v":1}""");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER, Kind: ContractsKind.Block),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto match = Assert.Single(result.Value);
        Assert.Equal(block.Id, match.Id);
    }

    [Fact]
    public async Task List_InsertableOnly_DropsRetiredHeadlessAndPayloadAbsentComponents()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent insertable = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        SharedComponent retired = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Beta", SharedComponentState.Retired);
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Gamma");
        SharedComponent payloadAbsent = await harness.AddComponentAsync(SharedComponentKind.Block, "Delta");
        _ = await harness.AddVersionAsync(insertable.Id, 1, new string('1', 64), """{"v":1}""");
        _ = await harness.AddVersionAsync(retired.Id, 1, new string('2', 64), """{"v":2}""");
        _ = await harness.AddVersionAsync(
            payloadAbsent.Id, 1, new string('3', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER, InsertableOnly: true),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto match = Assert.Single(result.Value);
        Assert.Equal(insertable.Id, match.Id);
    }

    [Fact]
    public async Task List_CategoryFilter_ReturnsOnlyMatchingComponents()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("extraction", "Extraction");
        SharedComponent extractor = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Extractor", categoryId: "extraction");
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Uncategorized Helper");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER, CategoryId: "extraction"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto match = Assert.Single(result.Value);
        Assert.Equal(extractor.Id, match.Id);
    }

    [Fact]
    public async Task List_SummaryCarriesCategoryIdAndResolvedName()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Analyzer", categoryId: "analysis");
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Dangling", categoryId: "ghost");
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Plain");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto analyzer = result.Value.Single(c => c.Name == "Analyzer");
        Assert.Equal("analysis", analyzer.CategoryId);
        Assert.Equal("Analysis", analyzer.CategoryName);
        SharedComponentSummaryDto dangling = result.Value.Single(c => c.Name == "Dangling");
        Assert.Equal("ghost", dangling.CategoryId);
        Assert.Null(dangling.CategoryName);
        SharedComponentSummaryDto plain = result.Value.Single(c => c.Name == "Plain");
        Assert.Null(plain.CategoryId);
        Assert.Null(plain.CategoryName);
    }

    [Fact]
    public async Task List_SummaryCarriesConsumingWorkflowTypes_WithTypeCategory()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent alpha = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        SharedComponent beta = await harness.AddComponentAsync(SharedComponentKind.Block, "Beta");
        _ = harness.Context.WorkflowTypeCategories.Add(
            new WorkflowTypeCategory { Id = "asset", Name = "Asset Management" });
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType("WT-1", "NS Testing") { CategoryId = "asset" });
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType("WT-2", "CIS - Global"));
        await SeedLinkedConsumerAsync(harness, alpha.Id, "WT-1");
        await SeedLinkedConsumerAsync(harness, alpha.Id, "WT-2");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto alphaDto = result.Value.Single(c => c.Name == "Alpha");
        Assert.Equal(2, alphaDto.ConsumingWorkflowTypes.Count);
        ConsumingWorkflowTypeDto cis = alphaDto.ConsumingWorkflowTypes[0];
        Assert.Equal("WT-2", cis.WorkflowTypeId);
        Assert.Equal("CIS - Global", cis.WorkflowTypeName);
        Assert.Null(cis.CategoryId);
        Assert.Null(cis.CategoryName);
        ConsumingWorkflowTypeDto ns = alphaDto.ConsumingWorkflowTypes[1];
        Assert.Equal("WT-1", ns.WorkflowTypeId);
        Assert.Equal("NS Testing", ns.WorkflowTypeName);
        Assert.Equal("asset", ns.CategoryId);
        Assert.Equal("Asset Management", ns.CategoryName);
        Assert.Empty(result.Value.Single(c => c.Name == "Beta").ConsumingWorkflowTypes);
    }

    [Fact]
    public async Task List_WorkflowTypeFilter_KeepsComponentsConsumedByThatType()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent alpha = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        SharedComponent beta = await harness.AddComponentAsync(SharedComponentKind.Block, "Beta");
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, "Unused");
        await SeedLinkedConsumerAsync(harness, alpha.Id, "WT-1");
        await SeedLinkedConsumerAsync(harness, beta.Id, "WT-2");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER, WorkflowTypeId: "WT-1"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto match = Assert.Single(result.Value);
        Assert.Equal(alpha.Id, match.Id);
    }

    [Fact]
    public async Task List_WorkflowTypeCategoryFilter_KeepsComponentsConsumedByTypesInThatCategory()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent alpha = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        SharedComponent beta = await harness.AddComponentAsync(SharedComponentKind.Block, "Beta");
        _ = harness.Context.WorkflowTypeCategories.Add(
            new WorkflowTypeCategory { Id = "asset", Name = "Asset Management" });
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType("WT-1", "NS Testing") { CategoryId = "asset" });
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType("WT-2", "CIS - Global"));
        await SeedLinkedConsumerAsync(harness, alpha.Id, "WT-1");
        await SeedLinkedConsumerAsync(harness, beta.Id, "WT-2");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER, WorkflowTypeCategoryId: "asset"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto match = Assert.Single(result.Value);
        Assert.Equal(alpha.Id, match.Id);
    }

    [Fact]
    public void Validator_RejectsEmptyIdentity_AndMalformedContentHash()
    {
        var validator = new ListSharedComponentsQueryValidator();

        ValidationResult emptyOrg = validator.Validate(new ListSharedComponentsQuery(string.Empty, USER));
        ValidationResult badHash = validator.Validate(new ListSharedComponentsQuery(ORG, USER, "xyz"));
        ValidationResult ok = validator.Validate(new ListSharedComponentsQuery(ORG, USER));

        Assert.False(emptyOrg.IsValid);
        Assert.False(badHash.IsValid);
        Assert.True(ok.IsValid);
    }

    private static async Task SeedLinkedConsumerAsync(
        SharedComponentsTestHarness harness,
        string componentId,
        string workflowTypeId)
    {
        var workflow = new Workflow($"{workflowTypeId} workflow") { WorkflowTypeId = workflowTypeId };
        var body = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, body, "linked")
        {
            SharedComponentId = componentId,
            SharedComponentLinkState = SharedComponentLinkState.Linked,
            SharedComponentVersionHash = new string('2', 64),
        };

        await harness.UnitOfWork.Workflows.AddAsync(workflow);
        _ = await harness.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task List_CountsViaSectionConsumersSeparately()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent block = await harness.AddComponentAsync(SharedComponentKind.Block, "Signer");
        SharedComponent container = await harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack");
        var directWorkflow = new Workflow("Direct Report") { WorkflowTypeId = "WT-DIRECT" };
        var plain = new Section("Standalone", directWorkflow) { Title = "Standalone" };
        _ = new Block(BlockTypes.AiQuery, plain, "signer_direct")
        {
            SharedComponentId = block.Id,
            SharedComponentLinkState = SharedComponentLinkState.Linked,
            SharedComponentVersionHash = new string('a', 64),
        };
        var viaWorkflow = new Workflow("Via Report") { WorkflowTypeId = "WT-VIA" };
        var linkedSection = new Section("Risk", viaWorkflow)
        {
            Title = "Risk",
            SharedComponentId = container.Id,
            SharedComponentLinkState = SharedComponentLinkState.Linked,
            SharedComponentVersionHash = new string('b', 64),
        };
        _ = new Block(BlockTypes.AiQuery, linkedSection, "signer_via")
        {
            SharedComponentId = block.Id,
            SharedComponentLinkState = SharedComponentLinkState.Linked,
            SharedComponentVersionHash = new string('a', 64),
        };
        await harness.UnitOfWork.Workflows.AddAsync(directWorkflow);
        await harness.UnitOfWork.Workflows.AddAsync(viaWorkflow);
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto signer = result.Value.Single(c => c.Name == "Signer");
        Assert.Equal(2, signer.ConsumerCount);
        Assert.Equal(1, signer.ViaSectionConsumerCount);
        SharedComponentSummaryDto pack = result.Value.Single(c => c.Name == "Risk Pack");
        Assert.Equal(1, pack.ConsumerCount);
        Assert.Equal(0, pack.ViaSectionConsumerCount);
    }

    [Fact]
    public async Task List_CountsBrokenOutCopiesSeparately_WithoutInflatingConsumers()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent alpha = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        await SeedConsumersAsync(harness, alpha.Id);

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto alphaDto = result.Value.Single(c => c.Name == "Alpha");
        Assert.Equal(2, alphaDto.ConsumerCount);
        Assert.Equal(1, alphaDto.BrokenOutInstanceCount);
    }

    [Fact]
    public async Task List_ComponentWithNoBrokenOutCopies_ReportsZero()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent alpha = await harness.AddComponentAsync(SharedComponentKind.Block, "Alpha");
        await SeedLinkedConsumerAsync(harness, alpha.Id, "WT-ONLY-LINKED");

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto alphaDto = result.Value.Single(c => c.Name == "Alpha");
        Assert.Equal(1, alphaDto.ConsumerCount);
        Assert.Equal(0, alphaDto.BrokenOutInstanceCount);
    }

    [Fact]
    public async Task List_CountsBrokenOutSectionCopies_Too()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent pack = await harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack");
        var workflow = new Workflow("Forked Report") { WorkflowTypeId = "WT-FORKED" };
        _ = new Section("Risk", workflow)
        {
            Title = "Risk",
            SharedComponentId = pack.Id,
            SharedComponentLinkState = SharedComponentLinkState.BrokenOut,
            SharedComponentVersionHash = new string('c', 64),
        };
        await harness.UnitOfWork.Workflows.AddAsync(workflow);
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListSharedComponentsQueryHandler(harness.UnitOfWork, harness.Normalizer);
        ErrorOr<List<SharedComponentSummaryDto>> result = await handler.Handle(
            new ListSharedComponentsQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentSummaryDto packDto = result.Value.Single(c => c.Name == "Risk Pack");
        Assert.Equal(0, packDto.ConsumerCount);
        Assert.Equal(1, packDto.BrokenOutInstanceCount);
    }

    private static async Task SeedConsumersAsync(SharedComponentsTestHarness harness, string componentId)
    {
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-1" };
        var body = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, body, "linked")
        {
            SharedComponentId = componentId,
            SharedComponentLinkState = SharedComponentLinkState.Linked,
            SharedComponentVersionHash = new string('2', 64),
        };
        _ = new Block(BlockTypes.AiQuery, body, "broken")
        {
            SharedComponentId = componentId,
            SharedComponentLinkState = SharedComponentLinkState.BrokenOut,
            SharedComponentVersionHash = new string('1', 64),
        };
        _ = new Block(BlockTypes.AiQuery, body, "stateless")
        {
            SharedComponentId = componentId,
        };
        _ = new Section("Linked", workflow)
        {
            Title = "Linked",
            SharedComponentId = componentId,
            SharedComponentLinkState = SharedComponentLinkState.Linked,
            SharedComponentVersionHash = new string('2', 64),
        };

        await harness.UnitOfWork.Workflows.AddAsync(workflow);
        _ = await harness.Context.SaveChangesAsync();
    }
}
