using IniaAi.Application.Common.DailyAllowances;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.DailyAllowances;
using IniaAi.WebApi.Models.Responses.DailyAllowances;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// Takes one request from the user's daily allowance when it arrives, and refuses it with 429 when
/// none is left. A request gets its count back only when it did not succeed, was not aborted and
/// started no model call. When no limit applies the response is left as it is. When the allowance
/// cannot be read, after the database's own retries of a deadlock, the request goes through.
/// A request with no user id, such as one signed in with client credentials, is not limited.
/// </summary>
public sealed class DailyAllowanceFilter : IAsyncResourceFilter
{
    /// <summary>The refusal's error code.</summary>
    public const string EXHAUSTED_CODE = "DailyAllowance.Exhausted";

    private const int WINDOW_SECONDS = 86400;

    private readonly DailyAllowanceKind _kind;
    private readonly IDailyAllowance _allowance;
    private readonly IRequestContext _requestContext;
    private readonly IRequestModelUse _modelUse;
    private readonly ILogger<DailyAllowanceFilter> _logger;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="DailyAllowanceFilter"/> class.
    /// </summary>
    public DailyAllowanceFilter(
        DailyAllowanceKind kind,
        IDailyAllowance allowance,
        IRequestContext requestContext,
        IRequestModelUse modelUse,
        ILogger<DailyAllowanceFilter> logger,
        TimeProvider? clock = null)
    {
        _kind = kind;
        _allowance = allowance;
        _requestContext = requestContext;
        _modelUse = modelUse;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        HttpContext http = context.HttpContext;
        DailyAllowanceTake? take = await TryTakeAsync(http, http.RequestAborted);

        if (take is null)
        {
            _ = await next();
            return;
        }

        if (!take.Taken)
        {
            _logger.LogInformation(
                "Refused a {Kind} request of user {UserId} in organization {OrganizationId}: the daily limit of {Limit} is used",
                _kind,
                LogSanitizer.ForLog(take.UserId),
                LogSanitizer.ForLog(_requestContext.OrganizationId),
                take.Limit);
            RateLimitHeaders.WriteRefusal(http.Response, DailyAllowanceKinds.All[take.Kind].PolicyName, take.Limit, WINDOW_SECONDS, SecondsToReset(take));
            string message = DailyAllowanceKinds.All[_kind].ExhaustedMessageFor(take.Limit);
            context.Result = new ObjectResult(new DailyAllowanceRefusal(message, EXHAUSTED_CODE, take.ResetsAt))
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
            };
            return;
        }

        Task<bool>? givenBack = null;
        Task<bool> GiveBackOnce(bool failed)
        {
            return givenBack ??= failed && !http.RequestAborted.IsCancellationRequested && !_modelUse.ModelCalled
                ? TryGiveBackAsync(take)
                : Task.FromResult(false);
        }

        http.Response.OnStarting(async () =>
        {
            bool returned = await GiveBackOnce(failed: !IsSuccess(http.Response.StatusCode));
            WriteLimitHeaders(http.Response, take, returned ? take.Remaining + 1 : take.Remaining);
        });

        ResourceExecutedContext executed = await next();

        _ = await GiveBackOnce(failed: (executed.Exception is not null && !executed.ExceptionHandled) || !IsSuccess(http.Response.StatusCode));
    }

    private static bool IsSuccess(int statusCode)
    {
        return statusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices;
    }

    private async Task<DailyAllowanceTake?> TryTakeAsync(HttpContext http, CancellationToken cancellationToken)
    {
        if (_requestContext.OrganizationId is not { } organizationId || _requestContext.UserId is not { } userId)
        {
            return null;
        }

        try
        {
            return await _allowance.TakeAsync(new DailyAllowanceRequest(_kind, organizationId, userId, http.User), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "The daily {Kind} allowance of user {UserId} could not be read; the request goes through", _kind, LogSanitizer.ForLog(userId));
            return null;
        }
    }

    private async Task<bool> TryGiveBackAsync(DailyAllowanceTake take)
    {
        try
        {
            await _allowance.GiveBackAsync(take, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A {Kind} request of user {UserId} that failed could not be given back", _kind, LogSanitizer.ForLog(take.UserId));
            return false;
        }
    }

    private void WriteLimitHeaders(HttpResponse response, DailyAllowanceTake take, int remaining)
    {
        RateLimitHeaders.Write(response, DailyAllowanceKinds.All[take.Kind].PolicyName, take.Limit, WINDOW_SECONDS, remaining, SecondsToReset(take));
    }

    private long SecondsToReset(DailyAllowanceTake take)
    {
        return Math.Max(0, (long)Math.Ceiling((take.ResetsAt - _clock.GetUtcNow().UtcDateTime).TotalSeconds));
    }
}
