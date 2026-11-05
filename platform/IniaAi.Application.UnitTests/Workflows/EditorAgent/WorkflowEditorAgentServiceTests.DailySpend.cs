using ErrorOr;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

public sealed partial class WorkflowEditorAgentServiceTests
{
    [Fact]
    public async Task SendMessageAsync_OnceTodaysBatchIsSpent_WarnsWithTheBatch_AndStartsNothing()
    {
        _editor.DailySpendBatchUsd = 10m;
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 10m, DateTime.UtcNow);

        ErrorOr<EditorAgentTurnView> sent = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());

        Assert.Equal(EditorAgentErrors.DAILY_SPEND_USED_CODE, sent.FirstError.Code);
        Assert.Equal(10m, sent.FirstError.Metadata![EditorAgentErrors.BATCH_USD_METADATA_KEY]);
        Assert.Empty(_client.StartRequests);
    }

    [Fact]
    public async Task SendMessageAsync_CountsOnlyTodaysEditorSpendOfTheOrganization()
    {
        _editor.DailySpendBatchUsd = 10m;
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 50m, DateTime.UtcNow.Date.AddMinutes(-1));
        await SeedEditorSpendAsync(OTHER_ORG_ID, 50m, DateTime.UtcNow);
        await SeedSpendAsync(ORG_ID, 50m, DateTime.UtcNow, UsageType.LlmBlockExecution);
        await SeedEditorSpendAsync(ORG_ID, 9m, DateTime.UtcNow);

        ErrorOr<EditorAgentTurnView> sent = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());

        Assert.False(sent.IsError);
    }

    [Fact]
    public async Task SendMessageAsync_UsesTheOrganizationsOwnBatch_OverTheDefault()
    {
        _editor.DailySpendBatchUsd = 10m;
        await SetOrganizationBatchAsync(ORG_ID, 100m);
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 60m, DateTime.UtcNow);

        ErrorOr<EditorAgentTurnView> sent = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());

        Assert.False(sent.IsError);
    }

    [Fact]
    public async Task UnlockDailySpendAsync_AddsOneBatchForToday_ThenTheMessageSends()
    {
        _editor.DailySpendBatchUsd = 10m;
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 12m, DateTime.UtcNow);

        ErrorOr<Success> unlocked = await Service.UnlockDailySpendAsync(
            ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken);
        ErrorOr<EditorAgentTurnView> sent = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());

        Assert.False(unlocked.IsError);
        Assert.False(sent.IsError);
        EditorAgentSpendUnlock unlock = Assert.Single(await UnlocksAsync());
        Assert.Equal((ORG_ID, USER_ID, DateOnly.FromDateTime(DateTime.UtcNow)), (unlock.OrganizationId, unlock.UserId, unlock.Day));
        Assert.Equal((1, 1), (unlock.FromBatches, unlock.Batches));
    }

    [Fact]
    public async Task UnlockDailySpendAsync_WhenTheBatchesAreNotUsedUp_AddsNothing()
    {
        _editor.DailySpendBatchUsd = 10m;
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 2m, DateTime.UtcNow);

        ErrorOr<Success> unlocked = await Service.UnlockDailySpendAsync(
            ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken);

        Assert.False(unlocked.IsError);
        Assert.Empty(await UnlocksAsync());
    }

    [Fact]
    public async Task UnlockDailySpendAsync_AnotherUsersConversation_IsRefused()
    {
        _editor.DailySpendBatchUsd = 10m;
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 12m, DateTime.UtcNow);

        ErrorOr<Success> unlocked = await Service.UnlockDailySpendAsync(
            ORG_ID, OTHER_USER_ID, conversation.Id, TestContext.Current.CancellationToken);

        Assert.True(unlocked.IsError);
        Assert.Empty(await UnlocksAsync());
    }

    [Fact]
    public async Task SendMessageAsync_MessagesStillRunning_CountAtTheirCap_SoQueuedOnesCannotPassUnseen()
    {
        _holdRuns = true;
        _editor.DailySpendBatchUsd = 10m;
        _editor.MaxBudgetUsdPerMessage = 3m;
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 7m, DateTime.UtcNow);

        ErrorOr<EditorAgentTurnView> first = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());
        ErrorOr<EditorAgentTurnView> second = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());

        Assert.False(first.IsError);
        Assert.Equal(EditorAgentErrors.DAILY_SPEND_USED_CODE, second.FirstError.Code);
        Assert.Equal(10m, second.FirstError.Metadata![EditorAgentErrors.USED_USD_METADATA_KEY]);
    }

    [Fact]
    public async Task UnlockDailySpendAsync_DayWellPastItsBatch_UnlocksEnoughForOneMoreBatch()
    {
        _editor.DailySpendBatchUsd = 10m;
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 34m, DateTime.UtcNow);

        _ = await Service.UnlockDailySpendAsync(ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken);
        ErrorOr<EditorAgentTurnView> sent = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());

        Assert.False(sent.IsError);
        EditorAgentSpendUnlock unlock = Assert.Single(await UnlocksAsync());
        Assert.Equal((1, 3), (unlock.FromBatches, unlock.Batches));
    }

    [Fact]
    public async Task UnlockDailySpendAsync_TwoUnlocksFromTheSamePoint_AddTheBatchesOnce()
    {
        _editor.DailySpendBatchUsd = 10m;
        EditorAgentConversationView conversation = await StartAsync();
        await SeedEditorSpendAsync(ORG_ID, 12m, DateTime.UtcNow);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = context.EditorAgentSpendUnlocks.Add(new EditorAgentSpendUnlock
            {
                OrganizationId = ORG_ID,
                UserId = OTHER_USER_ID,
                Day = DateOnly.FromDateTime(DateTime.UtcNow),
                FromBatches = 1,
                Batches = 1,
            });
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = context.EditorAgentSpendUnlocks.Add(new EditorAgentSpendUnlock
            {
                OrganizationId = ORG_ID,
                UserId = USER_ID,
                Day = DateOnly.FromDateTime(DateTime.UtcNow),
                FromBatches = 1,
                Batches = 1,
            });
            _ = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    private Task SeedEditorSpendAsync(string organizationId, decimal costUsd, DateTime createdAt)
    {
        return SeedSpendAsync(organizationId, costUsd, createdAt, UsageType.LlmEditorAgentChat);
    }

    private async Task SeedSpendAsync(string organizationId, decimal costUsd, DateTime createdAt, UsageType usageType)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.TokenUsages.Add(new TokenUsage
        {
            OrganizationId = organizationId,
            UsageType = usageType,
            ModelId = SONNET,
            CreatedAt = createdAt,
            Components =
            [
                new TokenUsageComponent { ComponentType = PricingComponentType.AgentRunSpend, Units = 1, Cost = costUsd },
            ],
        });
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SetOrganizationBatchAsync(string organizationId, decimal batchUsd)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Organization organization = await context.Organizations.SingleAsync(
            organization => organization.Id == organizationId, TestContext.Current.CancellationToken);
        organization.EditorAgentDailySpendBatchUsd = batchUsd;
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<EditorAgentSpendUnlock>> UnlocksAsync()
    {
        using IServiceScope scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().EditorAgentSpendUnlocks
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
