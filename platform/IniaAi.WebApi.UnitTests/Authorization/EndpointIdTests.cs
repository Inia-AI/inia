using System.Text.RegularExpressions;

namespace IniaAi.WebApi.UnitTests.Authorization;

/// <summary>
/// Every id an endpoint's route takes is named on <see cref="EndpointIds"/> with the kind of object
/// it names and the rule that keeps another organization's object out, so a new route parameter is
/// classified before it ships and the list cannot drift from the controllers.
/// </summary>
public partial class EndpointIdTests
{
    private const string UNREAD = "unread:";

    [Fact]
    public void EveryRouteParameter_IsClassified()
    {
        List<string> missing = [.. LiveKeys()
            .Where(key => !EndpointIds.All.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .Select(key => $"{key} is not classified: classify it in EndpointIds.")];

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryEntry_NamesALiveRouteParameter()
    {
        HashSet<string> live = [.. LiveKeys()];

        List<string> stale = [.. EndpointIds.All.Keys
            .Where(key => !live.Contains(key))
            .Order(StringComparer.Ordinal)
            .Select(key => $"{key} is on EndpointIds but no endpoint takes it. Remove the entry.")];

        Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void EveryReadRule_EndsWithTheLineItDescribes()
    {
        List<string> unsourced = [.. EndpointIds.All
            .Where(entry => !entry.Value.Rule.StartsWith(UNREAD, StringComparison.Ordinal) && !SourceLine().IsMatch(entry.Value.Rule))
            .Select(entry => $"{entry.Key}: \"{entry.Value.Rule}\" names no file:line.")];

        Assert.True(unsourced.Count == 0, string.Join(Environment.NewLine, unsourced));
    }

    private static IEnumerable<string> LiveKeys()
    {
        return ApiEndpoints.All()
            .SelectMany(endpoint => endpoint.RouteParameters.Select(parameter => $"{endpoint.Action}:{parameter}"))
            .Distinct();
    }

    [GeneratedRegex(@"\.cs:\d+$")]
    private static partial Regex SourceLine();
}
