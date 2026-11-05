using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Contracts.Responses;

/// <summary>
/// A paginated response class that aligns with OData response conventions.
/// </summary>
/// <typeparam name="T">The type of items contained in the paginated response.</typeparam>
public class ListResponse<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListResponse{T}"/> class.
    /// Calculates and sets the offset and next page link based on the provided parameters.
    /// </summary>
    /// <param name="items">The collection of items in the current page.</param>
    public ListResponse(IEnumerable<T> items)
    {
        Value = [.. items];
    }

    /// <summary>
    /// Gets or sets the collection of items in the current page.
    /// Corresponds to the OData field <c>value</c>.
    /// </summary>
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];
}
