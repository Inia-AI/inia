using IniaAi.Domain.Entities.SharedComponents.Enums;
using MudBlazor;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class SharedComponentKindIcon
{
    public static string For(SharedComponentKind kind)
    {
        return kind == SharedComponentKind.Section
            ? Icons.Material.Outlined.ViewAgenda
            : Icons.Material.Outlined.Widgets;
    }

    public static string For(ContractsKind kind)
    {
        return For((SharedComponentKind)(int)kind);
    }
}
