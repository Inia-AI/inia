namespace IniaAi.Services.FileDownload;

/// <summary>
/// How Chrome runs on this host: the user it runs as, null for the app's own user, and whether
/// its sandbox starts. The container runtime decides the sandbox, not the platform: Docker's
/// default seccomp filter refuses the user namespaces it needs, and a pod with no filter allows
/// them.
/// </summary>
internal sealed record BrowserIsolation(bool Sandboxed, string? User, string Description);
