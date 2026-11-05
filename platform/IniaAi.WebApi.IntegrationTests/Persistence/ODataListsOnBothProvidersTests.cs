using IniaAi.Application.Queries.Counterparties.ListCounterparties;
using IniaAi.Application.Queries.Counterparties.ListCounterpartyTypes;
using IniaAi.Application.Queries.Documents.ListDocumentPages;
using IniaAi.Application.Queries.Documents.ListDocuments;
using IniaAi.Application.Queries.ModelPricing.ListModelPricings;
using IniaAi.Application.Queries.OrganizationModelSettings.ListOrganizationModelSettings;
using IniaAi.Application.Queries.TokenUsage.ListTokenUsage;
using IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutions;
using IniaAi.Application.Queries.WorkflowOutputs.PaginatedResponseWorkflowOutputs;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.Contracts.Responses;
using Microsoft.EntityFrameworkCore;
using static IniaAi.WebApi.IntegrationTests.Persistence.BothProvidersLists;
using ExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

// The open lists on a real database (BothProvidersLists). It also reads the member the in-memory
// model cannot reach: the token usage's user. Each closed module checks its own lists in its folder.
public abstract class ODataListsOnBothProvidersTests
{
    private const string TEST_DB_PREFIX = "iniaai_odatalists_";
    private const string GUARD_NAME = "the OData list guard";

    private static readonly DateTime _created = new(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc);

    private readonly ICoreDatabaseFixture _core;
    private readonly string _provider;
    private readonly string _skipReason;

    protected ODataListsOnBothProvidersTests(ICoreDatabaseFixture core, string provider, string skipReason)
    {
        _core = core;
        _provider = provider;
        _skipReason = skipReason;
    }

    [Fact]
    public async Task Every_list_filters_sorts_pages_and_counts_on_a_real_database()
    {
        if (!_core.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }

        await using (CoreDbContext core = _core.CreateContext())
        {
            await SeedCoreAsync(core, TestContext.Current.CancellationToken);
        }

        await BothProvidersLists.RunAsync(_provider, TEST_DB_PREFIX, GUARD_NAME, SeedUserDataAsync, AssertEveryListAsync, _core);
    }

