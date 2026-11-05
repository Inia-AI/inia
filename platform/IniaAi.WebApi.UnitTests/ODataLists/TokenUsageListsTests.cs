using ErrorOr;
using IniaAi.Application.Queries.ModelPricing.ListModelPricings;
using IniaAi.Application.Queries.OrganizationModelSettings.ListOrganizationModelSettings;
using IniaAi.Application.Queries.TokenUsage.ListTokenUsage;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;
using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using ExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.WebApi.UnitTests.ODataLists;

public class TokenUsageListsTests : ODataListTests
{
    private const string ORGANIZATION_ID = "org-tokens";
    private const string USER_ID = "user-tokens";

    private static readonly DateTime _created = new(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheTokenUsageList_ReturnsTheSameJson()
    {
        await SeedTokenUsageAsync();

        AssertMatchesFixture(await ListTokenUsageAsync("?$orderby=createdAt"), "TokenUsage");
    }

    [Fact]
    public async Task TheTokenUsageList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedTokenUsageAsync();

        ODataPaginatedResponse<TokenUsageDto> page = await ListTokenUsageAsync(
            "?$filter=provider eq 'aws-bedrock' and workflowTypeId ne null&$orderby=createdAt desc&$top=1&$skip=1&$count=true");

        Assert.Equal(["usage-1"], page.Value.Select(usage => usage.Id));
        Assert.Equal(2, page.Count);
    }

    [Fact]
    public async Task TheModelPricingList_ReturnsTheSameJson()
    {
        await SeedModelPricingAsync();

        AssertMatchesFixture(await ListModelPricingsAsync("?$orderby=displayName"), "ModelPricings");
    }

    [Fact]
    public async Task TheModelPricingList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedModelPricingAsync();

        ODataPaginatedResponse<ModelPricingListItemDto> page = await ListModelPricingsAsync(
            "?$filter=isActive and effectiveFrom ge 2026-01-01T00:00:00Z&$orderby=displayName desc&$top=1&$skip=1&$count=true");

