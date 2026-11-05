using System.Reflection;

namespace IniaAi.WebApi.UnitTests.Authorization;

internal static class ControllerMethods
{
    public static MethodInfo Get(Type controllerType, string methodName)
    {
        MethodInfo? method = controllerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        return method!;
    }
}
