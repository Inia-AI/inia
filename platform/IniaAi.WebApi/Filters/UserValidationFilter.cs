using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Net;
using System.Security.Claims;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// An asynchronous action filter that validates the authenticated user before executing the action.
/// </summary>
public class UserValidationFilter : IAsyncActionFilter
{
    private readonly UserManager<User> _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserValidationFilter"/> class.
    /// </summary>
    /// <param name="userManager">The user manager used to retrieve user information.</param>
    public UserValidationFilter(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Called asynchronously before the action, after model binding is complete.
    /// Validates the user and adds the user object to the HttpContext items if valid.
    /// </summary>
    /// <param name="context">The action executing context.</param>
    /// <param name="next">The delegate to execute the next action filter or the action itself.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        string? userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            context.Result = new ObjectResult("User ID not found in claims.") { StatusCode = 401 };
            return;
        }

        if (await _userManager.GetUserAsync(context.HttpContext.User) is not User user)
        {
            context.Result = new ObjectResult($"User not found: {context.HttpContext.User}.")
            {
                StatusCode = (int)HttpStatusCode.NotFound
            };
            return;
        }

        context.HttpContext.Items["User"] = user;

        _ = await next();
    }
}
