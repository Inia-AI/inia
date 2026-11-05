using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Core.Documents;
using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;
using System.Text;

namespace IniaAi.Core.Rag.Strategies;

internal class RagAgentRagStrategy : IRagStrategy
{
    public async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(
        RagExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Logger.LogInformation(
            "Executing data retrieval using '{name}'.",
            GetType().Name);

        RagSettingsExecutionState executionState = context.RagSettings.ExecutionState
            ?? new RagSettingsExecutionState(context.RagSettings);

        // Validate query template
        string? queryTemplate = executionState.QueryTemplate;
        if (string.IsNullOrWhiteSpace(queryTemplate))
        {
            context.Logger.LogError("Query template is required for RAG Agent strategy");
            throw new InvalidOperationException("Query template is required for RAG Agent strategy");
        }

        // Parse iteration limit (1-10, default 3)
        int iterationLimit = int.TryParse(executionState.IterationLimit, out int limit)
            ? Math.Max(1, Math.Min(limit, 10))
            : 3;

        // Parse context limit (1-100, default 20)
        int contextLimit = int.TryParse(executionState.ContextLimit, out int ctxLimit)
            ? Math.Max(1, Math.Min(ctxLimit, 100))
            : 20;

        context.Logger.LogDebug(
            "RAG Agent configuration: IterationLimit={iterationLimit}, ContextLimit={contextLimit}",
            iterationLimit,
            contextLimit);

        if (context.Pages.Count == 0)
        {
            context.Logger.LogWarning("No pages available for RAG Agent analysis");
            return [];
        }

        // Check if pages have overviews
        bool hasOverviews = context.Pages.Any(p => !string.IsNullOrWhiteSpace(p.Overview));
        if (!hasOverviews)
        {
            context.Logger.LogError("Pages must have Overview field populated for RAG Agent strategy");
            throw new InvalidOperationException("Pages must have Overview field populated for RAG Agent strategy");
        }

        context.Logger.LogDebug(
            "RAG Agent starting with {pageCount} available pages",
            context.Pages.Count);

        // Build available pages dictionary (0-based indexing)
        var availablePages = context.Pages
            .Select((page, index) => new { Index = index, Page = page })
            .ToDictionary(p => p.Index, p => p.Page);

        var allSelectedPages = new HashSet<int>();
        var accumulatedPages = new Dictionary<int, Page>();
        var debugLog = new StringBuilder();
        int totalRounds = 0;

        try
        {
            for (int round = 1; round <= iterationLimit; round++)
            {
                totalRounds = round;
                cancellationToken.ThrowIfCancellationRequested();

                context.Logger.LogInformation(
                    "RAG Agent Round {round}: Page Selection + Evaluation",
                    round);

                _ = debugLog.AppendLine($"=== ROUND {round} ===");
                _ = debugLog.AppendLine();

                // PHASE 1: Page Selection
                context.Logger.LogDebug("RAG Agent Round {round} Phase 1: Selecting pages", round);

                var remainingPages = availablePages
                    .Where(kvp => !allSelectedPages.Contains(kvp.Key + 1))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                if (remainingPages.Count == 0)
                {
                    context.Logger.LogWarning(
                        "No remaining pages to select from at round {round}",
                        round);
                    _ = debugLog.AppendLine("Phase: Page Selection");
                    _ = debugLog.AppendLine("Status: No remaining pages");
                    _ = debugLog.AppendLine();
                    break;
                }

                context.Logger.LogDebug(
                    "RAG Agent Round {round} evaluating {count} remaining pages",
                    round,
                    remainingPages.Count);

                string remainingSummaries = PagesHelper.GetOverviewsInputContext(remainingPages);

                List<int> selectedPages = await ExecutePageSelectionAsync(
                    context,
                    queryTemplate,
                    remainingSummaries,
                    contextLimit,
                    round,
                    cancellationToken);

                _ = debugLog.AppendLine("Phase: Page Selection");
                _ = debugLog.AppendLine($"Pages Selected: {(selectedPages.Count > 0 ? string.Join(", ", selectedPages) : "None")}");
                _ = debugLog.AppendLine();

                if (selectedPages.Count == 0)
                {
                    context.Logger.LogWarning("No pages selected at round {round}", round);

                    if (round == 1)
                    {
                        _ = debugLog.AppendLine("Status: No relevant pages found (first round)");
                        _ = debugLog.AppendLine();
                        break;
                    }

                    _ = debugLog.AppendLine("Status: No pages selected, proceeding to evaluation with existing pages");
                    _ = debugLog.AppendLine();
                }
                else
                {
                    foreach (int pageNum in selectedPages)
                    {
                        _ = allSelectedPages.Add(pageNum);

                        if (availablePages.TryGetValue(pageNum - 1, out Page? page))
                        {
                            accumulatedPages[pageNum - 1] = page;
                        }
                    }

                    if (accumulatedPages.Count > contextLimit)
                    {
                        context.Logger.LogWarning(
                            "Accumulated pages ({count}) exceeds context limit ({limit}), trimming to most recent",
                            accumulatedPages.Count,
                            contextLimit);

                        accumulatedPages = accumulatedPages
                            .OrderByDescending(kvp => kvp.Key)
                            .Take(contextLimit)
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                        _ = debugLog.AppendLine($"Note: Trimmed to {contextLimit} pages to stay within context limit");
                        _ = debugLog.AppendLine();
                    }

                    context.Logger.LogDebug(
                        "Selected {newCount} new pages, total accumulated: {totalCount}",
                        selectedPages.Count,
                        accumulatedPages.Count);
                }

                // PHASE 2: Sufficiency Evaluation
                context.Logger.LogDebug("RAG Agent Round {round} Phase 2: Evaluating sufficiency", round);

                if (accumulatedPages.Count == 0)
                {
                    context.Logger.LogWarning("No accumulated pages to evaluate at round {round}", round);
                    _ = debugLog.AppendLine("Status: No pages to evaluate");
                    _ = debugLog.AppendLine();
                    break;
                }

                (string reasoning, string sufficiency, List<int> pagesToKeep) = await ExecuteSufficiencyEvaluationAsync(
                    context,
                    queryTemplate,
                    accumulatedPages,
                    contextLimit,
                    round,
                    cancellationToken);

                _ = debugLog.AppendLine("Phase: Sufficiency Evaluation");
                _ = debugLog.AppendLine($"Result: {sufficiency}");
                _ = debugLog.AppendLine($"Pages Kept: {string.Join(", ", pagesToKeep)}");
                _ = debugLog.AppendLine();
                _ = debugLog.AppendLine("Reasoning:");
                _ = debugLog.AppendLine(reasoning);
                _ = debugLog.AppendLine();

                if (pagesToKeep.Count > 0)
                {
                    var keptPages = new Dictionary<int, Page>();
                    var continuationPages = new List<int>();

                    foreach (int pageNum in pagesToKeep)
                    {
                        int zeroBasedIndex = pageNum - 1;

                        if (accumulatedPages.TryGetValue(zeroBasedIndex, out Page? page))
                        {
                            keptPages[zeroBasedIndex] = page;
                        }
                        else if (availablePages.TryGetValue(zeroBasedIndex, out Page? newPage))
                        {
                            keptPages[zeroBasedIndex] = newPage;
                            _ = allSelectedPages.Add(pageNum);
                            continuationPages.Add(pageNum);

                            context.Logger.LogDebug(
                                "Added continuation page {pageNum} at round {round}",
                                pageNum,
                                round);
                        }
                    }

                    if (continuationPages.Count > 0)
                    {
                        _ = debugLog.AppendLine($"Continuation Pages Added: {string.Join(", ", continuationPages)}");
                        _ = debugLog.AppendLine();
                    }

                    accumulatedPages = keptPages;
                }

                // Check if sufficient or round limit reached
                if (sufficiency == "SUFFICIENT")
                {
                    context.Logger.LogInformation(
                        "RAG Agent stopping at round {round}: Sufficient context achieved",
                        round);
                    break;
                }

                if (round == iterationLimit)
                {
                    context.Logger.LogInformation(
                        "RAG Agent stopping at round {round}: Round limit reached",
                        round);
                    break;
                }

                context.Logger.LogDebug("Context insufficient, continuing to next round");
            }

            // Store debug info for UI display
            var debugInfo = new StringBuilder();
            _ = debugInfo.AppendLine("RAG AGENT EXECUTION SUMMARY");
            _ = debugInfo.AppendLine("==========================");
            _ = debugInfo.AppendLine();
            _ = debugInfo.AppendLine($"Total Rounds Executed: {totalRounds}");
            _ = debugInfo.AppendLine($"Iteration Limit: {iterationLimit}");
            _ = debugInfo.AppendLine($"Context Limit: {contextLimit}");
            _ = debugInfo.AppendLine($"Final Pages Selected: {(allSelectedPages.Count > 0 ? string.Join(", ", allSelectedPages.OrderBy(p => p)) : "None")}");
            _ = debugInfo.AppendLine();
            _ = debugInfo.AppendLine("EXECUTION DETAILS");
            _ = debugInfo.AppendLine("=================");
            _ = debugInfo.AppendLine();
            _ = debugInfo.Append(debugLog);

            string debugInfoText = debugInfo.ToString();

            // Store debug info on execution state for access by block strategies
            executionState.DebugInfo = debugInfoText;
            context.Logger.LogDebug("RAG Agent debug info: {debugInfo}", debugInfoText);

            context.Logger.LogInformation(
                "RAG Agent completed: {pageCount} pages selected after {rounds} rounds",
                accumulatedPages.Count,
                totalRounds);

            // Return the accumulated pages as a single batch
            return new Dictionary<string, Dictionary<int, Page>>
            {
                { string.Empty, accumulatedPages }
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "RAG Agent execution failed");
            throw;
        }
    }

