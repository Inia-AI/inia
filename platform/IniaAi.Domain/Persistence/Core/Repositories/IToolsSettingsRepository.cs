using IniaAi.Domain.Tools.GraphRag;
using IniaAi.Domain.Tools.PdfOcr;
using IniaAi.Domain.Tools.RagStrategies;
using IniaAi.Domain.Tools.Reconciliation;
using IniaAi.Domain.Tools.Validation;

namespace IniaAi.Domain.Persistence.Core.Repositories;

public interface IToolsSettingsRepository
{
    Task<UseAutoDetectedTableOfContentsPrompts?> GetUseAutoDetectedTableOfContentsPromptsAsync();
    Task<UseAutoDetectedTableOfContentsPrompts?> GetUseAutoDetectedTableOfContentsPromptsAsNoTrackingAsync();
    Task UpdateUseAutoDetectedTableOfContentsPromptsAsync(UseAutoDetectedTableOfContentsPrompts prompts);
    Task<UseAutoDetectedTableOfContentsPrompts> CreateUseAutoDetectedTableOfContentsPromptsAsync(UseAutoDetectedTableOfContentsPrompts prompts);
    Task DeleteUseAutoDetectedTableOfContentsPromptsAsync(UseAutoDetectedTableOfContentsPrompts prompts);

    Task<PdfOcrPrompts?> GetPdfOcrPromptsAsync();
    Task<PdfOcrPrompts?> GetPdfOcrPromptsAsNoTrackingAsync();
    Task UpdatePdfOcrPromptsAsync(PdfOcrPrompts prompts);
    Task<PdfOcrPrompts> CreatePdfOcrPromptsAsync(PdfOcrPrompts prompts);
    Task DeletePdfOcrPromptsAsync(PdfOcrPrompts prompts);

    Task<ReconciliationPrompts?> GetReconciliationPromptsAsync();
    Task<ReconciliationPrompts?> GetReconciliationPromptsAsNoTrackingAsync();
    Task UpdateReconciliationPromptsAsync(ReconciliationPrompts prompts);
    Task<ReconciliationPrompts> CreateReconciliationPromptsAsync(ReconciliationPrompts prompts);
    Task DeleteReconciliationPromptsAsync(ReconciliationPrompts prompts);

    Task<GraphRagPrompts?> GetGraphRagPromptsAsync();
    Task<GraphRagPrompts?> GetGraphRagPromptsAsNoTrackingAsync();
    Task UpdateGraphRagPromptsAsync(GraphRagPrompts prompts);
    Task<GraphRagPrompts> CreateGraphRagPromptsAsync(GraphRagPrompts prompts);

    Task<ValidationPrompts?> GetValidationPromptsAsync();
    Task<ValidationPrompts?> GetValidationPromptsAsNoTrackingAsync();
    Task UpdateValidationPromptsAsync(ValidationPrompts prompts);
    Task<ValidationPrompts> CreateValidationPromptsAsync(ValidationPrompts prompts);
}
