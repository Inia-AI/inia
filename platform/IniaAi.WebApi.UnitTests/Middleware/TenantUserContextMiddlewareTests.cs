using Finbuckle.MultiTenant.Abstractions;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.MultiTenant;
using IniaAi.WebApi.Middleware;
using IniaAi.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Text.Json;

namespace IniaAi.WebApi.UnitTests.Middleware;

/// <summary>
/// The middleware asks the tenant access policy whether a signed-in user may act in the organization
/// a request names. The platform's policy keeps the old rule: members get in, and so does a super
/// admin anywhere. A closed module's policy can refuse even a super admin.
/// </summary>
public class TenantUserContextMiddlewareTests
{
    private const string OWN = "org-own";
    private const string OTHER = "org-other";
    private const string USER_ID = "user-1";
    private const string REFUSED_TITLE = "Not this one";

    [Fact]
    public async Task UnderThePlatformRule_AMemberGetsIn_AndANonMemberIsRefused()
    {
        var policy = new MembershipTenantAccessPolicy();

        (bool memberGotIn, _) = await InvokeAsync(OWN, superAdmin: false, policy);
        (bool strangerGotIn, HttpContext refused) = await InvokeAsync(OTHER, superAdmin: false, policy);

        Assert.True(memberGotIn);
        Assert.False(strangerGotIn);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.Response.StatusCode);
        Assert.Equal(MembershipTenantAccessPolicy.TITLE, await ProblemTitleAsync(refused));
    }

    [Fact]
    public async Task UnderThePlatformRule_ASuperAdminGetsIntoAnOrganizationTheyDoNotBelongTo()
    {
        (bool gotIn, _) = await InvokeAsync(OTHER, superAdmin: true, new MembershipTenantAccessPolicy());

        Assert.True(gotIn);
    }

    [Fact]
    public async Task APolicyThatRefusesOneOrganization_KeepsASuperAdminOutOfIt_AndOnlyOfIt()
    {
        var policy = new RefusesOneOrganization(OTHER);

        (bool gotIntoOther, HttpContext refused) = await InvokeAsync(OTHER, superAdmin: true, policy);
        (bool gotIntoOwn, _) = await InvokeAsync(OWN, superAdmin: true, policy);

        Assert.False(gotIntoOther);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.Response.StatusCode);
        Assert.Equal(REFUSED_TITLE, await ProblemTitleAsync(refused));
        Assert.True(gotIntoOwn);
    }

    private static async Task<(bool GotIn, HttpContext Context)> InvokeAsync(string organizationId, bool superAdmin, ITenantAccessPolicy policy)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new AuthorizeAttribute()), "tenant-scoped"));
        context.Request.Path = "/api/v1/workflow-categories/summaries";
        context.Response.Body = new MemoryStream();
        List<Claim> claims = [new("sub", USER_ID), new(TokenClaimNames.TenantIds, JsonSerializer.Serialize(new[] { OWN }))];
        if (superAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.SUPER_ADMIN));
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var tenant = new StaticMultiTenantContextAccessor<OrganizationTenantInfo>(new OrganizationTenantInfo(organizationId, organizationId));
        var currentUser = new CurrentUserService(new HttpContextAccessor { HttpContext = context }, tenant);
        bool gotIn = false;
        var middleware = new TenantUserContextMiddleware(
            _ =>
            {
                gotIn = true;
                return Task.CompletedTask;
            },
            NullLogger<TenantUserContextMiddleware>.Instance);

        await middleware.InvokeAsync(context, new RequestContext(), currentUser, tenant, policy);

        return (gotIn, context);
    }

    private static async Task<string?> ProblemTitleAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using JsonDocument problem = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return problem.RootElement.GetProperty("title").GetString();
    }

    private sealed class RefusesOneOrganization(string refused) : ITenantAccessPolicy
    {
        public ValueTask<TenantAccessDecision> DecideAsync(TenantAccessRequest request, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(request.OrganizationId == refused
                ? TenantAccessDecision.Refuse(REFUSED_TITLE, "This organization refuses staff.")
                : TenantAccessDecision.Allow);
        }
    }
}
