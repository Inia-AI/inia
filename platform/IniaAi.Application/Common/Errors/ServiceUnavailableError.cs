using ErrorOr;

namespace IniaAi.Application.Errors;

/// <summary>An error the API answers with 503: an outside service the request needs did not answer, or is closed for now.</summary>
public static class ServiceUnavailableError
{
    /// <summary>The <see cref="ErrorType"/> value of an error the API answers with 503.</summary>
    public const int ERROR_TYPE = 503;

    public static Error Of(string code, string description)
    {
        return Error.Custom(ERROR_TYPE, code, description);
    }
}
