using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Common;

/// <summary>
/// Provides extension methods to generate OData pagination links.
/// </summary>
public static class ODataLinkBuilder
{
    /// <summary>
    /// Creates an OData pagination link with an updated $skip value.
    /// </summary>
    /// <param name="urlHelper">The URL helper instance.</param>
    /// <param name="routePrefix">The OData route prefix.</param>
    /// <param name="options">The OData query options.</param>
    /// <param name="nextSkip">The value for the next $skip.</param>
    /// <returns>A string representing the OData link with updated query parameters.</returns>
    public static string? CreateODataLink(this IUrlHelper urlHelper, string routePrefix, ODataQueryOptions options, int nextSkip)
    {
        var queryParams = options.RawValues.ToDictionary();
        queryParams["$skip"] = nextSkip.ToString();

        string queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return urlHelper.Link(routeName: null, values: new { }) + "?" + queryString;
    }

    /// <summary>
    /// Converts OData raw query options into a dictionary of query parameters.
    /// </summary>
    /// <param name="raw">The raw OData query options.</param>
    /// <returns>A dictionary containing the query parameters.</returns>
    private static Dictionary<string, string> ToDictionary(this ODataRawQueryOptions raw)
    {
        var dict = new Dictionary<string, string>();
        if (raw.Filter is not null)
        {
            dict.Add("$filter", raw.Filter);
        }

        if (raw.Select is not null)
        {
            dict.Add("$select", raw.Select);
        }

        if (raw.OrderBy is not null)
        {
            dict.Add("$orderby", raw.OrderBy);
        }

        if (raw.Top is not null)
        {
            dict.Add("$top", raw.Top);
        }

        if (raw.Skip is not null)
        {
            dict.Add("$skip", raw.Skip);
        }

        if (raw.Expand is not null)
        {
            dict.Add("$expand", raw.Expand);
        }

        return dict;
    }
}
