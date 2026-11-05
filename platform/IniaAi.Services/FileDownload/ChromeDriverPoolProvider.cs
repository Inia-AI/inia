namespace IniaAi.Services.FileDownload;

internal static class ChromeDriverPoolProvider
{
    private static readonly Lazy<ChromeDriverPool> _lazyPool = new(() =>
        new ChromeDriverPool(
            maxDrivers: 3));

    public static ChromeDriverPool Instance => _lazyPool.Value;
}
