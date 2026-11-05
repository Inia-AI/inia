using System.Net;
using System.Reflection;
using IniaAi.Application;
using IniaAi.Domain.Entities.DailyAllowances;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Controllers.v1;
using IniaAi.WebApi.Filters;
using Microsoft.Net.Http.Headers;

namespace IniaAi.WebApi.UnitTests.Filters;

/// <summary>
/// The explore query and the refine request count against a daily allowance, which the platform
/// leaves unlimited: with its default every request goes through and no response carries a limit.
/// </summary>
public class DailyAllowanceFilterTests
{
    private const string ORGANIZATION_ID = "bank-org";
    private const string USER_ID = "bank-user";
    private const int REQUESTS = 12;

    [Theory]
    [InlineData(typeof(WorkflowExecutionsGraphController), nameof(WorkflowExecutionsGraphController.QueryProvenance), DailyAllowanceKind.Explore)]
    [InlineData(typeof(AiContentRefinementController), nameof(AiContentRefinementController.Refine), DailyAllowanceKind.Refine)]
    public void TheExploreQueryAndTheRefineRequest_CountAgainstTheirAllowance(Type controller, string action, DailyAllowanceKind kind)
    {
        DailyAllowanceAttribute? allowance = controller.GetMethod(action)!.GetCustomAttribute<DailyAllowanceAttribute>();

        Assert.Equal(kind, allowance?.Kind);
    }

    [Fact]
    public void NoOtherEndpoint_CountsAgainstAnAllowance()
    {
        List<string> counted =
        [
            .. typeof(WorkflowExecutionsGraphController).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods())
                .Where(method => method.GetCustomAttribute<DailyAllowanceAttribute>() is not null)
                .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [
                "AiContentRefinementController.Refine",
                "WorkflowExecutionsGraphController.QueryProvenance",
            ],
            counted);
    }

    [Fact]
    public async Task ThePlatformDefault_SetsNoLimit_AndAddsNoHeaders()
    {
        await using DailyAllowanceHost host = await DailyAllowanceHost.StartAsync(services => services.AddSeamDefaults());

        foreach (string path in new[] { DailyAllowanceHost.EXPLORE_PATH, DailyAllowanceHost.REFINE_PATH })
        {
            for (int request = 0; request < REQUESTS; request++)
            {
                using HttpResponseMessage response = await host.SendAsync(path, ORGANIZATION_ID, USER_ID);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.False(response.Headers.Contains(RateLimitHeaders.LIMIT_HEADER));
                Assert.False(response.Headers.Contains(RateLimitHeaders.POLICY_HEADER));
                Assert.False(response.Headers.Contains(HeaderNames.RetryAfter));
            }
        }
    }

    [Fact]
    public async Task ACrossOriginResponse_ExposesTheLimitHeaders_ToTheBrowser()
    {
        await using DailyAllowanceHost host = await DailyAllowanceHost.StartAsync(services => services.AddSeamDefaults());

        using HttpResponseMessage response = await host.SendAsync(DailyAllowanceHost.EXPLORE_PATH, ORGANIZATION_ID, USER_ID);

        string exposed = Assert.Single(response.Headers.GetValues(HeaderNames.AccessControlExposeHeaders));
        Assert.Equal(
            [HeaderNames.ContentDisposition, RateLimitHeaders.LIMIT_HEADER, RateLimitHeaders.POLICY_HEADER, HeaderNames.RetryAfter],
            exposed.Split(',', StringSplitOptions.TrimEntries));
    }
}
