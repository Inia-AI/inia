using IniaAi.WebApi.Contracts.Responses;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace IniaAi.Application.Common.OData;

/// <summary>
/// The one way an OData list applies a request to a query. A list projects its entities to the
/// DTO with an expression the database can run, and this applies <c>$filter</c>,
/// <c>$orderby</c>, <c>$skip</c>, <c>$top</c> and <c>$count</c> to that projection. The database
/// stores every timestamp in UTC, so a date literal in <c>$filter</c> is read as UTC. The
/// library's default reads it in the server's local time zone, and PostgreSQL refuses such a
/// value outright.
/// </summary>
public static class ODataQuery
{
    private const int UNPAGED_REPORTED_TOP = 50;

    private static readonly ODataQuerySettings _settings = new()
    {
        TimeZone = TimeZoneInfo.Utc,
        HandleNullPropagation = HandleNullPropagationOption.True,
    };

    /// <summary>
    /// Applies the request's <c>$filter</c> alone, for a caller that acts on every matching row
    /// rather than on a page of them.
    /// </summary>
    public static IQueryable<TModel> Filter<TModel>(this IQueryable<TModel> query, ODataQueryOptions<TModel> options)
    {
        return options.Filter is null
            ? query
            : (IQueryable<TModel>)options.Filter.ApplyTo(query, _settings);
    }

    /// <summary>
    /// Reads one page of <paramref name="query"/>. A <paramref name="pageSize"/> is the page a
    /// request that sends no <c>$top</c> gets, for a list over a table that only grows; without it
    /// such a request reads every row. It never shortens a <c>$top</c> the caller sent, so a
    /// caller paging by its own <c>$top</c> skips no rows, and a <c>$top</c> above the configured
    /// maximum is applied as sent. Without <c>$orderby</c> the page is taken in the database's
    /// order.
    /// </summary>
    public static async Task<ODataPaginatedResponse<TModel>> ToODataPageAsync<TModel>(
        this IQueryable<TModel> query,
        ODataQueryOptions<TModel> options,
        CancellationToken cancellationToken,
        int? pageSize = null)
        where TModel : notnull
    {
        IQueryable<TModel> filtered = query.Filter(options);

        long? totalCount = options.Count?.Value == true
            ? await LongCountAsync(filtered, cancellationToken)
            : null;

        IQueryable<TModel> page = options.OrderBy is null
            ? filtered
            : options.OrderBy.ApplyTo(filtered, _settings);

        if (options.Skip is not null)
        {
            page = page.Skip(options.Skip.Value);
        }

        int? top = options.Top?.Value ?? pageSize;
        if (top is not null)
        {
            page = page.Take(top.Value);
        }

        List<TModel> items = await ToListAsync(page, cancellationToken);

        // Clients read @odata.top as the page size, and a list read without $top has always
        // reported 50, whether or not a page was applied.
        return new ODataPaginatedResponse<TModel>(
            items: items,
            totalCount: totalCount ?? items.Count,
            skip: options.Skip?.Value ?? 0,
            top: top ?? UNPAGED_REPORTED_TOP);
    }

    private static Task<long> LongCountAsync<TModel>(IQueryable<TModel> query, CancellationToken cancellationToken)
    {
        return query.Provider is IAsyncQueryProvider
            ? query.LongCountAsync(cancellationToken)
            : Task.FromResult(query.LongCount());
    }

    private static Task<List<TModel>> ToListAsync<TModel>(IQueryable<TModel> query, CancellationToken cancellationToken)
    {
        return query.Provider is IAsyncQueryProvider
            ? query.ToListAsync(cancellationToken)
            : Task.FromResult(query.ToList());
    }
}
