using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.WorkflowOutput;

/// <summary>
/// Provides an example instance of <see cref="ListResponse{WorkflowCategoryDto}"/> for Swagger documentation.
/// </summary>
public class ListWorkflowCategoriesResponseExample : IExamplesProvider<ListResponse<WorkflowCategoryDto>>
{
    /// <summary>
    /// Returns an example of <see cref="ListResponse{WorkflowCategoryDto}"/> populated with sample data.
    /// </summary>
    /// <returns>An instance of <see cref="ListResponse{WorkflowCategoryDto}"/> with sample values.</returns>
    public ListResponse<WorkflowCategoryDto> GetExamples()
    {
        var data = new List<WorkflowCategoryDto>
        {
            new()
            {
                Id = "monitoring-category",
                Name = "Monitoring Workflows",
                Description = "Workflows for monitoring and analysis",
                Workflows =
                [
                    new()
                    {
                        Id = "wf-001",
                        WorkflowTypeId = "Generic",
                        WorkflowTypeName = "Generic",
                        IsAvailable = true,
                        DocumentClassKeys = ["Position", "NAV"],
                        //Variables = new()
                        //{
                        //    new WorkflowVariableDto { Name = "FundName", Type = "string", DefaultValue = "Example Fund" }
                        //},
                        CounterpartyTypeMappings = ["Corporation"]
                    }
                ]
            }
        };

        return new ListResponse<WorkflowCategoryDto>(data);
    }
}
