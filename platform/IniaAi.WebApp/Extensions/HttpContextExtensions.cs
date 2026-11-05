namespace IniaAi.WebApp.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// The client's address, as the forwarded-headers settings resolve it behind a proxy.
    /// </summary>
    public static string? ClientAddress(this HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString();
    }
}
