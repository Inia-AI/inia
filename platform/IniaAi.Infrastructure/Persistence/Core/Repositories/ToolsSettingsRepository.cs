using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Tools.GraphRag;
using IniaAi.Domain.Tools.PdfOcr;
using IniaAi.Domain.Tools.RagStrategies;
using IniaAi.Domain.Tools.Reconciliation;
using IniaAi.Domain.Tools.Validation;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.Core.Repositories;

public class ToolsSettingsRepository : IToolsSettingsRepository
{
    private readonly CoreDbContext _context;

    public ToolsSettingsRepository(CoreDbContext context)
    {
        _context = context;
    }

    public async Task<UseAutoDetectedTableOfContentsPrompts?> GetUseAutoDetectedTableOfContentsPromptsAsync()
    {
        return await _context
            .UseAutoDetectedTableOfContentsPrompts
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<UseAutoDetectedTableOfContentsPrompts?> GetUseAutoDetectedTableOfContentsPromptsAsNoTrackingAsync()
    {
        return await _context
            .UseAutoDetectedTableOfContentsPrompts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateUseAutoDetectedTableOfContentsPromptsAsync(UseAutoDetectedTableOfContentsPrompts prompts)
    {
        prompts.UpdatedAt = DateTime.UtcNow;
        _ = _context.UseAutoDetectedTableOfContentsPrompts.Update(prompts);
        _ = await _context.SaveChangesAsync();
    }

    public async Task<UseAutoDetectedTableOfContentsPrompts> CreateUseAutoDetectedTableOfContentsPromptsAsync(UseAutoDetectedTableOfContentsPrompts prompts)
    {
        _ = await _context.UseAutoDetectedTableOfContentsPrompts.AddAsync(prompts);
        _ = await _context.SaveChangesAsync();
        return prompts;
    }

    public async Task DeleteUseAutoDetectedTableOfContentsPromptsAsync(UseAutoDetectedTableOfContentsPrompts prompts)
    {
        _ = _context.UseAutoDetectedTableOfContentsPrompts.Remove(prompts);
        _ = await _context.SaveChangesAsync();
    }

    public Task<PdfOcrPrompts?> GetPdfOcrPromptsAsync()
    {
        return _context
            .PdfOcrPrompts
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<PdfOcrPrompts?> GetPdfOcrPromptsAsNoTrackingAsync()
    {
        return await _context
            .PdfOcrPrompts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task UpdatePdfOcrPromptsAsync(PdfOcrPrompts prompts)
    {
        prompts.UpdatedAt = DateTime.UtcNow;
        _ = _context.PdfOcrPrompts.Update(prompts);
        _ = await _context.SaveChangesAsync();
    }

    public async Task<PdfOcrPrompts> CreatePdfOcrPromptsAsync(PdfOcrPrompts prompts)
    {
        _ = await _context.PdfOcrPrompts.AddAsync(prompts);
        _ = await _context.SaveChangesAsync();
        return prompts;
    }

    public async Task DeletePdfOcrPromptsAsync(PdfOcrPrompts prompts)
    {
        _ = _context.PdfOcrPrompts.Remove(prompts);
        _ = await _context.SaveChangesAsync();
    }

    public async Task<ReconciliationPrompts?> GetReconciliationPromptsAsync()
    {
        return await _context
            .ReconciliationPrompts
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<ReconciliationPrompts?> GetReconciliationPromptsAsNoTrackingAsync()
    {
        return await _context
            .ReconciliationPrompts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateReconciliationPromptsAsync(ReconciliationPrompts prompts)
    {
        prompts.UpdatedAt = DateTime.UtcNow;
        _ = _context.ReconciliationPrompts.Update(prompts);
        _ = await _context.SaveChangesAsync();
    }

    public async Task<ReconciliationPrompts> CreateReconciliationPromptsAsync(ReconciliationPrompts prompts)
    {
        _ = await _context.ReconciliationPrompts.AddAsync(prompts);
        _ = await _context.SaveChangesAsync();
        return prompts;
    }

    public async Task DeleteReconciliationPromptsAsync(ReconciliationPrompts prompts)
    {
        _ = _context.ReconciliationPrompts.Remove(prompts);
        _ = await _context.SaveChangesAsync();
    }

    public Task<GraphRagPrompts?> GetGraphRagPromptsAsync()
    {
        return _context
            .GraphRagPrompts
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<GraphRagPrompts?> GetGraphRagPromptsAsNoTrackingAsync()
    {
        return await _context
            .GraphRagPrompts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateGraphRagPromptsAsync(GraphRagPrompts prompts)
    {
        prompts.UpdatedAt = DateTime.UtcNow;
        _ = _context.GraphRagPrompts.Update(prompts);
        _ = await _context.SaveChangesAsync();
    }

    public async Task<GraphRagPrompts> CreateGraphRagPromptsAsync(GraphRagPrompts prompts)
    {
        _ = await _context.GraphRagPrompts.AddAsync(prompts);
        _ = await _context.SaveChangesAsync();
        return prompts;
    }

    public Task<ValidationPrompts?> GetValidationPromptsAsync()
    {
        return _context
            .ValidationPrompts
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<ValidationPrompts?> GetValidationPromptsAsNoTrackingAsync()
    {
        return await _context
            .ValidationPrompts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateValidationPromptsAsync(ValidationPrompts prompts)
    {
        prompts.UpdatedAt = DateTime.UtcNow;
        _ = _context.ValidationPrompts.Update(prompts);
        _ = await _context.SaveChangesAsync();
    }

    public async Task<ValidationPrompts> CreateValidationPromptsAsync(ValidationPrompts prompts)
    {
        _ = await _context.ValidationPrompts.AddAsync(prompts);
        _ = await _context.SaveChangesAsync();
        return prompts;
    }
}