        Assert.Equal(["pricing-haiku"], page.Value.Select(pricing => pricing.Id));
        Assert.Equal(2, page.Count);
    }

    [Fact]
    public async Task TheModelSettingsList_ReturnsTheSameJson()
    {
        await SeedModelPricingAsync();
        await SeedModelSettingsAsync();

        AssertMatchesFixture(await ListModelSettingsAsync("?$orderby=priority"), "OrganizationModelSettings");
    }

    [Fact]
    public async Task TheModelSettingsList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedModelPricingAsync();
        await SeedModelSettingsAsync();

        ODataPaginatedResponse<TenantModelSettingsListItemDto> page = await ListModelSettingsAsync(
            "?$filter=isEnabled and tenantId ne null&$orderby=priority desc&$top=1&$skip=1&$count=true");

        Assert.Equal(["settings-org-block"], page.Value.Select(settings => settings.Id));
        Assert.Equal(2, page.Count);
    }

    private async Task<ODataPaginatedResponse<TokenUsageDto>> ListTokenUsageAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<TokenUsageDto>> result = await Handler<ListTokenUsageQueryHandler>().Handle(
            new ListTokenUsageQuery(ORGANIZATION_ID, USER_ID, Options<TokenUsageDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task<ODataPaginatedResponse<ModelPricingListItemDto>> ListModelPricingsAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<ModelPricingListItemDto>> result = await Handler<ListModelPricingsQueryHandler>().Handle(
            new ListModelPricingsQuery(Options<ModelPricingListItemDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task<ODataPaginatedResponse<TenantModelSettingsListItemDto>> ListModelSettingsAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<TenantModelSettingsListItemDto>> result = await Handler<ListOrganizationModelSettingsQueryHandler>().Handle(
            new ListOrganizationModelSettingsQuery(Options<TenantModelSettingsListItemDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task SeedTokenUsageAsync()
    {
        var user = new User(ORGANIZATION_ID)
        {
            Id = USER_ID,
            UserName = "analyst@example.com",
            Email = "analyst@example.com",
            FirstName = "Ada",
            LastName = "Lovelace",
        };

        TokenUsage blockUsage = BuildUsage("usage-1", 0, "aws-bedrock", "Corp");
        blockUsage.UserId = USER_ID;
        blockUsage.BlockType = BlockTypes.AiQuery;
        blockUsage.Components =
        [
            BuildComponent("usage-1-input", blockUsage.Id, PricingComponentType.Input, 1200, 0.0036m),
            BuildComponent("usage-1-output", blockUsage.Id, PricingComponentType.Output, 300, 0.0045m),
        ];

        TokenUsage embeddingUsage = BuildUsage("usage-2", 1, "cohere-api", null);
        embeddingUsage.UsageType = UsageType.EmbeddingQuery;
        embeddingUsage.ExecutionContext = ExecutionContext.DocumentUpload;
        embeddingUsage.Components = [BuildComponent("usage-2-input", embeddingUsage.Id, PricingComponentType.Input, 800, 0.0008m)];

        TokenUsage laterUsage = BuildUsage("usage-3", 2, "aws-bedrock", "Corp");

        TokenUsage otherOrganization = BuildUsage("usage-other", 3, "aws-bedrock", "Corp");
        otherOrganization.OrganizationId = "org-other";

        _ = UserData.Users.Add(user);
        UserData.TokenUsages.AddRange(blockUsage, embeddingUsage, laterUsage, otherOrganization);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedModelPricingAsync()
    {
        Core.ModelPricings.AddRange(
            BuildPricing("pricing-sonnet", "Claude Sonnet", PricingType.Llm, isActive: true, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            BuildPricing("pricing-haiku", "Claude Haiku", PricingType.Llm, isActive: true, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            BuildPricing("pricing-legacy", "Claude Legacy", PricingType.Llm, isActive: false, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            BuildPricing("pricing-embed", "Cohere Embed", PricingType.Embedding, isActive: true, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedModelSettingsAsync()
    {
        var organization = new Organization("Settings Org", ORGANIZATION_ID) { CreatedAt = _created, UpdatedAt = _created };
        _ = UserData.Organizations.Add(organization);
        UserData.OrganizationModelSettings.AddRange(
            BuildSettings("settings-global", null, UsageContext.BlockExecution, "pricing-sonnet", isEnabled: true, priority: 0),
            BuildSettings("settings-org-block", ORGANIZATION_ID, UsageContext.BlockExecution, "pricing-haiku", isEnabled: true, priority: 1),
            BuildSettings("settings-org-embedding", ORGANIZATION_ID, UsageContext.Embedding, "pricing-embed", isEnabled: true, priority: 2),
            BuildSettings("settings-org-disabled", ORGANIZATION_ID, UsageContext.DocumentProcessing, "pricing-missing", isEnabled: false, priority: 3));
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static TokenUsage BuildUsage(string id, int order, string provider, string? workflowTypeId)
    {
        return new TokenUsage
        {
            Id = id,
            OrganizationId = ORGANIZATION_ID,
            WorkflowTypeId = workflowTypeId,
            WorkflowExecutionId = $"execution-{id}",
            SectionId = $"section-{id}",
            BlockId = $"block-{id}",
            DocumentId = $"document-{id}",
            PageId = $"page-{id}",
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = ExecutionContext.WorkflowExecution,
            Provider = provider,
            Family = provider == "cohere-api" ? "cohere" : "anthropic",
            ModelId = provider == "cohere-api" ? "embed-v4" : "claude-sonnet",
            Duration = TimeSpan.FromSeconds(3 + order),
            Description = $"Usage {order}",
            Metadata = "{\"ragType\":\"none\"}",
            ModelPricingId = "pricing-sonnet",
            CreatedAt = _created.AddMinutes(order),
            UpdatedAt = _created.AddMinutes(order),
        };
    }

    private static TokenUsageComponent BuildComponent(string id, string tokenUsageId, PricingComponentType type, int units, decimal cost)
    {
        return new TokenUsageComponent
        {
            Id = id,
            TokenUsageId = tokenUsageId,
            ComponentType = type,
            Units = units,
            Cost = cost,
            CreatedAt = _created,
            UpdatedAt = _created,
        };
    }

    private static ModelPricing BuildPricing(string id, string displayName, PricingType type, bool isActive, DateTime effectiveFrom)
    {
        return new ModelPricing
        {
            Id = id,
            Provider = "aws-bedrock",
            Family = type == PricingType.Embedding ? "cohere" : "anthropic",
            ModelId = $"model-{id}",
            ModelAlias = $"alias-{id}",
            DisplayName = displayName,
            PricingType = type,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveFrom.AddYears(1),
            IsActive = isActive,
            CreatedAt = _created,
            UpdatedAt = _created,
        };
    }

    private static OrganizationModelSettings BuildSettings(
        string id, string? organizationId, UsageContext context, string modelPricingId, bool isEnabled, int priority)
    {
        return new OrganizationModelSettings
        {
            Id = id,
            OrganizationId = organizationId,
            UsageContext = context,
            ModelPricingId = modelPricingId,
            IsEnabled = isEnabled,
            Priority = priority,
            CreatedAt = _created,
            UpdatedAt = _created,
        };
    }
}
