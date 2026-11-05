
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Swagger;
using System.Text.Json;

namespace IniaAi.WebApi.ExampleProviders.Workflow;
/// <summary>
/// Provides an example instance of <see cref="StartWorkflowExecutionRequest"/> for Swagger documentation.
/// </summary>
public class StartWorkflowExecutionRequestExample : IExamplesProvider<StartWorkflowExecutionRequest>
{
    /// <summary>
    /// Returns a sample <see cref="StartWorkflowExecutionRequest"/> object with pre-populated data.
    /// </summary>
    /// <returns>A <see cref="StartWorkflowExecutionRequest"/> instance containing example data.</returns>
    public StartWorkflowExecutionRequest GetExamples()
    {
        return new StartWorkflowExecutionRequest
        {
            WorkflowId = "1b20c79a-919e-34f6-aaa1-5h8dcfe3b952",
            CounterpartyId = "2a70c79a-9e9e-44f6-aaa1-9c3dcfe3b951",
            Files =
            [
                new PostDocumentInfoRequest()
                {
                    FileId = "d3fa0db8-2f34-4dc4-9de2-376e168ae97f",
                    DocumentTags = ["finance", "confidential"],
                     DocumentClass = "internal-report"
                }
            ],
            Variables = new Dictionary<string, JsonElement>
            {
                { "AnalystName", JsonDocument.Parse("\"Jane Doe\"").RootElement },
                { "ReviewDate", JsonDocument.Parse("\"2025-03-25T00:00:00Z\"").RootElement },
                { "Confidential", JsonDocument.Parse("[\"Valuation\", \"Strategy\"]").RootElement }
            }
        };
    }
}
