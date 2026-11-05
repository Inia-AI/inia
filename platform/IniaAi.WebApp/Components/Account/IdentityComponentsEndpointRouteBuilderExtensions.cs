using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.Account.Pages.Manage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace IniaAi.WebApp.Components.Account;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    // These endpoints are required by the Identity Razor components defined
    // in the /Components/Account/Pages directory of this project.
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder accountGroup = endpoints.MapGroup("/Account");

        _ = accountGroup.MapPost("/PerformExternalLogin", (
            HttpContext context,
            [FromServices] SignInManager<User> signInManager,
            [FromForm] string provider,
            [FromForm] string returnUrl) =>
        {
            string redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/ExternalLogin",
                QueryString.Create("ReturnUrl", returnUrl));

            AuthenticationProperties properties = signInManager
                .ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return TypedResults.Challenge(properties, [provider]);
        });

        _ = accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            SignInManager<User> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        RouteGroupBuilder manageGroup = accountGroup
            .MapGroup("/Manage")
            .RequireAuthorization();

        _ = manageGroup.MapPost("/LinkExternalLogin", async (
            HttpContext context,
            [FromServices] SignInManager<User> signInManager,
            [FromForm] string provider) =>
        {
            // Clear the existing external cookie to ensure a clean login process
            await context.SignOutAsync(IdentityConstants.ExternalScheme);

            string redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/Manage/ExternalLogins",
                QueryString.Create("Action", ExternalLogins.LinkLoginCallbackAction));

            AuthenticationProperties properties = signInManager
                .ConfigureExternalAuthenticationProperties(
                    provider,
                    redirectUrl,
                    signInManager.UserManager.GetUserId(context.User));
            return TypedResults.Challenge(properties, [provider]);
        });

        ILoggerFactory loggerFactory = endpoints
            .ServiceProvider
            .GetRequiredService<ILoggerFactory>();
        ILogger downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

        _ = manageGroup.MapPost("/DownloadPersonalData", async (
            HttpContext context,
            [FromServices] UserManager<User> userManager,
            [FromServices] AuthenticationStateProvider authenticationStateProvider) =>
        {
            User? user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
            }

            string userId = await userManager.GetUserIdAsync(user);
            downloadLogger.LogInformation("User with ID '{UserId}' asked for their personal data.", userId);

            // Only include personal data for download
            var personalData = new Dictionary<string, string>();
            IEnumerable<System.Reflection.PropertyInfo> personalDataProps = typeof(User).GetProperties().Where(
                prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
            foreach (System.Reflection.PropertyInfo? p in personalDataProps)
            {
                personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
            }

            IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(user);
            foreach (UserLoginInfo l in logins)
            {
                personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
            }

            personalData.Add("Authenticator Key", (await userManager.GetAuthenticatorKeyAsync(user))!);
            byte[] fileBytes = JsonSerializer.SerializeToUtf8Bytes(personalData);

            _ = context.Response.Headers.TryAdd(
                "Content-Disposition",
                "attachment; filename=PersonalData.json");
            return TypedResults.File(
                fileBytes,
                contentType: "application/json",
                fileDownloadName: "PersonalData.json");
        });

        return accountGroup;
    }
}
