using IniaAi.WebApi.UnitTests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace IniaAi.WebApi.UnitTests.Authorization;

/// <summary>
/// Every endpoint that names roles answers 403 to a signed-in caller who holds none of them, before
/// any filter or action runs. The endpoints come from the host's own route table, so a new gate is
/// covered without a list to keep, in the open build and with the closed modules alike.
/// </summary>
public sealed class RoleGatesAtRuntimeTests(RoleGateHost host) : IClassFixture<RoleGateHost>
{
    private const string OUTSIDER = "RoleGateOutsider";

    [Fact]
    public async Task EveryRoleGatedEndpoint_Answers403_ToACallerWithoutItsRoles()
    {
        List<(string Method, string Path)> gated = [.. RoleGatedEndpoints()];
        Assert.NotEmpty(gated);

        List<string> open = [];
        foreach ((string method, string path) in gated)
        {
            HttpStatusCode status = await host.SendAsAsync(new HttpMethod(method), path, OUTSIDER);
            if (status != HttpStatusCode.Forbidden)
            {
                open.Add($"{method} {path}: {(int)status}");
            }
        }

        Assert.Empty(open);
    }

    private IEnumerable<(string Method, string Path)> RoleGatedEndpoints()
    {
        foreach (RouteEndpoint endpoint in host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            if (!endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(gate => !string.IsNullOrEmpty(gate.Roles)))
            {
                continue;
            }

            foreach (string method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                yield return (method, RouteSamples.PathOf(endpoint));
            }
        }
    }
}
