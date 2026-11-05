using System.Diagnostics;
using ErrorOr;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent.Evals;

/// <summary>
/// The editor agent's eval set, run against a live agent runtime and billed, so it runs
/// only with the paid-API opt-in, its own opt-in and a runtime address
/// (<see cref="EditorAgentEvalHost.RequireLive"/>), and never in CI. Each case is one test, so
/// the pass rate is the run's pass rate. Every case that starts is recorded in
/// <see cref="EditorAgentEvalReport"/>'s folder, a case that throws as failed with the error and
/// what it spent.
/// </summary>
public sealed class EditorAgentEvalTests(EditorAgentEvalReport report, ITestOutputHelper output)
    : IClassFixture<EditorAgentEvalReport>
{
    public static TheoryData<string> CaseIds => [.. EditorAgentEvalCases.All.Select(static evalCase => evalCase.Id)];

    [PaidApiTheory("the editor agent on a live agent runtime, about $1.20 for the whole set on Sonnet 5")]
    [MemberData(nameof(CaseIds))]
    public async Task EditorAgent_HandlesTheCase(string caseId)
    {
        EditorAgentEvalCase evalCase = EditorAgentEvalCases.All.Single(candidate => candidate.Id == caseId);
        await using EditorAgentEvalHost host = EditorAgentEvalHost.RequireLive(caseId);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = Stopwatch.StartNew();
        List<string> failures = [];
        List<EditorAgentTurnView> turns = [];
        string? conversationId = null;

        try
        {
            IReadOnlyList<string> reportIds = evalCase.AttachesReport
                ? [await host.SeedReportAsync(EditorAgentEvalCases.REPORT_COUNTERPARTY, EditorAgentEvalCases.REPORT_BLOCK, EditorAgentEvalCases.REPORT_ANSWER, cancellationToken)]
                : [];
            conversationId = (await host.StartAsync(cancellationToken)).Id;
            Workflow workflow = EditorAgentEvalWorkflow.Build();
            foreach (string message in evalCase.Messages)
            {
                turns.Add(await host.AskAsync(conversationId, message, workflow, turns.Count == 0 ? reportIds : [], cancellationToken));
            }

            foreach (EditorAgentTurnView turn in turns.Where(static turn => turn.Status != AgentConversationTurnStatus.Completed))
            {
                failures.Add($"Turn {turn.Number} ended {turn.Status}: {turn.Error}");
            }

            if (failures.Count == 0)
            {
                EditorAgentEvalGrading.ExpectLinksResolve(turns, workflow, failures);
                EditorAgentEvalGrading.ExpectLinkTextsMatch(turns, workflow, failures);
                await evalCase.Grade(new EditorAgentEvalOutcome(host, workflow, reportIds, turns, host.StagedDocumentNames(conversationId, turns[0].Number)), failures);
            }
        }
        catch (Exception exception)
        {
            failures.Add($"The case threw {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            clock.Stop();
            var result = new EditorAgentEvalResult(
                evalCase.Id,
                evalCase.Category,
                host.Model,
                failures.Count == 0,
                failures,
                await SpentAsync(host, conversationId, turns),
                Math.Round(clock.Elapsed.TotalSeconds, 1),
                turns.Count == 0 ? 0 : turns.Max(static turn => turn.Proposal?.Rounds ?? 0),
                turns.Count == 0 ? string.Empty : turns[^1].AnswerMarkdown ?? string.Empty);
            report.Record(result);
            output.WriteLine($"{result.CaseId}: {(result.Passed ? "pass" : "FAIL")} ${result.CostUsd:0.000} {result.Seconds}s");
            foreach (EditorAgentTurnView turn in turns)
            {
                output.WriteLine($"--- turn {turn.Number} answer ---\n{turn.AnswerMarkdown}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// What the conversation cost, read from the conversation so a turn the case never read back
    /// still counts; the turns read so far when the conversation cannot be read. A best-effort
    /// read: whatever it throws, the case is still recorded.
    /// </summary>
    private static async Task<decimal> SpentAsync(EditorAgentEvalHost host, string? conversationId, List<EditorAgentTurnView> turns)
    {
        decimal fromTurns = turns.Sum(static turn => turn.CostUsd);
        if (conversationId is null)
        {
            return fromTurns;
        }

        try
        {
            ErrorOr<EditorAgentConversationView> conversation = await host.Service.GetConversationAsync(
                EditorAgentEvalHost.ORG_ID, EditorAgentEvalHost.USER_ID, conversationId, CancellationToken.None);
            return conversation.IsError ? fromTurns : Math.Max(conversation.Value.TotalCostUsd, fromTurns);
        }
        catch (Exception)
        {
            return fromTurns;
        }
    }
}
