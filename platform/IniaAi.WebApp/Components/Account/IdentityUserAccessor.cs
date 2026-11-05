using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.WebApp.Components.Account;

# pragma warning disable CA1812
internal sealed class IdentityUserAccessor(UserManager<User> userManager, IdentityRedirectManager redirectManager)
# pragma warning restore CA1812
{
    public async Task<User> GetRequiredUserAsync(HttpContext context)
    {
        User? user = await userManager.GetUserAsync(context.User);

        if (user is null)
        {
            redirectManager.RedirectToWithStatus(
                "Account/InvalidUser",
                $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.",
                context);
        }

        return user;
    }
}
