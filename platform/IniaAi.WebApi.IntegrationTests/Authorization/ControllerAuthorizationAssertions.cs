using System.Reflection;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Authorization;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

internal static class ControllerAuthorizationAssertions
{
    internal static AuthorizeAttribute? GetMethodAuthorize(Type controllerType, string methodName)
    {
        MethodInfo? method = controllerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        return method!.GetCustomAttribute<AuthorizeAttribute>(inherit: false);
    }

    internal static void AssertRestrictedToWorkflowEditorAndSuperAdmin(Type controllerType, string methodName)
    {
        AuthorizeAttribute? authorize = GetMethodAuthorize(controllerType, methodName);

        Assert.NotNull(authorize);
        Assert.False(string.IsNullOrEmpty(authorize!.Roles));

        string[] roles = authorize.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(Roles.WORKFLOW_EDITOR, roles);
        Assert.Contains(Roles.SUPER_ADMIN, roles);
        Assert.DoesNotContain(Roles.ADMIN, roles);
        Assert.DoesNotContain(Roles.USER, roles);
    }

    internal static void AssertClassNotLockedToARole(Type controllerType)
    {
        IEnumerable<AuthorizeAttribute> classAuthorize = controllerType
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false);

        Assert.All(classAuthorize, attr => Assert.True(string.IsNullOrEmpty(attr.Roles)));
    }
}
