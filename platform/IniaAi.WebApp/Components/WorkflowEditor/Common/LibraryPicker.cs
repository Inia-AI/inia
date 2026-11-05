using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using IniaAi.WebApp.Components.WorkflowEditor.Dialogs;
using MudBlazor;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class LibraryPicker
{
    public static async Task<SharedComponentSummaryDto?> PickLibraryComponentAsync(
        this IDialogService dialogService,
        SharedComponentKind kind,
        string title)
    {
        var parameters = new DialogParameters<LibraryPickerDialog>
        {
            { dialog => dialog.Kind, kind },
        };
        IDialogReference dialog = await dialogService.ShowAsync<LibraryPickerDialog>(
            title,
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true });
        DialogResult? result = await dialog.Result;

        return result is null || result.Canceled ? null : result.Data as SharedComponentSummaryDto;
    }
}
