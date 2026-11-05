using Ardalis.Specification.EntityFrameworkCore;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Constants;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Specifications.Organizations;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Service for generating authentication tokens.
/// </summary>
public class TokenGenerationService : ITokenGenerationService
{
    private const string API_AUDIENCE = "api";
    private const string ACCESS_TOKEN_SCOPE = "offline_access api";
    private const string AGENT_RUNTIME_AUDIENCE = "agent-runtime";
    private const string AGENT_RUNTIME_SCOPE = "agent_runtime";
    private const string SCOPE_CLAIM = "scope";
    private const string OPENIDDICT_TOKEN_ID_CLAIM = "oi_tkn_id";

    private readonly UserManager<User> _userManager;
    private readonly UserDataDbContext _userDataDbContext;
    private readonly JwtSettings _jwtSettings;
    private readonly AgentRuntimeSettings _agentRuntimeSettings;

    public TokenGenerationService(
        UserManager<User> userManager,
        UserDataDbContext userDataDbContext,
        IOptions<JwtSettings> jwtSettings,
        IOptions<AgentRuntimeSettings> agentRuntimeSettings)
    {
        _userManager = userManager;
        _userDataDbContext = userDataDbContext;
        _jwtSettings = jwtSettings?.Value ?? throw new ArgumentNullException(nameof(jwtSettings));
        _agentRuntimeSettings = agentRuntimeSettings?.Value
            ?? throw new ArgumentNullException(nameof(agentRuntimeSettings));
    }

    /// <inheritdoc />
    public async Task<ClaimsIdentity> GenerateClaimsIdentityAsync(User user, CancellationToken cancellationToken = default)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);

        // Add standard claims with OpenIddict destinations for proper token generation
        identity.AddClaim(new Claim(Claims.Subject, user.Id));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id).SetDestinations(Destinations.AccessToken));
        identity.AddClaim(new Claim(Claims.Name, user.UserName ?? string.Empty).SetDestinations(Destinations.AccessToken));
        identity.AddClaim(new Claim(Claims.PreferredUsername, user.UserName ?? string.Empty).SetDestinations(Destinations.AccessToken));
        identity.AddClaim(new Claim(Claims.Email, user.Email ?? string.Empty).SetDestinations(Destinations.AccessToken));

        // Add roles
        IList<string> roles = await _userManager.GetRolesAsync(user);
        foreach (string role in roles)
        {
            identity.AddClaim(new Claim(Claims.Role, role).SetDestinations(Destinations.AccessToken));
        }

        // Add organization claims
        await AddOrganizationClaimsAsync(identity, user, cancellationToken);

        return identity;
    }

    /// <inheritdoc />
    public async Task<string> GenerateAccessTokenAsync(User user, CancellationToken cancellationToken = default)
    {
        ClaimsIdentity identity = await GenerateClaimsIdentityAsync(user, cancellationToken);

        return CreateSignedToken(
            identity,
            _jwtSettings.AccessTokenLifetime,
            API_AUDIENCE,
            ACCESS_TOKEN_SCOPE);
    }

    /// <inheritdoc />
    public async Task<string> GenerateRunTokenAsync(User user, string executionId, CancellationToken cancellationToken = default)
    {
        ClaimsIdentity identity = await GenerateClaimsIdentityAsync(user, cancellationToken);

        return await GenerateRunTokenAsync(identity, executionId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GenerateRunTokenAsync(
        ClaimsIdentity identity,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executionId))
        {
            throw new ArgumentException("Execution ID is required for a run token", nameof(executionId));
        }

        ClaimsIdentity runIdentity = identity.Clone();
        runIdentity.AddClaim(new Claim(TokenClaimNames.ExecutionId, executionId)
            .SetDestinations(Destinations.AccessToken));

        return Task.FromResult(CreateSignedToken(
            runIdentity,
            TimeSpan.FromHours(_agentRuntimeSettings.RunTokenValidityHours),
            AGENT_RUNTIME_AUDIENCE,
            AGENT_RUNTIME_SCOPE));
    }

    /// <summary>
    /// Signs an identity into a JWT with the shared Jwt settings. Audience and scope define the
    /// token's privilege shape: session tokens get "api"/"offline_access api"; run tokens get the
    /// distinct "agent-runtime" audience and narrow "agent_runtime" scope.
    /// Never emit the "at+jwt" type header here: the platform's OpenIddict validation registers no
    /// audiences, so that header — absent on these tokens — is what actually keeps a run token from
    /// authenticating as a session token on every session endpoint.
    /// </summary>
    private string CreateSignedToken(ClaimsIdentity identity, TimeSpan validity, string audience, string scope)
    {
        DateTime now = DateTime.UtcNow;
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
        identity.AddClaim(new Claim(SCOPE_CLAIM, scope));
        identity.AddClaim(new Claim(OPENIDDICT_TOKEN_ID_CLAIM, Guid.NewGuid().ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: audience,
            claims: identity.Claims,
            notBefore: now,
            expires: now.Add(validity),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Adds organization claims to the provided identity for the specified user.
    /// </summary>
    private async Task AddOrganizationClaimsAsync(ClaimsIdentity identity, User user, CancellationToken cancellationToken)
    {
        var memberships = await _userDataDbContext.UserOrganizations
            .WithSpecification(new UserActiveMembershipsSpec(user.Id))
            .Select(uo => new { uo.OrganizationId, uo.IsDefault })
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            return;
        }

        List<string> userOrganizations = memberships.ConvertAll(m => m.OrganizationId);
        identity.AddClaim(new Claim(TokenClaimNames.TenantIds, JsonSerializer.Serialize(userOrganizations))
            .SetDestinations(Destinations.AccessToken));

        string defaultOrgId = memberships.Find(m => m.IsDefault)?.OrganizationId ?? userOrganizations[0];
        identity.AddClaim(new Claim(TokenClaimNames.OrganizationId, defaultOrgId)
            .SetDestinations(Destinations.AccessToken));
        identity.AddClaim(new Claim(TokenClaimNames.TenantId, defaultOrgId)
            .SetDestinations(Destinations.AccessToken));
    }
}
