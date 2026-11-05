using Microsoft.Extensions.Logging;

namespace IniaAi.Testing.DependencyInjection;

internal sealed class ForwardingLogger<T>(ILogger inner) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return inner.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return inner.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
