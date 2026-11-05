using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NSubstitute;
using System.Globalization;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// Deterministic, offline tests for multi-target currency conversion in the ExchangeRate block.
/// Uses an NSubstitute <see cref="IExchangeRateService"/> so no live API key is required.
/// Includes byte-identical snapshot guards for the single-target path (no regression).
/// </summary>
public class ExchangeRateStrategyMultiTargetTests : IDisposable
{
    private const string DATE = "2024-01-15";
    private readonly CultureInfo _originalCulture;

    public ExchangeRateStrategyMultiTargetTests()
    {
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CultureInfo.CurrentCulture = _originalCulture;
    }

    // ---------- Helpers ----------

    private static IExchangeRateService CreateRateService(Dictionary<(string From, string To), decimal> rates)
    {
        var service = Substitute.For<IExchangeRateService>();
        _ = service.GetExchangeRateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(callInfo =>
            {
                string from = (string)callInfo[0];
                string to = (string)callInfo[1];

                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(1m);
                }

                if (rates.TryGetValue((from, to), out decimal rate))
                {
                    return Task.FromResult(rate);
                }

                throw new NotSupportedException($"No test rate configured for {from}->{to}");
            });

        return service;
    }

    private static BlockExecutionContext CreateContext(IExchangeRateService rateService)
    {
        return new BlockExecutionContext(
            logger: Substitute.For<ILogger<BlockExecutionContext>>(),
            chatConnector: Substitute.For<IChatConnector>(),
            ragService: null!,
            exchangeRateService: rateService,
            documentAnalysisService: null!,
            exaSearchService: null!,
            modelFamilyResolver: null!,
            fileStorageManager: null!,
            pdfFormat: FileFormatsUnderTest.Pdf,
            tokenUsageService: Substitute.For<ITokenUsageService>(),
            platformSettings: new PlatformSettings(),
            services: null!);
    }

    private static Block CreateBlock(ExchangeRateProperties props)
    {
        var workflow = new Section("1234", new Workflow("1234"));
        return new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = props,
        };
    }

    private static async Task<Block> RunAsync(ExchangeRateProperties props, IExchangeRateService rateService)
    {
        Block block = CreateBlock(props);
        BlockExecutionContext context = CreateContext(rateService);
        var strategy = new ExchangeRateStrategy();
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        return block;
    }

    // ---------- Single-target byte-identical snapshots (no regression) ----------

    [Fact]
    public async Task GetRate_SingleTarget_RawResponseByteIdentical()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);

        const string expected =
            "<rawValue>\n0.920000\n</rawValue>\n\n"
            + "<formattedValue>\nUSD/EUR on 2024-01-15: 0.920000\n</formattedValue>";
        Assert.Equal(expected, block.BlockResponse!.RawResponse);
        Assert.Null(block.BlockResponse.JsonResponse);
    }

    [Fact]
    public async Task ConvertSingleValue_SingleTarget_RawResponseByteIdentical()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.Convert,
            InputValue = "100",
        };

        Block block = await RunAsync(props, svc);

        const string expected =
            "<rawValue>\n0.920000\n</rawValue>\n\n"
            + "<formattedValue>\nUSD/EUR on 2024-01-15: 0.920000\n</formattedValue>\n\n"
            + "<convertedValue>\n92.00\n</convertedValue>";
        Assert.Equal(expected, block.BlockResponse!.RawResponse);
        Assert.Null(block.BlockResponse.JsonResponse);
    }

    [Fact]
    public async Task ConvertJson_SingleTarget_RawAndJsonResponseByteIdentical()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.Convert,
            InputValue = """{"currency": "USD", "amount": 100}""",
        };

        Block block = await RunAsync(props, svc);

        const string expectedJson = "{\n  \"currency\": \"EUR\",\n  \"amount\": 92.00\n}";
        const string expectedRaw =
            "<rates>\nUSD/EUR: 0.920000\n</rates>\n\n"
            + "<convertedJson>\n" + expectedJson + "\n</convertedJson>";
        // Newtonsoft's Formatting.Indented emits Environment.NewLine (CRLF on Windows), so
        // normalize line endings before the byte-identical comparison to stay platform-agnostic.
        Assert.Equal(expectedRaw, block.BlockResponse!.RawResponse!.ReplaceLineEndings("\n"));
        Assert.Equal(expectedJson, block.BlockResponse.JsonResponse!.ReplaceLineEndings("\n"));
    }

    // ---------- Multi-target: GetRate ----------

    [Fact]
    public async Task GetRate_MultiTarget_EmitsSuffixedTagsSingleFormattedBlockAndJsonMap()
    {
        IExchangeRateService svc = CreateRateService(new()
        {
            [("USD", "EUR")] = 0.92m,
            [("USD", "GBP")] = 0.79m,
        });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["GBP"],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("<rawValue_EUR>0.920000</rawValue_EUR>", raw);
        Assert.Contains("<rawValue_GBP>0.790000</rawValue_GBP>", raw);
        // Single combined formattedValue block, one line per currency.
        Assert.Contains(
            "<formattedValue>\nUSD/EUR on 2024-01-15: 0.920000\nUSD/GBP on 2024-01-15: 0.790000\n</formattedValue>",
            raw);
        Assert.DoesNotContain("<rawValue>", raw); // no bare (single-target) tag

        JObject map = JObject.Parse(block.BlockResponse.JsonResponse!);
        Assert.Equal(0.92m, map["EUR"]!.Value<decimal>());
        Assert.Equal(0.79m, map["GBP"]!.Value<decimal>());

        await svc.Received(1).GetExchangeRateAsync("USD", "EUR", Arg.Any<DateTime>());
        await svc.Received(1).GetExchangeRateAsync("USD", "GBP", Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetRate_MultiTarget_TargetEqualsSource_PassesThroughRateOne()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["USD"], // target == source
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("<rawValue_EUR>0.920000</rawValue_EUR>", raw);
        Assert.Contains("<rawValue_USD>1.000000</rawValue_USD>", raw);

        JObject map = JObject.Parse(block.BlockResponse.JsonResponse!);
        Assert.Equal(1m, map["USD"]!.Value<decimal>());
    }

    [Fact]
    public async Task GetRate_MultiTarget_DuplicatesAndCasing_NormalizedAndDeduped()
    {
        IExchangeRateService svc = CreateRateService(new()
        {
            [("USD", "EUR")] = 0.92m,
            [("USD", "GBP")] = 0.79m,
        });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "usd",
            ToCurrency = "EUR",
            ToCurrencies = ["eur", " EUR ", "gbp"], // dupes + casing + whitespace
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("<rawValue_EUR>", raw);
        Assert.Contains("<rawValue_GBP>", raw);
        // Only the two distinct targets are fetched — no duplicate EUR call.
        await svc.Received(1).GetExchangeRateAsync("USD", "EUR", Arg.Any<DateTime>());
        await svc.Received(1).GetExchangeRateAsync("USD", "GBP", Arg.Any<DateTime>());
        await svc.Received(2).GetExchangeRateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>());
    }

    // ---------- Multi-target: Convert single value ----------

    [Fact]
    public async Task ConvertSingleValue_MultiTarget_EmitsSuffixedTagsAndJsonMap()
    {
        IExchangeRateService svc = CreateRateService(new()
        {
            [("USD", "EUR")] = 0.92m,
            [("USD", "GBP")] = 0.79m,
        });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["GBP"],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.Convert,
            InputValue = "100",
        };

        Block block = await RunAsync(props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("<convertedValue_EUR>92.00</convertedValue_EUR>", raw);
        Assert.Contains("<convertedValue_GBP>79.00</convertedValue_GBP>", raw);
        Assert.DoesNotContain("<convertedValue>", raw); // no bare single-target tag

        JObject map = JObject.Parse(block.BlockResponse.JsonResponse!);
        Assert.Equal(92.00m, map["EUR"]!.Value<decimal>());
        Assert.Equal(79.00m, map["GBP"]!.Value<decimal>());
    }

    // ---------- Multi-target: Convert JSON (multi-source x multi-target matrix) ----------

    [Fact]
    public async Task ConvertJson_MultiSourceMultiTarget_ConvertsMatrixAndEmitsPerTargetDocs()
    {
        IExchangeRateService svc = CreateRateService(new()
        {
            [("USD", "EUR")] = 0.92m,
            [("GBP", "EUR")] = 1.17m,
            [("USD", "CHF")] = 0.90m,
            [("GBP", "CHF")] = 1.14m,
        });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["CHF"],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.Convert,
            InputValue = """
            {
                "items": [
                    {"currency": "USD", "amount": 100},
                    {"currency": "GBP", "amount": 200}
                ]
            }
            """,
        };

        Block block = await RunAsync(props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("<convertedJson_EUR>", raw);
        Assert.Contains("<convertedJson_CHF>", raw);
        Assert.Contains("<rates>", raw);
        Assert.Contains("USD/EUR: 0.920000", raw);
        Assert.Contains("GBP/CHF: 1.140000", raw);

        JObject map = JObject.Parse(block.BlockResponse.JsonResponse!);

        // EUR document
        JArray eurItems = (JArray)map["EUR"]!["items"]!;
        Assert.Equal("EUR", eurItems[0]!["currency"]!.ToString());
        Assert.Equal(92.00m, eurItems[0]!["amount"]!.Value<decimal>());   // 100 * 0.92
        Assert.Equal(234.00m, eurItems[1]!["amount"]!.Value<decimal>());  // 200 * 1.17

        // CHF document
        JArray chfItems = (JArray)map["CHF"]!["items"]!;
        Assert.Equal("CHF", chfItems[0]!["currency"]!.ToString());
        Assert.Equal(90.00m, chfItems[0]!["amount"]!.Value<decimal>());   // 100 * 0.90
        Assert.Equal(228.00m, chfItems[1]!["amount"]!.Value<decimal>());  // 200 * 1.14

        // Each distinct (source -> target) pair fetched exactly once.
        await svc.Received(1).GetExchangeRateAsync("USD", "EUR", Arg.Any<DateTime>());
        await svc.Received(1).GetExchangeRateAsync("GBP", "EUR", Arg.Any<DateTime>());
        await svc.Received(1).GetExchangeRateAsync("USD", "CHF", Arg.Any<DateTime>());
        await svc.Received(1).GetExchangeRateAsync("GBP", "CHF", Arg.Any<DateTime>());
    }

    [Fact]
    public async Task ConvertJson_MultiTarget_TargetEqualsSource_SkipsFetchForThatPair()
    {
        IExchangeRateService svc = CreateRateService(new()
        {
            [("USD", "GBP")] = 0.79m,
        });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "USD", // one target equals the only source
            ToCurrencies = ["GBP"],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.Convert,
            InputValue = """{"currency": "USD", "amount": 100}""",
        };

        Block block = await RunAsync(props, svc);

        JObject map = JObject.Parse(block.BlockResponse!.JsonResponse!);
        // USD target: pass-through (rate 1), amount unchanged.
        Assert.Equal("USD", map["USD"]!["currency"]!.ToString());
        Assert.Equal(100m, map["USD"]!["amount"]!.Value<decimal>());
        // GBP target: converted.
        Assert.Equal("GBP", map["GBP"]!["currency"]!.ToString());
        Assert.Equal(79.00m, map["GBP"]!["amount"]!.Value<decimal>());

        // No fetch for USD->USD; exactly one fetch total (USD->GBP).
        await svc.DidNotReceive().GetExchangeRateAsync("USD", "USD", Arg.Any<DateTime>());
        await svc.Received(1).GetExchangeRateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>());
    }

    // ---------- Edge cases: empty / blank ToCurrencies fall back to legacy single-target ----------

    [Fact]
    public async Task GetRate_EmptyToCurrenciesList_UsesLegacySingleTargetPath()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = [],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);

        const string expected =
            "<rawValue>\n0.920000\n</rawValue>\n\n"
            + "<formattedValue>\nUSD/EUR on 2024-01-15: 0.920000\n</formattedValue>";
        Assert.Equal(expected, block.BlockResponse!.RawResponse);
        Assert.Null(block.BlockResponse.JsonResponse);
    }

    [Fact]
    public async Task GetRate_AllBlankToCurrencies_UsesLegacySingleTargetPath()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["  ", ""],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);

        Assert.Contains("<rawValue>\n0.920000\n</rawValue>", block.BlockResponse!.RawResponse);
        Assert.DoesNotContain("_EUR", block.BlockResponse.RawResponse);
        Assert.Null(block.BlockResponse.JsonResponse);
    }

    [Fact]
    public async Task GetRate_DuplicateOfToCurrencyOnly_StaysSingleTargetByteIdentical()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["eur"], // dedupes to just EUR => single effective target
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);

        const string expected =
            "<rawValue>\n0.920000\n</rawValue>\n\n"
            + "<formattedValue>\nUSD/EUR on 2024-01-15: 0.920000\n</formattedValue>";
        Assert.Equal(expected, block.BlockResponse!.RawResponse);
        Assert.Null(block.BlockResponse.JsonResponse);
    }

    // ---------- Execution-state twin reconcile (never leaves a stale ToCurrencies) ----------

    [Fact]
    public async Task MultiTarget_PopulatesExecutionStateToCurrenciesWithAdditionalTargets()
    {
        IExchangeRateService svc = CreateRateService(new()
        {
            [("USD", "EUR")] = 0.92m,
            [("USD", "GBP")] = 0.79m,
        });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["GBP"],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);

        var resolved = (block.Properties as ExchangeRateProperties)!.ExecutionState;
        Assert.NotNull(resolved);
        Assert.Equal(["GBP"], resolved!.ToCurrencies);
    }

    [Fact]
    public async Task CollapsedToSingleTarget_NullsExecutionStateToCurrencies_NoStaleValue()
    {
        // Authored two entries that dedupe to a single effective target => twin must be nulled.
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["eur"], // collapses to just EUR
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);

        var resolved = (block.Properties as ExchangeRateProperties)!.ExecutionState;
        Assert.NotNull(resolved);
        Assert.Null(resolved!.ToCurrencies);
    }

    // ---------- Edge case: failure mid-matrix fails the whole block (all-or-nothing) ----------

    [Fact]
    public async Task GetRate_MultiTarget_OneRateFails_FailsWholeBlock()
    {
        // USD->GBP is not configured => substitute throws.
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["GBP"],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunAsync(props, svc);

        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Error getting exchange rate", block.BlockResponse!.RawResponse);
        Assert.NotNull(block.ExecutionState);
        Assert.Contains("Failed to get exchange rate", block.ExecutionState!.Error);
    }

    // ---------- Culture invariance (Finding #1): raw decimal text must not follow ambient culture ----------

    private static async Task<Block> RunUnderCultureAsync(string culture, ExchangeRateProperties props, IExchangeRateService svc)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return await RunAsync(props, svc);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task GetRate_SingleTarget_UnderCommaDecimalCulture_RendersDottedDecimals()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunUnderCultureAsync("de-DE", props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("0.920000", raw);
        Assert.DoesNotContain("0,920000", raw);
    }

    [Fact]
    public async Task ConvertSingleValue_SingleTarget_UnderCommaDecimalCulture_RendersDottedDecimals()
    {
        IExchangeRateService svc = CreateRateService(new() { [("USD", "EUR")] = 0.92m });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.Convert,
            InputValue = "100",
        };

        Block block = await RunUnderCultureAsync("de-DE", props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("0.920000", raw);
        Assert.Contains("92.00", raw);
        Assert.DoesNotContain("0,920000", raw);
        Assert.DoesNotContain("92,00", raw);
    }

    [Fact]
    public async Task GetRate_MultiTarget_UnderCommaDecimalCulture_RawIsDottedAndAgreesWithJsonMap()
    {
        IExchangeRateService svc = CreateRateService(new()
        {
            [("USD", "EUR")] = 0.92m,
            [("USD", "GBP")] = 0.79m,
        });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["GBP"],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.GetRate,
        };

        Block block = await RunUnderCultureAsync("de-DE", props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("0.920000", raw);
        Assert.Contains("0.790000", raw);
        Assert.DoesNotContain("0,920000", raw);
        Assert.DoesNotContain("0,790000", raw);

        // Raw tags and the JSON map agree (map is invariant; raw must be too).
        JObject map = JObject.Parse(block.BlockResponse.JsonResponse!);
        Assert.Equal(0.92m, map["EUR"]!.Value<decimal>());
        Assert.Equal(0.79m, map["GBP"]!.Value<decimal>());
        Assert.Contains(
            $"<rawValue_EUR>{map["EUR"]!.Value<decimal>().ToString("F6", CultureInfo.InvariantCulture)}</rawValue_EUR>",
            raw);
        Assert.Contains(
            $"<rawValue_GBP>{map["GBP"]!.Value<decimal>().ToString("F6", CultureInfo.InvariantCulture)}</rawValue_GBP>",
            raw);
    }

    [Fact]
    public async Task ConvertSingleValue_MultiTarget_UnderCommaDecimalCulture_RawIsDottedAndAgreesWithJsonMap()
    {
        IExchangeRateService svc = CreateRateService(new()
        {
            [("USD", "EUR")] = 0.92m,
            [("USD", "GBP")] = 0.79m,
        });
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["GBP"],
            UseCurrentDate = false,
            ExchangeDate = DATE,
            OperationMode = ExchangeRateOperationMode.Convert,
            InputValue = "100",
        };

        Block block = await RunUnderCultureAsync("de-DE", props, svc);
        string raw = block.BlockResponse!.RawResponse!;

        Assert.Contains("<convertedValue_EUR>92.00</convertedValue_EUR>", raw);
        Assert.Contains("<convertedValue_GBP>79.00</convertedValue_GBP>", raw);
        Assert.DoesNotContain("92,00", raw);
        Assert.DoesNotContain("79,00", raw);

        JObject map = JObject.Parse(block.BlockResponse.JsonResponse!);
        Assert.Equal(92.00m, map["EUR"]!.Value<decimal>());
        Assert.Equal(79.00m, map["GBP"]!.Value<decimal>());
    }
}
