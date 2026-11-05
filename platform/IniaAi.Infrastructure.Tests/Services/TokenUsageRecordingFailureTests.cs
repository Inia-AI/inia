using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// A usage record that fails to save is counted where an operator reads it.
/// </summary>
public class TokenUsageRecordingFailureTests
{
    [Fact]
    public async Task AUsageRecordThatFailsToSave_IsCountedForTheReadinessReport()
    {
        // A context disposed before the save stands for any save that throws.
        UserDataDbContext userData = new(new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"usage-failure-{Guid.NewGuid()}-userdata").Options);
        await using CoreDbContext core = new(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"usage-failure-{Guid.NewGuid()}-core").Options);
        await userData.DisposeAsync();

        ServiceCollection collection = new();
        _ = collection.AddSingleton<IUserDataDbContext>(userData);
        _ = collection.AddSingleton<ICoreDbContext>(core);
        await using ServiceProvider services = collection.BuildServiceProvider();
        var usage = new TokenUsageService(services, new MemoryCache(new MemoryCacheOptions()), NullLogger<TokenUsageService>.Instance);
        int before = TokenUsageRecordingFailures.Instance.Count;

        decimal cost = await usage.RecordLlmUsageAsync(
            new LlmUsageRecord
            {
                OrganizationId = "org-usage",
                WorkflowTypeId = "UpdateCounterpartyLastFinancialAnalysis",
                UsageType = UsageType.LlmBlockExecution,
                ExecutionContext = TokenExecutionContext.WorkflowExecution,
                Provider = "anthropic",
                Family = "anthropic",
                ModelId = "claude-sonnet-5",
                Components = [new UsageComponent { ComponentType = PricingComponentType.Input, Units = 10 }],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(0m, cost);
        Assert.True(TokenUsageRecordingFailures.Instance.Count > before);
    }
}
