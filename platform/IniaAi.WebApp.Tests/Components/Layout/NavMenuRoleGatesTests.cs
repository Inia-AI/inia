using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Components.Layout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Components.Layout;

/// <summary>
/// The left menu shows a link only to the roles its page admits. The router, not the menu, enforces
/// a page's <see cref="AuthorizeAttribute"/>, so a link shown to a role the page refuses leads to
/// Access Denied, and a link hidden from a role the page admits hides a page the user may open.
/// </summary>
public class NavMenuRoleGatesTests : SignedInRenderTest
{
    private const string EMAIL = "someone@example.test";

    private static readonly List<Type> _pages = [.. typeof(NavMenu).Assembly.GetTypes().Where(type => type.GetCustomAttributes<RouteAttribute>().Any())];

    private static readonly Lazy<Task<string[]>> _everyLink = new(EveryMenuLinkAsync);

    public static TheoryData<string> EveryRole => new(TestServiceProviderBuilder.EveryDefinedRole);

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task TheMenu_LinksExactlyThePagesTheSignedInRolesOpen(string role)
    {
        string[] held = [Roles.USER, role];
        _ = await SignInAsync(EMAIL, held);

        string[] shown = Links(RenderInApp<NavMenu>());

        string[] everyLink = await _everyLink.Value;
        Assert.Equal([.. everyLink.Where(route => Admits(PageAt(route), held))], shown);
    }

    private static async Task<string[]> EveryMenuLinkAsync()
    {
        await using var everyRole = new EveryRoleMenu();
        return await everyRole.LinksAsync();
    }

    private static string[] Links(IRenderedComponent<NavMenu> menu)
    {
        return [.. menu.FindAll("a[href]").Select(link => link.GetAttribute("href")!).Order(StringComparer.Ordinal)];
    }

    private static Type PageAt(string route)
    {
        return _pages.SingleOrDefault(page => page.GetCustomAttributes<RouteAttribute>().Any(attribute => attribute.Template == route))
            ?? throw new InvalidOperationException($"The menu links to {route}, which no page serves.");
    }

    private static bool Admits(Type page, string[] held)
    {
        string? roles = page.GetCustomAttribute<AuthorizeAttribute>()?.Roles;
        return roles is null || roles.Split(',').Intersect(held).Any();
    }

    /// <summary>
    /// The menu as a user who holds every role sees it: every link the menu can show.
    /// </summary>
    private sealed class EveryRoleMenu : SignedInRenderTest
    {
        public async Task<string[]> LinksAsync()
        {
            _ = await SignInAsync(EMAIL, [.. TestServiceProviderBuilder.EveryDefinedRole]);
            return NavMenuRoleGatesTests.Links(RenderInApp<NavMenu>());
        }
    }
}
