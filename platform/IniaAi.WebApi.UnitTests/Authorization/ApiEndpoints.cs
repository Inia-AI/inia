using System.Reflection;
using System.Text.RegularExpressions;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace IniaAi.WebApi.UnitTests.Authorization;

/// <summary>
/// One API endpoint as its attributes declare it: one entry per HTTP method of an action, with its
/// route, its role gates, the capabilities it needs and the parameters its route names.
/// </summary>
public sealed record ApiEndpoint(
    string Action,
    string Method,
    string Path,
    bool HasRoleGate,
    bool IsAnonymous,
    IReadOnlyList<string> RoleSets,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> RouteParameters,
    Type Controller,
    MethodInfo MethodInfo)
{
    /// <summary>The method and the path, as <c>GET api/v1/documents/{id}</c>.</summary>
    public string Route => $"{Method} {Path}";
}

/// <summary>Every endpoint of the API, read from its controllers by reflection.</summary>
public static partial class ApiEndpoints
{
    private const string CONTROLLER_SUFFIX = "Controller";
    private const string VERSION_PARAMETER = "version";

    public static IEnumerable<ApiEndpoint> All()
    {
        return typeof(ApiController).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => EachMethodOf(controller, method)));
    }

    public static ApiEndpoint Of(Type controller, MethodInfo method)
    {
        return EachMethodOf(controller, method).First();
    }

    private static IEnumerable<ApiEndpoint> EachMethodOf(Type controller, MethodInfo method)
    {
        List<AuthorizeAttribute> authorizes = [.. controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true), .. method.GetCustomAttributes<AuthorizeAttribute>()];
        bool anonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null
            || method.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
        List<string> capabilities =
        [
            .. controller.GetCustomAttributes<RequiresCapabilityAttribute>(inherit: true).Select(gate => gate.Capability),
            .. method.GetCustomAttributes<RequiresCapabilityAttribute>().Select(gate => gate.Capability),
        ];

        foreach (HttpMethodAttribute http in method.GetCustomAttributes<HttpMethodAttribute>())
        {
            string path = PathOf(controller, http.Template);
            foreach (string httpMethod in http.HttpMethods)
            {
                yield return new ApiEndpoint(
                    $"{controller.Name[..^CONTROLLER_SUFFIX.Length]}.{method.Name}",
                    httpMethod,
                    path,
                    !anonymous && authorizes.Any(authorize => !string.IsNullOrEmpty(authorize.Roles) || !string.IsNullOrEmpty(authorize.Policy)),
                    anonymous,
                    [.. authorizes.Where(authorize => !string.IsNullOrEmpty(authorize.Roles)).Select(authorize => authorize.Roles!)],
                    capabilities,
                    [.. RouteParameter().Matches(path).Select(match => match.Groups["name"].Value).Where(name => name != VERSION_PARAMETER)],
                    controller,
                    method);
            }
        }
    }

    private static string PathOf(Type controller, string? actionTemplate)
    {
        string controllerTemplate = controller.GetCustomAttribute<RouteAttribute>(inherit: true)?.Template ?? string.Empty;
        string route = actionTemplate is not null && actionTemplate.StartsWith('/')
            ? actionTemplate.TrimStart('/')
            : string.Join('/', new[] { controllerTemplate, actionTemplate }.Where(part => !string.IsNullOrEmpty(part)));

        return route
            .Replace("v{version:apiVersion}", "v1", StringComparison.Ordinal)
            .Replace("[controller]", controller.Name[..^CONTROLLER_SUFFIX.Length].ToLowerInvariant(), StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\{\*?(?<name>[A-Za-z]+)[^}]*\}")]
    internal static partial Regex RouteParameter();
}
