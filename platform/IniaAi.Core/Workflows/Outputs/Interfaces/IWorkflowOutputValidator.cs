using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Services.Models;

public interface IWorkflowOutputValidator
{
    bool IsValidBlockForOutput(Block block);
}
