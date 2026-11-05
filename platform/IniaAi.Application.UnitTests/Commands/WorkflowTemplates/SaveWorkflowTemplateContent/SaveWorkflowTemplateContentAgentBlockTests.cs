using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Common.Interfaces.GitHub;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Core;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;

/// <summary>
/// Proves the Agent block is authorable over the workflow-templates API path: raw template
/// JSON deserializes to typed properties through structural validation, the save-time gate
/// surfaces through the outer handler exactly as production's dispatcher emits it,
/// and a saved agent block round-trips the Core persistence layer intact.
/// </summary>
public class SaveWorkflowTemplateContentAgentBlockTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-AgentReport";
    private const string ALLOWED_MODEL = "test-model-a";
    private const string SKILL_CONTENT = """
        Extract line items for {ClientName}; emit exactly {"total": 0}.
        """;

    [Fact]
    public void AgentBlockJson_StructuralValidation_YieldsTypedProperties()
    {
        WorkflowStructuralValidationResult result = new WorkflowTemplateStructuralValidator()
            .Validate(BuildAgentWorkflowJson());

        Assert.True(result.IsValid);
        Block agentBlock = Assert.Single(
            result.Workflow!.GetAllBlocks(), b => b.Type == BlockTypes.Agent);
        var props = Assert.IsType<AgentProperties>(agentBlock.Properties);
        Assert.Equal(SKILL_CONTENT, AgentTestData.SkillContent(props, "extraction"));
        Assert.Equal(ALLOWED_MODEL, props.Model);
    }

    [Fact]
    public async Task RuntimeDisabled_AgentTemplateJson_RejectedThroughApiPath()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness, agentRuntimeEnabled: false);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildAgentWorkflowJson(),
                BaseVersionId: null, NewTypeName: "Agent Report"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors,
            e => e.Description.Contains("agent runtime", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RuntimeEnabled_AgentTemplateJson_SavesWithoutSkillWarnings_AndRoundTrips()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness, agentRuntimeEnabled: true);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildAgentWorkflowJson(),
                BaseVersionId: null, NewTypeName: "Agent Report"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Warnings);

        harness.Context.ChangeTracker.Clear();
        List<Block> savedBlocks = await harness.Context.Set<Block>()
            .ToListAsync(TestContext.Current.CancellationToken);
        Block agentBlock = Assert.Single(savedBlocks, b => b.Type == BlockTypes.Agent);
        var props = Assert.IsType<AgentProperties>(agentBlock.Properties);
        Assert.Equal(SKILL_CONTENT, AgentTestData.SkillContent(props, "extraction"));
        Assert.Equal(ALLOWED_MODEL, props.Model);
    }

    [Fact]
    public async Task UnknownSkillTag_ThroughApiPath_SavesWithAWarning()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness, agentRuntimeEnabled: true);
        string json = BuildAgentWorkflowJson(skillContent: "Consult {{ghost}} before answering.");

        ErrorOr<SaveWorkflowTemplateContentResult> saved = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, json,
                BaseVersionId: null, NewTypeName: "Agent Report"),
            TestContext.Current.CancellationToken);

        Assert.False(saved.IsError);
        Assert.Contains(saved.Value.Warnings, warning => warning.Message.Contains("ghost", StringComparison.Ordinal));
        _ = Assert.Single(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BedrockIdAgainstAFoundryAllowlist_ThroughApiPath_SavesTheNeutralId()
    {
        // A workflow copied from a Bedrock deployment carries the inference-profile id; a Foundry
        // deployment lists the same model by its plain name.
        const string bedrockSonnet = "us.anthropic.claude-sonnet-5-5";
        const string foundrySonnet = "claude-sonnet-5-5";
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(
            harness, agentRuntimeEnabled: true, allowedModels: [foundrySonnet, "claude-haiku-5-5"]);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildAgentWorkflowJson(model: bedrockSonnet),
                BaseVersionId: null, NewTypeName: "Agent Report"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(static e => e.Description)));
        harness.Context.ChangeTracker.Clear();
        Block agentBlock = Assert.Single(
            await harness.Context.Set<Block>().ToListAsync(TestContext.Current.CancellationToken),
            b => b.Type == BlockTypes.Agent);
        Assert.Equal(foundrySonnet, Assert.IsType<AgentProperties>(agentBlock.Properties).Model);
    }

    [Fact]
    public async Task ModelNoAllowlistFormMatches_ThroughApiPath_IsRefused()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(
            harness, agentRuntimeEnabled: true, allowedModels: ["claude-sonnet-5-5"]);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE,
                BuildAgentWorkflowJson(model: "us.anthropic.claude-opus-5-5"),
                BaseVersionId: null, NewTypeName: "Agent Report"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, static e => e.Description.Contains(
            "selects model 'us.anthropic.claude-opus-5-5', which is not in the allowed model list (claude-sonnet-5-5)",
            StringComparison.Ordinal));
    }

    private static string BuildAgentWorkflowJson(string? skillContent = null, string model = ALLOWED_MODEL)
    {
        var workflow = new Workflow("Agent Report") { Title = "Agent Report" };
        var section = new Section("Body", workflow);
        var block = new Block(BlockTypes.Agent, section, "extraction_agent") { Name = "Extraction agent" };
        var props = (AgentProperties)block.Properties;
        AgentTestData.ReplaceSkill(props, "extraction", skillContent ?? SKILL_CONTENT);
        props.Model = model;
        return new WorkflowCanonicalizer().ToCanonicalJson(workflow);
    }

    private static SaveWorkflowTemplateContentCommandHandler CreateHandler(
        CoreHarness harness, bool agentRuntimeEnabled, List<string>? allowedModels = null)
    {
        var settings = new PlatformSettings
        {
            AgentRuntime = new AgentRuntimeSettings
            {
                Enabled = agentRuntimeEnabled,
                AllowedModels = allowedModels ?? [ALLOWED_MODEL],
            },
        };
        ISender sender = SaveWorkflowSender.Create(
            new WorkflowSavePipeline(
                harness.UnitOfWork,
                harness.Context,
                new WorkflowCanonicalizer(),
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(settings.AgentRuntime),
                NullLogger<WorkflowSavePipeline>.Instance, IniaAi.Testing.DependencyInjection.SuperAdminLookups.OverEmptyUserData()),
            harness.Context,
            settings);

        IGitHubWorkflowVersioningService gitHub = Substitute.For<IGitHubWorkflowVersioningService>();
        _ = gitHub.CommitWorkflowAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GitHubCommitResult { Sha = "test-sha-1234567" });

        return new SaveWorkflowTemplateContentCommandHandler(
            harness.UnitOfWork,
            new WorkflowTemplateStructuralValidator(),
            new WorkflowTemplateSavePreflight(harness.UnitOfWork),
            sender,
            gitHub,
            new ServiceCollection().Configure<GitHubSettings>(_ => { }).BuildServiceProvider().GetRequiredService<IOptionsMonitor<GitHubSettings>>(),
            NullLogger<SaveWorkflowTemplateContentCommandHandler>.Instance);
    }

    private sealed class CoreHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }

        private readonly ServiceProvider _provider;

        public CoreHarness()
        {
            DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"savewftc-agent-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Context = new CoreDbContext(options);

            var services = new ServiceCollection();
            _ = services.AddScoped(typeof(ICoreRepository<>), typeof(CoreRepositoryWrapper<>));
            _ = services.AddScoped<CoreDbContext>(_ => Context);
            _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
            _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
            _ = services.AddScoped<IToolsSettingsRepository, ToolsSettingsRepository>();
            _provider = services.BuildServiceProvider();

            UnitOfWork = new CoreUnitOfWork(Context, _provider);
        }

        public void Dispose()
        {
            Context.Dispose();
            _provider.Dispose();
        }
    }
}
