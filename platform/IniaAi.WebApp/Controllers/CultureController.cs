using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApp.Controllers;

[Route("[controller]/[action]")]
public class CultureController : Controller
{
    public IActionResult Set(string culture, string redirectUri)
    {
        if (culture is not null)
        {
            HttpContext.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(
                    new RequestCulture(culture, culture)),
                new CookieOptions { Secure = Request.IsHttps, HttpOnly = true, SameSite = SameSiteMode.Lax });
        }

        return LocalRedirect(redirectUri);
    }
}
