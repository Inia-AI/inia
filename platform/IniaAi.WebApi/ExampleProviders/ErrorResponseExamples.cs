using ErrorOr;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders;

/// <summary>
/// Provides an example of a 404 Not Found error response for Swagger documentation.
/// </summary>
public class NotFoundErrorExample : IExamplesProvider<List<Error>>
{
    /// <summary>
    /// Returns a list containing a single <see cref="Error"/> representing a not found error.
    /// </summary>
    /// <returns>A list of <see cref="Error"/> instances.</returns>
    public List<Error> GetExamples()
    {
        return
        [
            Error.NotFound("WorkflowOutput.NotFound", "The workflow output with the specified ID was not found.")
        ];
    }
}

/// <summary>
/// Provides an example of a 400 Bad Request error response for Swagger documentation.
/// </summary>
public class BadRequestErrorExample : IExamplesProvider<List<Error>>
{
    /// <summary>
    /// Returns a list containing a single <see cref="Error"/> representing a validation error.
    /// </summary>
    /// <returns>A list of <see cref="Error"/> instances.</returns>
    public List<Error> GetExamples()
    {
        return
        [
            Error.Validation("WorkflowOutput.InvalidId", "The provided ID format is invalid.")
        ];
    }
}
