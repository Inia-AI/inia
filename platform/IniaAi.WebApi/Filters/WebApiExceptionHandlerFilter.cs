using IniaAi.WebApi.Extensions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// Handles unhandled exceptions occurring during HTTP request processing in the Web API.
/// </summary>
public class WebApiExceptionHandlerFilter : IExceptionHandler
{
    private static readonly string[] _formReaderNamespaces = ["Microsoft.AspNetCore.WebUtilities", "Microsoft.AspNetCore.Http.Features"];

    private readonly ILogger<WebApiExceptionHandlerFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebApiExceptionHandlerFilter"/> class with the specified logger.
    /// </summary>
    /// <param name="logger">The logger used to log error information.</param>
    public WebApiExceptionHandlerFilter(ILogger<WebApiExceptionHandlerFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to handle the specified exception asynchronously within the ASP.NET Core pipeline.
    /// </summary>
    /// <param name="httpContext">The HTTP context in which the exception occurred.</param>
    /// <param name="exception">The exception to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A <see cref="ValueTask{Boolean}"/> representing the asynchronous operation. The task result contains
    /// <c>true</c> if the exception was handled; otherwise, <c>false</c>.
    /// </returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (RequestRefusal(exception) is { } refusal)
        {
            _logger.LogWarning("A request was refused because its body could not be read: {Status} {Message}", refusal.Status, exception.Message);
            await WriteProblemAsync(httpContext, refusal, cancellationToken);
            return true;
        }

        _logger.LogError("An error occurred while processing your request: {message}", exception.Message);

        // The deploy repository's host template names its environment Development, so only a
        // developer's own machine may see the exception in the response; elsewhere it stays in the log.
        bool isLocalMachine = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsLocalDevelopment();

        var problemDetails = new ProblemDetails
        {
            Status = (int)HttpStatusCode.InternalServerError,
            Type = isLocalMachine ? exception.GetType().FullName : null,
            Title = "An unhandled error occurred",
            Detail = isLocalMachine
                ? $"{exception.Message}\n{exception}"
                : "Something went wrong. Please contact support."
        };

        await WriteProblemAsync(httpContext, problemDetails, cancellationToken);
        return true;
    }

    private static async Task WriteProblemAsync(HttpContext httpContext, ProblemDetails problem, CancellationToken cancellationToken)
    {
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status ?? (int)HttpStatusCode.InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    }

    /// <summary>
    /// A request the server could not read: a body over the size limit, or a form the form reader
    /// refused, such as a key over its length limit. The sender gets the reason, not a 500.
    /// </summary>
    private static ProblemDetails? RequestRefusal(Exception exception)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            return badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? new ProblemDetails
                {
                    Status = StatusCodes.Status413PayloadTooLarge,
                    Title = "The request body is too large",
                    Detail = "Send a smaller request body.",
                }
                : new ProblemDetails
                {
                    Status = badRequest.StatusCode,
                    Title = "The request could not be read",
                    Detail = "Check the request and send it again.",
                };
        }

        if (exception is InvalidDataException && _formReaderNamespaces.Contains(exception.TargetSite?.DeclaringType?.Namespace))
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The form in the request could not be read",
                Detail = "The form has too many fields, or a field name or value over the length limit. Shorten it and send the request again.",
            };
        }

        return null;
    }
}
