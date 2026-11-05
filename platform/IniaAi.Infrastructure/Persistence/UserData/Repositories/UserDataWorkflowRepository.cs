using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.UserData.Repositories;

public class UserDataWorkflowRepository : RepositoryBase<Workflow>, IUserDataWorkflowRepository
{
    public UserDataWorkflowRepository(UserDataDbContext context)
        : base(context) { }

    public override async Task<Workflow?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return await Entities
            .AsSplitQuery()
            .Include(w => w.Variables)
            .Include(w => w.DocumentClasses)
            .Include(w => w.RegressionFields)
            .Include(w => w.SignatureFields)
            .Include(w => w.Sections)
            .Include($"{nameof(Workflow.Sections)}.{nameof(Section.Blocks)}.{nameof(Block.TextInputs)}")
            .Include($"{nameof(Workflow.Sections)}.{nameof(Section.Blocks)}.{nameof(Block.RagSettings)}")
            .Include($"{nameof(Workflow.Sections)}.{nameof(Section.Blocks)}.{nameof(Block.BlockResponse)}")
            .FirstOrDefaultAsync(w => w.Id == id.ToString(), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The blocks with every placeholder source the dependency map reads, as the scheduler reads
    /// them: the text inputs and RAG settings, which live in their own tables, and the workflow's
    /// signature fields through <c>Section.Workflow</c>. The workflow and its sections are read once
    /// and attached in memory, so the signature fields are not repeated for every block.
    /// </summary>
    public async Task<Dictionary<int, List<Block>>> GetAllBlocksByWorkflowIdAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        Workflow? workflow = await Context.Set<Workflow>()
            .AsNoTracking()
            .Include(w => w.SignatureFields)
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken);

        Dictionary<string, Section> sections = await Context.Set<Section>()
            .AsNoTracking()
            .Where(s => s.WorkflowId == workflowId)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        List<Block> blocks = await Context.Set<Block>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(b => b.TextInputs)
            .Include(b => b.RagSettings)
            .Where(b => b.SectionId != null && b.Section!.WorkflowId == workflowId)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<int, List<Block>>();

        foreach (Block block in blocks)
        {
            if (block.SectionId is null || !sections.TryGetValue(block.SectionId, out Section? section))
            {
                continue;
            }

            if (workflow is not null)
            {
                section.Workflow = workflow;
            }

            block.Section = section;

            if (!result.TryGetValue(section.Order, out List<Block>? sectionBlocks))
            {
                sectionBlocks = [];
                result[section.Order] = sectionBlocks;
            }

            sectionBlocks.Add(block);
        }

        return result;
    }

    public async Task<List<WorkflowVariable>> GetAllVariablesByWorkflowIdAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        return await Context.Set<WorkflowVariable>()
            .AsNoTracking()
            .Where(v => v.WorkflowId == workflowId)
            .ToListAsync(cancellationToken);
    }

}
