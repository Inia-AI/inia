using Ardalis.Specification;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace IniaAi.Infrastructure.Tests.Services;

public class WorkflowActivityScriptEnsurerTests
{
    private const string VERSION_ID = "version-1";
    private const string WORKFLOW_TYPE_ID = "wf-type-1";

    private readonly ICoreUnitOfWork _coreUnitOfWork = Substitute.For<ICoreUnitOfWork>();
    private readonly IRepository<WorkflowVersion> _versionRepository = Substitute.For<IRepository<WorkflowVersion>>();
    private readonly IWorkflowActivityScriptWriter _writer = Substitute.For<IWorkflowActivityScriptWriter>();
    private readonly WorkflowCanonicalizer _canonicalizer = new();
    private readonly WorkflowActivityScriptEnsurer _ensurer;

    public WorkflowActivityScriptEnsurerTests()
    {
        _ = _coreUnitOfWork.Repository<WorkflowVersion>().Returns(_versionRepository);

        IServiceProvider provider = Substitute.For<IServiceProvider>();
        _ = provider.GetService(typeof(ICoreUnitOfWork)).Returns(_coreUnitOfWork);
        _ = provider.GetService(typeof(IWorkflowActivityScriptWriter)).Returns(_writer);
        _ = provider.GetService(typeof(WorkflowCanonicalizer)).Returns(_canonicalizer);

        IServiceScope scope = Substitute.For<IServiceScope>();
        _ = scope.ServiceProvider.Returns(provider);
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        _ = scopeFactory.CreateScope().Returns(scope);

        _ensurer = new WorkflowActivityScriptEnsurer(
            scopeFactory,
            NullLogger<WorkflowActivityScriptEnsurer>.Instance);
    }

    private static Workflow LiveTemplate()
    {
        var workflow = new Workflow("Template") { WorkflowTypeId = WORKFLOW_TYPE_ID };
        var section = new Section("Body", workflow);
        _ = new IniaAi.Domain.Entities.Workflows.Blocks.Block(
            IniaAi.Domain.Entities.Workflows.Enums.BlockTypes.Merge, section, "mergeBlock");
        return workflow;
    }

    private WorkflowVersion VersionRowMatching(Workflow workflow)
    {
        return new WorkflowVersion
        {
            Id = VERSION_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            ContentHash = _canonicalizer.ComputeContentHash(workflow),
        };
    }

    private void ArrangeVersion(WorkflowVersion? version)
    {
        _ = _versionRepository
            .GetFirstOrDefaultAsync(Arg.Any<ISpecification<WorkflowVersion>>(), Arg.Any<CancellationToken>())
            .Returns(version);
    }

    private void ArrangeTemplate(Workflow? workflow)
    {
        _ = _coreUnitOfWork.Workflows.GetByTypeIdAsync(WORKFLOW_TYPE_ID).Returns(workflow);
    }

    private static WorkflowActivityScript SomeScript()
    {
        return new WorkflowActivityScript(
            [new WorkflowActivityGroup(0, 0, "Merging the findings")],
            ["stableblock000000000000000000001"]);
    }

    [Fact]
    public async Task AVersionWithoutAScriptGetsOneWrittenAndSaved()
    {
        Workflow template = LiveTemplate();
        WorkflowVersion version = VersionRowMatching(template);
        ArrangeVersion(version);
        ArrangeTemplate(template);
        _ = _writer.WriteScriptAsync(template, Arg.Any<CancellationToken>()).Returns(WorkflowActivityScriptWriteResult.Written(SomeScript()));

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);

        Assert.NotNull(version.ActivityScriptJson);
        Assert.NotNull(WorkflowActivityScriptSerializer.TryDeserialize(version.ActivityScriptJson));
        _ = await _coreUnitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AVersionAlreadyCarryingAScriptIsLeftAlone()
    {
        Workflow template = LiveTemplate();
        WorkflowVersion version = VersionRowMatching(template);
        version.ActivityScriptJson = WorkflowActivityScriptSerializer.Serialize(SomeScript());
        string existing = version.ActivityScriptJson;
        ArrangeVersion(version);
        ArrangeTemplate(template);

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);

