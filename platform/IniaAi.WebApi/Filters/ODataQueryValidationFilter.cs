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
/// supports it, and a plain <c>search</c> parameter is read as <c>$search</c> too.
/// </summary>
public class ODataQueryValidationFilter : IActionFilter
{
    private const string SEARCH_CLAUSE = "$search";
    private const string SEARCH_UNSUPPORTED = "$search is not supported. Narrow the list with $filter.";

    private static readonly (string Clause, Func<ODataQueryOptions, object?> Parse)[] _clauses =
    [
        ("$filter", static options => options.Filter?.FilterClause),
        ("$orderby", static options => options.OrderBy?.OrderByClause),
        ("$top", static options => options.Top?.Value),
        ("$skip", static options => options.Skip?.Value),
        ("$count", static options => options.Count?.Value),
    ];

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (ODataQueryOptions queryOptions in context.ActionArguments.Values.OfType<ODataQueryOptions>())
        {
            if (FindUnparseableClause(queryOptions) is not (string clause, string message))
            {
                continue;
            }

            var modelState = new ModelStateDictionary();
            modelState.AddModelError(clause, message);

            ProblemDetailsFactory problemDetailsFactory = context.HttpContext.RequestServices
                .GetRequiredService<ProblemDetailsFactory>();
            ValidationProblemDetails problemDetails = problemDetailsFactory.CreateValidationProblemDetails(
                context.HttpContext,
                modelState,
                StatusCodes.Status400BadRequest);

            context.Result = new BadRequestObjectResult(problemDetails);
            return;
        }
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
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
