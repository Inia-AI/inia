using IniaAi.Core.Common;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.DocumentAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Documents;

public sealed class ExcelAnalysisPromptsLoader : PromptsLoader<ExcelAnalysisPrompts>
{
    public ExcelAnalysisPromptsLoader(ILogger logger, IServiceScopeFactory serviceScopeFactory)
        : base(logger, serviceScopeFactory)
    {
    }

    protected override async Task<ExcelAnalysisPrompts?> ReadNewestRowAsync(ICoreUnitOfWork coreUnitOfWork)
    {
        return await coreUnitOfWork.Repository<ExcelAnalysisPrompts>()
            .GetFirstOrDefaultAsync(new LatestExcelAnalysisPromptsSpec())
            .ConfigureAwait(false);
    }

    protected override ExcelAnalysisPrompts Defaults()
    {
        return ExcelAnalysisPromptsDefaults.Create();
    }
}
