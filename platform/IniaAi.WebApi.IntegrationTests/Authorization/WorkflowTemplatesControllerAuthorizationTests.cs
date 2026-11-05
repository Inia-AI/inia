using IniaAi.WebApi.Controllers.v1;
using Microsoft.AspNetCore.Authorization;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// Reflection-based guards for the WorkflowTemplates authorization model. These assert the
/// attribute wiring directly (no running server required), protecting the security decision that
/// every workflow-editing endpoint (list, agent guide, content read/write, type delete) is restricted to
/// WorkflowEditor or SuperAdmin — NOT Admin, NOT plain users. The pre-existing CreateWorkflow-page
/// summary endpoint stays open to any authenticated user.
/// </summary>
public class WorkflowTemplatesControllerAuthorizationTests
{
    private static readonly Type ControllerType = typeof(WorkflowTemplatesController);

    [Theory]
    [InlineData(nameof(WorkflowTemplatesController.GetSummary))]
    public void CreateWorkflowSummaryEndpoint_DoesNotRestrictByRole(string methodName)
    {
        AuthorizeAttribute? authorize = ControllerAuthorizationAssertions.GetMethodAuthorize(ControllerType, methodName);

        Assert.True(authorize is null || string.IsNullOrEmpty(authorize.Roles));
    }

    [Theory]
    [InlineData(nameof(WorkflowTemplatesController.GetAgentGuide))]
    [InlineData(nameof(WorkflowTemplatesController.GetWorkflowTemplates))]
    [InlineData(nameof(WorkflowTemplatesController.GetContent))]
    [InlineData(nameof(WorkflowTemplatesController.SaveContent))]
    [InlineData(nameof(WorkflowTemplatesController.GetVersions))]
    [InlineData(nameof(WorkflowTemplatesController.GetVersionContent))]
    [InlineData(nameof(WorkflowTemplatesController.GetVersionChanges))]
    [InlineData(nameof(WorkflowTemplatesController.CompareVersions))]
    [InlineData(nameof(WorkflowTemplatesController.DuplicateWorkflowType))]
    [InlineData(nameof(WorkflowTemplatesController.DeleteWorkflowType))]
    [InlineData(nameof(WorkflowTemplatesController.ExportWorkflowTemplate))]
    [InlineData(nameof(WorkflowTemplatesController.ImportWorkflowTemplatePreview))]
    [InlineData(nameof(WorkflowTemplatesController.ImportWorkflowTemplateApply))]
    [InlineData(nameof(WorkflowTemplatesController.ListDocumentClasses))]
    [InlineData(nameof(WorkflowTemplatesController.GetDocumentClass))]
    [InlineData(nameof(WorkflowTemplatesController.CreateDocumentClass))]
    [InlineData(nameof(WorkflowTemplatesController.UpdateDocumentClass))]
    [InlineData(nameof(WorkflowTemplatesController.RenameDocumentClass))]
    [InlineData(nameof(WorkflowTemplatesController.DeleteDocumentClass))]
    [InlineData(nameof(WorkflowTemplatesController.PromoteSharedComponent))]
    [InlineData(nameof(WorkflowTemplatesController.MaterializeSharedComponent))]
    [InlineData(nameof(WorkflowTemplatesController.GetWorkflowSharedComponentReconcile))]
    [InlineData(nameof(WorkflowTemplatesController.ApplySharedComponentReconcile))]
    public void EditingEndpoints_RestrictToWorkflowEditorAndSuperAdminOnly(string methodName)
    {
        ControllerAuthorizationAssertions.AssertRestrictedToWorkflowEditorAndSuperAdmin(ControllerType, methodName);
    }

    [Fact]
    public void Controller_DoesNotLockEntireClassToARole()
    {
        ControllerAuthorizationAssertions.AssertClassNotLockedToARole(ControllerType);
    }
}
