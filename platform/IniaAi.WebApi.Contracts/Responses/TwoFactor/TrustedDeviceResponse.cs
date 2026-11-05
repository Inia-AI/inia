namespace IniaAi.WebApi.Contracts.Responses.TwoFactor;

/// <summary>
/// Response containing information about a trusted device.
/// </summary>
public class TrustedDeviceResponse
{
    /// <summary>
    /// Unique identifier for the trusted device.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Name or description of the device.
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// When the device was trusted.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the trust expires.
    /// </summary>
    public DateTime TrustedUntil { get; set; }

    /// <summary>
    /// Whether this is the current device.
    /// </summary>
    public bool IsCurrent { get; set; }
}
