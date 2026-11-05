using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.Workflows;
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

namespace IniaAi.Application.UnitTests.Commands.Workflows.SaveWorkflow;

/// <summary>
/// Unknown agent tags never stop a save: a skill referencing a tag no producer supplies saves,
/// and the result carries a warning naming the tag and the block. Known references save with no warning.
/// </summary>
public class SaveWorkflowAgentTagWarningTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-AgentTagWarning";

    [Fact]
    public async Task UnknownSkillTag_SavesWithAWarningNamingTheTagAndBlock()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildAgentWorkflow("Consult {{ghost}}.")),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        string warning = Assert.Single(result.Value.Warnings!, warning => warning.Contains("ghost", StringComparison.Ordinal));
        Assert.Contains("extraction_agent", warning, StringComparison.Ordinal);
        _ = Assert.Single(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KnownSkillTags_SaveWithoutAWarning()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildAgentWorkflow("Read {{upstream_tag}} and {{Validator.decision}}.")),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.DoesNotContain(result.Value.Warnings ?? [], warning => warning.Contains("agent tags", StringComparison.Ordinal));
        _ = Assert.Single(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TagIntoALoopedProducersField_SavesWithAWarningSayingHowToFixIt()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);
        Workflow workflow = BuildAgentWorkflow("Read {{looped_tag.answer}}.");
        _ = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, workflow),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Warnings!, warning => warning.Contains("Reference '{{looped_tag}}'", StringComparison.Ordinal));
        _ = Assert.Single(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static Workflow BuildAgentWorkflow(string skillContent)
    {
        var workflow = new Workflow("Agent tag confirm") { Title = "Agent tag confirm" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.Merge, section, replacementTag: "upstream_tag")
        {
            Properties = new MergeProperties { TextTemplate = "base" },
        };
        var agent = new Block(BlockTypes.Agent, section, replacementTag: "extraction_agent");
        var props = (AgentProperties)agent.Properties;
        props.WorkerSkills.Add(AgentTestData.Skill("extraction", skillContent));
        props.Model = "test-model-a";
        return workflow;
    }

    private static SaveWorkflowCommandHandler CreateHandler(CoreHarness harness)
    {
        return new SaveWorkflowCommandHandler(
            new WorkflowSavePipeline(
                harness.UnitOfWork,
                harness.Context,
                new WorkflowCanonicalizer(),
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(AgentTestData.EnabledRuntime()),
                NullLogger<WorkflowSavePipeline>.Instance, IniaAi.Testing.DependencyInjection.SuperAdminLookups.OverEmptyUserData()),
            harness.Context,
            NullLogger<SaveWorkflowCommandHandler>.Instance);
    }

    private sealed class CoreHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }

        private readonly ServiceProvider _provider;

        public CoreHarness()
        {
            DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"savewf-agent-confirm-{Guid.NewGuid()}")
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
