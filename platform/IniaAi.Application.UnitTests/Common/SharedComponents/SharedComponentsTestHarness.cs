using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Common.Signatures;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Testing;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Common.SharedComponents;

internal sealed class SharedComponentsTestHarness : IDisposable
{
    public const string DROPPED_BLOCK_FIELD = "ShouldExcludeBlockOutputFromCitationSources";

    public CoreDbContext Context { get; }
    public DbContextOptions<CoreDbContext> CoreOptions { get; }
    public ICoreUnitOfWork UnitOfWork { get; }
    public WorkflowCanonicalizer Canonicalizer { get; } = new();
    public SharedComponentContentNormalizer Normalizer { get; }

    public IUserDataUnitOfWork UserDataUnitOfWork => _provider.GetRequiredService<IUserDataUnitOfWork>();
    public UserDataDbContext UserDataContext => _provider.GetRequiredService<UserDataDbContext>();

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

    public SharedComponentsTestHarness()
    {
        string databaseName = $"sharedcomp-{Guid.NewGuid()}";
        CoreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"{databaseName}-core")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        Context = new CoreDbContext(CoreOptions);

        var services = new ServiceCollection();
        _ = services.AddDbContext<UserDataDbContext>(o => o.UseInMemoryDatabase($"{databaseName}-userdata"));
        _ = services.AddScoped(_ => Context);
        _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
        _ = services.AddTestDatabaseServicesWithoutDbContext();
        _provider = services.BuildServiceProvider();

