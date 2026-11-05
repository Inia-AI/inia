using System.Collections.Frozen;

namespace IniaAi.Application.Common.Dispatching;

internal sealed class RequestDispatchers(FrozenDictionary<Type, object> byRequestType)
{
    private readonly FrozenDictionary<Type, object> _byRequestType = byRequestType;

    public RequestDispatcher<TResponse> For<TResponse>(IRequest<TResponse> request)
    {
        Type requestType = request.GetType();

        if (!_byRequestType.TryGetValue(requestType, out object? dispatcher))
        {
            throw new InvalidOperationException($"No handler is registered for {requestType.FullName}.");
        }

        return (RequestDispatcher<TResponse>)dispatcher;
    }
}
