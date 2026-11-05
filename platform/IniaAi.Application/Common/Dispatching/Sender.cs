namespace IniaAi.Application.Common.Dispatching;

internal sealed class Sender(IServiceProvider services, RequestDispatchers dispatchers) : ISender
{
    private readonly IServiceProvider _services = services;
    private readonly RequestDispatchers _dispatchers = dispatchers;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _dispatchers.For(request).Send(request, _services, cancellationToken);
    }
}