        UnitOfWork = _provider.GetRequiredService<ICoreUnitOfWork>();
        Normalizer = new SharedComponentContentNormalizer(Canonicalizer);
    }

    public WorkflowSavePipeline CreatePipeline(
        AgentRuntimeSettings? agentRuntime = null,
        IAgentRunLimitsProvider? runLimitsProvider = null)
    {
        return new WorkflowSavePipeline(
            UnitOfWork,
            Context,
            Canonicalizer,
            new WorkflowDiffService(),
            new WorkflowNarrativeGenerator(),
            Options.Create(agentRuntime ?? new AgentRuntimeSettings()),
            NullLogger<WorkflowSavePipeline>.Instance,
            SuperAdmins,
            runLimitsProvider);
    }

    /// <summary>The stored role lookup the save pipeline asks whether a saver is a super admin.</summary>
    public ISuperAdminLookup SuperAdmins => _provider.GetRequiredService<ISuperAdminLookup>();

    /// <summary>Gives <paramref name="userId"/> the super admin role in the user-data database.</summary>
    public async Task MakeSuperAdminAsync(string userId)
    {
        IdentityRole? role = await UserDataContext.Roles.SingleOrDefaultAsync(r => r.Name == Roles.SUPER_ADMIN);
        if (role is null)
        {
            role = new IdentityRole(Roles.SUPER_ADMIN) { NormalizedName = Roles.SUPER_ADMIN.ToUpperInvariant() };
            _ = UserDataContext.Roles.Add(role);
        }

        _ = UserDataContext.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = role.Id });
        _ = await UserDataContext.SaveChangesAsync();
    }

    public SharedComponentFanOutService CreateFanOutService(WorkflowSavePipeline? pipeline = null)
    {
        return CreateFanOutService(pipeline, agentRuntime: null);
    }

    public SharedComponentFanOutService CreateFanOutService(IBaselineReader baselines)
    {
        return CreateFanOutService(pipeline: null, agentRuntime: null, baselines);
    }

    public SharedComponentFanOutService CreateFanOutService(AgentRuntimeSettings? agentRuntime)
    {
        return CreateFanOutService(pipeline: null, agentRuntime);
    }

    private SharedComponentFanOutService CreateFanOutService(
        WorkflowSavePipeline? pipeline,
        AgentRuntimeSettings? agentRuntime,
        IBaselineReader? baselines = null)
    {
        return new SharedComponentFanOutService(
            UnitOfWork,
            UserDataUnitOfWork,
            Context,
            pipeline ?? CreatePipeline(agentRuntime),
            Normalizer,
            new SharedComponentReconciler(Normalizer),
            Canonicalizer,
            new WorkflowTemplateStructuralValidator(),
            new SharedComponentEditor(Canonicalizer, Normalizer),
            baselines ?? new NoBaselineReader(),
            Options.Create(agentRuntime ?? new AgentRuntimeSettings()),
            NullLogger<SharedComponentFanOutService>.Instance);
    }

    public (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        string json = Normalizer.ToDefinitionJson(section);

        return (json, Normalizer.ComputeContentHashOfDefinitionJson(json));
    }

    public (string DefinitionJson, string ContentHash) Definition(Block block)
    {
        string json = Normalizer.ToDefinitionJson(block);

        return (json, Normalizer.ComputeContentHashOfDefinitionJson(json));
    }

    /// <summary>
    /// A definition as a Library version stored it before the block model dropped a field: the
    /// payload and its stored hash still carry the field, which a copy written today has not.
    /// </summary>
    public (string DefinitionJson, string ContentHash) StoredBeforeAFieldWasDropped(string definitionJson)
    {
        var stored = JObject.Parse(definitionJson);
        IEnumerable<JObject> blocks = stored[nameof(Section.Blocks)] is JArray sectionBlocks
            ? sectionBlocks.OfType<JObject>()
            : [stored];
        foreach (JObject block in blocks)
        {
            block[DROPPED_BLOCK_FIELD] = true;
        }

        string json = stored.ToString(Formatting.None);

        return (json, Normalizer.ComputeContentHashOfDefinitionJson(json));
    }

    public WorkflowVersion BuildWorkflowVersion(
        string workflowTypeId,
        string contentHash,
        DateTime? createdAt = null,
        string createdByUserId = "user-test")
    {
        return new WorkflowVersion
        {
            WorkflowTypeId = workflowTypeId,
            ContentHash = contentHash,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = createdByUserId,
        };
    }

    public WorkflowBaselineRegistration BuildRegistration(
        string workflowTypeId,
        string organizationId,
        string? blessedContentHash,
        BaselineRegistrationState state = BaselineRegistrationState.Active,
        string markedByUserId = "user-test")
    {
        return new WorkflowBaselineRegistration
        {
            WorkflowTypeId = workflowTypeId,
            OrganizationId = organizationId,
            CounterpartyId = string.Empty,
            ExecutionId = $"exec-{organizationId}",
            MarkedByUserId = markedByUserId,
            MarkedAt = DateTime.UtcNow,
            BlessedContentHash = blessedContentHash,
            State = state,
        };
    }

    public async Task<SharedComponent> AddComponentAsync(
        SharedComponentKind kind,
        string name,
        SharedComponentState state = SharedComponentState.Active,
        string? description = null,
        string? categoryId = null,
        string? id = null)
    {
        var component = new SharedComponent
        {
            Kind = kind,
            Name = name,
            State = state,
            Description = description,
            CategoryId = categoryId,
        };
        if (id is not null)
        {
            component.Id = id;
        }

        _ = await Context.SharedComponents.AddAsync(component);
        _ = await Context.SaveChangesAsync();

        return component;
    }

    public async Task AddWorkflowAsync(Workflow workflow)
    {
        _ = Context.Add(workflow);
        _ = await Context.SaveChangesAsync();
    }

    public async Task<SharedComponentCategory> AddCategoryAsync(
        string id,
        string name,
        int? displayOrder = null,
        string? description = null)
    {
        var category = new SharedComponentCategory
        {
            Id = id,
            Name = name,
            DisplayOrder = displayOrder,
            Description = description,
        };
        _ = await Context.SharedComponentCategories.AddAsync(category);
        _ = await Context.SaveChangesAsync();

        return category;
    }

    public async Task<SharedComponentVersion> AddVersionAsync(
        string componentId,
        int sequenceNumber,
        string contentHash,
        string? contentJson,
        DateTime? payloadAbsentAt = null,
        string? parentVersionId = null)
    {
        var version = new SharedComponentVersion
        {
            SharedComponentId = componentId,
            SequenceNumber = sequenceNumber,
            ContentHash = contentHash,
            ParentVersionId = parentVersionId,
            PayloadAbsentAt = payloadAbsentAt,
            CreatedByUserId = "user-test",
        };
        if (contentJson is not null)
        {
            version.ContentJson = contentJson;
        }

        _ = await Context.SharedComponentVersions.AddAsync(version);
        _ = await Context.SaveChangesAsync();

        return version;
    }

    public (ISender Sender, SaveWorkflowCommandHandler SaveHandler) CreateRealSaveSender(
        AgentRuntimeSettings? agentRuntime = null)
    {
        WorkflowSavePipeline pipeline = CreatePipeline(agentRuntime);
        var saveHandler = new SaveWorkflowCommandHandler(
            pipeline,
            Context,
            NullLogger<SaveWorkflowCommandHandler>.Instance);

        PlatformSettings settings = new() { AgentRuntime = agentRuntime ?? new AgentRuntimeSettings() };

        return (SaveWorkflowSender.Create(pipeline, Context, settings), saveHandler);
    }

    public async Task<string> SaveTemplateAsync(
        SaveWorkflowCommandHandler saveHandler,
        string organizationId,
        string userId,
        string workflowTypeId,
        Workflow workflow)
    {
        _ = Context.Add(new WorkflowType(workflowTypeId, "Report"));
        _ = await Context.SaveChangesAsync();

        ErrorOr<SaveWorkflowResult> saveResult = await saveHandler.Handle(
            new SaveWorkflowCommand(organizationId, userId, workflowTypeId, workflow),
            CancellationToken.None);
        Assert.False(saveResult.IsError);

        return saveResult.Value.VersionId;
    }

    public void Dispose()
    {
        Context.Dispose();
        _provider.Dispose();
    }
}
