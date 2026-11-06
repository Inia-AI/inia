using IniaAi.Application.Common.Capabilities;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Controllers.v1;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace IniaAi.WebApi.UnitTests.Authorization;

/// <summary>
/// Each endpoint in the core design's capability table carries its capability, so a closed module
/// that takes the capability away gets a 403 from the server, not only a hidden button. The
/// closed modules' own endpoints are checked in their test folders.
/// </summary>
public class CapabilityGateMatrixTests
{
    public static TheoryData<Type, string?, string> Gates => new()
    {
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.StartWorkflowExecution), OrganizationCapabilities.RUN },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.Submit), OrganizationCapabilities.RUN },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.GetReRunInputs), OrganizationCapabilities.RUN },
        { typeof(FilesController), nameof(FilesController.UploadAsync), OrganizationCapabilities.UPLOAD },
        { typeof(WorkflowExecutionsGraphController), null, OrganizationCapabilities.EXPLORE },
        { typeof(AiContentRefinementController), null, OrganizationCapabilities.REFINE },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.GetExecutionLogs), OrganizationCapabilities.RUN_LOGS },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.GetExecutionLogById), OrganizationCapabilities.RUN_LOGS },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.GetExecutionLogPlaceholders), OrganizationCapabilities.RUN_LOGS },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.GetAgentActivity), OrganizationCapabilities.RUN_LOGS },
        { typeof(ExecutedWorkflowsController), nameof(ExecutedWorkflowsController.Download), OrganizationCapabilities.RUN_EVIDENCE },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.ListAgentRunFiles), OrganizationCapabilities.RUN_EVIDENCE },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.DownloadAgentRunTrace), OrganizationCapabilities.RUN_EVIDENCE },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.DownloadAgentRunFile), OrganizationCapabilities.RUN_EVIDENCE },
        { typeof(WorkflowReportsController), nameof(WorkflowReportsController.GetCost), OrganizationCapabilities.RUN_EVIDENCE },
        { typeof(TokenUsageController), nameof(TokenUsageController.GetMyUsageSummary), OrganizationCapabilities.USAGE_COST },
        { typeof(UsersController), nameof(UsersController.GetUsers), OrganizationCapabilities.TEAM_VIEW },
        { typeof(SignaturesController), null, OrganizationCapabilities.SIGNATURES },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.GetExecutionBlocks), OrganizationCapabilities.SIGNATURES },
        { typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.TestSignaturePrompt), OrganizationCapabilities.SIGNATURES },
    };

    [Theory]
    [MemberData(nameof(Gates))]
    public void TheEndpoint_NeedsItsCapability(Type controller, string? methodName, string capability)
    {
        Assert.Equal([capability], CapabilityGates.Of(controller, methodName));
    }

    /// <summary>
    /// Standalone processing stores and pages a document outside a run, so it also needs upload: an
    /// organization that cannot upload cannot store or page a document this way either.
    /// </summary>
    [Theory]
    [InlineData(nameof(DocumentsController.ProcessDocument))]
    [InlineData(nameof(DocumentsController.CreateTextDocument))]
    public void StandaloneProcessing_NeedsItsCapabilityAndUpload(string methodName)
    {
        Assert.Equal([OrganizationCapabilities.STANDALONE_PROCESSING, OrganizationCapabilities.UPLOAD], CapabilityGates.Of(typeof(DocumentsController), methodName));
    }

    [Theory]
    [InlineData(typeof(UsersController), nameof(UsersController.GetMyTenants))]
    [InlineData(typeof(WorkflowReportsController), nameof(WorkflowReportsController.GetById))]
    [InlineData(typeof(WorkflowExecutionsController), nameof(WorkflowExecutionsController.UpdateState))]
    [InlineData(typeof(DocumentsController), nameof(DocumentsController.ListDocuments))]
    public void AnEndpointEveryCustomerUses_NeedsNoCapability(Type controller, string methodName)
    {
        Assert.Empty(CapabilityGates.Of(controller, methodName));
    }

    [Theory]
    [InlineData(nameof(DocumentsController.DeleteDocument))]
    [InlineData(nameof(DocumentsController.BulkDeleteDocuments))]
    public void DocumentDeletion_LetsStaffThrough_AndAnyoneElseOnlyWithDeleteOwnDocuments(string methodName)
    {
        StaffOrCapabilityAttribute gate = ControllerMethods.Get(typeof(DocumentsController), methodName).GetCustomAttribute<StaffOrCapabilityAttribute>()!;

        Assert.Equal(Domain.Entities.UserManagement.Roles.ADMIN_AND_OPERATOR_ROLES, gate.StaffRoles);
        Assert.Equal(OrganizationCapabilities.DELETE_OWN_DOCUMENTS, gate.Capability);
    }

    [Fact]
    public void EveryGate_NamesALaunchCapability()
    {
        IEnumerable<string> named = typeof(ApiController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetCustomAttributes<RequiresCapabilityAttribute>()
                .Concat(type.GetMethods().SelectMany(method => method.GetCustomAttributes<RequiresCapabilityAttribute>())))
            .Select(gate => gate.Capability);

        Assert.All(named, capability => Assert.Contains(capability, (IReadOnlySet<string>)OrganizationCapabilities.All));
    }
}
