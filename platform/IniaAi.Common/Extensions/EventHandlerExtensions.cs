using Microsoft.Extensions.Logging;

namespace IniaAi.Common;

public static class EventHandlerExtensions
{
    /// <summary>
    /// Raises the event to each subscriber in turn. A subscriber's fault is logged with
    /// <paramref name="failure"/> and never reaches the raiser or the other subscribers.
    /// </summary>
    public static void RaiseEach<T>(this EventHandler<T>? handlers, object sender, T args, ILogger logger, string failure)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<T> handler in handlers.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                handler(sender, args);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "{Failure}", failure);
            }
        }
    }
}
