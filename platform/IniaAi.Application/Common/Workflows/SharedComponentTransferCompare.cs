using IniaAi.Common.Serialization;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Common.Workflows;

internal static class SharedComponentTransferCompare
{
    internal static string LocalizeHeadDefinition(
        TransferComponentDto component,
        IReadOnlyDictionary<string, SharedComponentCorrelator.Correlation> correlations)
    {
        if (JsonText.Parse(component.HeadDefinitionJson) is not JObject root
            || root["Entries"] is not JArray entries)
        {
            return component.HeadDefinitionJson;
        }

        bool changed = false;
        foreach (JObject entry in entries.OfType<JObject>())
        {
            if (entry["SharedComponentId"] is not JValue { Type: JTokenType.String } idValue)
            {
                continue;
            }

            string originId = (string)idValue!;
            if (correlations.TryGetValue(originId, out SharedComponentCorrelator.Correlation? correlation)
                && correlation.LocalComponentId is { } localId
                && !string.Equals(localId, originId, StringComparison.Ordinal))
            {
                entry["SharedComponentId"] = localId;
                changed = true;
            }
        }

        return changed ? root.ToString(Formatting.None) : component.HeadDefinitionJson;
    }
}
