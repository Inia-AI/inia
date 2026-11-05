using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Domain.Persistence.UserData.Repositories;

public interface IUserDataWorkflowRepository : IRepository<Workflow>
{
    Task<Dictionary<int, List<Block>>> GetAllBlocksByWorkflowIdAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<List<WorkflowVariable>> GetAllVariablesByWorkflowIdAsync(string workflowId, CancellationToken cancellationToken = default);
}
