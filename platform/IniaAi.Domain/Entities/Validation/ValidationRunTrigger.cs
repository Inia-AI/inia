namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// What started the act this run belongs to. Default <see cref="User"/>, so every run written
/// before the schedule existed reads as what it was — a run somebody pressed.
/// </summary>
public enum ValidationRunTrigger
{
    User = 0,
    Schedule = 1,
}
