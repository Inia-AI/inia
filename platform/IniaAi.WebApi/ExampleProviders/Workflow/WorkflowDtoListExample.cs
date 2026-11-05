using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApi.Models.Dto;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Workflow;

/// <summary>
/// Provides Swagger example data for a list of <see cref="WorkflowDto"/> objects.
/// </summary>
public class WorkflowDtoListExample : IExamplesProvider<List<WorkflowDto>>
{
    /// <summary>
    /// Returns example values for a list of <see cref="WorkflowDto"/> used in Swagger documentation.
    /// </summary>
    /// <returns>A predefined list of <see cref="WorkflowDto"/> instances.</returns>
    public List<WorkflowDto> GetExamples()
    {
        return
        [
            new WorkflowDto
            {
                Id = "4e2a6d49-9c4f-4e9d-9d97-1e6e5c31de2a",
                WorkflowTypeId = "RealMoneyFundsCreditReport",
                WorkflowTypeName = "Real Money Funds Credit Report",
                IsAvailable = true,
                CreditCost = 25,
                DocumentClassKeys = ["RMF-Credit", "General"]
            },
            new WorkflowDto
            {
                Id = "9f64ac3e-26de-456d-9271-49773b6a63c5",
                WorkflowTypeId = "CorporatesCreditReport",
                WorkflowTypeName = "Corporates Credit Report",
                IsAvailable = true,
                DocumentClassKeys = ["Corporate-Credit", "Risk Assessment"]
            },
            new WorkflowDto
            {
                Id = "d1fcf7f2-1b23-48ab-b228-87e51f0d7b6d",
                WorkflowTypeId = WorkflowType.TEST_WORKFLOW_TYPE,
                WorkflowTypeName = "Test Workflow Type",
                IsAvailable = false,
                DocumentClassKeys = ["Experimental"]
            }
        ];
    }
}
