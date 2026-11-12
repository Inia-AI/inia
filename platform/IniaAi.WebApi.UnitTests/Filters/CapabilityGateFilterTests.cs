using System.Reflection;
using IniaAi.Application.Common.Reports;
using IniaAi.Domain.Entities.UserManagement;
using System.Security.Claims;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Testing;
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
using Microsoft.Extensions.Logging;

namespace IniaAi.WebApi.UnitTests.Filters;

/// <summary>
/// A gated endpoint answers 403 when the organization lacks its capability, and goes through when the
/// organization has it. The gate is built the way MVC builds it, from the endpoint's attribute.
/// </summary>
public class CapabilityGateFilterTests
{
    private const string ORGANIZATION_ID = "org-1";
    private const string USER_ID = "user-1";
    private const string QUERY_CANARY = "CANARY-QUERY";

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

    [Fact]
    public async Task AStaffOrCapabilityEndpoint_LetsStaffThrough_WithoutTheCapability()
    {
        IActionResult? result = await StaffOrCapabilityAsync(new EveryCapability(), Roles.ADMIN);

        Assert.Null(result);
    }

    [Fact]
    public async Task AStaffOrCapabilityEndpoint_RefusesAPlainUser_WhoseOrganizationLacksTheCapability()
    {
        IActionResult? result = await StaffOrCapabilityAsync(new EveryCapability(), Roles.USER);

        ObjectResult refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(OrganizationCapabilities.DELETE_OWN_DOCUMENTS, Assert.IsType<ProblemDetails>(refused.Value).Extensions[CapabilityGateFilter.CAPABILITY_EXTENSION]);
    }

    [Fact]
    public async Task AStaffOrCapabilityEndpoint_LetsAPlainUserThrough_WhenTheOrganizationHasTheCapability()
    {
        IActionResult? result = await StaffOrCapabilityAsync(new Granting(OrganizationCapabilities.DELETE_OWN_DOCUMENTS), Roles.USER);

        Assert.Null(result);
    }

    private static async Task<IActionResult?> StaffOrCapabilityAsync(IOrganizationCapabilities capabilities, string role)
    {
        var services = new ServiceCollection().AddSingleton(capabilities).AddSingleton<IRequestContext>(new RequestContext { OrganizationId = ORGANIZATION_ID });
        _ = services.AddLogging().AddControllers();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test")),
        };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        var gate = (IAsyncAuthorizationFilter)new StaffOrCapabilityAttribute(Roles.ADMIN_AND_OPERATOR_ROLES, OrganizationCapabilities.DELETE_OWN_DOCUMENTS).CreateInstance(provider);

        await gate.OnAuthorizationAsync(context);

        return context.Result;
    }

    [Fact]
    public async Task AReportContentEndpoint_IsRefusedWith403_WhenTheRuleHidesTheReport()
    {
        IActionResult? result = await ReportContentAsync(new Hiding("This report failed."), "run-1");

        ObjectResult refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("This report failed.", Assert.IsType<ProblemDetails>(refused.Value).Detail);
    }

    [Fact]
    public async Task AReportContentEndpoint_GoesThrough_UnderThePlatformsRule()
    {
        Assert.Null(await ReportContentAsync(new EveryReportContent(), "run-1"));
    }

    [Fact]
    public async Task AReportContentGate_WhoseRouteLacksTheNamedValue_FailsClosed()
    {
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => ReportContentAsync(new EveryReportContent(), reportId: null));
    }

    [Fact]
    public void Explore_IsGatedByTheReportContentRule_OnItsRunIdRoute()
    {
        ReportContentAttribute gate = typeof(IniaAi.WebApi.Controllers.v1.WorkflowExecutionsGraphController).GetCustomAttribute<ReportContentAttribute>()!;

        Assert.Equal("runId", gate.RouteValue);
    }

    private static async Task<IActionResult?> ReportContentAsync(IReportContentRule rule, string? reportId)
    {
        var services = new ServiceCollection().AddSingleton(rule).AddSingleton<IRequestContext>(new RequestContext { OrganizationId = ORGANIZATION_ID });
        _ = services.AddLogging().AddControllers();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        var routes = new RouteData();
        if (reportId is not null)
        {
            routes.Values["id"] = reportId;
        }
        var context = new AuthorizationFilterContext(new ActionContext(http, routes, new ActionDescriptor()), []);
        var gate = (IAsyncAuthorizationFilter)new ReportContentAttribute().CreateInstance(provider);

        await gate.OnAuthorizationAsync(context);

        return context.Result;
    }

    private sealed class Hiding(string refusal) : IReportContentRule
    {
        public ValueTask<string?> RefusalAsync(string organizationId, string workflowExecutionId, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<string?>(refusal);
        }
    }

    private sealed class Granting(params string[] capabilities) : IOrganizationCapabilities
    {
        public ValueTask<IReadOnlySet<string>> GetAsync(string organizationId, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IReadOnlySet<string>>(capabilities.ToHashSet(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task ARefusal_LeavesOneWarning_NamingTheUserTheOrganizationTheActionAndTheCapability_AndAnAllowedRequestLeavesNone()
    {
        var logs = new CapturingLoggerProvider();
        var capabilities = new OrganizationCapabilitiesWithout(OrganizationCapabilities.USAGE_COST);

        IActionResult? allowed = await GateAsync(capabilities, ORGANIZATION_ID, OrganizationCapabilities.RUN, logs);
        Assert.Null(allowed);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Warning);

        IActionResult? refused = await GateAsync(capabilities, ORGANIZATION_ID, OrganizationCapabilities.USAGE_COST, logs);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(refused).StatusCode);
        CapturedLogEntry warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Dictionary<string, object?> values = warning.Values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
        Assert.Equal(USER_ID, values["UserId"]);
        Assert.Equal(ORGANIZATION_ID, values["OrganizationId"]);
        Assert.Equal("TokenUsage.GetMyUsageSummary", values["Action"]);
        Assert.Equal(OrganizationCapabilities.USAGE_COST, values["Capability"]);
        Assert.DoesNotContain(QUERY_CANARY, warning.Text, StringComparison.Ordinal);
    }

    private static async Task<IActionResult?> GateAsync(IOrganizationCapabilities capabilities, string organizationId, string capability, CapturingLoggerProvider logs)
    {
        var services = new ServiceCollection()
            .AddSingleton(capabilities)
            .AddSingleton<IRequestContext>(new RequestContext { OrganizationId = organizationId, UserId = USER_ID })
            .AddLogging(logging => logging.AddProvider(logs));
        _ = services.AddControllers();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.Path = "/api/v1/token-usage/me";
        http.Request.QueryString = new QueryString("?search=" + QUERY_CANARY);
        var action = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor { ControllerName = "TokenUsage", ActionName = "GetMyUsageSummary" };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), action), []);
        var gate = (IAsyncAuthorizationFilter)new RequiresCapabilityAttribute(capability).CreateInstance(provider);

        await gate.OnAuthorizationAsync(context);

        return context.Result;
    }

    private static async Task<IActionResult?> GateAsync(IOrganizationCapabilities capabilities, string? organizationId, string capability)
    {
        var services = new ServiceCollection().AddSingleton(capabilities).AddSingleton<IRequestContext>(new RequestContext { OrganizationId = organizationId });
        _ = services.AddLogging().AddControllers();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        var gate = (IAsyncAuthorizationFilter)new RequiresCapabilityAttribute(capability).CreateInstance(provider);

        await gate.OnAuthorizationAsync(context);

        return context.Result;
    }
}
