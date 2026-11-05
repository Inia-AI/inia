using System.Reflection;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Controllers.v1;
using Microsoft.AspNetCore.Authorization;

namespace IniaAi.WebApi.UnitTests.Authorization;

/// <summary>
/// Reflection-based guards for the report feedback authorization model: any authenticated reader
/// may leave, list and delete feedback on their organization's reports; moving an item's status and the
/// cross-organization read by workflow type admit the editor roles only. Kept here, beside the
/// validation guards, so CI checks the model on every run.
/// </summary>
public class ReportFeedbackAuthorizationTests
{
    [Theory]
    [InlineData(nameof(WorkflowReportsController.CreateFeedback))]
    [InlineData(nameof(WorkflowReportsController.ListFeedback))]
    [InlineData(nameof(WorkflowReportsController.DeleteFeedback))]
    public void ReaderEndpoints_RequireAuthenticationOnly(string methodName)
    {
        MethodInfo method = GetMethod(typeof(WorkflowReportsController), methodName);

        Assert.NotNull(typeof(WorkflowReportsController).GetCustomAttribute<AuthorizeAttribute>(inherit: true));
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false));

        AuthorizeAttribute? authorize = method.GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        Assert.True(authorize is null || string.IsNullOrEmpty(authorize.Roles));
    }

    [Fact]
    public void StatusChange_RestrictsToEditorRoles()
    {
        MethodInfo method = GetMethod(typeof(WorkflowReportsController), nameof(WorkflowReportsController.UpdateFeedbackStatus));

        AssertEditorRoles(method.GetCustomAttribute<AuthorizeAttribute>(inherit: false));
    }

    [Fact]
    public void CrossOrganizationRead_RestrictsToSuperAdminAtClassLevel()
    {
        Type controller = typeof(WorkflowTypeFeedbackController);

        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false));
        AuthorizeAttribute? authorize = controller.GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        Assert.NotNull(authorize);
        Assert.Equal(Roles.SUPER_ADMIN, authorize!.Roles);

        string[] roles = authorize.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(Roles.WORKFLOW_EDITOR, roles);
        Assert.DoesNotContain(Roles.ADMIN, roles);
        Assert.DoesNotContain(Roles.USER, roles);

        MethodInfo method = GetMethod(controller, nameof(WorkflowTypeFeedbackController.ListFeedback));
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false));
    }

    private static void AssertEditorRoles(AuthorizeAttribute? authorize)
    {
        Assert.NotNull(authorize);
        Assert.Equal(Roles.EDITOR_ROLES, authorize!.Roles);

        string[] roles = authorize.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(Roles.WORKFLOW_EDITOR, roles);
        Assert.Contains(Roles.SUPER_ADMIN, roles);
        Assert.DoesNotContain(Roles.ADMIN, roles);
        Assert.DoesNotContain(Roles.USER, roles);
    }

    private static MethodInfo GetMethod(Type controller, string methodName)
    {
        MethodInfo? method = controller.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        return method!;
    }
}
