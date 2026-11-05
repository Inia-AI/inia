namespace IniaAi.Common.Exceptions;

/// <summary>
/// INIA.AI exception related to AI model rate limiting
/// </summary>
public class IniaAiRateLimitException : Exception
{
    /// <summary>
    /// Create a new exception
    /// </summary>
    public IniaAiRateLimitException() : base() { }

    /// <summary>
    /// Create a new exception with the specified message
    /// </summary>
    /// <param name="message">Error message</param>
    public IniaAiRateLimitException(string message) : base(message) { }
}
