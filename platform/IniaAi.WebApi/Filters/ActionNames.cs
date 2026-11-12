using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace IniaAi.WebApi.Filters;

/// <summary>
/// The name a refusal is logged under: the controller and the action, never the request's path,
/// so no id, token or query text of a request reaches the log.
/// </summary>
public static class ActionNames
{
    private const string UNNAMED = "unnamed action";

    /// <summary>The controller and action of a controller action, such as <c>Documents.ListDocuments</c>.</summary>
    public static string Of(ActionDescriptor action)
    {
        return action is ControllerActionDescriptor controllerAction
            ? $"{controllerAction.ControllerName}.{controllerAction.ActionName}"
            : UNNAMED;
    }
}
