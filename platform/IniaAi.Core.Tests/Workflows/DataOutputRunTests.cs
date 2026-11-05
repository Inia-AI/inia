using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IniaAi.Core.Tests.Workflows.Scheduling.Helpers.VerifiedGroupRuns;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A run stores the JSON of each block sent as data beside the report, and fails a block sent as
/// data whose result is not JSON, saying why on its log row.
/// </summary>
public class DataOutputRunTests : IClassFixture<WorkflowsFixture<DataOutputRunTests>>
{
    private readonly IServiceProvider _sp;

    public DataOutputRunTests(WorkflowsFixture<DataOutputRunTests> fixture, ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task ABlockSentAsData_StoresItsJsonUnderItsName()
    {
        const string json = """{ "limit": 120000 }""";
        Workflow root = OneBlockWorkflow("data-json", json);

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);
        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        await using UserDataDbContext db = await _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        ReportDataOutput output = Assert.Single(await db.ReportDataOutputs.AsNoTracking()
            .Where(row => row.WorkflowReportId == handler.WorkflowExecutionId)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("facilities", output.Name);
        Assert.Equal(json, output.Json);
    }

    [Fact]
    public async Task ABlockSentAsDataWhoseResultIsNotJson_FailsWithTheReasonOnItsLogRow()
    {
        Workflow root = OneBlockWorkflow("data-text", "Facilities: overdraft of 120,000.");

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);
        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        WorkflowExecutionLog log = Assert.Single(ReadLogs(_sp, handler.WorkflowExecutionId!, "Facilities JSON"));
        Assert.Equal(BlockExecutionStatus.Failed, log.Status);
        Assert.Equal(
            "The block \"Facilities JSON\" sends its result to other systems as \"facilities\", but its result is not JSON. Make the block return JSON, then run it again.",
            log.Error);

        await using UserDataDbContext db = await _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.False(await db.ReportDataOutputs.AnyAsync(
            row => row.WorkflowReportId == handler.WorkflowExecutionId,
            TestContext.Current.CancellationToken));
    }

    private static Workflow OneBlockWorkflow(string id, string text)
    {
        Workflow root = new() { Id = $"{id}-root", Title = id };
        Section section = new("S", root) { Id = $"{id}-sec", Order = 0 };
        _ = new Block(BlockTypes.Merge, section, replacementTag: "facilities_json")
        {
            Id = $"{id}-block",
            Name = "Facilities JSON",
            DataOutputName = "facilities",
            Properties = new MergeProperties { TextTemplate = text },
        };

        return root;
    }
}
