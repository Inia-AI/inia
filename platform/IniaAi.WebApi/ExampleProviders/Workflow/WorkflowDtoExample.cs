using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApi.Models.Dto;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Workflow;

/// <summary>
/// Provides an example instance of <see cref="WorkflowDto"/> for Swagger documentation.
/// </summary>
public class WorkflowDtoExample : IExamplesProvider<WorkflowDto>
{
    /// <summary>
    /// Returns a sample <see cref="WorkflowDto"/> object with pre-populated data.
    /// </summary>
    /// <returns>A <see cref="WorkflowDto"/> instance containing example data.</returns>
    public WorkflowDto GetExamples()
    {
        return new WorkflowDto
        {
            Id = "8bcf5a4e-f379-45cb-876a-529a5c0dbdb4",
            WorkflowTypeId = WorkflowType.TEST_WORKFLOW_TYPE,
            WorkflowTypeName = "Real Money Funds Credit Report",
            IsAvailable = true,
            CreditCost = 25,
            DocumentClassKeys =
            [
                "Financials",
                "Strategy",
                "Valuation"
            ]
        };
    }
}
