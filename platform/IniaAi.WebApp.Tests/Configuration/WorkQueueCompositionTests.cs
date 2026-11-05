using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Services.Documents;
using IniaAi.Services.Queues;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IniaAi.WebApp.Tests.Configuration;

/// <summary>
/// Every work queue the WebApp composition root registers is an in-memory per-process
/// singleton, so it comes with the hosted worker that drains it: a queue without its worker
/// strands its work forever.
/// </summary>
public class WorkQueueCompositionTests
{
    internal static IServiceCollection BuildWebAppServices()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformSettings:UserDataDbProvider"] = "PostgreSQL",
        });
        new ServicesConfigurator(builder).SetupServices();
        return builder.Services;
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

    /// <summary>
    /// The document upload and text-document commands run in-process too, and their handlers
    /// take the document processing queue — without it the WebApp resolves the handler and
    /// fails at runtime on the first upload.
    /// </summary>
    [Fact]
    public void SetupServices_DrainsTheDocumentProcessingQueue()
    {
        AssertQueueDrainedByWorker<DocumentProcessingRequest, DocumentProcessingWorker>(BuildWebAppServices());
    }

    [Fact]
    public void SetupServices_LeavesNoWorkQueueWithoutItsDrain()
    {
        IServiceCollection services = BuildWebAppServices();
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
