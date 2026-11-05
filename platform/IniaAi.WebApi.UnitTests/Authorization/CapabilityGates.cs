using IniaAi.WebApi.Attributes;
using System.Reflection;

namespace IniaAi.WebApi.UnitTests.Authorization;

internal static class CapabilityGates
{
    /// <summary>The capabilities an endpoint needs, from its own attributes and its controller's.</summary>
    public static string[] Of(Type controller, string? methodName = null)
    {
        IEnumerable<RequiresCapabilityAttribute> gates = controller.GetCustomAttributes<RequiresCapabilityAttribute>(inherit: true);
        if (methodName is not null)
        {
            gates = gates.Concat(ControllerMethods.Get(controller, methodName).GetCustomAttributes<RequiresCapabilityAttribute>(inherit: true));
        }

        return [.. gates.Select(gate => gate.Capability).Distinct(StringComparer.Ordinal)];
    }
}
