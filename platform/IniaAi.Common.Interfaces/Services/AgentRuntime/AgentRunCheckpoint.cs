namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// One finished run's session bundle as the runtime served it: the compressed manifest bytes
/// and the digest the runtime declared for them in the fetch response's header. The two travel
/// together because the digest is what makes the bytes checkable — a caller re-hashes
/// <see cref="Content"/> and refuses it unless it matches <see cref="DeclaredSha256"/>.
/// </summary>
public sealed record AgentRunCheckpoint(byte[] Content, string DeclaredSha256)
{
    /// <summary>
    /// Which checkpoint of the run this is, in the runtime's own numbering. The fetch route
    /// serves the terminal bundle, which the runtime numbers <c>-1</c> so it can never collide
    /// with a pause segment.
    /// </summary>
    public const int TERMINAL_SEGMENT = -1;
}