    private async Task AssertEveryListAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        ODataPaginatedResponse<WebApi.Contracts.Responses.Documents.DocumentSummaryDto> documents = await ListAsync<PaginatedResponseDocumentsQueryHandler, PaginatedResponseDocumentsQuery, WebApi.Contracts.Responses.Documents.DocumentSummaryDto>(
            services, "?$filter=sizeBytes gt 0 and processingError eq null&$orderby=createdAt desc" + PAGE_TWO_COUNTED,
            options => new PaginatedResponseDocumentsQuery(ORGANIZATION_ID, USER_ID, options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(documents, ["document-processed"], 2, "documents");

        ODataPaginatedResponse<WebApi.Contracts.Responses.Documents.PageSummaryDto> pages = await ListAsync<PaginatedResponseDocumentPagesQueryHandler, PaginatedResponseDocumentPagesQuery, WebApi.Contracts.Responses.Documents.PageSummaryDto>(
            services, "?$filter=hasEmbedding or ocrNeedsReview&$orderby=pageNumber desc" + PAGE_TWO_COUNTED,
            options => new PaginatedResponseDocumentPagesQuery(ORGANIZATION_ID, USER_ID, "document-processed", options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(pages, ["page-1"], 2, "document pages");
        Assert.Equal(200, Assert.Single(pages.Value).TextPreview!.Length);

        ODataPaginatedResponse<WebApi.Contracts.Responses.Counterparty.CounterpartyListItemDto> counterparties = await ListAsync<ListCounterpartiesQueryHandler, ListCounterpartiesQuery, WebApi.Contracts.Responses.Counterparty.CounterpartyListItemDto>(
            services, "?$filter=parentId eq 'counterparty-parent' or contains(nameVariants, 'Alpha')&$orderby=name desc" + PAGE_TWO_COUNTED,
            options => new ListCounterpartiesQuery(ORGANIZATION_ID, USER_ID, options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(counterparties, ["counterparty-child"], 2, "counterparties");
        Assert.Equal(["type-corporation"], Assert.Single(counterparties.Value).CounterpartyTypeIds);

        ODataPaginatedResponse<WebApi.Contracts.Responses.CounterpartyTypes.CounterpartyTypeListItemDto> types = await ListAsync<ListCounterpartyTypesQueryHandler, ListCounterpartyTypesQuery, WebApi.Contracts.Responses.CounterpartyTypes.CounterpartyTypeListItemDto>(
            services, "?$filter=counterpartyCount gt 0 and startswith(id, 'type-')&$orderby=counterpartyCount desc,name" + PAGE_TWO_COUNTED,
            options => new ListCounterpartyTypesQuery(ORGANIZATION_ID, USER_ID, options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(types, ["type-hedge-fund"], 2, "counterparty types");

        ODataPaginatedResponse<WebApi.Contracts.Responses.TokenUsage.TokenUsageDto> usage = await ListAsync<ListTokenUsageQueryHandler, ListTokenUsageQuery, WebApi.Contracts.Responses.TokenUsage.TokenUsageDto>(
            services, "?$filter=provider eq 'aws-bedrock' and totalCost gt 0&$orderby=createdAt desc" + PAGE_TWO_COUNTED,
            options => new ListTokenUsageQuery(ORGANIZATION_ID, USER_ID, options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(usage, ["usage-1"], 2, "token usage");
        WebApi.Contracts.Responses.TokenUsage.TokenUsageDto usageRow = Assert.Single(usage.Value);
        Assert.Equal("Ada Lovelace", usageRow.UserDisplayName);
        Assert.Equal(0.0081m, usageRow.TotalCost);
        Assert.Null(usageRow.WorkflowTypeName);

        ODataPaginatedResponse<WebApi.Contracts.Responses.ModelPricing.ModelPricingListItemDto> pricing = await ListAsync<ListModelPricingsQueryHandler, ListModelPricingsQuery, WebApi.Contracts.Responses.ModelPricing.ModelPricingListItemDto>(
            services, "?$filter=isActive and startswith(id, 'pricing-lists-')&$orderby=displayName desc" + PAGE_TWO_COUNTED,
            options => new ListModelPricingsQuery(options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(pricing, ["pricing-lists-haiku"], 2, "model pricing");

        ODataPaginatedResponse<WebApi.Contracts.Responses.TenantModelSettings.TenantModelSettingsListItemDto> settings = await ListAsync<ListOrganizationModelSettingsQueryHandler, ListOrganizationModelSettingsQuery, WebApi.Contracts.Responses.TenantModelSettings.TenantModelSettingsListItemDto>(
            services, "?$filter=isEnabled and tenantDisplayName ne 'Global Default'&$orderby=priority desc" + PAGE_TWO_COUNTED,
            options => new ListOrganizationModelSettingsQuery(options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(settings, ["settings-block"], 2, "model settings");
        Assert.Equal("Claude Haiku", Assert.Single(settings.Value).ModelDisplayName);

        ODataPaginatedResponse<WebApi.Contracts.Shared.Users.UserDto> users = await ListAsync<ListUsersQueryHandler, ListUsersQuery, WebApi.Contracts.Shared.Users.UserDto>(
            services, "?$filter=lastName ne null&$orderby=username desc" + PAGE_TWO_COUNTED,
            options => new ListUsersQuery(ORGANIZATION_ID, USER_ID, options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(users, [USER_ID], 2, "users");
        Assert.Equal("Ada Lovelace", Assert.Single(users.Value).FullName);

        ODataPaginatedResponse<WebApi.Contracts.Responses.WorkflowOutput.WorkflowExecutionSummaryDto> executions = await ListAsync<ListWorkflowExecutionsQueryHandler, ListWorkflowExecutionsQuery, WebApi.Contracts.Responses.WorkflowOutput.WorkflowExecutionSummaryDto>(
            services, "?$filter=startswith(title, 'Replay') or counterpartyName eq 'Parent Holdings'&$orderby=createdAt desc" + PAGE_TWO_COUNTED,
            options => new ListWorkflowExecutionsQuery(ORGANIZATION_ID, USER_ID, options), (handler, query) => handler.Handle(query, cancellationToken));
        AssertPage(executions, ["execution-1"], 2, "workflow executions");
        Assert.Equal(TimeSpan.FromMinutes(3), Assert.Single(executions.Value).Duration);
    }

    private static async Task SeedUserDataAsync(UserDataDbContext context, CancellationToken cancellationToken)
    {
        context.Users.AddRange(
            new User(ORGANIZATION_ID) { Id = "user-grace", UserName = "grace@example.com", Email = "grace@example.com", FirstName = "Grace", LastName = "Hopper" },
            new User(ORGANIZATION_ID) { Id = "user-nameless", UserName = "nameless@example.com", Email = "nameless@example.com" });
        _ = await context.SaveChangesAsync(cancellationToken);

        Document processed = BuildDocument("document-processed", 2048, 0);
        processed.RequestProcessing(_created.AddMinutes(1));
        processed.RecordProcessed(_created.AddMinutes(2));
        Document queued = BuildDocument("document-queued", 4096, 1);
        queued.RequestProcessing(_created.AddMinutes(3));
        Document failed = BuildDocument("document-failed", 512, 2);
        failed.RecordProcessingFailure("The scan is unreadable.");
        context.Documents.AddRange(processed, queued, failed, BuildDocument("document-empty", 0, 3));
        _ = await context.SaveChangesAsync(cancellationToken);

        var embedding = new EmbeddingVector("cohere-embed-v4", [0.1, 0.2]);
        _ = context.Add(embedding);
        Page embedded = BuildPage(context, processed, "page-1", 1, PageStatus.ProcessedSuccessfully);
        embedded.Text = new string('a', 250);
        embedded.EmbeddingVectorId = embedding.Id;
        Page needsReview = BuildPage(context, processed, "page-2", 2, PageStatus.OCRNeedsReview);
        needsReview.RawText = "Short raw text.";
        _ = BuildPage(context, processed, "page-3", 3, PageStatus.NotProcessed);
        _ = await context.SaveChangesAsync(cancellationToken);

        var corporation = new CounterpartyType(CounterpartyType.TYPE_CORPORATION) { Id = "type-corporation" };
        var hedgeFund = new CounterpartyType("Hedge Fund") { Id = "type-hedge-fund" };
        var insurer = new CounterpartyType("Insurer") { Id = "type-insurer" };
        context.CounterpartyTypes.AddRange(corporation, hedgeFund, insurer);
        context.Counterparties.AddRange(
            BuildCounterparty("counterparty-parent", "Parent Holdings", "Parent", null, corporation, hedgeFund),
            BuildCounterparty("counterparty-child", "Alpha Child", "Alpha; Child", "counterparty-parent", corporation),
            BuildCounterparty("counterparty-sibling", "Beta Child", "Beta", "counterparty-parent", hedgeFund),
            BuildCounterparty("counterparty-loose", "Gamma Loose", "Gamma", null));
        _ = await context.SaveChangesAsync(cancellationToken);

        context.TokenUsages.AddRange(
            BuildUsage("usage-1", 0, "aws-bedrock", USER_ID, 0.0036m, 0.0045m),
            BuildUsage("usage-2", 1, "cohere-api", null, 0.0008m),
            BuildUsage("usage-3", 2, "aws-bedrock", null, 0.001m));
        _ = await context.SaveChangesAsync(cancellationToken);

        context.OrganizationModelSettings.AddRange(
            BuildSettings("settings-global", null, UsageContext.BlockExecution, "pricing-lists-sonnet", isEnabled: true, priority: 0),
            BuildSettings("settings-block", ORGANIZATION_ID, UsageContext.BlockExecution, "pricing-lists-haiku", isEnabled: true, priority: 1),
            BuildSettings("settings-embedding", ORGANIZATION_ID, UsageContext.Embedding, "pricing-lists-sonnet", isEnabled: true, priority: 2),
            BuildSettings("settings-disabled", ORGANIZATION_ID, UsageContext.DocumentProcessing, "pricing-lists-sonnet", isEnabled: false, priority: 3));
        _ = await context.SaveChangesAsync(cancellationToken);

        WorkflowExecution draft = BuildExecution("execution-1", "Annual review", 0);
        draft.CounterpartyId = "counterparty-parent";
        draft.StartedAt = _created.AddMinutes(1);
        draft.CompletedAt = _created.AddMinutes(4);
        context.WorkflowExecutions.AddRange(draft, BuildExecution("execution-2", "Quarterly review", 1), BuildExecution("execution-3", "Replay review", 2));
        _ = await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCoreAsync(CoreDbContext context, CancellationToken cancellationToken)
    {
        if (await context.ModelPricings.AnyAsync(pricing => pricing.Id.StartsWith("pricing-lists-"), cancellationToken))
        {
            return;
        }

        context.ModelPricings.AddRange(
            BuildPricing("pricing-lists-sonnet", "Claude Sonnet", isActive: true),
            BuildPricing("pricing-lists-haiku", "Claude Haiku", isActive: true),
            BuildPricing("pricing-lists-legacy", "Claude Legacy", isActive: false));
        _ = await context.SaveChangesAsync(cancellationToken);
    }

    private static Document BuildDocument(string id, long sizeBytes, int order)
    {
        var fileMetadata = new FileMetadata(ORGANIZATION_ID, $"{id}.pdf", sizeBytes, StorageProvider.Database) { Id = $"file-{id}" };
        return new Document(fileMetadata, MediaType.ApplicationPdf)
        {
            Id = id,
            OrganizationId = ORGANIZATION_ID,
            Name = $"{id}.pdf",
            UserId = USER_ID,
            CreatedAt = _created.AddHours(order),
            UpdatedAt = _created.AddHours(order),
        };
    }

    private static Page BuildPage(UserDataDbContext context, Document document, string id, int pageNumber, PageStatus status)
    {
        var page = new Page(document, pageNumber, pageNumber) { Status = status };
        _ = context.Add(page);
        context.Entry(page).Property(item => item.Id).CurrentValue = id;
        return page;
    }

    private static Counterparty BuildCounterparty(string id, string name, string nameVariants, string? parentId, params CounterpartyType[] types)
    {
        return new Counterparty(ORGANIZATION_ID, name)
        {
            Id = id,
            ParentId = parentId,
            NameVariants = nameVariants,
            CounterpartyTypes = [.. types],
            CreatedAt = _created,
            UpdatedAt = _created,
        };
    }

    private static TokenUsage BuildUsage(string id, int order, string provider, string? userId, params decimal[] costs)
    {
        return new TokenUsage
        {
            Id = id,
            OrganizationId = ORGANIZATION_ID,
            UserId = userId,
            WorkflowTypeId = "Corp",
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = ExecutionContext.WorkflowExecution,
            BlockType = BlockTypes.AiQuery,
            Provider = provider,
            Family = "anthropic",
            ModelId = "claude-sonnet",
            Components = [.. costs.Select((cost, index) => new TokenUsageComponent
            {
                Id = $"{id}-component-{index}",
                TokenUsageId = id,
                ComponentType = index == 0 ? PricingComponentType.Input : PricingComponentType.Output,
                Units = 100,
                Cost = cost,
            })],
            CreatedAt = _created.AddMinutes(order),
            UpdatedAt = _created.AddMinutes(order),
        };
    }

    private static OrganizationModelSettings BuildSettings(string id, string? organizationId, UsageContext context, string modelPricingId, bool isEnabled, int priority)
    {
        return new OrganizationModelSettings
        {
            Id = id,
            OrganizationId = organizationId,
            UsageContext = context,
            ModelPricingId = modelPricingId,
            IsEnabled = isEnabled,
            Priority = priority,
        };
    }

    private static WorkflowExecution BuildExecution(string id, string title, int order)
    {
        return new WorkflowExecution
        {
            Id = id,
            WorkflowTypeId = "Corp",
            Title = title,
            UserId = USER_ID,
            OrganizationId = ORGANIZATION_ID,
            WorkflowState = WorkflowState.Draft,
            CreatedAt = _created.AddMinutes(order),
            UpdatedAt = _created.AddMinutes(order),
        };
    }

    private static ModelPricing BuildPricing(string id, string displayName, bool isActive)
    {
        return new ModelPricing
        {
            Id = id,
            Provider = "aws-bedrock",
            Family = "anthropic",
            ModelId = $"model-{id}",
            DisplayName = displayName,
            PricingType = PricingType.Llm,
            EffectiveFrom = _created,
            IsActive = isActive,
        };
    }
}

public sealed class ODataListsOnBothProvidersPostgresTests(PostgresCoreDatabaseFixture fixture)
    : ODataListsOnBothProvidersTests(fixture, BothProvidersUserDataDatabase.POSTGRESQL, "Set INIA_TEST_PG to run the OData list guard on PostgreSQL."),
    IClassFixture<PostgresCoreDatabaseFixture>;

public sealed class ODataListsOnBothProvidersSqlServerTests(SqlServerCoreDatabaseFixture fixture)
    : ODataListsOnBothProvidersTests(fixture, BothProvidersUserDataDatabase.MSSQL, "Set INIA_TEST_MSSQL to run the OData list guard on SQL Server."),
    IClassFixture<SqlServerCoreDatabaseFixture>;
