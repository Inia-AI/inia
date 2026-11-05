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
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// End-to-end coverage of per-iteration counterparty placeholders: inside a loop body,
/// <c>{ListTitle.key…}</c> resolves against the counterparty named by that iteration's list item,
/// then walks the freeform bag value exactly as a block response is walked — JSON by path, XML-tagged
/// text by tag, and a bare key verbatim. Merge blocks only, so no model call is involved.
/// </summary>
public class LoopItemCounterpartyPlaceholderTests : IClassFixture<WorkflowsFixture<LoopItemCounterpartyPlaceholderTests>>
{
    private const string FUND_WITH_OWN_STYLE = "Loop Placeholder Akito Fund";
    private const string FUND_INHERITING_STYLE = "Loop Placeholder Maverick Fund";
    private const string UNKNOWN_FUND = "Loop Placeholder Unlisted Fund";
    private const string MANAGER_NAME = "Loop Placeholder Manager";

    private const string FUND_STYLE_JSON = "{\"currency\":\"JPY\",\"units\":\"millions\"}";
    private const string MANAGER_STYLE_JSON =
        "{\"currency\":\"GBP\",\"additionalInstructions\":\"NAV is prior-month close\"}";
    private const string FUND_NOTES_XML = "<summary>Solid quarter</summary><risk>FX exposure</risk>";

    private readonly WorkflowsFixture<LoopItemCounterpartyPlaceholderTests> _fixture;
    private readonly IServiceProvider _sp;

    public LoopItemCounterpartyPlaceholderTests(
        WorkflowsFixture<LoopItemCounterpartyPlaceholderTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task LoopBody_JsonBagValue_TraversesPerIteration()
    {
        await SeedCounterpartiesAsync();

        Workflow executed = await RunAsync(BuildLoopWorkflow($"{FUND_WITH_OWN_STYLE}\n{FUND_INHERITING_STYLE}"));

        Assert.Contains("Currency: JPY", GetResponse(executed, "Funds_body_0"));
        Assert.Contains("Currency: GBP", GetResponse(executed, "Funds_body_1"));
    }

    [Fact]
    public async Task LoopBody_BareListTitle_StillResolvesToTheFundName()
    {
        await SeedCounterpartiesAsync();

        Workflow executed = await RunAsync(BuildLoopWorkflow($"{FUND_WITH_OWN_STYLE}\n{FUND_INHERITING_STYLE}"));

        Assert.Contains($"Fund: {FUND_WITH_OWN_STYLE}", GetResponse(executed, "Funds_body_0"));
        Assert.Contains($"Fund: {FUND_INHERITING_STYLE}", GetResponse(executed, "Funds_body_1"));
    }

    [Fact]
    public async Task LoopBody_WholeBagValue_RendersVerbatim()
    {
        await SeedCounterpartiesAsync();

        Workflow executed = await RunAsync(BuildLoopWorkflow(FUND_WITH_OWN_STYLE));

        Assert.Contains($"Style: {FUND_STYLE_JSON}", GetResponse(executed, "Funds_body_0"));
    }

    [Fact]
    public async Task LoopBody_XmlTaggedBagValue_ExtractsTagPerIteration()
    {
        await SeedCounterpartiesAsync();

        Workflow executed = await RunAsync(BuildLoopWorkflow(FUND_WITH_OWN_STYLE));

        Assert.Contains("Notes: Solid quarter", GetResponse(executed, "Funds_body_0"));
    }

    [Fact]
    public async Task LoopBody_FundWithoutOwnStyle_InheritsFromItsManager()
    {
        await SeedCounterpartiesAsync();

        Workflow executed = await RunAsync(BuildLoopWorkflow(FUND_INHERITING_STYLE));

        Assert.Contains("Instructions: NAV is prior-month close", GetResponse(executed, "Funds_body_0"));
    }

    [Fact]
    public async Task LoopBody_UnknownFund_FallsBackToNoDataText()
    {
        await SeedCounterpartiesAsync();

        Workflow executed = await RunAsync(BuildLoopWorkflow(UNKNOWN_FUND));

        string noDataText = _sp.GetRequiredService<IOptions<PlatformSettings>>()
            .Value.IniaAi.NoDataForPlaceHolderText;
        string response = GetResponse(executed, "Funds_body_0");

        Assert.Contains($"Currency: {noDataText}", response);
        Assert.Contains($"Fund: {UNKNOWN_FUND}", response);
    }

    private static string GetResponse(Workflow workflow, string replacementTag)
    {
        Block block = workflow.Sections
            .SelectMany(s => s.Blocks)
            .Single(b => b.ReplacementTag == replacementTag);

        return block.BlockResponse?.RawResponse ?? string.Empty;
    }

    private static Workflow BuildLoopWorkflow(string loopedListTemplate)
    {
        Workflow root = new() { Id = "loop-cp-root", Title = "loop-cp" };
        Section section = new("Funds", root) { Id = "loop-cp-sec-0", Order = 0 };

        var loopStart = new Block(BlockTypes.LoopStart, section)
        {
            Properties = new LoopStartProperties
            {
                ListTitle = "Funds",
                LoopedListTemplate = loopedListTemplate,
            },
        };
        loopStart.StableBlockId = "loop-cp-loopstart";

        var body = new Block(BlockTypes.Merge, section, replacementTag: "body")
        {
            Properties = new MergeProperties
            {
                TextTemplate = """
                    Fund: {Funds}
                    Currency: {Funds.reportingStyle.currency}
                    Instructions: {Funds.reportingStyle.additionalInstructions}
                    Style: {Funds.reportingStyle}
                    Notes: {Funds.notes.summary}
                    """,
            },
        };
        Assert.NotNull(body);

        _ = new Block(BlockTypes.LoopEnd, section);

        return root;
    }

    private async Task<Workflow> RunAsync(Workflow workflow)
    {
        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        return await TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1).ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: _fixture.GetOrganization().Id,
            jobId: Guid.NewGuid().ToString(),
            uow,
            inputs: [],
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task SeedCounterpartiesAsync()
    {
        string organizationId = _fixture.GetOrganization().Id;

        IDbContextFactory<UserDataDbContext> dbFactory = _sp
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        if (await db.Counterparties.AnyAsync(c => c.Name == FUND_WITH_OWN_STYLE, TestContext.Current.CancellationToken))
        {
            return;
        }

        var manager = new Counterparty(organizationId, MANAGER_NAME)
        {
            Properties = new Dictionary<string, string> { ["reportingStyle"] = MANAGER_STYLE_JSON },
        };
        _ = db.Counterparties.Add(manager);

        _ = db.Counterparties.Add(new Counterparty(organizationId, FUND_WITH_OWN_STYLE)
        {
            ParentId = manager.Id,
            Properties = new Dictionary<string, string>
            {
                ["reportingStyle"] = FUND_STYLE_JSON,
                ["notes"] = FUND_NOTES_XML,
            },
        });

        _ = db.Counterparties.Add(new Counterparty(organizationId, FUND_INHERITING_STYLE)
        {
            ParentId = manager.Id,
        });

        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
