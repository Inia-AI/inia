using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;

namespace IniaAi.WebApi.UnitTests.Authorization;

/// <summary>
/// Every API endpoint carries a role gate, or is named on <see cref="OpenEndpoints"/> with the reason
/// it is open. Admin and editing endpoints share the API with customers, so an endpoint that is
/// added without a gate fails here instead of shipping open to every signed-in user.
/// </summary>
public class EndpointGateTests
{
    private const string CONTROLLER_SUFFIX = "Controller";

    [Fact]
    public void EveryEndpoint_HasARoleGateOrIsOnTheOpenList()
    {
        ILookup<string, OpenEndpoint> listed = OpenEndpoints.All.ToLookup(endpoint => endpoint.Action);

        List<string> problems = [];
        foreach (ApiEndpoint endpoint in ApiEndpoints.All())
        {
            if (endpoint.HasRoleGate)
            {
                continue;
            }

            if (listed[endpoint.Action].FirstOrDefault() is not OpenEndpoint entry)
            {
                problems.Add(
                    $"{endpoint.Route} ({endpoint.Action}) is open to every {(endpoint.IsAnonymous ? "caller, signed in or not" : "signed-in user")}. "
                    + "Add [Authorize(Roles = ...)] to it, or add it to OpenEndpoints with the reason it is open.");
            }
            else if (entry.Anonymous != endpoint.IsAnonymous)
            {
                problems.Add(
                    $"{endpoint.Route} ({endpoint.Action}) is {(endpoint.IsAnonymous ? "anonymous" : "signed-in only")}, "
                    + $"but OpenEndpoints lists it as {(entry.Anonymous ? "anonymous" : "signed-in only")}. Review it and correct the entry.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryOpenListEntry_NamesAnOpenEndpoint()
    {
        ILookup<string, ApiEndpoint> endpoints = ApiEndpoints.All().ToLookup(endpoint => endpoint.Action);

        List<string> stale = [.. OpenEndpoints.All
            .Where(entry => !endpoints[entry.Action].Any() || endpoints[entry.Action].Any(endpoint => endpoint.HasRoleGate))
            .Select(entry => $"{entry.Action} is on OpenEndpoints but is gone or now has a role gate. Remove the entry.")];

        Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void EveryOpenListEntry_SaysWhyItIsOpen()
    {
        Assert.All(OpenEndpoints.All, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason), entry.Action));
        Assert.Equal(OpenEndpoints.All.Count, OpenEndpoints.All.Select(entry => entry.Action).Distinct().Count());
    }

    /// <summary>
    /// A tenant-optional endpoint skips both tenant checks, so it may take nothing about an
    /// organization or a user from the request. A new one is read for that before it joins this list.
    /// </summary>
    [Fact]
    public void OnlyTheKnownEndpoints_AreTenantOptional()
    {
        IEnumerable<string> tenantOptional = typeof(ApiController).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Where(method => method.GetCustomAttribute<TenantOptionalAttribute>() is not null
                    || controller.GetCustomAttribute<TenantOptionalAttribute>(inherit: true) is not null)
                .Select(method => $"{controller.Name[..^CONTROLLER_SUFFIX.Length]}.{method.Name}"));

        Assert.Equal(["Profile.ChangePasswordAsync", "Profile.SignOutEverywhereAsync", "Users.GetMyTenants"], tenantOptional.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Both tenant checks skip every path that starts with a health or documentation prefix, so a
    /// controller route under one of them would be served with no tenant check at all.
    /// </summary>
    [Fact]
    public void NoControllerRoute_StartsWithAPathTheTenantChecksSkip()
    {
        Assert.True(MiddlewareConstants.ShouldSkipPath("/health"));

        List<string> skipped = [.. ApiEndpoints.All()
            .Where(endpoint => MiddlewareConstants.ShouldSkipPath("/" + endpoint.Path))
            .Select(endpoint => endpoint.Route)];

        Assert.True(skipped.Count == 0, "Served with no tenant check: " + string.Join(", ", skipped));
    }

    [Fact]
    public void AnEndpointWithoutAGate_IsReportedWithItsRoute()
    {
        ApiEndpoint ungated = ApiEndpoints.Of(typeof(UngatedController), ControllerMethods.Get(typeof(UngatedController), nameof(UngatedController.Leak)));

        Assert.False(ungated.HasRoleGate);
        Assert.False(ungated.IsAnonymous);
        Assert.Equal("GET api/v1/ungated/leak", ungated.Route);
        Assert.Equal("Ungated.Leak", ungated.Action);
    }

    [Authorize]
    [Route("api/v{version:apiVersion}/ungated")]
    public sealed class UngatedController : ControllerBase
    {
        [HttpGet("leak")]
        public IActionResult Leak()
        {
            return Ok();
        }
    }
}
