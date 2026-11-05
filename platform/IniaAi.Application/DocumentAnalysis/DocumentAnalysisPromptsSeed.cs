using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.DocumentAnalysis;

namespace IniaAi.Application.DocumentAnalysis;

/// <summary>Seeds the Excel analysis and Build Topics prompt defaults when no stored row carries their date.</summary>
public sealed class DocumentAnalysisPromptsSeed : ICorePromptsSeed
{
    public void Seed(ICoreDbContext context, IReadOnlyDictionary<string, bool> tableStatuses)
    {
        if (!tableStatuses[nameof(context.ExcelAnalysisPrompts)]
            || !context.ExcelAnalysisPrompts.Any(p => p.CreatedAt == ExcelAnalysisPromptsDefaults.PromptsUpdateDate))
        {
            _ = context.ExcelAnalysisPrompts.Add(ExcelAnalysisPromptsDefaults.Create());
        }

        if (!tableStatuses[nameof(context.TopicsPrompts)]
            || !context.TopicsPrompts.Any(p => p.CreatedAt == TopicsPromptsDefaults.PromptsUpdateDate))
        {
            _ = context.TopicsPrompts.Add(TopicsPromptsDefaults.Create());
        }
    }
}
