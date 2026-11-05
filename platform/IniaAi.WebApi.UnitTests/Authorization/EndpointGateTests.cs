using IniaAi.WebApi.Controllers.v1.Base;
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
        foreach (Endpoint endpoint in Endpoint.All())
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
        ILookup<string, Endpoint> endpoints = Endpoint.All().ToLookup(endpoint => endpoint.Action);

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

    [Fact]
    public void AnEndpointWithoutAGate_IsReportedWithItsRoute()
    {
        Endpoint ungated = Endpoint.Of(typeof(UngatedController), nameof(UngatedController.Leak));

        Assert.False(ungated.HasRoleGate);
        Assert.False(ungated.IsAnonymous);
        Assert.Equal("GET api/v1/ungated/leak", ungated.Route);
        Assert.Equal("Ungated.Leak", ungated.Action);
    }

    private sealed record Endpoint(string Action, string Route, bool HasRoleGate, bool IsAnonymous)
    {
        public static IEnumerable<Endpoint> All()
        {
            return typeof(ApiController).Assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(type))
                .SelectMany(controller => controller
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
                    .Select(method => Of(controller, method)));
        }

        public static Endpoint Of(Type controller, string methodName)
        {
            return Of(controller, ControllerMethods.Get(controller, methodName));
        }

        private static Endpoint Of(Type controller, MethodInfo method)
        {
            IEnumerable<AuthorizeAttribute> authorizes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(method.GetCustomAttributes<AuthorizeAttribute>());
            bool anonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null
                || method.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            HttpMethodAttribute http = method.GetCustomAttributes<HttpMethodAttribute>().First();

            return new Endpoint(
                $"{controller.Name[..^CONTROLLER_SUFFIX.Length]}.{method.Name}",
                $"{http.HttpMethods.First()} {RouteOf(controller, http.Template)}",
                !anonymous && authorizes.Any(authorize => !string.IsNullOrEmpty(authorize.Roles) || !string.IsNullOrEmpty(authorize.Policy)),
                anonymous);
        }

        private static string RouteOf(Type controller, string? actionTemplate)
        {
            string controllerTemplate = controller.GetCustomAttribute<RouteAttribute>(inherit: true)?.Template ?? string.Empty;
            string route = actionTemplate is not null && actionTemplate.StartsWith('/')
                ? actionTemplate.TrimStart('/')
                : string.Join('/', new[] { controllerTemplate, actionTemplate }.Where(part => !string.IsNullOrEmpty(part)));

            return route
                .Replace("v{version:apiVersion}", "v1", StringComparison.Ordinal)
                .Replace("[controller]", controller.Name[..^CONTROLLER_SUFFIX.Length].ToLowerInvariant(), StringComparison.Ordinal);
        }
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
