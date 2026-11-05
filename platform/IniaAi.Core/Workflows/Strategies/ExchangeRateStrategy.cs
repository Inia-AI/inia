using IniaAi.AiConnection.Abstractions;
using IniaAi.Core.Workflows.Helpers;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace IniaAi.Core.Workflows.Strategies;

public class ExchangeRateStrategy : IBlockStrategy
{
    private const string DATE_FORMAT = "yyyy-MM-dd";
    private const string RATE_FORMAT = "F6";

    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        BlockStrategyOutput output = new();

        ExchangeRateProperties props = block.Properties as ExchangeRateProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(ExchangeRateProperties)}");

        block.ExecutionState ??= new (block);
        props.ExecutionState ??= new (props);

        ExchangeRateConfig config = ExtractConfiguration(props);

        if (!ValidateConfiguration(config, block, context))
        {
            return output;
        }

        props.ExecutionState.FromCurrency = config.FromCurrency;
        props.ExecutionState.ToCurrency = config.ToCurrency;

        // Reconcile the resolved additional targets on the execution-state twin unconditionally:
        // the extras when genuinely multi-target, otherwise null so a deduped/collapsed list never
        // leaves a stale value on the twin and single-target execution serialization stays unchanged.
        props.ExecutionState.ToCurrencies = config.TargetCurrencies.Count > 1
            ? [.. config.TargetCurrencies.Skip(1)]
            : null;

        try
        {
            if (config.OperationMode is ExchangeRateOperationMode.Convert
                && !string.IsNullOrWhiteSpace(config.InputValue))
            {
                await ExecuteConvertModeAsync(block, config, context, cancellationToken);
            }
            else
            {
                await ExecuteGetRateModeAsync(block, config, context, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex,
                "{block} Failed to get exchange rate: {Error}",
                block, ex.Message);

            _ = new BlockResponse(block) { RawResponse = $"{IChatConnector.ERROR_PREFIX}Error getting exchange rate: {ex.Message}" };
            block.ExecutionState.Error = $"Failed to get exchange rate: {ex.Message}";
        }

        return output;
    }

    private static async Task ExecuteGetRateModeAsync(
        Block block,
        ExchangeRateConfig config,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (config.TargetCurrencies.Count == 1)
        {
            await ExecuteSingleTargetGetRateAsync(block, config, context);
            return;
        }

        await ExecuteMultiTargetGetRateAsync(block, config, context, cancellationToken);
    }

    private static async Task ExecuteSingleTargetGetRateAsync(
        Block block,
        ExchangeRateConfig config,
        BlockExecutionContext context)
    {
        context.Logger.LogDebug(
            "{block} Getting exchange rate from {FromCurrency} to {ToCurrency} for date {Date}",
            block, config.FromCurrency, config.ToCurrency, config.ExchangeDate);

        decimal rate = await context.ExchangeRateService.GetExchangeRateAsync(
            config.FromCurrency, config.ToCurrency, config.ExchangeDate
        ).ConfigureAwait(false);

        string rawResponse = FormattableString.Invariant($"<rawValue>\n{rate:F6}\n</rawValue>\n\n<formattedValue>\n{config.FromCurrency}/{config.ToCurrency} on {config.ExchangeDate:yyyy-MM-dd}: {rate:F6}\n</formattedValue>");

        _ = new BlockResponse(block) { RawResponse = rawResponse };

        context.Logger.LogInformation(
            "{block} Exchange rate retrieved: {FromCurrency}/{ToCurrency} on {Date} = {Rate}",
            block, config.FromCurrency, config.ToCurrency, config.ExchangeDate, rate);
    }

    private static async Task ExecuteMultiTargetGetRateAsync(
        Block block,
        ExchangeRateConfig config,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        var rawValueTags = new List<string>();
        var formattedLines = new List<string>();
        var map = new JObject();

        foreach (string target in config.TargetCurrencies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            decimal rate = await context.ExchangeRateService.GetExchangeRateAsync(
                config.FromCurrency, target, config.ExchangeDate
            ).ConfigureAwait(false);

            rawValueTags.Add($"<rawValue_{target}>{rate.ToString(RATE_FORMAT, CultureInfo.InvariantCulture)}</rawValue_{target}>");
            formattedLines.Add($"{config.FromCurrency}/{target} on {config.ExchangeDate.ToString(DATE_FORMAT, CultureInfo.InvariantCulture)}: {rate.ToString(RATE_FORMAT, CultureInfo.InvariantCulture)}");
            map[target] = rate;
        }

        string rawResponse =
            $"{string.Join("\n", rawValueTags)}\n\n<formattedValue>\n{string.Join("\n", formattedLines)}\n</formattedValue>";
        string jsonResponse = map.ToString(Formatting.Indented);

        _ = new BlockResponse(block)
        {
            RawResponse = rawResponse,
            JsonResponse = jsonResponse,
        };

        context.Logger.LogInformation(
            "{block} Exchange rates retrieved for {FromCurrency} to {TargetCount} targets on {Date}",
            block, config.FromCurrency, config.TargetCurrencies.Count, config.ExchangeDate);
    }

    private static async Task ExecuteConvertModeAsync(
        Block block,
        ExchangeRateConfig config,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        (bool isJson, decimal? decimalValue, JObject? jsonValue) = CurrencyConversionHelper.ParseInput(config.InputValue!);

        if (decimalValue.HasValue)
        {
            if (config.TargetCurrencies.Count == 1)
            {
                await ExecuteSingleValueConversionAsync(block, config, decimalValue.Value, context);
            }
            else
            {
                await ExecuteMultiTargetSingleValueConversionAsync(block, config, decimalValue.Value, context, cancellationToken);
            }

            return;
        }

        if (isJson && jsonValue is not null)
        {
            if (config.TargetCurrencies.Count == 1)
            {
                await ExecuteJsonConversionAsync(block, config, jsonValue, context, cancellationToken);
            }
            else
            {
                await ExecuteMultiTargetJsonConversionAsync(block, config, jsonValue, context, cancellationToken);
            }
        }
    }

    private static async Task ExecuteSingleValueConversionAsync(
        Block block,
        ExchangeRateConfig config,
        decimal value,
        BlockExecutionContext context)
    {
        decimal rate = await context.ExchangeRateService.GetExchangeRateAsync(
            config.FromCurrency, config.ToCurrency, config.ExchangeDate
        ).ConfigureAwait(false);

        decimal converted = CurrencyConversionHelper.ConvertSingleValue(value, rate);

        string rawResponse = FormattableString.Invariant($"<rawValue>\n{rate:F6}\n</rawValue>\n\n<formattedValue>\n{config.FromCurrency}/{config.ToCurrency} on {config.ExchangeDate:yyyy-MM-dd}: {rate:F6}\n</formattedValue>\n\n<convertedValue>\n{converted}\n</convertedValue>");

        _ = new BlockResponse(block) { RawResponse = rawResponse };

        context.Logger.LogInformation(
            "{block} Converted {Value} {FromCurrency} to {Converted} {ToCurrency} (rate: {Rate})",
            block, value, config.FromCurrency, converted, config.ToCurrency, rate);
    }

    private static async Task ExecuteMultiTargetSingleValueConversionAsync(
        Block block,
        ExchangeRateConfig config,
        decimal value,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        var convertedTags = new List<string>();
        var map = new JObject();

        foreach (string target in config.TargetCurrencies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            decimal rate = await context.ExchangeRateService.GetExchangeRateAsync(
                config.FromCurrency, target, config.ExchangeDate
            ).ConfigureAwait(false);

            decimal converted = CurrencyConversionHelper.ConvertSingleValue(value, rate);

            convertedTags.Add($"<convertedValue_{target}>{converted.ToString(CultureInfo.InvariantCulture)}</convertedValue_{target}>");
            map[target] = converted;
        }

        string rawResponse = string.Join("\n", convertedTags);
        string jsonResponse = map.ToString(Formatting.Indented);

        _ = new BlockResponse(block)
        {
            RawResponse = rawResponse,
            JsonResponse = jsonResponse,
        };

        context.Logger.LogInformation(
            "{block} Converted {Value} {FromCurrency} to {TargetCount} target currencies",
            block, value, config.FromCurrency, config.TargetCurrencies.Count);
    }

    private static async Task ExecuteJsonConversionAsync(
        Block block,
        ExchangeRateConfig config,
        JObject jsonValue,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        HashSet<string> detectedCurrencies = CurrencyConversionHelper.DetectAllCurrencies(jsonValue);

        if (detectedCurrencies.Count == 0)
        {
            _ = detectedCurrencies.Add(config.FromCurrency);
        }

        context.Logger.LogDebug(
            "{block} Detected currencies in JSON: {Currencies}",
            block, string.Join(", ", detectedCurrencies));

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var rateDescriptions = new List<string>();

        foreach (string currency in detectedCurrencies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (currency.Equals(config.ToCurrency, StringComparison.OrdinalIgnoreCase))
            {
                rates[currency] = 1m;
                continue;
            }

            decimal rate = await context.ExchangeRateService.GetExchangeRateAsync(
                currency, config.ToCurrency, config.ExchangeDate
            ).ConfigureAwait(false);

            rates[currency] = rate;
            rateDescriptions.Add(FormattableString.Invariant($"{currency}/{config.ToCurrency}: {rate:F6}"));

            context.Logger.LogDebug(
                "{block} Fetched rate {Currency}/{ToCurrency} = {Rate}",
                block, currency, config.ToCurrency, rate);
        }

        CurrencyConversionHelper.ConvertJsonCurrency(jsonValue, rates, config.ToCurrency, context.Logger);

        string jsonResponse = jsonValue.ToString(Formatting.Indented);
        string rawResponse = $"<rates>\n{string.Join("\n", rateDescriptions)}\n</rates>\n\n<convertedJson>\n{jsonResponse}\n</convertedJson>";

        _ = new BlockResponse(block)
        {
            RawResponse = rawResponse,
            JsonResponse = jsonResponse
        };

        context.Logger.LogInformation(
            "{block} Converted JSON with {CurrencyCount} source currencies to {ToCurrency}",
            block, detectedCurrencies.Count, config.ToCurrency);
    }

    private static async Task ExecuteMultiTargetJsonConversionAsync(
        Block block,
        ExchangeRateConfig config,
        JObject jsonValue,
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        HashSet<string> detectedCurrencies = CurrencyConversionHelper.DetectAllCurrencies(jsonValue);

        if (detectedCurrencies.Count == 0)
        {
            _ = detectedCurrencies.Add(config.FromCurrency);
        }

        context.Logger.LogDebug(
            "{block} Detected currencies in JSON: {Currencies}",
            block, string.Join(", ", detectedCurrencies));

        var ratesByTarget = new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);
        var rateDescriptions = new List<string>();

        foreach (string target in config.TargetCurrencies)
        {
            var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (string source in detectedCurrencies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    rates[source] = 1m;
                    continue;
                }

                decimal rate = await context.ExchangeRateService.GetExchangeRateAsync(
                    source, target, config.ExchangeDate
                ).ConfigureAwait(false);

                rates[source] = rate;
                rateDescriptions.Add(FormattableString.Invariant($"{source}/{target}: {rate:F6}"));
            }

            ratesByTarget[target] = rates;
        }

        Dictionary<string, JObject> convertedByTarget = CurrencyConversionHelper.ConvertJsonToTargets(
            jsonValue, config.TargetCurrencies, ratesByTarget, context.Logger);

        var convertedJsonTags = new List<string>();
        var map = new JObject();

        foreach (string target in config.TargetCurrencies)
        {
            JObject converted = convertedByTarget[target];
            string docString = converted.ToString(Formatting.Indented);
            convertedJsonTags.Add($"<convertedJson_{target}>\n{docString}\n</convertedJson_{target}>");
            map[target] = converted;
        }

        string rawResponse =
            $"<rates>\n{string.Join("\n", rateDescriptions)}\n</rates>\n\n{string.Join("\n\n", convertedJsonTags)}";
        string jsonResponse = map.ToString(Formatting.Indented);

        _ = new BlockResponse(block)
        {
            RawResponse = rawResponse,
            JsonResponse = jsonResponse,
        };

        context.Logger.LogInformation(
            "{block} Converted JSON with {CurrencyCount} source currencies to {TargetCount} targets",
            block, detectedCurrencies.Count, config.TargetCurrencies.Count);
    }

    private record ExchangeRateConfig(
        string FromCurrency,
        string ToCurrency,
        IReadOnlyList<string> TargetCurrencies,
        DateTime ExchangeDate,
        ExchangeRateOperationMode OperationMode,
        string? InputValue);

    private static ExchangeRateConfig ExtractConfiguration(ExchangeRateProperties props)
    {
        string fromCurrency = props.ExecutionState?.FromCurrency?.Trim().ToUpperInvariant() ?? "";
        string toCurrency = props.ExecutionState?.ToCurrency?.Trim().ToUpperInvariant() ?? "";
        string? inputValue = props.ExecutionState?.InputValue;

        IReadOnlyList<string> targetCurrencies = BuildEffectiveTargets(toCurrency, props.ExecutionState?.ToCurrencies);

        DateTime exchangeDate = DateTime.UtcNow.Date;

        if (!props.UseCurrentDate)
        {
            string? dateInput = props.ExecutionState?.ExchangeDate;

            if (DateTime.TryParse(dateInput, out DateTime parsedDate))
            {
                exchangeDate = parsedDate.Date;
            }
        }

        return new ExchangeRateConfig(fromCurrency, toCurrency, targetCurrencies, exchangeDate, props.OperationMode, inputValue);
    }

    /// <summary>
    /// Builds the effective target set: the primary <paramref name="toCurrency"/> first, then each
    /// additional target in authored order, all normalized (trim + upper) and de-duplicated
    /// case-insensitively. Blank entries are dropped. When there are no additional targets this
    /// yields the single legacy target, preserving byte-identical single-target behaviour.
    /// </summary>
    private static IReadOnlyList<string> BuildEffectiveTargets(string toCurrency, List<string>? additionalTargets)
    {
        var effective = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(toCurrency) && seen.Add(toCurrency))
        {
            effective.Add(toCurrency);
        }

        if (additionalTargets is not null)
        {
            foreach (string? candidate in additionalTargets)
            {
                string normalized = candidate?.Trim().ToUpperInvariant() ?? "";

                if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                {
                    effective.Add(normalized);
                }
            }
        }

        return effective;
    }

    private static bool ValidateConfiguration(ExchangeRateConfig config, Block block, BlockExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(config.FromCurrency) || string.IsNullOrWhiteSpace(config.ToCurrency))
        {
            context.Logger.LogWarning(
                "{block} Missing currency codes - FromCurrency: {FromCurrency}, ToCurrency: {ToCurrency}",
                block, config.FromCurrency, config.ToCurrency);

            _ = new BlockResponse(block) { RawResponse = $"{IChatConnector.ERROR_PREFIX}Missing currency codes" };
            block.ExecutionState ??= new(block);
            block.ExecutionState.Error = "Missing currency codes";
            return false;
        }

        return true;
    }
}
