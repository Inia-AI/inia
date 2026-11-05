namespace IniaAi.Application.Common.Dispatching;

internal abstract class RequestDispatcher<TResponse>
{
    public abstract Task<TResponse> Send(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken);
}
