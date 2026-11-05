using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Testing.DependencyInjection;

public class ServiceScopeFactory : IServiceScopeFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceScopeFactory()
    {
        _serviceProvider = new ServiceProvider();
    }

    public ServiceScopeFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IServiceScope CreateScope()
    {
        return new ServiceScope(_serviceProvider);
    }

    private class ServiceScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; }

        public ServiceScope(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public void Dispose()
        {
        }
    }
}
