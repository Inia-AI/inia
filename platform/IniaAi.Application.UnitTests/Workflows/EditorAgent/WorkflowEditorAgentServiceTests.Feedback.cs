using ErrorOr;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

/// <summary>
/// The "Use feedback" button: the open report feedback the user chooses goes to the agent with
/// the runs that hold it, a save of a change the agent names for an item marks the item addressed, and an item the
/// agent suggests dismissing is dismissed only when the user presses Dismiss.
/// </summary>
public sealed partial class WorkflowEditorAgentServiceTests
{
    private const string FEEDBACK_MESSAGE = "Fix the feedback I chose on this workflow's reports.";
    private const string WRONG_TOTAL = "The total should be in millions, not thousands.";
    private const string MISSING_SEGMENT = "The segment table leaves out the services segment.";
    private const string TASTE = "I would prefer shorter sentences here.";
    private const string THEIR_COMMENT = "The rating rationale repeats itself.";

    [Fact]
    public async Task CountOpenFeedbackAsync_CountsTheOrganizationsOpenItemsOfTheType_AndEveryOrganizationsForASuperAdmin()
    {
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1);
        string otherType = await SeedReportAsync(ORG_ID, OTHER_TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string deleted = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1, deleted: true);
        _ = await SeedFeedbackAsync(ORG_ID, mine, WRONG_TOTAL);
        _ = await SeedFeedbackAsync(ORG_ID, mine, TASTE, ReportFeedbackStatus.Dismissed);
        _ = await SeedFeedbackAsync(OTHER_ORG_ID, theirs, THEIR_COMMENT);
        _ = await SeedFeedbackAsync(ORG_ID, otherType, WRONG_TOTAL, workflowTypeId: OTHER_TYPE);
        _ = await SeedFeedbackAsync(ORG_ID, deleted, WRONG_TOTAL);

