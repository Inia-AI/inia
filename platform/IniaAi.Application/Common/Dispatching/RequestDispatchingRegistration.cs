using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Frozen;
using System.Reflection;

namespace IniaAi.Application.Common.Dispatching;

public static class RequestDispatchingRegistration
{
    public static IServiceCollection AddRequestDispatching(this IServiceCollection services, Assembly assembly)
    {
        return services.AddRequestDispatching(assembly.GetTypes());
    }

    internal static IServiceCollection AddRequestDispatching(this IServiceCollection services, IEnumerable<Type> candidateTypes)
    {
        Dictionary<Type, object> dispatchers = [];

        foreach (IGrouping<Type, (Type Handler, Type Contract)> handlersOfRequest in HandlerContracts(candidateTypes)
            .GroupBy(pair => pair.Contract.GenericTypeArguments[0]))
        {
            List<(Type Handler, Type Contract)> handlers = [.. handlersOfRequest];

            if (handlers.Count > 1)
            {
                throw new InvalidOperationException(
                    $"{handlersOfRequest.Key.FullName} has more than one handler: {string.Join(", ", handlers.Select(pair => pair.Handler.FullName))}.");
            }

            (Type handler, Type contract) = handlers[0];
            _ = services.AddTransient(contract, handler);
            dispatchers[handlersOfRequest.Key] = CreateDispatcher(handlersOfRequest.Key, contract.GenericTypeArguments[1]);
        }

        _ = services.AddSingleton(new RequestDispatchers(dispatchers.ToFrozenDictionary()));
        _ = services.AddTransient<ISender, Sender>();

        return services;
    }

    internal static IEnumerable<(Type Handler, Type Contract)> HandlerContracts(IEnumerable<Type> candidateTypes)
    {
        return candidateTypes
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(contract => (type, contract)));
    }

    private static object CreateDispatcher(Type requestType, Type responseType)
    {
        Type dispatcherType = responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(ErrorOr<>)
            ? typeof(ValidatingDispatcher<,>).MakeGenericType(requestType, responseType.GenericTypeArguments[0])
            : typeof(HandlerOnlyDispatcher<,>).MakeGenericType(requestType, responseType);

        return Activator.CreateInstance(dispatcherType)!;
    }
}
