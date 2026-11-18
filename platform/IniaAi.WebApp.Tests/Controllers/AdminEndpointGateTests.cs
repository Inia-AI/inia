using IniaAi.WebApp.Attributes;
using IniaAi.WebApp.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Controllers;

/// <summary>
/// Every controller action of the admin app carries a role gate or the processor's key gate, or
/// is named here with the reason it is open. Pages have their own gate tests; a controller added
/// without a gate fails here instead of shipping open to anyone who reaches the host.
/// </summary>
public class AdminEndpointGateTests
{
    private const string CONTROLLER_SUFFIX = "Controller";

    private static readonly Dictionary<string, string> _open = new(StringComparer.Ordinal);

    [Fact]
    public void EveryAction_HasAGate_OrIsOpenForAStatedReason()
    {
        List<string> ungated = [.. Actions().Where(action => !action.Gated && !_open.ContainsKey(action.Name)).Select(action => action.Name)];

        Assert.True(ungated.Count == 0, "No gate and not on the open list: " + string.Join(", ", ungated));
    }

    [Fact]
    public void EveryOpenEntry_NamesALiveActionWithoutAGate()
    {
        Dictionary<string, bool> gatedByName = Actions().ToDictionary(action => action.Name, action => action.Gated, StringComparer.Ordinal);

        Assert.All(_open.Keys, name => Assert.True(gatedByName.TryGetValue(name, out bool gated) && !gated, name));
    }

    [Fact]
    public void TheListSeesTheControllersItShould()
    {
        Assert.Contains(Actions(), action => action is { Name: "FileDownload.DownloadDocument", Gated: true });
        Assert.Contains(Actions(), action => action is { Name: "Log.DownloadLog", Gated: true } or { Name: "Log.Download", Gated: true });
    }

    private static IEnumerable<(string Name, bool Gated)> Actions()
    {
        return typeof(FileDownloadController).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && typeof(IActionResult).IsAssignableFrom(Unwrap(method.ReturnType)))
                .Select(method => ($"{controller.Name[..^CONTROLLER_SUFFIX.Length]}.{method.Name}", Gated(controller) || Gated(method))));
    }

    private static Type Unwrap(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) ? type.GetGenericArguments()[0] : type;
    }

    private static bool Gated(MemberInfo member)
    {
        return member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(authorize => !string.IsNullOrEmpty(authorize.Roles) || !string.IsNullOrEmpty(authorize.Policy))
            || member.GetCustomAttributes<RoleOrMessageProcessorApiKeyAuthorizeAttribute>(inherit: true).Any();
    }
}
