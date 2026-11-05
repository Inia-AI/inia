using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.DocumentInfo;

/// <summary>
/// Provides an example instance of <see cref="PostDocumentInfoRequest"/> for Swagger documentation.
/// </summary>
public class PostDocumentInfoRequestExample : IExamplesProvider<PostDocumentInfoRequest>
{
    /// <summary>
    /// Returns an example of <see cref="PostDocumentInfoRequest"/> with sample data.
    /// </summary>
    /// <returns>An instance of <see cref="PostDocumentInfoRequest"/> populated with example values.</returns>
    public PostDocumentInfoRequest GetExamples()
    {
        return new PostDocumentInfoRequest
        {
            FileId = "d3fa0db8-2f34-4dc4-9de2-376e168ae97f",
            DocumentTags = ["finance", "confidential"],
            DocumentClass = "internal-report",
            ShouldUseOcr = true
        };
    }
}
