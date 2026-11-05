using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowTemplates.ValidateWorkflowTemplateContent;
using IniaAi.Application.Errors;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.WorkflowTemplates;

public class ValidateWorkflowTemplateContentQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";

    [Fact]
    public async Task Validate_AbsentType_ReportsWouldCreateAndWritesNothing()
    {
        using var harness = new CoreHarness();
        ValidateWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await handler.Handle(
            new ValidateWorkflowTemplateContentQuery(ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.WouldCreateWorkflowType);
        Assert.Null(result.Value.HeadVersionId);
        await AssertNothingWrittenAsync(harness);
    }

    [Fact]
    public async Task Validate_ExistingTypeWithFreshBase_ReportsHeadAndNoCreate()
    {
        using var harness = new CoreHarness();
        WorkflowVersion head = await SeedTypeWithHeadAsync(harness);
        ValidateWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await handler.Handle(
            new ValidateWorkflowTemplateContentQuery(ORG, USER, TYPE, BuildWorkflowJson(), head.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.WouldCreateWorkflowType);
        Assert.Equal(head.Id, result.Value.HeadVersionId);
        Assert.Empty(await harness.Context.Workflows.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Validate_RetiredType_ReportsNotFound_AsTheSaveWould()
    {
        using var harness = new CoreHarness();
        WorkflowVersion head = await SeedTypeWithHeadAsync(harness);
        WorkflowType type = await harness.Context.WorkflowTypes.SingleAsync(TestContext.Current.CancellationToken);
        type.RetiredAt = DateTime.UtcNow;
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        ValidateWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await handler.Handle(
            new ValidateWorkflowTemplateContentQuery(ORG, USER, TYPE, BuildWorkflowJson(), head.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task Validate_StaleBaseVersionId_ReturnsVersionConflictAndWritesNothing()
    {
        using var harness = new CoreHarness();
        WorkflowVersion head = await SeedTypeWithHeadAsync(harness);
        ValidateWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await handler.Handle(
            new ValidateWorkflowTemplateContentQuery(ORG, USER, TYPE, BuildWorkflowJson(), "stale-version"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("WorkflowTemplate.VersionConflict", result.FirstError.Code);
        Assert.Equal(head.Id, result.FirstError.Metadata?["currentHeadVersionId"]);
        Assert.Single(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Validate_StructurallyInvalidJson_ReturnsValidationErrorsAndWritesNothing()
    {
        using var harness = new CoreHarness();
        ValidateWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await handler.Handle(
            new ValidateWorkflowTemplateContentQuery(ORG, USER, TYPE, "{ \"Sections\": 5 }", BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        await AssertNothingWrittenAsync(harness);
    }

    [Fact]
    public async Task Validate_UnknownSharedComponentReference_ReturnsUnknownComponentAndWritesNothing()
    {
        using var harness = new CoreHarness();
        ValidateWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await handler.Handle(
            new ValidateWorkflowTemplateContentQuery(
                ORG, USER, TYPE, BuildWorkflowJsonLinkedTo("ghost-component"), BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(
            result.Errors,
            error => error.Description.Contains(SharedComponentErrorCodes.UNKNOWN_COMPONENT, StringComparison.Ordinal));
        await AssertNothingWrittenAsync(harness);
    }

    [Fact]
    public async Task Validate_KnownSharedComponentReference_Passes()
    {
        using var harness = new CoreHarness();
        _ = harness.Context.Set<SharedComponent>().Add(
            new SharedComponent { Id = "c-block", Kind = SharedComponentKind.Block, Name = "KYC" });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        ValidateWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await handler.Handle(
            new ValidateWorkflowTemplateContentQuery(
                ORG, USER, TYPE, BuildWorkflowJsonLinkedTo("c-block"), BaseVersionId: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Validate_UnknownCategoryForNewType_ReturnsCategoryErrorAndWritesNothing()
    {
        using var harness = new CoreHarness();
        ValidateWorkflowTemplateContentQueryHandler handler = CreateHandler(harness);

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await handler.Handle(
            new ValidateWorkflowTemplateContentQuery(
                ORG, USER, TYPE, BuildWorkflowJson(), BaseVersionId: null, NewTypeCategoryId: "no-such-category"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        await AssertNothingWrittenAsync(harness);
    }

    private static async Task AssertNothingWrittenAsync(CoreHarness harness)
    {
        Assert.Empty(await harness.Context.WorkflowTypes.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await harness.Context.Workflows.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await harness.Context
            .Set<WorkflowTypeOrganizationAccess>()
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<WorkflowVersion> SeedTypeWithHeadAsync(CoreHarness harness)
    {
        _ = harness.Context.WorkflowTypes.Add(new WorkflowType(TYPE, "Existing"));
        var head = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = new string('a', 64),
            WorkflowJson = BuildWorkflowJson(),
            CreatedByUserId = USER,
        };
        _ = harness.Context.WorkflowVersions.Add(head);
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return head;
    }

    private static ValidateWorkflowTemplateContentQueryHandler CreateHandler(CoreHarness harness)
    {
        return new ValidateWorkflowTemplateContentQueryHandler(
            new WorkflowTemplateStructuralValidator(),
            new WorkflowTemplateSavePreflight(harness.UnitOfWork));
    }

    private static string BuildWorkflowJson(string prompt = "Who is the counterparty?")
    {
        return new WorkflowCanonicalizer().ToCanonicalJson(BuildWorkflow(prompt));
    }

    private static string BuildWorkflowJsonLinkedTo(string sharedComponentId)
    {
        Workflow workflow = BuildWorkflow("Who is the counterparty?");
        Block block = workflow.Sections.First().Blocks.First();
        block.SharedComponentId = sharedComponentId;
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = new string('b', 64);

        return new WorkflowCanonicalizer().ToCanonicalJson(workflow);
    }

    private static Workflow BuildWorkflow(string prompt)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return workflow;
    }

    private sealed class CoreHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }

        private readonly ServiceProvider _provider;

        public CoreHarness()
        {
            DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"validatewftc-{Guid.NewGuid()}")
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
