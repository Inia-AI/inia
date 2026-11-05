namespace IniaAi.Common.Attributes;

/// <summary>
/// Indicates whether the enum entry is active/supported.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public class ActiveEnumEntryAttribute : Attribute
{
    public bool IsActive { get; }

    public ActiveEnumEntryAttribute(bool isActive = true)
    {
        IsActive = isActive;
    }
}
