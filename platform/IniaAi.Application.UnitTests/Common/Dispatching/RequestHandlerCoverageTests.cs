using IniaAi.Application.Common.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Common.Dispatching;

public class RequestHandlerCoverageTests
{
    private static readonly Type[] _applicationTypes = typeof(DependencyInjection).Assembly.GetTypes();

    [Fact]
    public void EveryRequestType_HasExactlyOneHandler()
    {
        ILookup<Type, Type> handlersByRequest = RequestDispatchingRegistration
            .HandlerContracts(_applicationTypes)
            .ToLookup(pair => pair.Contract.GenericTypeArguments[0], pair => pair.Handler);

        List<string> wrong = [.. _applicationTypes
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.GetInterfaces().Any(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .Select(request => (Request: request, Handlers: handlersByRequest[request].Count()))
            .Where(coverage => coverage.Handlers != 1)
            .Select(coverage => $"{coverage.Request.FullName}: {coverage.Handlers} handlers")
            .Order()];

        Assert.True(wrong.Count == 0, "Each request needs exactly one handler, or the sender cannot find it:\n  " + string.Join("\n  ", wrong));
    }

    [Fact]
    public void AddApplication_RegistersTheSender()
    {
        ServiceCollection services = new();
        _ = services.AddApplication();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        _ = scope.ServiceProvider.GetRequiredService<ISender>();
    }
}
