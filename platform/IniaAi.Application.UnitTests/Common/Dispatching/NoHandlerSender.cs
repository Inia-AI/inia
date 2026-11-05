using IniaAi.Application.Common.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Common.Dispatching;

/// <summary>A dispatcher with no handlers, for a handler under test that has nothing to send.</summary>
internal static class NoHandlerSender
{
    public static ISender Create()
    {
        return new ServiceCollection().AddRequestDispatching([]).BuildServiceProvider().GetRequiredService<ISender>();
    }
}
