using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Controllers.v1;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace IniaAi.WebApi.UnitTests.Authorization;

/// <summary>
/// Staff work every day without SuperAdmin: deployment-wide authoring admits editors, operations
/// admit operators, and SuperAdmin stays for break-glass work. Each row names the roles an endpoint
/// admits once every <see cref="AuthorizeAttribute"/> on it and on its controller is combined; ASP.NET
/// requires every attribute to pass, so a role must appear in each one to get through.
/// </summary>
public class RoleGateMatrixTests
{
    public static TheoryData<Type, string, string[]> Gates => new()
    {
        { typeof(DocumentCategoriesController), nameof(DocumentCategoriesController.CreateDocumentCategory), [Roles.WORKFLOW_EDITOR, Roles.SUPER_ADMIN] },
        { typeof(DocumentCategoriesController), nameof(DocumentCategoriesController.UpdateDocumentCategory), [Roles.WORKFLOW_EDITOR, Roles.SUPER_ADMIN] },
        { typeof(DocumentCategoriesController), nameof(DocumentCategoriesController.DeleteDocumentCategory), [Roles.WORKFLOW_EDITOR, Roles.SUPER_ADMIN] },
        { typeof(CounterpartyTypesController), nameof(CounterpartyTypesController.CreateCounterpartyType), [Roles.WORKFLOW_EDITOR, Roles.SUPER_ADMIN] },
        { typeof(CounterpartyTypesController), nameof(CounterpartyTypesController.UpdateCounterpartyType), [Roles.WORKFLOW_EDITOR, Roles.SUPER_ADMIN] },
        { typeof(CounterpartyTypesController), nameof(CounterpartyTypesController.DeleteCounterpartyType), [Roles.WORKFLOW_EDITOR, Roles.SUPER_ADMIN] },
        { typeof(DiagnosticsController), nameof(DiagnosticsController.DownloadLog), [Roles.OPERATOR, Roles.SUPER_ADMIN] },
        { typeof(ModelPricingController), nameof(ModelPricingController.GetModelPricings), [Roles.OPERATOR, Roles.SUPER_ADMIN] },
        { typeof(ModelPricingController), nameof(ModelPricingController.Update), [Roles.OPERATOR, Roles.SUPER_ADMIN] },
        { typeof(TenantModelSettingsController), nameof(TenantModelSettingsController.GetTenantModelSettings), [Roles.OPERATOR, Roles.SUPER_ADMIN] },
        { typeof(TenantModelSettingsController), nameof(TenantModelSettingsController.Update), [Roles.OPERATOR, Roles.SUPER_ADMIN] },
        { typeof(TokenUsageController), nameof(TokenUsageController.GetOrganizationDashboard), [Roles.OPERATOR, Roles.SUPER_ADMIN, Roles.ADMIN] },
        { typeof(TokenUsageController), nameof(TokenUsageController.ListTokenUsage), [Roles.OPERATOR, Roles.SUPER_ADMIN, Roles.ADMIN] },
        { typeof(TokenUsageController), nameof(TokenUsageController.GetWorkflowTypeUsageSummary), [Roles.OPERATOR, Roles.SUPER_ADMIN, Roles.ADMIN] },
        { typeof(DocumentsController), nameof(DocumentsController.DeleteDocument), [Roles.ADMIN, Roles.SUPER_ADMIN, Roles.OPERATOR, Roles.USER] },
        { typeof(DocumentsController), nameof(DocumentsController.BulkDeleteDocuments), [Roles.ADMIN, Roles.SUPER_ADMIN, Roles.OPERATOR, Roles.USER] },
        { typeof(WorkflowTypeFeedbackController), nameof(WorkflowTypeFeedbackController.ListFeedback), [Roles.SUPER_ADMIN] },
        { typeof(WorkflowReportsController), nameof(WorkflowReportsController.UpdateFeedbackStatus), [Roles.WORKFLOW_EDITOR, Roles.SUPER_ADMIN] },
    };

    [Theory]
    [MemberData(nameof(Gates))]
    public void TheEndpoint_AdmitsExactlyItsRoles(Type controller, string methodName, string[] expected)
    {
        Assert.Equal(expected.Order(StringComparer.Ordinal), AdmittedRoles(controller, methodName).Order(StringComparer.Ordinal));
    }

    internal static IEnumerable<string> AdmittedRoles(Type controller, string methodName)
    {
        List<string[]> gates = RoleGates(controller, methodName);

        Assert.NotEmpty(gates);

        return gates.Skip(1).Aggregate(gates[0].AsEnumerable(), (admitted, gate) => admitted.Intersect(gate));
    }

    private static List<string[]> RoleGates(Type controller, string methodName)
    {
        MethodInfo method = ControllerMethods.Get(controller, methodName);

        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false));

        return [.. controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(method.GetCustomAttributes<AuthorizeAttribute>(inherit: false))
            .Where(authorize => !string.IsNullOrEmpty(authorize.Roles))
            .Select(authorize => authorize.Roles!.Split(','))];
    }
}
