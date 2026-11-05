using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Proposals;

/// <summary>
/// A workflow file split into what its author wrote and what a run left on it, by the run evidence
/// keys a proposal ignores: the execution states of the workflow, its sections, blocks, settings
/// and inputs, every block output, agent traces and citation traces.
/// </summary>
public static class WorkflowRunEvidence
{
    /// <summary>Every string of the workflow outside its run evidence.</summary>
    public static IEnumerable<string> AuthoredStrings(JToken workflow)
    {
        return StringsOf(ProposalJson.Without(workflow, ProposalJson.EvidenceKeys));
    }

    /// <summary>Every string under a run evidence key, at any depth.</summary>
    public static IEnumerable<string> EvidenceStrings(JToken workflow)
    {
        var found = new List<string>();
        Collect(workflow, found);
        return found;
    }

    private static void Collect(JToken token, List<string> found)
    {
        if (token is JObject obj)
        {
            foreach (JProperty property in obj.Properties())
            {
                if (ProposalJson.EvidenceKeys.Contains(property.Name))
                {
                    found.AddRange(StringsOf(property.Value));
                }
                else
                {
                    Collect(property.Value, found);
                }
            }
        }
        else if (token is JArray array)
        {
            foreach (JToken item in array)
            {
                Collect(item, found);
            }
        }
    }

    private static IEnumerable<string> StringsOf(JToken token)
    {
        if (token.Type == JTokenType.String)
        {
            return [token.Value<string>()!];
        }

        return token.SelectTokens("$..*")
            .Where(static value => value.Type == JTokenType.String)
            .Select(static value => value.Value<string>()!);
    }
}
