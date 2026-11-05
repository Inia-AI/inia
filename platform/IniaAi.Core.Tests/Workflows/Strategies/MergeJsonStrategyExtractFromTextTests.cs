using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class MergeJsonStrategyExtractFromTextTests : IClassFixture<WorkflowsFixture<MergeJsonStrategyExtractFromTextTests>>
{
    private readonly IServiceProvider _testServiceProvider;

    private const string KLABIN_FOUR_PAGES = """
        <page1(Klabin-SA-2025-Annual-Report.pdf)>
        {
          "page_number": 1,
          "topics": {
            "is_business_description": [],
            "is_segment_notes": [],
            "is_geographic_notes": [],
            "is_risk_factors": [],
            "is_corporate_governance": [],
            "is_corporate_ownership": []
          }
        }
        </page1(Klabin-SA-2025-Annual-Report.pdf)>

        <page2(Klabin-SA-2025-Annual-Report.pdf)>
        {
          "page_number": 2,
          "topics": {
            "is_business_description": [2],
            "is_segment_notes": [],
            "is_geographic_notes": [],
            "is_risk_factors": [],
            "is_corporate_governance": [],
            "is_corporate_ownership": []
          }
        }
        </page2(Klabin-SA-2025-Annual-Report.pdf)>

        <page3(Klabin-SA-2025-Annual-Report.pdf)>
        {
          "page_number": 3,
          "topics": {
            "is_business_description": [3],
            "is_segment_notes": [],
            "is_geographic_notes": [],
            "is_risk_factors": [],
            "is_corporate_governance": [],
            "is_corporate_ownership": []
          }
        }
        </page3(Klabin-SA-2025-Annual-Report.pdf)>

        <page4(Klabin-SA-2025-Annual-Report.pdf)>
        {
          "page_number": 4,
          "topics": {
            "is_business_description": [],
            "is_segment_notes": [],
            "is_geographic_notes": [],
            "is_risk_factors": [4],
            "is_corporate_governance": [],
            "is_corporate_ownership": []
          }
        }
        </page4(Klabin-SA-2025-Annual-Report.pdf)>
        """;

    public MergeJsonStrategyExtractFromTextTests(
        WorkflowsFixture<MergeJsonStrategyExtractFromTextTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
    }

    [Fact]
    public async Task XmlTagWildcard_ExtractsAndMergesEveryTaggedJson()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            KLABIN_FOUR_PAGES,
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var businessDesc = (JArray)result["topics"]!["is_business_description"]!;
        Assert.Equal(new[] { 2, 3 }, businessDesc.Select(t => t.Value<int>()));
    }

    [Fact]
    public async Task XmlTagGlob_PageStar_FiltersByName()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            KLABIN_FOUR_PAGES,
            JsonExtractionMode.XmlTagPattern,
            "page*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var businessDesc = (JArray)result["topics"]!["is_business_description"]!;
        var riskFactors = (JArray)result["topics"]!["is_risk_factors"]!;
        Assert.Equal(new[] { 2, 3 }, businessDesc.Select(t => t.Value<int>()));
        Assert.Equal(new[] { 4 }, riskFactors.Select(t => t.Value<int>()));
    }

    [Fact]
    public async Task XmlTagGlob_ExactName_MatchesOnlyOne()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """
            <page1(file.pdf)>
            { "page_number": 1, "topics": { "is_business_description": [] } }
            </page1(file.pdf)>
            <page2(file.pdf)>
            { "page_number": 2, "topics": { "is_business_description": [2] } }
            </page2(file.pdf)>
            """,
            JsonExtractionMode.XmlTagPattern,
            "page1");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["page_number"]!.Value<int>());
        Assert.Empty((JArray)result["topics"]!["is_business_description"]!);
    }

    [Fact]
    public async Task DynamicTagNames_PairedByBackreference()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """
            <page1(file.pdf)>
            { "topics": { "flag": [1] } }
            </page1(file.pdf)>
            <page2(file.pdf)>
            { "topics": { "flag": [2] } }
            </page2(file.pdf)>
            """,
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var flag = (JArray)result["topics"]!["flag"]!;
        Assert.Equal(new[] { 1, 2 }, flag.Select(t => t.Value<int>()));
    }

    [Fact]
    public async Task RegexMode_CaptureGroupOne_ExtractsJson()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            KLABIN_FOUR_PAGES,
            JsonExtractionMode.Regex,
            @"<page\d+\([^)]*\)>([\s\S]*?)</page\d+\([^)]*\)>");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var businessDesc = (JArray)result["topics"]!["is_business_description"]!;
        Assert.Equal(new[] { 2, 3 }, businessDesc.Select(t => t.Value<int>()));
    }

    [Fact]
    public async Task ArrayLeafConcatenation_PreservesIntegerType()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """
            <page2>
            { "topics": { "is_business_description": [2] } }
            </page2>
            <page3>
            { "topics": { "is_business_description": [3] } }
            </page3>
            """,
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var arr = (JArray)result["topics"]!["is_business_description"]!;
        Assert.Equal(JTokenType.Integer, arr[0]!.Type);
        Assert.Equal(JTokenType.Integer, arr[1]!.Type);
        Assert.Equal(new[] { 2, 3 }, arr.Select(t => t.Value<int>()));
    }

    [Fact]
    public async Task InvalidJsonSnippet_SkippedAndOthersRetained()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """
            <page1>
            { not json }
            </page1>
            <page2>
            { "page_number": 2, "topics": { "flag": [2] } }
            </page2>
            """,
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(2, result["page_number"]!.Value<int>());
        Assert.Equal(new[] { 2 }, ((JArray)result["topics"]!["flag"]!).Select(t => t.Value<int>()));
    }

    [Fact]
    public async Task EmptyInputText_ReturnsEmptyObject()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            "",
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task NoMatches_ReturnsEmptyObject()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            "plain text with no tags",
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task SingleMatch_ReturnsThatJson()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """
            <only>
            { "a": 1, "b": [2,3] }
            </only>
            """,
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(new[] { 2, 3 }, ((JArray)result["b"]!).Select(t => t.Value<int>()));
    }

    [Fact]
    public async Task RegexTimeout_DoesNotHangAndIsTolerant()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            new string('a', 30) + "X",
            JsonExtractionMode.Regex,
            "(a+)+$");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task MixedInputs_SingleAndExtract_AllFlowIntoSameMerge()
    {
        // Input 0 = single JSON, Input 1 = extract-from-text. Both feed the same deep-merge.
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
        };

        var props = (MergeJsonProperties)block.Properties!;
        props.JsonInputs.Add("""{"a":1}""");
        props.JsonInputs.Add("""<p>{"b":2}</p><p>{"c":3}</p>""");
        props.InputExtractions.Add(new MergeJsonInputExtraction { Mode = JsonExtractionMode.Single });
        props.InputExtractions.Add(new MergeJsonInputExtraction
        {
            Mode = JsonExtractionMode.XmlTagPattern,
            Pattern = "*",
        });
        props.SetExecutionState(asPlaceHolder: false, [], StringComparison.InvariantCultureIgnoreCase);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(2, result["b"]!.Value<int>());
        Assert.Equal(3, result["c"]!.Value<int>());
    }

    [Fact]
    public async Task MissingExtractionEntry_DefaultsToSingle()
    {
        // No InputExtractions configured at all → behaves exactly like the legacy path.
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
        };

        var props = (MergeJsonProperties)block.Properties!;
        props.JsonInputs.Add("""{"a":1}""");
        props.JsonInputs.Add("""{"b":2}""");
        // Note: InputExtractions intentionally left empty.
        props.SetExecutionState(asPlaceHolder: false, [], StringComparison.InvariantCultureIgnoreCase);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(2, result["b"]!.Value<int>());
    }

    [Fact]
    public async Task CancellationRequested_ThrowsOperationCancelled()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            "<a>{\"x\":1}</a>",
            JsonExtractionMode.XmlTagPattern,
            "*");

        using CancellationTokenSource cts = new();
        cts.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => strategy.ExecuteBlockAsync(block, context, cts.Token));
    }

    [Fact]
    public async Task PartialInputExtractions_RemainingInputsDefaultToSingle()
    {
        // 3 JsonInputs, only 1 InputExtractions entry → inputs 1 and 2 must default to Single (legacy path).
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
        };

        var props = (MergeJsonProperties)block.Properties!;
        props.JsonInputs.Add("<page>{\"a\":1}</page>");
        props.JsonInputs.Add("""{"b":2}""");
        props.JsonInputs.Add("""{"c":3}""");
        props.InputExtractions.Add(new MergeJsonInputExtraction
        {
            Mode = JsonExtractionMode.XmlTagPattern,
            Pattern = "*",
        });
        props.SetExecutionState(asPlaceHolder: false, [], StringComparison.InvariantCultureIgnoreCase);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(2, result["b"]!.Value<int>());
        Assert.Equal(3, result["c"]!.Value<int>());
    }

    [Fact]
    public async Task RegexMode_PatternWithoutCaptureGroup_ReturnsEmpty()
    {
        // Pattern `\d+` has Groups.Count == 1 (only group 0). contentGroup == 1 → match.Groups.Count <= 1
        // → snippet skipped. The strategy logs a warning but returns no tokens.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            "abc 123 def 456",
            JsonExtractionMode.Regex,
            @"\d+");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task RegexMode_MultipleCaptureGroups_OnlyGroupOneIsUsed()
    {
        // Contract: capture group 1 wraps the JSON snippet. Subsequent groups are ignored.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """
            <p>{"a":1}</p>
            <p>{"b":2}</p>
            """,
            JsonExtractionMode.Regex,
            @"<p>(\{[^}]*\})(</p>)");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(2, result["b"]!.Value<int>());
    }

    [Fact]
    public async Task XmlTagPattern_EmptyPattern_ReturnsEmptyObject()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            "<a>{\"x\":1}</a>",
            JsonExtractionMode.XmlTagPattern,
            "");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task RegexMode_EmptyPattern_ReturnsEmptyObject()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            "<a>{\"x\":1}</a>",
            JsonExtractionMode.Regex,
            "");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task XmlTagGlob_LiteralDot_DoesNotMatchOtherChars()
    {
        // GlobToRegex must Regex.Escape the literal `.` so it can't act as a "any char" metachar.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """
            <page.1>{"a":1}</page.1>
            <pageX1>{"b":2}</pageX1>
            """,
            JsonExtractionMode.XmlTagPattern,
            "page.1");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Null(result["b"]);
    }

    [Fact]
    public async Task UnregisteredLegacyJson_DeserializesAndExecutesAsSingleMode()
    {
        // Simulates loading a pre-feature workflow row from the DB:
        // JSON has only `JsonInputs`, no `InputExtractions` key. The execution must match
        // exactly today's MergeJson behavior with no NRE on the missing list.
        const string LEGACY_JSON = """
            {
              "Type": "MergeJson",
              "JsonInputs": ["{\"a\":1}", "{\"b\":2}"]
            }
            """;

        var props = Newtonsoft.Json.JsonConvert.DeserializeObject<MergeJsonProperties>(LEGACY_JSON)!;
        Assert.NotNull(props.InputExtractions);
        Assert.Empty(props.InputExtractions);

        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "Legacy",
            Properties = props,
        };
        props.SetExecutionState(asPlaceHolder: false, [], StringComparison.InvariantCultureIgnoreCase);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(2, result["b"]!.Value<int>());
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip_PreservesInputExtractions()
    {
        var original = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}" },
            InputExtractions =
            {
                new MergeJsonInputExtraction
                {
                    Mode = JsonExtractionMode.XmlTagPattern,
                    Pattern = "page*",
                },
            },
        };

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(original);
        var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<MergeJsonProperties>(json)!;

        Assert.Single(deserialized.InputExtractions);
        Assert.Equal(JsonExtractionMode.XmlTagPattern, deserialized.InputExtractions[0].Mode);
        Assert.Equal("page*", deserialized.InputExtractions[0].Pattern);
    }

    [Fact]
    public void SystemTextJson_LegacyJsonWithoutInputExtractions_DeserializesAsEmpty()
    {
        // Production DB rehydration path uses System.Text.Json (BlockPropertiesJsonDefaults).
        // A pre-feature row has no InputExtractions key — STJ must leave the field-initialized empty list intact.
        // (BlockTypes/Type is omitted here — the parameterless ctor sets Type = MergeJson; production options
        // also wire up JsonStringEnumConverter, but that's orthogonal to what this test pins.)
        const string LEGACY_JSON = """
            {
              "JsonInputs": ["{\"a\":1}"]
            }
            """;

        var props = System.Text.Json.JsonSerializer.Deserialize<MergeJsonProperties>(LEGACY_JSON)!;

        Assert.NotNull(props.InputExtractions);
        Assert.Empty(props.InputExtractions);
        Assert.Single(props.JsonInputs);
        Assert.Equal("""{"a":1}""", props.JsonInputs[0]);
    }

    [Fact]
    public async Task XmlTagPattern_NestedSameNameTags_LazyMatchSkipsInvalidSnippet()
    {
        // Lazy `[\s\S]*?` + backreference \1 pairs the first <a> with the first </a>.
        // The inner content `<a>{...}` is not valid JSON → snippet skipped, outer match also skipped (non-JSON wrapper).
        // Pin current behavior so a future refactor doesn't silently change it.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """<a><a>{"x":1}</a></a>""",
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task XmlTagPattern_EmptyStringSnippet_DoesNotOverwriteExistingValue()
    {
        // Parity with MergeJsonStrategyTests.DeepMerge_EmptyStringDoesNotOverwriteExistingValue — the same
        // skip-empty-string rule must apply when snippets are extracted (they go through the same merge path).
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            """
            <a>{"Cash":{"IP-1":9238}}</a>
            <b>{"Cash":{"IP0":10728,"IP-1":""}}</b>
            """,
            JsonExtractionMode.XmlTagPattern,
            "*");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var cash = (JObject)result["Cash"]!;
        Assert.Equal(10728, cash["IP0"]!.Value<int>());
        Assert.Equal(9238, cash["IP-1"]!.Value<int>());
    }

    [Fact]
    public async Task PatternWithPlaceholder_IsNotResolvedAtExecutionTime()
    {
        // Regression guard: PlaceholderReflectionHelper only walks string / Dict<string,string> / List<string> —
        // List<MergeJsonInputExtraction> is invisible to it. If someone enables Pattern resolution in a future
        // refactor, this test should be updated. For now: literal placeholders pass through unchanged.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateSingleExtractInput(
            "<page1>{\"a\":1}</page1>",
            JsonExtractionMode.XmlTagPattern,
            "{block_5}");  // Literal — not a real placeholder reference here, used to assert non-resolution

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Pattern `{block_5}` doesn't match `<page1>` so no snippets extracted → empty merge.
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ModeSingle_IgnoresPattern()
    {
        // Even with a non-default Pattern set, Mode=Single must take the legacy path.
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
        };

        var props = (MergeJsonProperties)block.Properties!;
        props.JsonInputs.Add("""{"a":1}""");
        props.InputExtractions.Add(new MergeJsonInputExtraction
        {
            Mode = JsonExtractionMode.Single,
            Pattern = @"<page\d+>",  // would slice to nothing if mistakenly applied
        });
        props.SetExecutionState(asPlaceHolder: false, [], StringComparison.InvariantCultureIgnoreCase);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
    }

    private (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) CreateSingleExtractInput(
        string inputText,
        JsonExtractionMode extractionMode,
        string pattern)
    {
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
        };

        var props = (MergeJsonProperties)block.Properties!;
        props.JsonInputs.Add(inputText);
        props.InputExtractions.Add(new MergeJsonInputExtraction
        {
            Mode = extractionMode,
            Pattern = pattern,
        });
        props.SetExecutionState(asPlaceHolder: false, [], StringComparison.InvariantCultureIgnoreCase);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        return (block, context, strategy);
    }

    private static JObject AssertValidJsonObject(Block block)
    {
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        var parsed = JToken.Parse(block.BlockResponse.RawResponse);
        _ = Assert.IsType<JObject>(parsed);
        return (JObject)parsed;
    }
}
