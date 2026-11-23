using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using IniaAi.Configuration.Storage;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// Builds the S3 client for each configured store: Amazon S3 itself, an S3-compatible store named by
/// <see cref="S3StorageSettings.ServiceUrl"/>, or MinIO.
/// </summary>
public static class ObjectStorageClients
{
    private const string HTTP = "http://";
    private const string HTTPS = "https://";
    private const string DEFAULT_COMPATIBLE_REGION = "us-east-1";

    /// <summary>
    /// The client for <c>FileStorage:S3</c>. On AWS it signs with the host's role unless keys are
    /// set; with a service URL it talks to an S3-compatible store.
    /// </summary>
    public static IAmazonS3 ForS3(S3StorageSettings settings)
    {
        var config = new AmazonS3Config();
        if (string.IsNullOrEmpty(settings.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }
        else
        {
            UseCompatibleStore(config, settings.ServiceUrl, settings.Region);
        }

        return string.IsNullOrEmpty(settings.AccessKeyId)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), config);
    }

    /// <summary>
    /// The client for <c>FileStorage:MinIO</c>. MinIO is internal infrastructure, so the client skips
    /// the system proxy: .NET matches no CIDR range in <c>NO_PROXY</c>, and a corporate proxy would
    /// otherwise take the requests.
    /// </summary>
    public static IAmazonS3 ForMinio(MinioStorageSettings settings)
    {
        string host = settings.Endpoint.StartsWith(HTTP) ? settings.Endpoint[HTTP.Length..]
            : settings.Endpoint.StartsWith(HTTPS) ? settings.Endpoint[HTTPS.Length..]
            : settings.Endpoint;
        var config = new AmazonS3Config { HttpClientFactory = new NoProxyHttpClientFactory() };
        UseCompatibleStore(config, (settings.UseSSL ? HTTPS : HTTP) + host, settings.Region);

        return new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config);
    }

    /// <summary>
    /// An S3-compatible store takes path-style addresses, and some reject the checksums the SDK now
    /// sends with every upload, so the client sends them only where the operation requires one.
    /// </summary>
    private static void UseCompatibleStore(AmazonS3Config config, string serviceUrl, string? region)
    {
        config.ServiceURL = serviceUrl;
        config.ForcePathStyle = true;
        config.AuthenticationRegion = string.IsNullOrEmpty(region) ? DEFAULT_COMPATIBLE_REGION : region;
        config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
        config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
    }

    private sealed class NoProxyHttpClientFactory : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig)
        {
            return new HttpClient(new SocketsHttpHandler { UseProxy = false });
        }
    }
}
