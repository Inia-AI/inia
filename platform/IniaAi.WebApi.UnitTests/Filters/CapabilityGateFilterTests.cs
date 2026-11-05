using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Filters;
using IniaAi.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.UnitTests.Filters;

/// <summary>
/// A gated endpoint answers 403 when the organization lacks its capability, and goes through when the
/// organization has it. The gate is built the way MVC builds it, from the endpoint's attribute.
/// </summary>
public class CapabilityGateFilterTests
{
    private const string ORGANIZATION_ID = "org-1";

    [Fact]
    public async Task AnEndpointBehindARemovedCapability_IsRefusedWith403_NamingTheCapability()
    {
        IActionResult? result = await GateAsync(new OrganizationCapabilitiesWithout(OrganizationCapabilities.USAGE_COST), ORGANIZATION_ID, OrganizationCapabilities.USAGE_COST);

        ObjectResult refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(refused.Value);
        Assert.Equal(CapabilityGateFilter.TITLE, problem.Title);
        Assert.Equal(CapabilityGateFilter.DETAIL, problem.Detail);
        Assert.Equal(OrganizationCapabilities.USAGE_COST, problem.Extensions[CapabilityGateFilter.CAPABILITY_EXTENSION]);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task AnEndpointBehindAKeptCapability_GoesThrough()
    {
        IActionResult? result = await GateAsync(new OrganizationCapabilitiesWithout(OrganizationCapabilities.USAGE_COST), ORGANIZATION_ID, OrganizationCapabilities.RUN);

        Assert.Null(result);
    }

    [Fact]
    public async Task AnEndpointBehindACapability_IsRefused_WhenTheRequestNamesNoOrganization()
    {
        IActionResult? result = await GateAsync(new EveryCapability(), organizationId: null, OrganizationCapabilities.RUN);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task ThePlatformDefault_LetsEveryGatedEndpointThrough()
    {
        foreach (string capability in OrganizationCapabilities.All)
        {
            Assert.Null(await GateAsync(new EveryCapability(), ORGANIZATION_ID, capability));
        }
    }

    private static async Task<IActionResult?> GateAsync(IOrganizationCapabilities capabilities, string? organizationId, string capability)
    {
        var services = new ServiceCollection().AddSingleton(capabilities).AddSingleton<IRequestContext>(new RequestContext { OrganizationId = organizationId });
        _ = services.AddControllers();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        var gate = (IAsyncAuthorizationFilter)new RequiresCapabilityAttribute(capability).CreateInstance(provider);

        await gate.OnAuthorizationAsync(context);

        return context.Result;
    }
}
