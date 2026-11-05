using System.Reflection;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Controllers.v1;
using Microsoft.AspNetCore.Authorization;

namespace IniaAi.WebApi.UnitTests.Authorization;

/// <summary>
/// The counterparty bundle transport is an administrator's act: export reveals every email and
/// financial analysis of the organization, import rewrites its master data. Both admit Admin and
/// SuperAdmin only; the CRUD surface stays open to every authenticated user of the organization.
/// </summary>
public class CounterpartiesControllerAuthorizationTests
{
    private static readonly Type ControllerType = typeof(CounterpartiesController);

    [Theory]
    [InlineData(nameof(CounterpartiesController.Export))]
    [InlineData(nameof(CounterpartiesController.Import))]
    public void BundleEndpoints_RestrictToAdminAndSuperAdmin(string methodName)
    {
        MethodInfo method = ControllerMethods.Get(ControllerType, methodName);

        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false));
        AuthorizeAttribute? authorize = method.GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        Assert.NotNull(authorize);
        Assert.Equal(Roles.ADMIN_ROLES, authorize!.Roles);

        string[] roles = authorize.Roles!.Split(',');
        Assert.Contains(Roles.ADMIN, roles);
        Assert.Contains(Roles.SUPER_ADMIN, roles);
        Assert.DoesNotContain(Roles.USER, roles);
        Assert.DoesNotContain(Roles.WORKFLOW_EDITOR, roles);
    }
}
