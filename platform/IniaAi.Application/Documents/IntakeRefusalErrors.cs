using ErrorOr;

namespace IniaAi.Application.Documents;

/// <summary>
/// The error a gate refusal travels as on the wire, whichever mouth refused. The code is the
/// contract, <c>File.Refused.</c> followed by the reason; the metadata key lets a caller route it.
/// </summary>
public static class IntakeRefusalErrors
{
    public const string CODE_PREFIX = "File.Refused.";
    public const string REASON_METADATA_KEY = "IntakeRefusalReason";

    public static Error Refused(string reasonCode, string detail)
    {
        return Error.Validation(
            CODE_PREFIX + reasonCode,
            detail,
            new Dictionary<string, object> { [REASON_METADATA_KEY] = reasonCode });
    }

    public static Error Refused(IntakeScreening screening)
    {
        return Refused(screening.ReasonCode!, screening.Detail!);
    }

    public static bool IsRefusal(Error error)
    {
        return error.Metadata?.ContainsKey(REASON_METADATA_KEY) == true;
    }
}
