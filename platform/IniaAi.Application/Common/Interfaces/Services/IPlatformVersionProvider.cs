namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Exposes the version of the running platform build to the Application layer.
/// </summary>
public interface IPlatformVersionProvider
{
    /// <summary>
    /// The informational version of the running build, e.g. "1.0.0+&lt;commit sha&gt;"
    /// when CI supplies the source revision.
    /// </summary>
    string PlatformVersion { get; }
}
