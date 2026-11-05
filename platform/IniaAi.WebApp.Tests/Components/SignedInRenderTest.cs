using Bunit;
using Bunit.TestDoubles;
using IniaAi.Application;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.StoredKeys;
using IniaAi.Services.Jobs;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// Renders an editor-app component the way a signed-in user sees it: MudBlazor, the real
/// <see cref="AppState"/> over a SQLite user-data database, and bUnit's authentication state, which
/// drives every AuthorizeView. Seed users, sign one in, then render. A derived class adds services in
/// its constructor only: bUnit refuses a registration once the first render or database read has
/// built the provider.
/// </summary>
public abstract class SignedInRenderTest : BunitContext
{
    protected const string ORGANIZATION_ID = "org-inia";
    protected const string ORGANIZATION_NAME = "Inia AI";

    private readonly BunitAuthorizationContext _authorization;
    private bool _rolesSeeded;

    [ModuleInitializer]
    internal static void AllowSlowMachinesToFinishRendering()
    {
        // Waits return as soon as they pass, so a longer default costs nothing on a pass; the one-second
        // default fails renders that wait on the database on a loaded CI machine.
        BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);
    }

    /// <param name="userDataInterceptors">Run on every query to the user-data database.</param>
    protected SignedInRenderTest(params IInterceptor[] userDataInterceptors)
    {
        // MudBlazor makes JavaScript calls of its own that no test here is about.
        JSInterop.Mode = JSRuntimeMode.Loose;
        _authorization = AddAuthorization();

        _ = Services.AddMudServices(options =>
        {
            options.SnackbarConfiguration.ShowTransitionDuration = 0;
            options.SnackbarConfiguration.HideTransitionDuration = 0;
        });
        _ = Services.AddSqliteTestDatabaseServices(userDataInterceptors);
        _ = Services.AddApplication();
        _ = Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _ = Services.AddStoredKeys();
        _ = Services.AddSingleton(Options.Create(new PlatformSettings()));
        _ = Services.AddSingleton<IJobsService, JobsService>();
        _ = Services.AddSingleton<IHostApplicationLifetime, ApplicationLifetime>();
        _ = Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = Services.AddSingleton(WebApplication.CreateEmptyBuilder(new WebApplicationOptions()).Environment);
        _ = Services.AddIdentityCore<User>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<UserDataDbContext>();
        _ = Services.AddScoped<IUserSessionRevoker, UserSessionRevoker>();
        _ = Services.AddScoped<UserAccountAdminService>();
        _ = Services.AddScoped<AppState>();
    }

    /// <summary>
    /// Adds a member of <see cref="ORGANIZATION_ID"/> who holds <paramref name="roles"/> in the database.
    /// </summary>
    protected async Task<User> SeedUserAsync(string email, params string[] roles)
    {
        UserDataDbContext db = await DatabaseAsync();

        if (await db.Organizations.FindAsync(ORGANIZATION_ID) is null)
        {
            _ = db.Organizations.Add(new Organization { Id = ORGANIZATION_ID, Name = ORGANIZATION_NAME });
        }

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = email.Split('@')[0],
            LastName = "Tester",
            OrganizationId = ORGANIZATION_ID,
        };
        _ = db.Users.Add(user);
        _ = db.UserOrganizations.Add(new UserOrganization { UserId = user.Id, OrganizationId = ORGANIZATION_ID, IsDefault = true });

        foreach (string role in roles.Distinct())
        {
            IdentityRole identityRole = await db.Roles.SingleAsync(r => r.Name == role);
            _ = db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = identityRole.Id });
        }

        _ = await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Makes <paramref name="user"/> an active member of another organization too.
    /// </summary>
    protected async Task SeedMembershipAsync(User user, string organizationId, string organizationName)
    {
        await SeedAsync(
            new Organization { Id = organizationId, Name = organizationName },
            new UserOrganization { UserId = user.Id, OrganizationId = organizationId });
    }

    /// <summary>
    /// Saves <paramref name="entities"/> to the user-data database.
    /// </summary>
    protected async Task SeedAsync(params object[] entities)
    {
        UserDataDbContext db = await DatabaseAsync();
        db.AddRange(entities);
        _ = await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a user with <paramref name="roles"/> and makes them the signed-in user of the next render.
    /// </summary>
    protected async Task<User> SignInAsync(string email, params string[] roles)
    {
        User user = await SeedUserAsync(email, roles);

        _ = _authorization.SetAuthorized(email);
        // SetRoles rebuilds the principal without the claims set before it (bUnit 2.11.3), so the
        // claims go last.
        _ = _authorization.SetRoles([.. roles.Distinct()]);
        _ = _authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, user.Id));

        return user;
    }

    /// <summary>
    /// Where MudBlazor renders menus, selects and tooltips; set by <see cref="RenderInApp{TComponent}"/>.
    /// </summary>
    protected IRenderedComponent<MudPopoverProvider> Popovers { get; private set; } = null!;

    /// <summary>
    /// Renders <typeparamref name="TComponent"/> beside the popover provider the app's layout renders,
    /// so its menus and tooltips open as they do in the app.
    /// </summary>
    protected IRenderedComponent<TComponent> RenderInApp<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        Popovers ??= Render<MudPopoverProvider>();

        return Render(parameters);
    }

    protected static AngleSharp.Dom.IElement Button<TComponent>(IRenderedComponent<TComponent> rendered, string text)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        return rendered.FindAll("button").First(button => button.TextContent.Trim() == text);
    }

    /// <summary>
    /// Whether the rendered markup holds a control a user can find by <paramref name="name"/>: its
    /// accessible name (aria-label) or the text of a button.
    /// </summary>
    protected static bool Shows(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> rendered, string name)
    {
        return rendered.FindAll("[aria-label]").Any(element => element.GetAttribute("aria-label") == name)
            || rendered.FindAll("button").Any(button => button.TextContent.Trim() == name);
    }

    private async Task<UserDataDbContext> DatabaseAsync()
    {
        UserDataDbContext db = Services.GetRequiredService<UserDataDbContext>();

        if (!_rolesSeeded)
        {
            db.Roles.AddRange(TestServiceProviderBuilder.EveryDefinedRole.Select(role => new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() }));
            _ = await db.SaveChangesAsync();
            _rolesSeeded = true;
        }

        return db;
    }
}
