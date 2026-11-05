using System.Collections.Specialized;
using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Contracts.Responses;

/// <summary>
/// A paginated response class that aligns with OData response conventions.
/// </summary>
/// <typeparam name="T">The type of items contained in the paginated response.</typeparam>
public class ODataPaginatedResponse<T> : ListResponse<T>
    where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ODataPaginatedResponse{T}"/> class.
    /// Calculates and sets the offset and next page link based on the provided parameters.
    /// </summary>
    /// <param name="items">The collection of items in the current page.</param>
    /// <param name="totalCount">The total number of items available across all pages.</param>
    /// <param name="skip">The number of items to skip (used to calculate the offset).</param>
    /// <param name="top">The maximum number of items to return (page size).</param>
    /// <param name="baseUri">The base URI for constructing the next page link.</param>
    public ODataPaginatedResponse(IEnumerable<T> items, long totalCount, int skip, int top, Uri? baseUri = null) : base(items)
    {
        Count = totalCount;
        Skip = skip;
        Top = top;

        if (baseUri is not null && skip + top < totalCount)
        {
            int nextSkip = skip + top;
            var uriBuilder = new UriBuilder(baseUri);
            NameValueCollection query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
            query["$skip"] = nextSkip.ToString();
            query["$top"] = top.ToString();
            uriBuilder.Query = query.ToString();
            NextLink = uriBuilder.ToString();
        }
    }

    /// <summary>
    /// Gets or sets the total number of items across all pages.
    /// Corresponds to the OData field <c>@odata.count</c>.
    /// </summary>
    [JsonPropertyName("@odata.count")]
    public long Count { get; set; }

    /// <summary>
    /// Gets or sets the URL to the next page of results, if any.
    /// Corresponds to the OData field <c>@odata.nextLink</c>.
    /// </summary>
    [JsonPropertyName("@odata.nextLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextLink { get; set; }

    /// <summary>
    /// Gets or sets the number of items skipped before starting this page.
    /// Corresponds to the custom OData-style field <c>@odata.skip</c>.
    /// </summary>
    [JsonPropertyName("@odata.skip")]
    public int Skip { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of items returned in this page.
    /// Corresponds to the custom OData-style field <c>@odata.top</c>.
    /// </summary>
    [JsonPropertyName("@odata.top")]
    public int Top { get; set; }
}
