using IniaAi.Common.Constants;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IniaAi.Infrastructure.Tests.Services;

public class TokenGenerationServiceRunTokenTests : IDisposable
{
    private const string SECRET_KEY = "test-signing-secret-key-that-is-long-enough-for-hs256-signing";
    private const string WRONG_SECRET_KEY = "a-completely-different-secret-key-that-must-fail-validation!";
    private const string ISSUER = "https://test-issuer.inia.ai";
    private const string API_AUDIENCE = "api";
    private const string AGENT_RUNTIME_AUDIENCE = "agent-runtime";
    private const string AGENT_RUNTIME_SCOPE = "agent_runtime";
    private const string ACCESS_TOKEN_TYPE_HEADER = "at+jwt";
    private const string ORGANIZATION_ID = "org-1";
    private const string EXECUTION_ID = "exec-42";

    private readonly InMemoryIdentityHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GenerateRunTokenAsync_ValidatesAgainstKeyIssuerAndAgentRuntimeAudience()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();

        string token = await service.GenerateRunTokenAsync(
            user, EXECUTION_ID, TestContext.Current.CancellationToken);

        ClaimsPrincipal principal = ValidateToken(token, AGENT_RUNTIME_AUDIENCE);
        Assert.Equal(EXECUTION_ID, GetClaim(principal, TokenClaimNames.ExecutionId));
        Assert.Equal(ORGANIZATION_ID, GetClaim(principal, TokenClaimNames.OrganizationId));
        Assert.Equal(ORGANIZATION_ID, GetClaim(principal, TokenClaimNames.TenantId));
        Assert.Equal(user.Id, GetClaim(principal, "sub"));
    }

    [Fact]
    public async Task GenerateRunTokenAsync_CarriesNarrowAgentRuntimeScope()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();

        string token = await service.GenerateRunTokenAsync(
            user, EXECUTION_ID, TestContext.Current.CancellationToken);

        ClaimsPrincipal principal = ValidateToken(token, AGENT_RUNTIME_AUDIENCE);
        Assert.Equal(AGENT_RUNTIME_SCOPE, GetClaim(principal, "scope"));
    }

    [Fact]
    public async Task GenerateRunTokenAsync_IsNotShapedLikeASessionToken()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();

        string token = await service.GenerateRunTokenAsync(
            user, EXECUTION_ID, TestContext.Current.CancellationToken);

        _ = Assert.Throws<SecurityTokenInvalidAudienceException>(
            () => ValidateToken(token, API_AUDIENCE));

        JwtSecurityToken decoded = DecodeWithoutValidation(token);
        string? scope = decoded.Claims.FirstOrDefault(c => c.Type == "scope")?.Value;
        Assert.NotNull(scope);
        Assert.DoesNotContain("offline_access", scope);
        Assert.DoesNotContain(API_AUDIENCE, decoded.Audiences);
    }

    [Fact]
    public async Task GenerateRunTokenAsync_DoesNotCarryTheAccessTokenTypeHeader()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();

        string token = await service.GenerateRunTokenAsync(
            user, EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.NotEqual(ACCESS_TOKEN_TYPE_HEADER, DecodeWithoutValidation(token).Header.Typ);
    }

    [Fact]
    public async Task GenerateRunTokenAsync_RejectsAWrongSigningKey()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();

        string token = await service.GenerateRunTokenAsync(
            user, EXECUTION_ID, TestContext.Current.CancellationToken);

        _ = Assert.ThrowsAny<SecurityTokenException>(
            () => ValidateToken(token, AGENT_RUNTIME_AUDIENCE, secretKey: WRONG_SECRET_KEY));
    }

    [Fact]
    public async Task GenerateRunTokenAsync_DefaultsToTwentyFourHourExpiry()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();

        string token = await service.GenerateRunTokenAsync(
            user, EXECUTION_ID, TestContext.Current.CancellationToken);

        JwtSecurityToken decoded = AssertValidAndDecode(token, AGENT_RUNTIME_AUDIENCE);
        TimeSpan validity = decoded.ValidTo - DateTime.UtcNow;
        Assert.InRange(validity, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24.1));
    }

    [Fact]
    public async Task GenerateRunTokenAsync_RespectsConfiguredValidity()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService(runTokenValidityHours: 2);

        string token = await service.GenerateRunTokenAsync(
            user, EXECUTION_ID, TestContext.Current.CancellationToken);

        JwtSecurityToken decoded = AssertValidAndDecode(token, AGENT_RUNTIME_AUDIENCE);
        TimeSpan validity = decoded.ValidTo - DateTime.UtcNow;
        Assert.InRange(validity, TimeSpan.FromHours(1.9), TimeSpan.FromHours(2.1));
    }

    [Fact]
    public async Task GenerateRunTokenAsync_ThrowsOnEmptyExecutionId()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GenerateRunTokenAsync(user, " ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_KeepsSessionShapeAndConfiguredExpiry()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();

        string token = await service.GenerateAccessTokenAsync(user, TestContext.Current.CancellationToken);

        ClaimsPrincipal principal = ValidateToken(token, API_AUDIENCE);
        Assert.Equal(user.Id, GetClaim(principal, "sub"));
        Assert.Equal(ORGANIZATION_ID, GetClaim(principal, TokenClaimNames.OrganizationId));
        Assert.Equal("offline_access api", GetClaim(principal, "scope"));
        Assert.Null(GetClaim(principal, TokenClaimNames.ExecutionId));

        JwtSecurityToken decoded = DecodeWithoutValidation(token);
        TimeSpan validity = decoded.ValidTo - DateTime.UtcNow;
        Assert.InRange(validity, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(65));
    }

    [Fact]
    public async Task GenerateRunTokenAsync_FromAPrebuiltIdentity_SignsPerRunWithoutMutatingIt()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();
        ClaimsIdentity identity = await service.GenerateClaimsIdentityAsync(
            user, TestContext.Current.CancellationToken);
        int claimCountBeforeMinting = identity.Claims.Count();

        string firstToken = await service.GenerateRunTokenAsync(
            identity, "exec-1", TestContext.Current.CancellationToken);
        string secondToken = await service.GenerateRunTokenAsync(
            identity, "exec-2", TestContext.Current.CancellationToken);

        ClaimsPrincipal firstPrincipal = ValidateToken(firstToken, AGENT_RUNTIME_AUDIENCE);
        ClaimsPrincipal secondPrincipal = ValidateToken(secondToken, AGENT_RUNTIME_AUDIENCE);
        Assert.Equal("exec-1", GetClaim(firstPrincipal, TokenClaimNames.ExecutionId));
        Assert.Equal("exec-2", GetClaim(secondPrincipal, TokenClaimNames.ExecutionId));
        Assert.Equal(ORGANIZATION_ID, GetClaim(firstPrincipal, TokenClaimNames.OrganizationId));
        Assert.Equal(AGENT_RUNTIME_SCOPE, GetClaim(firstPrincipal, "scope"));

        _ = Assert.Single(DecodeWithoutValidation(secondToken).Claims, c => c.Type == "jti");
        _ = Assert.Single(DecodeWithoutValidation(secondToken).Claims, c => c.Type == TokenClaimNames.ExecutionId);
        Assert.Equal(claimCountBeforeMinting, identity.Claims.Count());
    }

    [Fact]
    public async Task GenerateRunTokenAsync_FromAPrebuiltIdentity_ThrowsOnEmptyExecutionId()
    {
        User user = await CreateUserWithOrganizationAsync();
        TokenGenerationService service = CreateService();
        ClaimsIdentity identity = await service.GenerateClaimsIdentityAsync(
            user, TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GenerateRunTokenAsync(identity, " ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateClaimsIdentityAsync_MultiOrgUser_CarriesAllTenantsAndTheDefaultOrg()
    {
        User user = await _harness.CreateUserAsync("multi-org-user@inia.ai");
        _harness.Context.Organizations.AddRange(new Organization("A", "org-a"), new Organization("B", "org-b"), new Organization("C", "org-c"));
        _harness.Context.UserOrganizations.AddRange(
            new UserOrganization { UserId = user.Id, OrganizationId = "org-a", IsActive = true, IsDefault = false },
            new UserOrganization { UserId = user.Id, OrganizationId = "org-b", IsActive = true, IsDefault = true },
            new UserOrganization { UserId = user.Id, OrganizationId = "org-c", IsActive = false, IsDefault = false });
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        TokenGenerationService service = CreateService();

        ClaimsIdentity identity = await service.GenerateClaimsIdentityAsync(
            user, TestContext.Current.CancellationToken);

        Claim tenantIds = Assert.Single(identity.Claims, c => c.Type == "tenant_ids");
        string[]? tenants = System.Text.Json.JsonSerializer.Deserialize<string[]>(tenantIds.Value);
        Assert.NotNull(tenants);
        Assert.Equal(["org-a", "org-b"], tenants.Order());
        Assert.Equal("org-b", identity.Claims.Single(c => c.Type == TokenClaimNames.OrganizationId).Value);
        Assert.Equal("org-b", identity.Claims.Single(c => c.Type == TokenClaimNames.TenantId).Value);
    }

    [Fact]
    public async Task GenerateClaimsIdentityAsync_MultiOrgUserWithoutADefault_FallsBackToTheFirstActiveOrg()
    {
        User user = await _harness.CreateUserAsync("no-default-org-user@inia.ai");
        _harness.Context.Organizations.AddRange(new Organization("A", "org-a"), new Organization("B", "org-b"));
        _harness.Context.UserOrganizations.AddRange(
            new UserOrganization { UserId = user.Id, OrganizationId = "org-a", IsActive = true, IsDefault = false },
            new UserOrganization { UserId = user.Id, OrganizationId = "org-b", IsActive = true, IsDefault = false });
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        TokenGenerationService service = CreateService();

        ClaimsIdentity identity = await service.GenerateClaimsIdentityAsync(
            user, TestContext.Current.CancellationToken);

        Assert.Equal("org-a", identity.Claims.Single(c => c.Type == TokenClaimNames.OrganizationId).Value);
    }

    private async Task<User> CreateUserWithOrganizationAsync()
    {
        User user = await _harness.CreateUserAsync("run-token-user@inia.ai");
        user.OrganizationId = ORGANIZATION_ID;
        _ = await _harness.UserManager.UpdateAsync(user);
        _harness.Context.Organizations.Add(new Organization("Run token organization", ORGANIZATION_ID));
        _harness.Context.UserOrganizations.Add(
            new UserOrganization { UserId = user.Id, OrganizationId = ORGANIZATION_ID, IsDefault = true });
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    private TokenGenerationService CreateService(int? runTokenValidityHours = null)
    {
        var jwtSettings = new JwtSettings
        {
            TokenValidityInMinutes = 60,
            SecretKey = SECRET_KEY,
            Issuer = ISSUER,
        };
        var agentRuntimeSettings = new AgentRuntimeSettings();
        if (runTokenValidityHours is not null)
        {
            agentRuntimeSettings.RunTokenValidityHours = runTokenValidityHours.Value;
        }

        return new TokenGenerationService(
            _harness.UserManager,
            _harness.Context,
            Options.Create(jwtSettings),
            Options.Create(agentRuntimeSettings));
    }

    private static ClaimsPrincipal ValidateToken(string token, string audience, string secretKey = SECRET_KEY)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidIssuer = ISSUER,
            ValidAudience = audience,
            ValidateLifetime = true,
        };

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(token, validationParameters, out _);
    }

    private static JwtSecurityToken AssertValidAndDecode(string token, string audience)
    {
        _ = ValidateToken(token, audience);
        return DecodeWithoutValidation(token);
    }

    private static JwtSecurityToken DecodeWithoutValidation(string token)
    {
        return new JwtSecurityTokenHandler().ReadJwtToken(token);
    }

    private static string? GetClaim(ClaimsPrincipal principal, string claimType)
    {
        return principal.FindFirst(claimType)?.Value;
    }
}
