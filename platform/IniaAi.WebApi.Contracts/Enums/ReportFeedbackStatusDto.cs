namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Lifecycle status of a feedback item. Mirrors the domain <c>ReportFeedbackStatus</c>;
/// serialized by name on the wire.
/// </summary>
public enum ReportFeedbackStatusDto
{
    /// <summary>
    /// Recorded and not yet looked at by an editor.
    /// </summary>
    Open = 0,

    /// <summary>
    /// An editor acted on the item.
    /// </summary>
    Addressed = 1,

    /// <summary>
    /// An editor decided not to act on the item; the DTO carries the reason.
    /// </summary>
    Dismissed = 2,
}