    private static async Task<List<int>> ExecutePageSelectionAsync(
        RagExecutionContext context,
        string query,
        string pageSummaries,
        int contextLimit,
        int roundNumber,
        CancellationToken cancellationToken)
    {
        string systemPrompt = @"You are an expert document analyst specializing in page selection and relevance assessment.";

        string userPrompt = $@"You are tasked with selecting the most relevant page(s) from a PDF document based on page summaries and a given query.

## INPUTS:
<query_or_topic>
{query}
</query_or_topic>

<page_summaries>
{pageSummaries}
</page_summaries>

## INSTRUCTIONS:
1. Select up to {contextLimit} pages that directly address the query or topic, avoiding redundancy and prioritizing relevance over quantity
2. Format your output: List selected pages as comma-separated integers (use 0 if none found) within the <answer> tag

## OUTPUT FORMAT:
<answer>
[Page numbers in CSV format]
</answer>

## EXAMPLES:
<answer>
1,3,5
</answer>
";

        ResponseContentSequence responseContent = await context.ChatConnector.GetResponseAsync(
            userPrompt,
            systemPrompt,
            maxTokens: -1,
            shouldUseAdvancedModel: false,
            model: null,
            family: null,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        context.AccumulateTokens(responseContent);

        string? response = responseContent.GetPlainText() ?? string.Empty;

        string answerText = ExtractXmlTag(response, "answer");

        context.Logger.LogDebug(
            "RAG Agent Round {round} Page Selection Response: {response}",
            roundNumber,
            response);

        List<int> parsedPages = Utils.ParseStringForNumbersList(answerText);

        context.Logger.LogDebug(
            "RAG Agent Round {round} Parsed Selected Pages: {pages}",
            roundNumber,
            parsedPages.Count > 0 ? string.Join(", ", parsedPages) : "None");

        return parsedPages;
    }

    private static async Task<(string reasoning, string sufficiency, List<int> pagesToKeep)> ExecuteSufficiencyEvaluationAsync(
        RagExecutionContext context,
        string query,
        Dictionary<int, Page> currentPages,
        int contextLimit,
        int roundNumber,
        CancellationToken cancellationToken)
    {
        string systemPrompt = @"You are an expert document analyst specializing in information sufficiency evaluation and completeness assessment.";

        string currentPagesText = PagesHelper.GetTextsInputContext(currentPages, useReadableText: true);

        string userPrompt = $@"You are tasked with determining if the provided pages contain sufficient page numbers to address the given query. If insufficient, we will retrieve additional pages.

## INPUTS:
<query_or_topic>
{query}
</query_or_topic>

<pages>
{currentPagesText}
</pages>

## INSTRUCTIONS:
1. From the pages(s), determine which are relevant to the query or topic and should be kept for analysis
   - You may request to keep additional page numbers beyond those provided if they likely contain relevant continuations
2. In your answer, explain whether the current pages are sufficient/relevant
   - If SUFFICIENT, briefly describe what information is available in the selected pages
   - If INSUFFICIENT, explain what additional information or page types are needed
3. Format your output: Provide reasoning in <answer>, page numbers as comma-separated integers in <pages_to_keep>, and status in <sufficiency>

## OUTPUT FORMAT:
<answer>
[Concisely explain whether the information is sufficient; if not, specify what additional details are needed.]
</answer>

<pages_to_keep>
[CSV list of page numbers that are relevant and should be kept for analysis.]
</pages_to_keep>

<sufficiency>
[Either ""SUFFICIENT"" or ""INSUFFICIENT""]
</sufficiency>

## EXAMPLES:
<answer>
The selected pages contain comprehensive financial data including revenue breakdowns and profit margins needed for the analysis.
</answer>

<pages_to_keep>
1,3,5
</pages_to_keep>

<sufficiency>
SUFFICIENT
</sufficiency>
";

        ResponseContentSequence responseContent = await context.ChatConnector.GetResponseAsync(
            userPrompt,
            systemPrompt,
            maxTokens: -1,
            shouldUseAdvancedModel: false,
            model: null,
            family: null,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        context.AccumulateTokens(responseContent);

        string? response = responseContent.GetPlainText() ?? string.Empty;

        context.Logger.LogDebug(
            "RAG Agent Round {round} Sufficiency Evaluation Response: {response}",
            roundNumber,
            response);

        string reasoning = ExtractXmlTag(response, "answer");
        string sufficiency = ExtractXmlTag(response, "sufficiency")?.Trim().ToUpperInvariant() ?? "INSUFFICIENT";
        string pagesToKeepText = ExtractXmlTag(response, "pages_to_keep");
        List<int> pagesToKeep = Utils.ParseStringForNumbersList(pagesToKeepText);

        context.Logger.LogDebug(
            "RAG Agent Round {round} Sufficiency Result: {sufficiency}, Pages to Keep: {pages}",
            roundNumber,
            sufficiency,
            pagesToKeep.Count > 0 ? string.Join(", ", pagesToKeep) : "None");

        return (reasoning, sufficiency, pagesToKeep);
    }

    private static string ExtractXmlTag(string text, string tagName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string openTag = $"<{tagName}>";
        string closeTag = $"</{tagName}>";

        int startIndex = text.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1)
        {
            return string.Empty;
        }

        startIndex += openTag.Length;
        int endIndex = text.IndexOf(closeTag, startIndex, StringComparison.OrdinalIgnoreCase);

        return endIndex == -1 ? string.Empty : text[startIndex..endIndex].Trim();
    }
}
