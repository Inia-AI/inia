using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.CreateSharedComponent;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class CreateSharedComponentCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task Create_BlockComponent_PersistsComponentAndFirstVersion()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        CreateSharedComponentCommandHandler handler = CreateHandler(harness);
        (string definitionJson, string expectedHash) = harness.Definition(BuildBlock("Who signs?"));

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Signer check", ContractsKind.Block, "analysis", "Reusable", definitionJson),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(ContractsKind.Block, result.Value.Kind);
        Assert.Equal(expectedHash, result.Value.HeadContentHash);
        SharedComponent component = Assert.Single(harness.Context.SharedComponents);
        Assert.Equal("Signer check", component.Name);
        Assert.Equal(SharedComponentKind.Block, component.Kind);
        Assert.Equal("analysis", component.CategoryId);
        Assert.Equal(SharedComponentState.Active, component.State);
        SharedComponentVersion version = Assert.Single(harness.Context.SharedComponentVersions);
        Assert.Equal(1, version.SequenceNumber);
        Assert.Null(version.ParentVersionId);
        Assert.Equal(expectedHash, version.ContentHash);
        Assert.Equal(version.Id, result.Value.HeadVersionId);
        Assert.Contains("Who signs?", version.ContentJson);
    }

    [Fact]
    public async Task Create_InlineSectionComponent_PersistsFirstVersion()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        CreateSharedComponentCommandHandler handler = CreateHandler(harness);
        (string definitionJson, string expectedHash) = harness.Definition(BuildInlineSection());

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Risk section", ContractsKind.Section, "analysis", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponent component = Assert.Single(harness.Context.SharedComponents);
        Assert.Equal(SharedComponentKind.Section, component.Kind);
        SharedComponentVersion version = Assert.Single(harness.Context.SharedComponentVersions);
        Assert.Equal(expectedHash, version.ContentHash);
        Assert.Equal(1, version.SequenceNumber);
    }

    [Fact]
    public async Task Create_SectionReferencingActiveBlock_Persists()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        SharedComponent target = await harness.AddComponentAsync(SharedComponentKind.Block, "KYC", id: "c-kyc");
        (string blockJson, string blockHash) = harness.Definition(BuildBlock("KYC prompt"));
        _ = await harness.AddVersionAsync(target.Id, 1, blockHash, blockJson);
        CreateSharedComponentCommandHandler handler = CreateHandler(harness);
        (string definitionJson, _) = harness.Definition(BuildSectionReferencing("c-kyc"));

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Composed section", ContractsKind.Section, "analysis", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersion version = harness.Context.SharedComponentVersions.Single(v => v.SharedComponentId == result.Value.SharedComponentId);
        Assert.Contains("c-kyc", version.ContentJson);
    }

    [Fact]
    public async Task Create_UnknownCategory_ReturnsUnknownCategory()
    {
        using var harness = new SharedComponentsTestHarness();
        CreateSharedComponentCommandHandler handler = CreateHandler(harness);
        (string definitionJson, _) = harness.Definition(BuildBlock("Who signs?"));

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Signer check", ContractsKind.Block, "missing-category", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_CATEGORY, result.FirstError.Code);
        Assert.Empty(harness.Context.SharedComponents);
    }

    [Fact]
    public async Task Create_SectionReferencingMissingTarget_Rejected()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        CreateSharedComponentCommandHandler handler = CreateHandler(harness);
        (string definitionJson, _) = harness.Definition(BuildSectionReferencing("c-nonexistent"));

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Composed section", ContractsKind.Section, "analysis", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Description);
        Assert.Empty(harness.Context.SharedComponents);
    }

    [Fact]
    public async Task Create_SectionReferencingSectionKind_Rejected()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        SharedComponent sectionTarget = await harness.AddComponentAsync(SharedComponentKind.Section, "Nested", id: "c-section");
        (string sectionJson, string sectionHash) = harness.Definition(BuildInlineSection());
        _ = await harness.AddVersionAsync(sectionTarget.Id, 1, sectionHash, sectionJson);
        CreateSharedComponentCommandHandler handler = CreateHandler(harness);
        (string definitionJson, _) = harness.Definition(BuildSectionReferencing("c-section"));

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Composed section", ContractsKind.Section, "analysis", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(SharedComponentErrorCodes.KIND_MISMATCH, result.FirstError.Description);
        Assert.DoesNotContain(harness.Context.SharedComponents, c => c.Name == "Composed section");
    }

    [Fact]
    public void Validator_RejectsNameLongerThanTheColumn()
    {
        var validator = new CreateSharedComponentCommandValidator();

        Assert.False(validator.Validate(new CreateSharedComponentCommand(
            ORG, USER, new string('x', SharedComponent.NAME_MAX_LENGTH + 1),
            ContractsKind.Block, "analysis", null, "{}")).IsValid);
        Assert.True(validator.Validate(new CreateSharedComponentCommand(
            ORG, USER, new string('x', SharedComponent.NAME_MAX_LENGTH),
            ContractsKind.Block, "analysis", null, "{}")).IsValid);
    }

    [Fact]
    public async Task Create_AgentBlockDefinition_DisabledRuntime_Rejected()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        CreateSharedComponentCommandHandler handler = CreateHandler(harness);
        (string definitionJson, _) = harness.Definition(AgentTestData.AgentBlockDefinition());

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Extractor", ContractsKind.Block, "analysis", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.AGENT_RUNTIME_DISABLED, result.FirstError.Code);
        Assert.Empty(harness.Context.SharedComponents);
    }

    [Fact]
    public async Task Create_AgentBlockDefinition_EnabledRuntime_Persists()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        CreateSharedComponentCommandHandler handler = CreateHandler(harness, AgentTestData.EnabledRuntime());
        (string definitionJson, string expectedHash) = harness.Definition(AgentTestData.AgentBlockDefinition());

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Extractor", ContractsKind.Block, "analysis", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(expectedHash, result.Value.HeadContentHash);
        _ = Assert.Single(harness.Context.SharedComponents);
    }

    [Fact]
    public async Task Create_AgentBlockDefinition_DisallowedModel_Rejected()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        CreateSharedComponentCommandHandler handler = CreateHandler(harness, AgentTestData.EnabledRuntime());
        (string definitionJson, _) = harness.Definition(AgentTestData.AgentBlockDefinition(model: "rogue-model"));

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Extractor", ContractsKind.Block, "analysis", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.AGENT_BLOCK_INVALID, result.FirstError.Code);
        Assert.Empty(harness.Context.SharedComponents);
    }

    [Fact]
    public async Task Create_ParameterizedSectionWithAgent_EnabledRuntime_Creates()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        CreateSharedComponentCommandHandler handler = CreateHandler(harness, AgentTestData.EnabledRuntime());
        (string definitionJson, _) = harness.Definition(AgentTestData.AgentSection(parameterized: true));

        ErrorOr<CreateSharedComponentResult> result = await handler.Handle(
            new CreateSharedComponentCommand(
                ORG, USER, "Agent section", ContractsKind.Section, "analysis", null, definitionJson),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _ = Assert.Single(harness.Context.SharedComponents);
    }

    private static CreateSharedComponentCommandHandler CreateHandler(
        SharedComponentsTestHarness harness,
        AgentRuntimeSettings? agentRuntime = null)
    {
        return new CreateSharedComponentCommandHandler(
            harness.UnitOfWork,
            harness.Context,
            harness.CreateFanOutService(agentRuntime),
            NullLogger<CreateSharedComponentCommandHandler>.Instance);
    }

    private static Block BuildBlock(string prompt)
    {
        var scratch = new Workflow("scratch");
        var section = new Section("Host", scratch) { Title = "Host" };
        return new Block(BlockTypes.AiQuery, section, "signer")
        {
            Name = "Who signs",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    private static Section BuildInlineSection()
    {
        var scratch = new Workflow("scratch");
        var section = new Section("Risk", scratch) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, section, "alpha")
        {
            Name = "Alpha",
            Properties = new AiQueryProperties { UserPromptTemplate = "Alpha prompt" },
        };
        _ = new Block(BlockTypes.AiQuery, section, "beta")
        {
            Name = "Beta",
            Properties = new AiQueryProperties { UserPromptTemplate = "Beta prompt" },
        };

        return section;
    }

    private static Section BuildSectionReferencing(string referencedComponentId)
    {
        var scratch = new Workflow("scratch");
        var section = new Section("Risk", scratch) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, section, "intro")
        {
            Name = "Intro",
            Properties = new AiQueryProperties { UserPromptTemplate = "Intro prompt" },
        };
        section.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = referencedComponentId },
        ];

        return section;
    }
}