        ErrorOr<int> editor = await Service.CountOpenFeedbackAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);
        ErrorOr<int> superAdmin = await Service.CountOpenFeedbackAsync(ORG_ID, SUPER_ADMIN_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);
        ErrorOr<int> plain = await Service.CountOpenFeedbackAsync(ORG_ID, PLAIN_USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);

        Assert.Equal(1, editor.Value);
        Assert.Equal(2, superAdmin.Value);
        Assert.Equal(EditorAgentErrors.FORBIDDEN_CODE, plain.FirstError.Code);
    }

    /// <summary>
    /// The user picked four reports, so one report slot is left: it goes to the run with the most
    /// open items. The other run's item is listed as not staged, and another organization's item
    /// is not sent to an editor at all.
    /// </summary>
    [Fact]
    public async Task UseFeedback_ListsTheOpenItems_AndStagesTheRunWithTheMostInTheSlotsThePicksLeave()
    {
        List<string> picked = [];
        for (int index = 0; index < EditorAgentReports.MAX_PICKED - 1; index++)
        {
            picked.Add(await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, INITECH, WorkflowState.Draft, daysAgo: 5 + index));
        }

        string busy = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 3);
        string quiet = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1);
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1);
        string total = await SeedFeedbackAsync(ORG_ID, busy, WRONG_TOTAL, quotedText: "Revenue 12,400 thousand");
        string segment = await SeedFeedbackAsync(ORG_ID, busy, MISSING_SEGMENT);
        string taste = await SeedFeedbackAsync(ORG_ID, quiet, TASTE);
        string their = await SeedFeedbackAsync(OTHER_ORG_ID, theirs, THEIR_COMMENT);
        EditorAgentConversationView conversation = await StartAsync();

        EditorAgentTurnView turn = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, FEEDBACK_MESSAGE, SONNET, EditorAgentWorkflows.Build())
            {
                ReportIds = picked,
                FeedbackItemIds = [total, segment, taste, their],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, turn.Number);

        Assert.Equal(3, turn.FeedbackSent);
        Assert.Equal(3, (await ReadTurnAsync(conversation.Id)).FeedbackSent);
        AgentRunStartRequest run = Assert.Single(_client.StartRequests);
        Assert.Contains("read docs/feedback.md", run.Prompt, StringComparison.Ordinal);
        string list = DocumentText(run, EditorAgentFeedback.DOCUMENT);
        Assert.Contains("3 open items.", list, StringComparison.Ordinal);
        Assert.Contains($"## {total}", list, StringComparison.Ordinal);
        Assert.Contains($"## {segment}", list, StringComparison.Ordinal);
        Assert.Contains($"## {taste}", list, StringComparison.Ordinal);
        Assert.Contains($"- Comment: {WRONG_TOTAL}", list, StringComparison.Ordinal);
        Assert.Contains("- Quoted: \"Revenue 12,400 thousand\"", list, StringComparison.Ordinal);
        Assert.Contains($"- Report: {busy} (staged whole as report-{busy}.md)", list, StringComparison.Ordinal);
        Assert.Contains($"- Report: {quiet} (not staged", list, StringComparison.Ordinal);
        Assert.DoesNotContain(their, list, StringComparison.Ordinal);
        Assert.Contains(run.Documents, document => document.Name == EditorAgentReports.RunDocumentName(busy));
        Assert.DoesNotContain(run.Documents, document => document.Name.Contains(quiet, StringComparison.Ordinal));
        Assert.DoesNotContain(run.Documents, document => document.Name.Contains(theirs, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UseFeedback_ForASuperAdmin_StagesAnotherOrganizationsRunFromThatOrganization()
    {
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1);
        string their = await SeedFeedbackAsync(OTHER_ORG_ID, theirs, THEIR_COMMENT);
        EditorAgentConversationView conversation = (await Service.GetOrStartConversationAsync(
            ORG_ID, SUPER_ADMIN_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;

        EditorAgentTurnView turn = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, SUPER_ADMIN_ID, conversation.Id, FEEDBACK_MESSAGE, SONNET, EditorAgentWorkflows.Build())
            {
                FeedbackItemIds = [their],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, turn.Number);

        AgentRunStartRequest run = Assert.Single(_client.StartRequests);
        Assert.Contains($"## {their}", DocumentText(run, EditorAgentFeedback.DOCUMENT), StringComparison.Ordinal);
        Assert.Equal(EditorAgentWorkflows.TYPE, (string?)JObject.Parse(DocumentText(run, EditorAgentReports.RunDocumentName(theirs)))["WorkflowTypeId"]);
    }

    [Fact]
    public async Task UseFeedback_WithNothingOpen_IsRefused_AndStartsNothing()
    {
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string dismissed = await SeedFeedbackAsync(ORG_ID, mine, TASTE, ReportFeedbackStatus.Dismissed);
        EditorAgentConversationView conversation = await StartAsync();

        ErrorOr<EditorAgentTurnView> sent = await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, FEEDBACK_MESSAGE, SONNET, EditorAgentWorkflows.Build())
            {
                FeedbackItemIds = [dismissed],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(EditorAgentErrors.NO_OPEN_FEEDBACK_CODE, sent.FirstError.Code);
        Assert.Empty(_client.StartRequests);
        Assert.Empty((await Service.GetConversationAsync(ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken)).Value.Turns);
    }

    /// <summary>
    /// The agent names two items for its change: one the conversation was sent, which the save
    /// marks addressed in the saved version, and one it was never sent, which stays open. An item
    /// sent but not named stays open too.
    /// </summary>
    [Fact]
    public async Task SavingAChangeThatNamesAnItem_MarksItAddressedInTheSavedVersion_AndNothingElse()
    {
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1);
        string named = await SeedFeedbackAsync(ORG_ID, mine, WRONG_TOTAL);
        string notNamed = await SeedFeedbackAsync(ORG_ID, mine, MISSING_SEGMENT);
        string neverSent = await SeedFeedbackAsync(OTHER_ORG_ID, theirs, THEIR_COMMENT);
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject summary = Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG);
        summary["changes"]![0]!["feedback"] = new JArray(named, neverSent);
        AgentWrites(RunId(conversation.Id, 1), Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT), summary);

        EditorAgentTurnView sent = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, FEEDBACK_MESSAGE, SONNET, workflow)
            {
                FeedbackItemIds = [named, notNamed],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, sent.Number);
        _ = await Service.AcceptChangesAsync(
            new EditorAgentAcceptRequest(ORG_ID, USER_ID, sent.Id, null, workflow), TestContext.Current.CancellationToken);
        EditorAgentSaveNote note = (await Service.PeekSaveNoteAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value!;
        ErrorOr<Success> committed = await Service.CommitSaveNoteAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, note, "version-7", TestContext.Current.CancellationToken);

        Assert.False(committed.IsError);
        ReportFeedbackItem addressed = await ReadFeedbackAsync(named);
        Assert.Equal(ReportFeedbackStatus.Addressed, addressed.Status);
        Assert.Equal("version-7", addressed.AddressedInVersionId);
        Assert.Equal(USER_ID, addressed.StatusChangedByUserId);
        Assert.Equal(ReportFeedbackStatus.Open, (await ReadFeedbackAsync(notNamed)).Status);
        Assert.Equal(ReportFeedbackStatus.Open, (await ReadFeedbackAsync(neverSent)).Status);
        Assert.Equal(1, (await Service.CountOpenFeedbackAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task ASuggestedDismissal_ShowsWithTheItem_AndDismissesOnlyWhenTheUserPressesDismiss()
    {
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string taste = await SeedFeedbackAsync(ORG_ID, mine, TASTE);
        string total = await SeedFeedbackAsync(ORG_ID, mine, WRONG_TOTAL);
        EditorAgentConversationView conversation = await StartAsync();
        AgentWritesOut(RunId(conversation.Id, 1), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EditorAgentFeedback.DISMISS_FILE] = new JObject
            {
                ["dismiss"] = new JArray(
                    new JObject { ["id"] = taste, ["reason"] = "A matter of style, which the workflow leaves to the analyst." },
                    new JObject { ["id"] = "not-a-sent-item", ["reason"] = "Unknown." }),
            }.ToString(),
        });

        EditorAgentTurnView sent = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, FEEDBACK_MESSAGE, SONNET, EditorAgentWorkflows.Build())
            {
                FeedbackItemIds = [taste, total],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, sent.Number);

        EditorAgentDismissSuggestionView suggestion = Assert.Single((await ReadTurnAsync(conversation.Id)).DismissSuggestions);
        Assert.Equal((taste, TASTE, ReportFeedbackStatus.Open), (suggestion.FeedbackItemId, suggestion.Comment, suggestion.Status));
        Assert.Equal(ReportFeedbackStatus.Open, (await ReadFeedbackAsync(taste)).Status);

        ErrorOr<Success> notSuggested = await Service.DismissFeedbackAsync(ORG_ID, USER_ID, sent.Id, total, TestContext.Current.CancellationToken);
        ErrorOr<Success> anotherUser = await Service.DismissFeedbackAsync(ORG_ID, OTHER_USER_ID, sent.Id, taste, TestContext.Current.CancellationToken);
        ErrorOr<Success> dismissed = await Service.DismissFeedbackAsync(ORG_ID, USER_ID, sent.Id, taste, TestContext.Current.CancellationToken);

        Assert.Equal(EditorAgentErrors.FEEDBACK_NOT_SUGGESTED_CODE, notSuggested.FirstError.Code);
        Assert.Equal(EditorAgentErrors.FEEDBACK_NOT_SUGGESTED_CODE, anotherUser.FirstError.Code);
        Assert.False(dismissed.IsError);
        ReportFeedbackItem item = await ReadFeedbackAsync(taste);
        Assert.Equal(ReportFeedbackStatus.Dismissed, item.Status);
        Assert.Equal(suggestion.Reason, item.DismissReason);
        Assert.Equal(ReportFeedbackStatus.Open, (await ReadFeedbackAsync(total)).Status);
        Assert.Equal(ReportFeedbackStatus.Dismissed, Assert.Single((await ReadTurnAsync(conversation.Id)).DismissSuggestions).Status);
    }

    [Fact]
    public async Task ListOpenFeedbackAsync_ListsTheOpenItemsTheCountCounts_NewestFirst()
    {
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1);
        string older = await SeedFeedbackAsync(ORG_ID, mine, WRONG_TOTAL, quotedText: "Revenue 12,400 thousand", daysAgo: 2);
        string newer = await SeedFeedbackAsync(ORG_ID, mine, MISSING_SEGMENT);
        _ = await SeedFeedbackAsync(ORG_ID, mine, TASTE, ReportFeedbackStatus.Dismissed);
        string their = await SeedFeedbackAsync(OTHER_ORG_ID, theirs, THEIR_COMMENT);

        IReadOnlyList<EditorAgentFeedbackOption> editor = (await Service.ListOpenFeedbackAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;
        IReadOnlyList<EditorAgentFeedbackOption> superAdmin = (await Service.ListOpenFeedbackAsync(
            ORG_ID, SUPER_ADMIN_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;

        Assert.Equal([newer, older], editor.Select(static item => item.Id));
        Assert.Equal((WRONG_TOTAL, "Revenue 12,400 thousand"), (editor[1].Comment, editor[1].QuotedText));
        Assert.Equal(new[] { newer, older, their }.Order(StringComparer.Ordinal), superAdmin.Select(static item => item.Id).Order(StringComparer.Ordinal));
        Assert.Equal(older, superAdmin[^1].Id);
    }

    /// <summary>
    /// The user chose one of three open items: the agent gets only that one, told how many are
    /// open, and only that item's run is staged.
    /// </summary>
    [Fact]
    public async Task UseFeedback_SendsOnlyTheChosenItems()
    {
        string busy = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 3);
        string quiet = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1);
        _ = await SeedFeedbackAsync(ORG_ID, busy, WRONG_TOTAL);
        _ = await SeedFeedbackAsync(ORG_ID, busy, MISSING_SEGMENT);
        string chosen = await SeedFeedbackAsync(ORG_ID, quiet, TASTE);
        EditorAgentConversationView conversation = await StartAsync();

        EditorAgentTurnView turn = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, FEEDBACK_MESSAGE, SONNET, EditorAgentWorkflows.Build())
            {
                FeedbackItemIds = [chosen],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, turn.Number);

        Assert.Equal(1, turn.FeedbackSent);
        AgentRunStartRequest run = Assert.Single(_client.StartRequests);
        string list = DocumentText(run, EditorAgentFeedback.DOCUMENT);
        Assert.Contains("3 open items; these are the 1 the user chose.", list, StringComparison.Ordinal);
        Assert.Contains($"## {chosen}", list, StringComparison.Ordinal);
        Assert.DoesNotContain(WRONG_TOTAL, list, StringComparison.Ordinal);
        Assert.Contains(run.Documents, document => document.Name == EditorAgentReports.RunDocumentName(quiet));
        Assert.DoesNotContain(run.Documents, document => document.Name.Contains(busy, StringComparison.Ordinal));
    }

    /// <summary>
    /// Delete reaches exactly what the "Choose feedback" list may show the caller: an editor's
    /// own organization's items of the type, whatever their status, and a super admin's of every
    /// organization. Anything else is not found and stays.
    /// </summary>
    [Fact]
    public async Task DeleteFeedbackAsync_DeletesAnItemTheListMayShow_AndNothingElse()
    {
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string theirs = await SeedReportAsync(OTHER_ORG_ID, EditorAgentWorkflows.TYPE, GLOBEX, WorkflowState.Draft, daysAgo: 1);
        string otherType = await SeedReportAsync(ORG_ID, OTHER_TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string open = await SeedFeedbackAsync(ORG_ID, mine, WRONG_TOTAL);
        string dismissed = await SeedFeedbackAsync(ORG_ID, mine, TASTE, ReportFeedbackStatus.Dismissed);
        string their = await SeedFeedbackAsync(OTHER_ORG_ID, theirs, THEIR_COMMENT);
        string ofOtherType = await SeedFeedbackAsync(ORG_ID, otherType, MISSING_SEGMENT, workflowTypeId: OTHER_TYPE);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        ErrorOr<Deleted> plain = await Service.DeleteFeedbackAsync(ORG_ID, PLAIN_USER_ID, EditorAgentWorkflows.TYPE, open, cancellationToken);
        ErrorOr<Deleted> anotherOrganization = await Service.DeleteFeedbackAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, their, cancellationToken);
        ErrorOr<Deleted> anotherType = await Service.DeleteFeedbackAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, ofOtherType, cancellationToken);
        ErrorOr<Deleted> unknown = await Service.DeleteFeedbackAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, "no-such-item", cancellationToken);

        Assert.Equal(EditorAgentErrors.FORBIDDEN_CODE, plain.FirstError.Code);
        Assert.Equal(EditorAgentErrors.FEEDBACK_NOT_FOUND_CODE, anotherOrganization.FirstError.Code);
        Assert.Equal(EditorAgentErrors.FEEDBACK_NOT_FOUND_CODE, anotherType.FirstError.Code);
        Assert.Equal(EditorAgentErrors.FEEDBACK_NOT_FOUND_CODE, unknown.FirstError.Code);
        Assert.Equal(4, await CountFeedbackRowsAsync());

        Assert.False((await Service.DeleteFeedbackAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, open, cancellationToken)).IsError);
        Assert.False((await Service.DeleteFeedbackAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, dismissed, cancellationToken)).IsError);
        Assert.False((await Service.DeleteFeedbackAsync(ORG_ID, SUPER_ADMIN_ID, EditorAgentWorkflows.TYPE, their, cancellationToken)).IsError);

        Assert.Equal(1, await CountFeedbackRowsAsync());
        Assert.Equal(0, (await Service.CountOpenFeedbackAsync(ORG_ID, SUPER_ADMIN_ID, EditorAgentWorkflows.TYPE, cancellationToken)).Value);
    }

    /// <summary>
    /// An item the agent named for its change is deleted before the save. The message still shows
    /// what it was sent, and the save skips the missing item quietly and marks the rest.
    /// </summary>
    [Fact]
    public async Task ACitedItemDeletedBeforeTheSave_IsSkippedQuietly()
    {
        string mine = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        string deleted = await SeedFeedbackAsync(ORG_ID, mine, WRONG_TOTAL);
        string kept = await SeedFeedbackAsync(ORG_ID, mine, MISSING_SEGMENT);
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        string draft = workflow.ToExportJson(includeRunTraces: true);
        JObject summary = Summary(workflow, EditorAgentWorkflows.PRODUCER_TAG);
        summary["changes"]![0]!["feedback"] = new JArray(deleted, kept);
        AgentWrites(RunId(conversation.Id, 1), Edited(draft, EditorAgentWorkflows.PRODUCER_TAG, "UserPromptTemplate", NEW_PRODUCER_PROMPT), summary);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        EditorAgentTurnView sent = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, FEEDBACK_MESSAGE, SONNET, workflow)
            {
                FeedbackItemIds = [deleted, kept],
            },
            cancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, sent.Number);
        Assert.False((await Service.DeleteFeedbackAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, deleted, cancellationToken)).IsError);
        _ = await Service.AcceptChangesAsync(new EditorAgentAcceptRequest(ORG_ID, USER_ID, sent.Id, null, workflow), cancellationToken);
        EditorAgentSaveNote note = (await Service.PeekSaveNoteAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, cancellationToken)).Value!;
        ErrorOr<Success> committed = await Service.CommitSaveNoteAsync(ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, note, "version-8", cancellationToken);

        Assert.False(committed.IsError);
        Assert.Equal(ReportFeedbackStatus.Addressed, (await ReadFeedbackAsync(kept)).Status);
        Assert.Equal(2, (await ReadTurnAsync(conversation.Id)).FeedbackSent);
    }

    [Fact]
    public async Task UseFeedback_WithMoreThanTheCap_IsRefused()
    {
        EditorAgentConversationView conversation = await StartAsync();

        ErrorOr<EditorAgentTurnView> sent = await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, FEEDBACK_MESSAGE, SONNET, EditorAgentWorkflows.Build())
            {
                FeedbackItemIds = [.. Enumerable.Range(0, EditorAgentFeedback.MAX_SENT + 1).Select(static index => $"item-{index}")],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(EditorAgentErrors.TOO_MANY_FEEDBACK_CODE, sent.FirstError.Code);
        Assert.Empty(_client.StartRequests);
    }

    private async Task<string> SeedFeedbackAsync(
        string organizationId,
        string executionId,
        string comment,
        ReportFeedbackStatus status = ReportFeedbackStatus.Open,
        string? quotedText = null,
        string workflowTypeId = EditorAgentWorkflows.TYPE,
        int daysAgo = 0)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var item = ReportFeedbackItem.Create(
            organizationId, executionId, workflowTypeId, quotedText, ReportFeedbackCategory.WrongFigure, comment, "reader");
        item.CreatedAt = item.CreatedAt.AddDays(-daysAgo);
        if (status != ReportFeedbackStatus.Open)
        {
            item.SetStatus(status, "Seeded.", "reader");
        }

        _ = context.ReportFeedbackItems.Add(item);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return item.Id;
    }

    private async Task<int> CountFeedbackRowsAsync()
    {
        using IServiceScope scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserDataDbContext>()
            .ReportFeedbackItems
            .CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task<ReportFeedbackItem> ReadFeedbackAsync(string id)
    {
        using IServiceScope scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserDataDbContext>()
            .ReportFeedbackItems
            .AsNoTracking()
            .SingleAsync(item => item.Id == id, TestContext.Current.CancellationToken);
    }
}
