namespace IniaAi.AiConnection.HttpHandling.Enums;

/// <summary>
/// Types of retry scenarios
/// </summary>
internal enum RetryType
{
    None,
    RateLimit,
    ServiceUnavailable
}
