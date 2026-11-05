using IniaAi.Application.Common.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.Queues;

/// <summary>
/// The queue singleton behind both of its faces (writer for commands, reader for the
/// worker) and the transient runner — without a hosted worker. Real hosts register through
/// the four-type overload in <c>IniaAi.Services.Queues.WorkQueueHosting</c>, which adds the
/// drain; this form is for the test composition root, which is deliberately worker-less.
/// </summary>
public static class WorkQueueRegistration
{
    public static IServiceCollection AddWorkQueue<TQueue, TRequest, TRunner>(this IServiceCollection services)
        where TQueue : class, IWorkQueue<TRequest>, IWorkQueueReader<TRequest>
        where TRunner : class, IWorkRunner<TRequest>
    {
        return services
            .AddSingleton<TQueue>()
            .AddSingleton<IWorkQueue<TRequest>>(sp => sp.GetRequiredService<TQueue>())
            .AddSingleton<IWorkQueueReader<TRequest>>(sp => sp.GetRequiredService<TQueue>())
            .AddTransient<IWorkRunner<TRequest>, TRunner>();
    }
}
