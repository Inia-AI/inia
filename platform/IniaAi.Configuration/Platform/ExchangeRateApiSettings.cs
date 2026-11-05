namespace IniaAi.Configuration.Platform;

public class ExchangeRateApiSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://v6.exchangerate-api.com";

    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 1000;

    public int CacheExpirationMinutes { get; set; } = 60;
}
