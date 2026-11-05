using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.Common.Dispatching;

internal sealed class HandlerOnlyDispatcher<TRequest, TResponse> : RequestDispatcher<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> Send(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken)
    {
        return services.GetRequiredService<IRequestHandler<TRequest, TResponse>>().Handle((TRequest)request, cancellationToken);
    }
}
