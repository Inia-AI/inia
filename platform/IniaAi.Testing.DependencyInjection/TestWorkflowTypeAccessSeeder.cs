using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;

namespace IniaAi.Testing.DependencyInjection;

public static class TestWorkflowTypeAccessSeeder
{
    public static void GrantWorkflowTypeAccess(
        this CoreDbContext context,
        string workflowTypeId,
        string organizationId)
    {
        _ = context.WorkflowTypeOrganizationAccess.Add(new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = workflowTypeId,
            OrganizationId = organizationId,
        });
    }
}
