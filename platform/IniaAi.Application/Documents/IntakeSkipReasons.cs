using IniaAi.FileFormats.Unlock;

namespace IniaAi.Application.Documents;

/// <summary>
/// The reason a mouth with nobody to ask for a password gives when it leaves a file behind:
/// the gate's refusal detail, or the non-interactive unlock policy's wording for a file that
/// stayed locked. Null when the file was admitted.
/// </summary>
public static class IntakeSkipReasons
{
    public static string? NonInteractive(IntakeScreening screening)
    {
        return screening.Outcome switch
        {
            IntakeOutcome.Admitted => null,
            IntakeOutcome.Refused => screening.Detail!,
            _ => NonInteractiveUnlockPolicy.GetSkipReason(screening.Unlock!.Status) ?? screening.Detail!,
        };
    }
}
