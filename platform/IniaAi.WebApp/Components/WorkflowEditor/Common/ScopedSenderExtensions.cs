using IniaAi.Application.Common.Dispatching;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// Dispatches one request from a Blazor component through its own short-lived DI scope, so
/// long-lived circuits never hold scoped services (DbContexts) across renders.
/// </summary>
public static class ScopedSenderExtensions
{
    public static async Task<TResponse> SendScopedAsync<TResponse>(
        this IServiceScopeFactory scopeFactory,
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        return await sender.Send(request, cancellationToken);
    }
}
