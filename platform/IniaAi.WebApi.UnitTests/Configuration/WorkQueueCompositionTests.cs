using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Services.Queues;
using IniaAi.WebApi.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// Every work queue the WebApi composition root registers comes with the hosted worker that
/// drains it: a queue without its worker strands its work forever. The slice under test is
/// <c>AddSingletonServices</c>, the method that owns the platform's work-queue registrations — the
/// full <c>AddPresentation</c> root pulls platform-settings resolution and cannot build in a unit
/// test.
/// </summary>
public class WorkQueueCompositionTests
{
    internal static IServiceCollection BuildWebApiSingletonServices()
    {
        ServiceCollection services = new();
        services.AddSingletonServices(new PlatformSettings());
        return services;
    }

    internal static void AssertQueueDrainedByWorker<TRequest, TWorker>(IServiceCollection services)
        where TWorker : QueueWorker<TRequest>
    {
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkQueue<TRequest>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkRunner<TRequest>));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(TWorker));
    }

    [Fact]
    public void AddSingletonServices_LeavesNoWorkQueueWithoutItsDrain()
    {
        AssertEveryQueueIsDrained(BuildWebApiSingletonServices());
    }

    /// <summary>Every registered work queue has its runner and a hosted worker.</summary>
    internal static void AssertEveryQueueIsDrained(IServiceCollection services)
    {
        List<ServiceDescriptor> queues = [.. services.Where(descriptor =>
            descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IWorkQueue<>))];

        Assert.NotEmpty(queues);

        foreach (ServiceDescriptor queue in queues)
        {
            Type request = queue.ServiceType.GenericTypeArguments[0];
            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkRunner<>).MakeGenericType(request));
            Assert.Contains(services, descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType is not null
                && typeof(QueueWorker<>).MakeGenericType(request).IsAssignableFrom(descriptor.ImplementationType));
        }
    }
}
