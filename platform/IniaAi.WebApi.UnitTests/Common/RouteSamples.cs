using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace IniaAi.WebApi.UnitTests.Common;

/// <summary>A request path for a route endpoint, with each parameter filled with a value its constraint accepts.</summary>
public static class RouteSamples
{
    public static string PathOf(RouteEndpoint endpoint)
    {
        return "/" + string.Join("/", endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(Fill))));
    }

    private static string Fill(RoutePatternPart part)
    {
        return part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternParameterPart { Name: "version" } => "1",
            RoutePatternParameterPart parameter when parameter.ParameterPolicies.Any(policy => policy.Content is "int" or "long") => "1",
            RoutePatternParameterPart parameter when parameter.ParameterPolicies.Any(policy => policy.Content is "guid") => Guid.Empty.ToString(),
            RoutePatternParameterPart => "test-id",
            RoutePatternSeparatorPart separator => separator.Content,
            _ => string.Empty,
        };
    }
}
