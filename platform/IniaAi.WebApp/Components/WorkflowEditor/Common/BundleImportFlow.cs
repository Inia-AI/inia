using ErrorOr;
using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.Application.Queries.WorkflowTransfer.ImportWorkflowTemplatePreview;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using IniaAi.WebApp.Components.WorkflowEditor.Dialogs;
using IniaAi.WebApp.Services;
using MudBlazor;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class BundleImportFlow
{
    private static readonly DialogOptions _dialogOptions = new()
    {
        CloseButton = true,
        MaxWidth = MaxWidth.Large,
        FullWidth = true,
    };

    public static async Task<ImportWorkflowTemplateApplyResult?> RunAsync(
        IServiceScopeFactory scopeFactory,
        IDialogService dialogService,
        AppState appState,
        string organizationId,
        string userId,
        string workflowTypeId,
        WorkflowTransferBundle bundle)
    {
        ErrorOr<ImportWorkflowTemplatePreviewResult> preview = await scopeFactory.SendScopedAsync(
            new ImportWorkflowTemplatePreviewQuery(organizationId, userId, workflowTypeId, bundle));
        if (preview.IsError)
        {
            appState.Notify(
                $"Could not preview the bundle import: {preview.FirstError.Description}",
                Severity.Error);

            return null;
        }

        var parameters = new DialogParameters<ImportBundleDialog>
        {
            { dialog => dialog.OrganizationId, organizationId },
            { dialog => dialog.UserId, userId },
            { dialog => dialog.WorkflowTypeId, workflowTypeId },
            { dialog => dialog.Bundle, bundle },
            { dialog => dialog.Preview, preview.Value },
        };
        IDialogReference dialog = await dialogService.ShowAsync<ImportBundleDialog>(
            "Import workflow bundle",
            parameters,
            _dialogOptions);
        DialogResult? result = await dialog.Result;

        return result is null || result.Canceled || result.Data is not ImportWorkflowTemplateApplyResult applied
            ? null
            : applied;
    }
}
