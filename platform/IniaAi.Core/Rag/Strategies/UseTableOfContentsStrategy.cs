using IniaAi.Core.Rag.Interfaces;
using IniaAi.Core.Workflows.Outputs.TableOfContents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class UseTableOfContentsStrategy : IRagStrategy
{
    public async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(
        RagExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.Logger.LogInformation(
            "Executing data retrieval using '{name}'.",
            GetType().Name);

        RagSettingsExecutionState executionState = context.RagSettings.ExecutionState
            ?? new RagSettingsExecutionState(context.RagSettings);

        var toc = TableOfContentsList.FromJson(executionState.TableOfContentsInput);

        if (toc is null)
        {
            context.Logger.LogError(
                "Invalid Table of Contents input: {toc}",
                executionState.TableOfContentsInput);

            return [];
        }

        Dictionary<string, Dictionary<int, Page>> pagesToReturn = [];

        for (int i = 0; i < toc.Sections.Count; i++)
        {
            TableOfContentsEntry section = toc.Sections[i];

            if (section.StartPage is null)
            {
                context.Logger.LogWarning(
                    "Skipping section {section} because it has no start page.",
                    i);

                continue;
            }

            if (section.EndPage is null)
            {
                context.Logger.LogWarning(
                    "Skipping section {section} because it has no end page.",
                    i);

                continue;
            }

            int start = section.StartPage.Value - 1;
            int nextSectionStart = i + 1 < toc.Sections.Count
                ? toc.Sections[i + 1].StartPage is not null
                    ? toc.Sections[i + 1].StartPage!.Value - 1
                    : int.MaxValue
                : int.MaxValue;

            int end = section.EndPage.Value - 1 >= 0
                ? section.EndPage.Value - 1
                : nextSectionStart - 1;

            start += executionState.PageAdjustment;
            end += executionState.PageAdjustment;

            var sectionPages = context.Pages
                .Where((page, index) => index >= start && index <= end)
                .ToList();

            Dictionary<int, Page> indexedSectionPages = [];

            for (int j = 0; j < sectionPages.Count; j++)
            {
                int originalIndex = start + j;
                if (originalIndex >= 0 && originalIndex < context.Pages.Count)
                {
                    indexedSectionPages[originalIndex] = sectionPages[j];
                }
            }

            if (section.Title is null)
            {
                continue;
            }

            if (!pagesToReturn.TryGetValue(section.Title, out Dictionary<int, Page>? existingPages))
            {
                pagesToReturn[section.Title] = [];
            }

            foreach ((int index, Page page) in indexedSectionPages)
            {
                pagesToReturn[section.Title][index] = page;
            }
        }

        return await Task.FromResult(pagesToReturn);
    }
}
