using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Workflows.Helpers;

public static class CurrencyConversionHelper
{
    public static (bool IsJson, decimal? DecimalValue, JObject? JsonValue) ParseInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (false, null, null);
        }

        try
        {
            var json = JObject.Parse(input);
            return (true, null, json);
        }
        catch (JsonReaderException)
        {
            if (decimal.TryParse(input, out decimal decimalValue))
            {
                return (false, decimalValue, null);
            }

            return (false, null, null);
        }
    }

    public static decimal ConvertSingleValue(decimal value, decimal rate)
    {
        return Math.Round(value * rate, 2);
    }

    public static HashSet<string> DetectAllCurrencies(JObject obj)
    {
        var currencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectCurrencies(obj, currencies);
        return currencies;
    }

    private static void CollectCurrencies(JObject obj, HashSet<string> currencies)
    {
        string? currency = DetectJsonCurrency(obj);
        if (currency is not null)
        {
            _ = currencies.Add(currency);
        }

        foreach (JProperty property in obj.Properties())
        {
            switch (property.Value.Type)
            {
                case JTokenType.Object:
                    CollectCurrencies((JObject)property.Value, currencies);
                    break;
                case JTokenType.Array:
                    foreach (JToken item in (JArray)property.Value)
                    {
                        if (item.Type is JTokenType.Object)
                        {
                            CollectCurrencies((JObject)item, currencies);
                        }
                    }

                    break;
            }
        }
    }

    public static string? DetectJsonCurrency(JObject obj)
    {
        JProperty? currencyProperty = TryGetCurrencyProperty(obj);
        return currencyProperty?.Value.ToString().Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Converts a single source JSON document into several target currencies at once.
    /// The input is left untouched; each target gets its own deep clone converted in place
    /// (via <see cref="ConvertJsonCurrency"/>) using that target's per-source rates.
    /// </summary>
    /// <param name="source">The source JSON document (not mutated).</param>
    /// <param name="targetCurrencies">Effective target currencies, already normalized/de-duped.</param>
    /// <param name="ratesByTarget">
    /// For each target currency, the map of detected source currency -&gt; rate (source-&gt;target).
    /// </param>
    /// <returns>Map of target currency -&gt; converted document, preserving <paramref name="targetCurrencies"/> order.</returns>
    public static Dictionary<string, JObject> ConvertJsonToTargets(
        JObject source,
        IReadOnlyList<string> targetCurrencies,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> ratesByTarget,
        ILogger logger)
    {
        var result = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        foreach (string target in targetCurrencies)
        {
            var clone = (JObject)source.DeepClone();

            if (ratesByTarget.TryGetValue(target, out IReadOnlyDictionary<string, decimal>? rates))
            {
                ConvertJsonCurrency(clone, rates, target, logger);
            }

            result[target] = clone;
        }

        return result;
    }

    public static void ConvertJsonCurrency(
        JObject obj,
        IReadOnlyDictionary<string, decimal> rates,
        string toCurrency,
        ILogger logger)
    {
        JProperty? currencyProperty = TryGetCurrencyProperty(obj);

        if (currencyProperty is not null)
        {
            string jsonCurrency = currencyProperty.Value.ToString().Trim().ToUpperInvariant();

            if (rates.TryGetValue(jsonCurrency, out decimal rate))
            {
                ConvertNumbersAtLevel(obj, rate);
                currencyProperty.Value = toCurrency;
            }
            else
            {
                logger.LogWarning(
                    "No rate available for currency '{Currency}'. Skipping conversion for this object.",
                    jsonCurrency);
            }
        }

        RecurseIntoChildren(obj, rates, toCurrency, logger);
    }

    private static JProperty? TryGetCurrencyProperty(JObject obj)
    {
        JProperty? property = obj.Properties()
            .FirstOrDefault(p => p.Name.Equals("currency", StringComparison.OrdinalIgnoreCase));

        return property?.Value?.Type is JTokenType.String
            && !string.IsNullOrWhiteSpace(property.Value.ToString())
            ? property
            : null;
    }

    private static void ConvertNumbersAtLevel(JObject obj, decimal rate)
    {
        foreach (JProperty? property in obj.Properties().ToList())
        {
            if (property.Value.Type is JTokenType.Float or JTokenType.Integer)
            {
                property.Value = ConvertSingleValue(property.Value.Value<decimal>(), rate);
            }
            else if (property.Value.Type is JTokenType.Array)
            {
                ConvertArrayNumbers((JArray)property.Value, rate);
            }
        }
    }

    private static void RecurseIntoChildren(
        JObject obj,
        IReadOnlyDictionary<string, decimal> rates,
        string toCurrency,
        ILogger logger)
    {
        foreach (JProperty property in obj.Properties())
        {
            switch (property.Value.Type)
            {
                case JTokenType.Object:
                    ConvertJsonCurrency((JObject)property.Value, rates, toCurrency, logger);
                    break;
                case JTokenType.Array:
                    foreach (JToken item in (JArray)property.Value)
                    {
                        if (item.Type is JTokenType.Object)
                        {
                            ConvertJsonCurrency((JObject)item, rates, toCurrency, logger);
                        }
                    }

                    break;
            }
        }
    }

    private static void ConvertArrayNumbers(JArray array, decimal rate)
    {
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i].Type is JTokenType.Float or JTokenType.Integer)
            {
                array[i] = ConvertSingleValue(array[i].Value<decimal>(), rate);
            }
        }
    }
}
