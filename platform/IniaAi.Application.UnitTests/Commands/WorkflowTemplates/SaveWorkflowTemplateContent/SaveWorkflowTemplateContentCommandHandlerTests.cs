using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Interfaces.GitHub;
using IniaAi.Application.Common.Signatures;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
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
using NSubstitute.ExceptionExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;

public class SaveWorkflowTemplateContentCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";
    private const string SOURCE_TYPE = "WT-Report-Source";
    private const string API_AUTHOR_NAME = "Template Desk";
    private const string API_AUTHOR_EMAIL = "template-desk@example.com";

    [Fact]
    public async Task Upsert_CreatesTypeAccessAndFirstVersion()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null,
                NewTypeName: "Report Workflow", NewTypeDescription: "desc"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.CreatedWorkflowType);
        Assert.False(result.Value.WasDeduped);

        WorkflowType type = await harness.Context.WorkflowTypes
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TYPE, type.Id);
        Assert.Equal("Report Workflow", type.Name);

        WorkflowTypeOrganizationAccess access = await harness.Context
            .Set<WorkflowTypeOrganizationAccess>()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ORG, access.OrganizationId);
        Assert.Equal(TYPE, access.WorkflowTypeId);

        WorkflowVersion version = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(result.Value.VersionId, version.Id);
    }

    [Fact]
    public async Task ExistingTypeWithoutAccess_StillSaves_NoAccessGranted()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(TYPE, "Existing"));
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.CreatedWorkflowType);
        _ = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);

        List<WorkflowTypeOrganizationAccess> accessRows = await harness.Context
            .Set<WorkflowTypeOrganizationAccess>()
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(accessRows);
    }

    [Fact]
    public async Task RetiredType_ReportsNotFound_AndWritesNothing()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(TYPE, "Deleted") { RetiredAt = DateTime.UtcNow });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, result.FirstError.Code);
        Assert.False(await harness.Context.WorkflowVersions.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await harness.Context.Workflows.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Upsert_CreatingType_LeavesLineageUnset()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null,
                NewTypeName: "Report Workflow"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        WorkflowType type = await harness.Context.WorkflowTypes
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(type.DuplicatedFromWorkflowTypeId);
    }

    [Fact]
    public async Task Upsert_OnDuplicatedType_PreservesLineage()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(TYPE, "Variant")
        {
            DuplicatedFromWorkflowTypeId = SOURCE_TYPE,
        });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        WorkflowType type = await harness.Context.WorkflowTypes
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SOURCE_TYPE, type.DuplicatedFromWorkflowTypeId);
    }

    [Fact]
    public async Task UnknownSharedComponentId_ReturnsValidationError_NothingCreated()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, SharedComponentLinkTestData.LinkedWorkflowJson(), BaseVersionId: null,
                NewTypeName: "Report"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Sections[0].Blocks[0].SharedComponentId", result.FirstError.Code);
        Assert.Contains(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Description);

        List<WorkflowType> types = await harness.Context.WorkflowTypes
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(types);
    }

    [Fact]
    public async Task SharedComponentKindMismatch_ReturnsValidationError()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.SharedComponents.Add(new SharedComponent
        {
            Id = "component-1",
            Kind = SharedComponentKind.Section,
            Name = "A section component",
        });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, SharedComponentLinkTestData.LinkedWorkflowJson(), BaseVersionId: null,
                NewTypeName: "Report"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.KIND_MISMATCH, result.FirstError.Description);
    }

    [Fact]
    public async Task SectionReferencingBlockComponent_ReturnsSectionPathKindMismatch()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.SharedComponents.Add(new SharedComponent
        {
            Id = "component-1",
            Kind = SharedComponentKind.Block,
            Name = "A block component",
        });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, SharedComponentLinkTestData.SectionLinkedWorkflowJson(),
                BaseVersionId: null, NewTypeName: "Report"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Sections[0].SharedComponentId", result.FirstError.Code);
        Assert.Contains(SharedComponentErrorCodes.KIND_MISMATCH, result.FirstError.Description);
    }

    [Fact]
    public async Task KnownSharedComponent_RightKind_SavesAndVersionPayloadKeepsLinkFields()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.SharedComponents.Add(new SharedComponent
        {
            Id = "component-1",
            Kind = SharedComponentKind.Block,
            Name = "A block component",
        });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, SharedComponentLinkTestData.LinkedWorkflowJson(), BaseVersionId: null,
                NewTypeName: "Report"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        WorkflowVersion version = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"SharedComponentId\"", version.WorkflowJson);
        Assert.Contains("\"SharedComponentBindings\"", version.WorkflowJson);
    }

    [Fact]
    public async Task Upsert_SaveFails_RollsBackOrphanTypeAndAccess()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        // New type, but a non-null baseVersionId against a type with no head version forces the
        // delegated save to return a conflict — the just-created type must be rolled back, not orphaned.
        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: "phantom"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);

        List<WorkflowType> types = await harness.Context.WorkflowTypes
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(types);
        List<WorkflowTypeOrganizationAccess> accessRows = await harness.Context
            .Set<WorkflowTypeOrganizationAccess>()
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(accessRows);
    }

    [Fact]
    public async Task StructuralError_NothingCreated()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, "{ not valid json", BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);

        List<WorkflowType> types = await harness.Context.WorkflowTypes
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(types);
    }

    [Fact]
    public async Task InvalidCategory_ValidationError()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null,
                NewTypeCategoryId: "no-such-category"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("NewWorkflowType.CategoryId", result.FirstError.Code);
    }

    [Fact]
    public async Task UnknownPlaceholder_WarningsPropagate()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE,
                BuildWorkflowJson(prompt: "Use {unknown_tag} here"), BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Warnings, warning => warning.Message.Contains("unknown_tag"));
    }

    [Fact]
    public async Task StaleBaseVersion_Conflict()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> first = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null),
            TestContext.Current.CancellationToken);
        Assert.False(first.IsError);

        ErrorOr<SaveWorkflowTemplateContentResult> stale = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE,
                BuildWorkflowJson(prompt: "changed"), BaseVersionId: "not-the-head"),
            TestContext.Current.CancellationToken);

        Assert.True(stale.IsError);
        Assert.Equal(ErrorType.Conflict, stale.FirstError.Type);
    }

    [Fact]
    public async Task MatchingBaseVersion_Saves()
    {
        using var harness = new CoreHarness();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowTemplateContentResult> first = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null),
            TestContext.Current.CancellationToken);

        ErrorOr<SaveWorkflowTemplateContentResult> second = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE,
                BuildWorkflowJson(prompt: "changed"), BaseVersionId: first.Value.VersionId),
            TestContext.Current.CancellationToken);

        Assert.False(second.IsError);
        Assert.False(second.Value.CreatedWorkflowType);
        Assert.NotEqual(first.Value.VersionId, second.Value.VersionId);
    }

    [Fact]
    public async Task SuccessfulSave_MirrorsTemplateToGitHub()
    {
        using var harness = new CoreHarness();
        IGitHubWorkflowVersioningService gitHub = CreateGitHub();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness, gitHub);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null,
                NewTypeName: "Report"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await gitHub.Received(1).CommitWorkflowAsync(
            TYPE,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<string>(content => content.Contains("Report")),
            $"Save workflow {TYPE}",
            API_AUTHOR_NAME,
            API_AUTHOR_EMAIL,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DedupedSave_DoesNotMirrorToGitHub()
    {
        using var harness = new CoreHarness();
        IGitHubWorkflowVersioningService gitHub = CreateGitHub();
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness, gitHub);

        ErrorOr<SaveWorkflowTemplateContentResult> first = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null,
                NewTypeName: "Report"),
            TestContext.Current.CancellationToken);
        Assert.False(first.IsError);
        gitHub.ClearReceivedCalls();

        WorkflowVersion head = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);

        ErrorOr<SaveWorkflowTemplateContentResult> second = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, head.WorkflowJson,
                BaseVersionId: first.Value.VersionId),
            TestContext.Current.CancellationToken);

        Assert.False(second.IsError);
        Assert.True(second.Value.WasDeduped);
        await AnyCommit(gitHub.DidNotReceive());
    }

    [Fact]
    public async Task GitHubMirrorFailure_DoesNotFailSave()
    {
        using var harness = new CoreHarness();
        IGitHubWorkflowVersioningService gitHub = CreateGitHub();
        _ = AnyCommit(gitHub).ThrowsAsync(new InvalidOperationException("GitHub PAT is not configured in app settings."));
        SaveWorkflowTemplateContentCommandHandler handler = CreateHandler(harness, gitHub);

        ErrorOr<SaveWorkflowTemplateContentResult> result = await handler.Handle(
            new SaveWorkflowTemplateContentCommand(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null,
                NewTypeName: "Report"),
            TestContext.Current.CancellationToken);

        // Best-effort mirror: the save is persisted, so a GitHub failure must not fail it.
        Assert.False(result.IsError);
    }

    private static IGitHubWorkflowVersioningService CreateGitHub()
    {
        IGitHubWorkflowVersioningService gitHub = Substitute.For<IGitHubWorkflowVersioningService>();
        _ = AnyCommit(gitHub).Returns(new GitHubCommitResult { Sha = "test-sha-1234567" });
        return gitHub;
    }

    // Shared "any CommitWorkflowAsync call" specification for stubbing and verifying.
    private static Task<GitHubCommitResult> AnyCommit(IGitHubWorkflowVersioningService gitHub) =>
        gitHub.CommitWorkflowAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

    private static IOptionsMonitor<GitHubSettings> GitHubSettingsWithApiAuthor()
    {
        return new ServiceCollection()
            .Configure<GitHubSettings>(settings =>
            {
                settings.ApiAuthorName = API_AUTHOR_NAME;
                settings.ApiAuthorEmail = API_AUTHOR_EMAIL;
            })
            .BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<GitHubSettings>>();
    }

    private static SaveWorkflowTemplateContentCommandHandler CreateHandler(
        CoreHarness harness, IGitHubWorkflowVersioningService? gitHub = null)
    {
        ISender sender = SaveWorkflowSender.Create(
            new WorkflowSavePipeline(
                harness.UnitOfWork,
                harness.Context,
                new WorkflowCanonicalizer(),
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(new AgentRuntimeSettings()),
                NullLogger<WorkflowSavePipeline>.Instance, IniaAi.Testing.DependencyInjection.SuperAdminLookups.OverEmptyUserData()),
            harness.Context);

        return new SaveWorkflowTemplateContentCommandHandler(
            harness.UnitOfWork,
            new WorkflowTemplateStructuralValidator(),
            new WorkflowTemplateSavePreflight(harness.UnitOfWork),
            sender,
            gitHub ?? CreateGitHub(),
            GitHubSettingsWithApiAuthor(),
            NullLogger<SaveWorkflowTemplateContentCommandHandler>.Instance);
    }

    private static string BuildWorkflowJson(string prompt = "Who is the counterparty?")
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return new WorkflowCanonicalizer().ToCanonicalJson(workflow);
    }

    private sealed class CoreHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }

        private readonly ServiceProvider _provider;

        public CoreHarness()
        {
            DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"savewftc-{Guid.NewGuid()}")
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
