using Microsoft.AspNetCore.Hosting;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// The API on a real Kestrel server on a free port, for a test of what the in-memory test server
/// does not do, such as enforcing the request body size limit. The limit is small so a test
/// passes it with a small body.
/// </summary>
public class KestrelWebApplicationFactory : CustomWebApplicationFactory
{
    public const int MAX_REQUEST_BODY_BYTES = 64 * 1024;

    public KestrelWebApplicationFactory()
    {
        UseKestrel(0);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        _ = builder.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MAX_REQUEST_BODY_BYTES);
    }
}
