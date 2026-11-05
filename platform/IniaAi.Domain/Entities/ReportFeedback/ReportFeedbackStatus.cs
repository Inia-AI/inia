namespace IniaAi.Domain.Entities.ReportFeedback;

/// <summary>
/// Where a feedback item sits in its lifecycle. Only editor roles move an item out of
/// <see cref="Open"/>; a <see cref="Dismissed"/> item carries the reason it was dismissed.
/// Stored as its ordinal: append new members before the closing brace, never insert.
/// </summary>
public enum ReportFeedbackStatus
{
    Open = 0,
    Addressed = 1,
    Dismissed = 2,
}
