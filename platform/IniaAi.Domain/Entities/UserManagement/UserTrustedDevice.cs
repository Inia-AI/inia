namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// Represents a device that has been trusted for 2FA bypass.
/// </summary>
public class UserTrustedDevice
{
    /// <summary>
    /// Unique identifier for the trusted device record.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The ID of the user who owns this trusted device.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// A unique fingerprint identifying the device (browser fingerprint, device ID, etc.).
    /// </summary>
    public string DeviceFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Optional friendly name for the device (e.g., "Chrome on Windows").
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// The date and time until which this device is trusted.
    /// </summary>
    public DateTime TrustedUntil { get; set; }

    /// <summary>
    /// The IP address from which the device was trusted.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// The User-Agent string of the device.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// When this trusted device record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    #region Navigation Properties

    /// <summary>
    /// The user who owns this trusted device.
    /// </summary>
    public User? User { get; set; }

    #endregion
}