        Assert.Equal(existing, version.ActivityScriptJson);
        _ = await _writer.DidNotReceiveWithAnyArgs().WriteScriptAsync(default!, TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMissingVersionWritesNothing()
    {
        ArrangeVersion(null);

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);

        _ = await _writer.DidNotReceiveWithAnyArgs().WriteScriptAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ALiveTemplateThatNoLongerMatchesTheVersionHashWritesNothing()
    {
        Workflow template = LiveTemplate();
        WorkflowVersion version = VersionRowMatching(template);
        version.ContentHash = new string('b', 64);
        ArrangeVersion(version);
        ArrangeTemplate(template);

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);

        Assert.Null(version.ActivityScriptJson);
        _ = await _writer.DidNotReceiveWithAnyArgs().WriteScriptAsync(default!, TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AVersionSavedBeforeABlockFieldWasDroppedStillMatchesItsUnchangedTemplate()
    {
        Workflow template = LiveTemplate();
        var stored = JObject.Parse(_canonicalizer.ToCanonicalJson(template));
        foreach (JObject block in stored.SelectTokens("$.Sections[*].Blocks[*]").OfType<JObject>())
        {
            block["ShouldExcludeBlockOutputFromCitationSources"] = true;
        }

        var version = new WorkflowVersion
        {
            Id = VERSION_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            ContentHash = _canonicalizer.ComputeContentHash(stored.ToString(Formatting.None)),
            WorkflowJson = stored.ToString(Formatting.None),
        };
        ArrangeVersion(version);
        ArrangeTemplate(template);
        _ = _writer.WriteScriptAsync(template, Arg.Any<CancellationToken>()).Returns(WorkflowActivityScriptWriteResult.Written(SomeScript()));

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);

        Assert.NotNull(version.ActivityScriptJson);
    }

    [Fact]
    public async Task ATransientWriterFailureLeavesTheVersionForTheNextSubmission()
    {
        Workflow template = LiveTemplate();
        WorkflowVersion version = VersionRowMatching(template);
        ArrangeVersion(version);
        ArrangeTemplate(template);
        _ = _writer.WriteScriptAsync(template, Arg.Any<CancellationToken>())
            .Returns(WorkflowActivityScriptWriteResult.TransientFailure);

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);

        Assert.Null(version.ActivityScriptJson);
        _ = await _coreUnitOfWork.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADeclineIsCachedOnTheVersionSoLaterSubmissionsStopPaying()
    {
        Workflow template = LiveTemplate();
        WorkflowVersion version = VersionRowMatching(template);
        ArrangeVersion(version);
        ArrangeTemplate(template);
        _ = _writer.WriteScriptAsync(template, Arg.Any<CancellationToken>())
            .Returns(WorkflowActivityScriptWriteResult.Declined);

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);

        WorkflowActivityScript? cached = WorkflowActivityScriptSerializer.TryDeserialize(version.ActivityScriptJson);
        Assert.NotNull(cached);
        Assert.Empty(cached.Groups);
        _ = await _coreUnitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);
        _ = await _writer.Received(1).WriteScriptAsync(template, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARepositoryFailureIsSwallowedNotPropagated()
    {
        _ = _versionRepository
            .GetFirstOrDefaultAsync(Arg.Any<ISpecification<WorkflowVersion>>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorkflowVersion?>>(_ => throw new InvalidOperationException("db down"));

        await _ensurer.EnsureScriptAsync(VERSION_ID, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentBackgroundRequestsForOneVersionGenerateOnce()
    {
        Workflow template = LiveTemplate();
        WorkflowVersion version = VersionRowMatching(template);
        ArrangeVersion(version);
        ArrangeTemplate(template);

        var gate = new TaskCompletionSource<WorkflowActivityScriptWriteResult>();
        _ = _writer.WriteScriptAsync(template, Arg.Any<CancellationToken>()).Returns(_ => gate.Task);

        _ensurer.EnsureScriptInBackground(VERSION_ID, WORKFLOW_TYPE_ID);
        _ensurer.EnsureScriptInBackground(VERSION_ID, WORKFLOW_TYPE_ID);
        gate.SetResult(WorkflowActivityScriptWriteResult.Written(SomeScript()));

        await Eventually.UntilAsync(() => version.ActivityScriptJson is not null, "the activity script to be written", cancellationToken: TestContext.Current.CancellationToken);
        _ = await _writer.Received(1).WriteScriptAsync(template, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheWrittenScriptSurvivesARealDbContextRoundTrip()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddSingleton(new WorkflowCanonicalizer());
        IWorkflowActivityScriptWriter writer = Substitute.For<IWorkflowActivityScriptWriter>();
        _ = services.AddSingleton(writer);
        using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();

        Workflow template;
        string versionId;
        using (IServiceScope seedScope = provider.CreateScope())
        {
            ICoreUnitOfWork core = seedScope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();
            template = LiveTemplate();
            await core.Workflows.AddAsync(template);
            var canonicalizer = seedScope.ServiceProvider.GetRequiredService<WorkflowCanonicalizer>();
            var version = new WorkflowVersion
            {
                WorkflowTypeId = WORKFLOW_TYPE_ID,
                ContentHash = canonicalizer.ComputeContentHash(template),
                CreatedByUserId = "user-test",
            };
            versionId = version.Id;
            await core.Repository<WorkflowVersion>().AddAsync(version);
            _ = await core.CompleteAsync(TestContext.Current.CancellationToken);
        }

        _ = writer
            .WriteScriptAsync(Arg.Any<Workflow>(), Arg.Any<CancellationToken>())
            .Returns(WorkflowActivityScriptWriteResult.Written(SomeScript()));

        var ensurer = new WorkflowActivityScriptEnsurer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowActivityScriptEnsurer>.Instance);
        await ensurer.EnsureScriptAsync(versionId, WORKFLOW_TYPE_ID, TestContext.Current.CancellationToken);

        using IServiceScope readScope = provider.CreateScope();
        ICoreUnitOfWork reader = readScope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();
        WorkflowVersion? persisted = await reader
            .Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new WorkflowVersionByIdSpec(versionId),
                TestContext.Current.CancellationToken);

        Assert.NotNull(persisted);
        Assert.NotNull(WorkflowActivityScriptSerializer.TryDeserialize(persisted.ActivityScriptJson));
    }
}
