using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Workflows.Strategies;

internal static class JsonMergeHelpers
{
    public static void DeepMergeObjects(JObject target, JObject source)
    {
        foreach (JProperty property in source.Properties())
        {
            JToken? existing = target[property.Name];

            if (existing is JObject existingObj && property.Value is JObject sourceObj)
            {
                DeepMergeObjects(existingObj, sourceObj);
            }
            else if (existing is JArray existingArr && property.Value is JArray sourceArr)
            {
                foreach (JToken item in sourceArr)
                {
                    existingArr.Add(item.DeepClone());
                }
            }
            else
            {
                if (existing is not null
                    && property.Value.Type == JTokenType.String
                    && property.Value.Value<string>() == "")
                {
                    continue;
                }

                target[property.Name] = property.Value.DeepClone();
            }
        }
    }
}
