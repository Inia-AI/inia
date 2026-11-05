using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Reruns;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A block flagged to update its counterparty writes its raw output back — to the
/// LastFinancialAnalysisResult column, or (output type Property) merged into the freeform Properties
/// bag under a configured key while leaving other keys intact. Regular execution only, where a
/// CounterpartyId is resolved — never the monitoring Funds loop, which carries no CounterpartyId.
/// A rerun writes nothing back, but the run itself still sees the value.
/// </summary>
public class CounterpartyWriteBackTests : IClassFixture<WorkflowsFixture<CounterpartyWriteBackTests>>
{
    private const string MERGE_OUTPUT = "MERGED WRITE-BACK OUTPUT";
    private const string READER_TAG = "reader";
    private const string STORED_FINANCIALS = "STORED FINANCIALS BEFORE THE RUN";

    private readonly WorkflowsFixture<CounterpartyWriteBackTests> _fixture;
    private readonly IServiceProvider _sp;

    public CounterpartyWriteBackTests(
        WorkflowsFixture<CounterpartyWriteBackTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task PropertyResultType_MergesRawResponseIntoBag_PreservingExistingKeys()
    {
        string counterpartyId = await SeedCounterpartyAsync(
            new Dictionary<string, string> { ["existing"] = "keep" });

        await RunAsync(counterpartyId, BuildWriteBackWorkflow(CounterpartyResultType.Property, "reportingStyle"));

        Counterparty reloaded = await ReloadAsync(counterpartyId);
        Assert.NotNull(reloaded.Properties);
        Assert.Equal(MERGE_OUTPUT, reloaded.Properties!["reportingStyle"]);
        Assert.Equal("keep", reloaded.Properties!["existing"]);
        Assert.Null(reloaded.LastFinancialAnalysisResult);
    }

    [Fact]
    public async Task PropertyResultType_KeyDiffersOnlyByCase_UpdatesExistingKeyInPlace()
    {
        string counterpartyId = await SeedCounterpartyAsync(
            new Dictionary<string, string> { ["reportingStyle"] = "old" });

        // The block writes "ReportingStyle" (different case) — it must update the existing
        // "reportingStyle" entry, not add a second case-variant key.
        await RunAsync(counterpartyId, BuildWriteBackWorkflow(CounterpartyResultType.Property, "ReportingStyle"));

        Counterparty reloaded = await ReloadAsync(counterpartyId);
        Assert.NotNull(reloaded.Properties);
        Assert.Single(reloaded.Properties!);
        Assert.Equal(MERGE_OUTPUT, reloaded.Properties!["reportingStyle"]);
    }

    [Fact]
    public async Task LastFinancialAnalysisResultType_WritesColumn_AndLeavesBagUntouched()
    {
        string counterpartyId = await SeedCounterpartyAsync(
            new Dictionary<string, string> { ["existing"] = "keep" });

        await RunAsync(
            counterpartyId,
            BuildWriteBackWorkflow(CounterpartyResultType.LastFinancialAnalysisResult, key: null));

        Counterparty reloaded = await ReloadAsync(counterpartyId);
        Assert.Equal(MERGE_OUTPUT, reloaded.LastFinancialAnalysisResult);
        Assert.NotNull(reloaded.Properties);
        Assert.False(reloaded.Properties!.ContainsKey("reportingStyle"));
        Assert.Equal("keep", reloaded.Properties!["existing"]);
    }

    [Fact]
    public async Task ARerun_LeavesTheStoredFinancials_AndALaterBlockOfTheRunStillReadsTheNewValue()
    {
        string counterpartyId = await SeedCounterpartyAsync(
            new Dictionary<string, string> { ["existing"] = "keep" },
            lastFinancialAnalysisResult: STORED_FINANCIALS);
        Workflow workflow = BuildWriteBackWorkflow(CounterpartyResultType.LastFinancialAnalysisResult, key: null);
        AddReader(workflow, "{Counterparty.LastFinancialAnalysisResult}");

        Workflow executed = await RunAsync(counterpartyId, workflow, isRerun: true);

        Counterparty reloaded = await ReloadAsync(counterpartyId);
        Assert.Equal(STORED_FINANCIALS, reloaded.LastFinancialAnalysisResult);
        Assert.Equal(MERGE_OUTPUT, ReaderOutput(executed));
    }

    [Fact]
    public async Task ARerun_LeavesTheStoredPropertyBag_AndALaterBlockOfTheRunStillReadsTheNewKey()
    {
        string counterpartyId = await SeedCounterpartyAsync(
            new Dictionary<string, string> { ["reportingStyle"] = "old" });
        Workflow workflow = BuildWriteBackWorkflow(CounterpartyResultType.Property, "reportingStyle");
        AddReader(workflow, "{Counterparty.reportingStyle}");

        Workflow executed = await RunAsync(counterpartyId, workflow, isRerun: true);

        Counterparty reloaded = await ReloadAsync(counterpartyId);
        Assert.Equal("old", reloaded.Properties!["reportingStyle"]);
        Assert.Equal(MERGE_OUTPUT, ReaderOutput(executed));
    }

    [Fact]
    public async Task AReport_OnARootExecution_StillWritesTheStoredFinancials()
    {
        string counterpartyId = await SeedCounterpartyAsync([], lastFinancialAnalysisResult: STORED_FINANCIALS);

        await RunAsync(
            counterpartyId,
            BuildWriteBackWorkflow(CounterpartyResultType.LastFinancialAnalysisResult, key: null),
            isRerun: false);

        Assert.Equal(MERGE_OUTPUT, (await ReloadAsync(counterpartyId)).LastFinancialAnalysisResult);
    }

    private static void AddReader(Workflow workflow, string template)
    {
        Section section = new("Reader", workflow) { Id = "wb-sec-1", Order = 1 };

        _ = new Block(BlockTypes.Merge, section, replacementTag: READER_TAG)
        {
            Properties = new MergeProperties { TextTemplate = template },
        };
    }

    private static string? ReaderOutput(Workflow executed)
    {
        return executed.Sections
            .SelectMany(static section => section.Blocks)
            .Single(static block => block.ReplacementTag == READER_TAG)
            .BlockResponse?.RawResponse;
    }

    private static Workflow BuildWriteBackWorkflow(CounterpartyResultType resultType, string? key)
    {
        Workflow root = new() { Id = "wb-root", Title = "wb" };
        Section section = new("Writer", root) { Id = "wb-sec-0", Order = 0 };

        var writer = new Block(BlockTypes.Merge, section, replacementTag: "writer")
        {
            Properties = new MergeProperties { TextTemplate = MERGE_OUTPUT },
            ShouldUpdateCounterpartyWithResult = true,
            CounterpartyResultType = resultType,
            UpdateCounterpartyPropertyKey = key,
        };
        Assert.NotNull(writer);

        return root;
    }

    private async Task<Workflow> RunAsync(string counterpartyId, Workflow workflow, bool? isRerun = null)
    {
        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);
        handler.CounterpartyId = counterpartyId;
        string jobId = Guid.NewGuid().ToString();

        if (isRerun is { } rerun)
        {
            var execution = new WorkflowExecution
            {
                OrganizationId = _fixture.GetOrganization().Id,
                CounterpartyId = counterpartyId,
                JobId = jobId,
                WorkflowState = WorkflowState.Queued,
            };
            await uow.Repository<WorkflowExecution>().AddAsync(execution);
            _ = await uow.CompleteAsync(TestContext.Current.CancellationToken);

            await handler.CreateRootOutputAsync(uow, jobId, workflow, _fixture.GetOrganization().Id, userId: null, workflowExecutionId: execution.Id);
        }

        using IDisposable rerunWrites = RerunWrites.Scope(isRerun == true);

        return await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: _fixture.GetOrganization().Id,
            jobId: jobId,
            uow,
            inputs: [],
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<string> SeedCounterpartyAsync(Dictionary<string, string> properties, string? lastFinancialAnalysisResult = null)
    {
        IDbContextFactory<UserDataDbContext> dbFactory = _sp
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        var counterparty = new Counterparty(_fixture.GetOrganization().Id, "Write-Back Fund")
        {
            Properties = properties,
            LastFinancialAnalysisResult = lastFinancialAnalysisResult,
        };
        _ = db.Counterparties.Add(counterparty);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return counterparty.Id;
    }

    private async Task<Counterparty> ReloadAsync(string counterpartyId)
    {
        IDbContextFactory<UserDataDbContext> dbFactory = _sp
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        return await db.Counterparties
            .AsNoTracking()
            .SingleAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken);
    }
}
