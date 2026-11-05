using IniaAi.Domain.Entities.UserManagement;
using System.Security.Claims;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Service for generating authentication tokens.
/// </summary>
public interface ITokenGenerationService
{
    /// <summary>
    /// Generates a ClaimsIdentity with all required claims for a user.
    /// </summary>
    /// <param name="user">The user to generate claims for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ClaimsIdentity with user claims.</returns>
    Task<ClaimsIdentity> GenerateClaimsIdentityAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a JWT access token for a user.
    /// </summary>
    /// <param name="user">The user to generate the token for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The JWT access token string.</returns>
    Task<string> GenerateAccessTokenAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a per-run JWT for the agent runtime: the user's standard claims plus an
    /// execution_id claim binding the token to a single run, with the distinct "agent-runtime"
    /// audience and narrow "agent_runtime" scope. Session-authenticated endpoints refuse it because
    /// it carries no "at+jwt" type header; only agent-callback endpoints accept it. Validity comes
    /// from AgentRuntimeSettings (long enough to outlive a paused run awaiting input).
    /// </summary>
    /// <param name="user">The user the run executes on behalf of.</param>
    /// <param name="executionId">The workflow execution the token is scoped to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signed JWT string.</returns>
    Task<string> GenerateRunTokenAsync(User user, string executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs a run token from a prebuilt identity without touching the database — for callers
    /// minting many run tokens for one user (a workflow block per run): build the identity once
    /// with <see cref="GenerateClaimsIdentityAsync"/>, then sign per run cheaply. The identity is
    /// never mutated; each call signs a cloned copy carrying that run's execution_id claim.
    /// </summary>
    /// <param name="identity">The user's prebuilt claims identity.</param>
    /// <param name="executionId">The workflow execution the token is scoped to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signed JWT string.</returns>
    Task<string> GenerateRunTokenAsync(
        ClaimsIdentity identity,
        string executionId,
        CancellationToken cancellationToken = default);
}
