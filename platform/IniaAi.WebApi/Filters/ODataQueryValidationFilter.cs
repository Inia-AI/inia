using IniaAi.Application.Common.OData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// Parses every OData query clause an action receives before the action runs, so a clause the
/// parser rejects is answered with 400 and a problem details body naming the clause, instead of
/// reaching the unhandled-exception handler as a 500. <c>$search</c> is refused outright: no list
/// supports it, and a plain <c>search</c> parameter is read as <c>$search</c> too. A clause given
/// twice in two spellings (<c>filter</c> and <c>$FILTER</c>) is refused before model binding,
/// where the OData binder would otherwise throw.
/// </summary>
public class ODataQueryValidationFilter : IActionFilter, IResourceFilter
{
    private const string DOLLAR = "$";
    private const string SEARCH_CLAUSE = "$search";
    private const string SEARCH_UNSUPPORTED = "$search is not supported. Narrow the list with $filter.";
    private const string GIVEN_TWICE = "This query option is given more than once. Give each query option once.";

    private static readonly (string Clause, Func<ODataQueryOptions, object?> Parse)[] _clauses =
    [
        ("$filter", static options => options.Filter?.FilterClause),
        ("$orderby", static options =>
        {
            ODataQuery.CheckOrderBy(options);
            return null;
        }),
        ("$top", static options => options.Top?.Value),
        ("$skip", static options => options.Skip?.Value),
        ("$count", static options => options.Count?.Value),
    ];

    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        bool bindsQueryOptions = context.ActionDescriptor.Parameters
            .Any(parameter => typeof(ODataQueryOptions).IsAssignableFrom(parameter.ParameterType));
        if (bindsQueryOptions && FindClauseGivenTwice(context.HttpContext.Request.Query.Keys) is string clause)
        {
            context.Result = BadRequest(context.HttpContext, clause, GIVEN_TWICE);
        }
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (ODataQueryOptions queryOptions in context.ActionArguments.Values.OfType<ODataQueryOptions>())
        {
            if (FindUnparseableClause(queryOptions) is not (string clause, string message))
            {
                continue;
            }

            context.Result = BadRequest(context.HttpContext, clause, message);
            return;
        }
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private static BadRequestObjectResult BadRequest(HttpContext httpContext, string clause, string message)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(clause, message);

        ProblemDetailsFactory problemDetailsFactory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        ValidationProblemDetails problemDetails = problemDetailsFactory.CreateValidationProblemDetails(
            httpContext,
            modelState,
            StatusCodes.Status400BadRequest);

        return new BadRequestObjectResult(problemDetails);
    }

    private static string? FindClauseGivenTwice(IEnumerable<string> queryKeys)
    {
        return queryKeys
            .Select(key => key.ToLowerInvariant())
            .Where(key => ODataQueryOptions.IsSystemQueryOption(key, isDollarSignOptional: true))
            .GroupBy(key => key.StartsWith(DOLLAR, StringComparison.Ordinal) ? key : DOLLAR + key, StringComparer.Ordinal)
            .FirstOrDefault(spellings => spellings.Count() > 1)?.Key;
    }

    private static (string Clause, string Message)? FindUnparseableClause(ODataQueryOptions queryOptions)
    {
        if (queryOptions.RawValues.Search is not null)
        {
            return (SEARCH_CLAUSE, SEARCH_UNSUPPORTED);
        }

        foreach ((string clause, Func<ODataQueryOptions, object?> parse) in _clauses)
        {
            try
            {
                _ = parse(queryOptions);
            }
            catch (ODataException exception)
            {
                return (clause, exception.Message);
            }
        }

        return null;
    }
}
