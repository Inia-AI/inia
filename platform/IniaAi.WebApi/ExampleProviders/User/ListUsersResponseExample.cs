using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Shared.Users;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.WorkflowOutput;

/// <summary>
/// Provides an example instance of <see cref="ODataPaginatedResponse{UserDto}"/> for Swagger documentation.
/// </summary>
public class ListUsersResponseExample : IExamplesProvider<ODataPaginatedResponse<UserDto>>
{
    /// <summary>
    /// Returns an example of <see cref="ODataPaginatedResponse{UserDto}"/> populated with sample data.
    /// </summary>
    /// <returns>An instance of <see cref="ODataPaginatedResponse{UserDto}"/> with sample values.</returns>
    public ODataPaginatedResponse<UserDto> GetExamples()
    {
        var data = new List<UserDto>
        {
            new() {
                Id = "f7a1e02b-a238-43c3-9e25-88535a31a841",
                Username = "john.doe",
                Email = "john.doe@test.com",
                FirstName = "John",
                LastName = "Doe",
                MiddleName = "Michael",
                FullName = "John Michael Doe"
            },
            new() {
                Id = "e8b3a42a-2d69-4b18-8f5a-2fd8e2427df4",
                Username = "jane.smith",
                Email = "jane.smith@test.com",
                FirstName = "Jane",
                LastName = "Smith",
                MiddleName = null,
                FullName = "Jane Smith"
            }
        };

        return new ODataPaginatedResponse<UserDto>(
            data,
            totalCount: data.Count,
            skip: 0,
            top: 25
        );
    }
}
