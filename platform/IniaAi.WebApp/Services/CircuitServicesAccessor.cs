using Microsoft.AspNetCore.Components.Server.Circuits;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Gives code that runs outside a Blazor circuit's scope, such as a database interceptor built
/// once for the app, the services of the circuit whose event it is handling. This is the pattern
/// in Microsoft's "Access server-side Blazor services from a different DI scope".
/// </summary>
public sealed class CircuitServicesAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> _circuitServices = new();

    public IServiceProvider? Services
    {
        get => _circuitServices.Value;
        set => _circuitServices.Value = value;
    }
}

/// <summary>
/// Hands each inbound circuit event its circuit's services for the length of the event.
/// </summary>
public sealed class CircuitServicesAccessorHandler(IServiceProvider services, CircuitServicesAccessor accessor) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next)
    {
        return async context =>
        {
            accessor.Services = services;
            await next(context);
            accessor.Services = null;
        };
    }
}
