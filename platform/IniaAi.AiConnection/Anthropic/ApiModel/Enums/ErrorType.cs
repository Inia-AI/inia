using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Enums;

/// <summary>
/// Represents the type of error that occurred.
/// </summary>
internal enum ErrorType
{
    /// <summary>
    /// There was an issue with the format or content of your request.
    /// </summary>
    [EnumMember(Value = "invalid_request_error")]
    InvalidRequest = 400,

    /// <summary>
    /// There's an issue with your API key.
    /// </summary>
    [EnumMember(Value = "authentication_error")]
    Authentication = 401,

    /// <summary>
    /// Your API key does not have permission to use the specified resource.
    /// </summary>
    [EnumMember(Value = "permission_error")]
    Permission = 403,

    /// <summary>
    /// The requested resource was not found.
    /// </summary>
    [EnumMember(Value = "not_found_error")]
    NotFound = 404,

    /// <summary>
    /// Your account has hit a rate limit.
    /// </summary>
    [EnumMember(Value = "rate_limit_error")]
    RateLimit = 429,

    /// <summary>
    /// An unexpected error has occurred internal to Anthropic's systems.
    /// </summary>
    [EnumMember(Value = "api_error")]
    Api = 500,

    /// <summary>
    /// Anthropic's API is temporarily overloaded.
    /// </summary>
    [EnumMember(Value = "overloaded_error")]
    Overloaded = 529
}
