using IniaAi.Configuration.Security;
using IniaAi.WebApp.Components.Account.Pages;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Configuration;

/// <summary>
/// The admin app's pages that take a credential or send a mail share the API's sign-in limit, so
/// guessing across accounts through the admin app is bounded too.
/// </summary>
public class SignInRateLimitTests
{
    public static TheoryData<Type> LimitedPages => new()
    {
        typeof(Login),
        typeof(LoginWith2fa),
        typeof(LoginWithRecoveryCode),
        typeof(ForgotPassword),
        typeof(ResetPassword),
        typeof(ResendEmailConfirmation),
    };

    [Theory]
    [MemberData(nameof(LimitedPages))]
    public void EachSignInAndMailPage_IsLimited(Type page)
    {
        EnableRateLimitingAttribute? limit = page.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.Equal(SignInRateLimitSettings.POLICY_NAME, limit?.PolicyName);
    }
}
