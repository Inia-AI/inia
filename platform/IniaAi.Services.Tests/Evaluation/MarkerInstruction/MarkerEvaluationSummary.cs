using System.Globalization;
using System.Text.Json;
using IniaAi.Testing;

namespace IniaAi.Services.Tests.Evaluation.MarkerInstruction;

/// <summary>One line of the evaluation's output file: one run of one case against one block.</summary>
public sealed record MarkerRunRecord(
    string Workflow,
    string Block,
    string Case,
    string Goal,
    string Carrier,
    int Repeat,
    string? Model,
    string? Effort,
    string? Thinking,
    MarkerOutcome Result,
    int OutputCharacters);

/// <summary>One row of the summary: how often the marker appeared, with its 95% Wilson interval.</summary>
public sealed record MarkerRate(string Workflow, string Goal, string Carrier, int Appeared, int Clean, int Inconclusive, double? Rate, double Low, double High);

/// <summary>Turns the evaluation's output lines into rates per workflow, goal and carrier. An inconclusive run is counted apart, never as clean.</summary>
public static class MarkerEvaluationSummary
{
    private const double Z_95 = 1.959963984540054;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static IReadOnlyList<MarkerRunRecord> Read(IEnumerable<string> lines)
    {
        return [.. lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => JsonSerializer.Deserialize<MarkerRunRecord>(line, _json)!)];
    }

    public static string Line(MarkerRunRecord record)
    {
        return JsonSerializer.Serialize(record, _json);
    }

    public static IReadOnlyList<MarkerRate> Rates(IEnumerable<MarkerRunRecord> records)
    {
        return [.. records
            .GroupBy(record => (record.Workflow, record.Goal, record.Carrier))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                int appeared = group.Count(record => record.Result is MarkerOutcome.MarkerAppeared);
                int clean = group.Count(record => record.Result is MarkerOutcome.Clean);
                (double low, double high) = Wilson(appeared, appeared + clean);
                return new MarkerRate(group.Key.Workflow, group.Key.Goal, group.Key.Carrier, appeared, clean, group.Count(record => record.Result is MarkerOutcome.Inconclusive), appeared + clean == 0 ? null : (double)appeared / (appeared + clean), low, high);
            })];
    }

    public static string Format(IEnumerable<MarkerRate> rates)
    {
        return string.Join('\n', rates.Select(rate => string.Create(
            CultureInfo.InvariantCulture,
            $"{rate.Workflow} {rate.Goal} {rate.Carrier}: appeared {rate.Appeared}, clean {rate.Clean}, inconclusive {rate.Inconclusive}, rate {(rate.Rate is { } value ? value.ToString("0.000", CultureInfo.InvariantCulture) : "n/a")} [{rate.Low:0.000}, {rate.High:0.000}]")));
    }

    /// <summary>The 95% Wilson score interval of <paramref name="successes"/> in <paramref name="trials"/>; (0, 1) with no trials.</summary>
    public static (double Low, double High) Wilson(int successes, int trials)
    {
        if (trials == 0)
        {
            return (0, 1);
        }

        double proportion = (double)successes / trials;
        double z2 = Z_95 * Z_95;
        double centre = (proportion + (z2 / (2 * trials))) / (1 + (z2 / trials));
        double margin = Z_95 * Math.Sqrt((proportion * (1 - proportion) / trials) + (z2 / (4.0 * trials * trials))) / (1 + (z2 / trials));
        return (Math.Max(0, centre - margin), Math.Min(1, centre + margin));
    }
}
