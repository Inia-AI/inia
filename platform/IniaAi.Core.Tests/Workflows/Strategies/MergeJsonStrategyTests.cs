using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class MergeJsonStrategyTests : IClassFixture<WorkflowsFixture<MergeJsonStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;

    public MergeJsonStrategyTests(WorkflowsFixture<MergeJsonStrategyTests> fixture, ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
    }

    [Fact]
    public async Task MergeTwoSimpleObjects_ReturnsMerged()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""",
            """{"b":2}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(2, result["b"]!.Value<int>());
    }

    [Fact]
    public async Task MergeObjectsWithDuplicateKeys_LastWins()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""",
            """{"a":2}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(2, result["a"]!.Value<int>());
    }

    [Fact]
    public async Task MergeThreeObjects_AllKeysPresent()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""",
            """{"b":2}""",
            """{"c":3}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(2, result["b"]!.Value<int>());
        Assert.Equal(3, result["c"]!.Value<int>());
    }

    [Fact]
    public async Task DeepMerge_NestedObjects_Merged()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":{"x":1}}""",
            """{"a":{"y":2}}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var nested = (JObject)result["a"]!;
        Assert.Equal(1, nested["x"]!.Value<int>());
        Assert.Equal(2, nested["y"]!.Value<int>());
    }

    [Fact]
    public async Task DeepMerge_NestedObjectConflict_LastWins()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":{"x":1}}""",
            """{"a":{"x":2}}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(2, result["a"]!["x"]!.Value<int>());
    }

    [Fact]
    public async Task DeepMerge_ThreeLevelsDeep()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":{"b":{"c":1}}}""",
            """{"a":{"b":{"d":2}}}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var deep = (JObject)result["a"]!["b"]!;
        Assert.Equal(1, deep["c"]!.Value<int>());
        Assert.Equal(2, deep["d"]!.Value<int>());
    }

    [Fact]
    public async Task DeepMerge_NestedArrays_Concatenated()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"tags":["a"]}""",
            """{"tags":["b"]}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var tags = (JArray)result["tags"]!;
        Assert.Equal(2, tags.Count);
        Assert.Equal("a", tags[0]!.Value<string>());
        Assert.Equal("b", tags[1]!.Value<string>());
    }

    [Fact]
    public async Task DeepMerge_ObjectAndPrimitiveSameKey_LastWins()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":{"x":1}}""",
            """{"a":5}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(5, result["a"]!.Value<int>());
    }

    [Fact]
    public async Task DeepMerge_ArrayAndObjectSameKey_LastWins()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":[1]}""",
            """{"a":{"x":1}}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(JTokenType.Object, result["a"]!.Type);
        Assert.Equal(1, result["a"]!["x"]!.Value<int>());
    }

    [Fact]
    public async Task DeepMerge_MixedNestedAndFlat()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1,"b":{"x":1}}""",
            """{"b":{"y":2},"c":3}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal(3, result["c"]!.Value<int>());
        var b = (JObject)result["b"]!;
        Assert.Equal(1, b["x"]!.Value<int>());
        Assert.Equal(2, b["y"]!.Value<int>());
    }

    [Fact]
    public async Task MergeTwoArrays_Concatenated()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "[1,2]",
            "[3,4]");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JArray result = AssertValidJsonArray(block);
        Assert.Equal(4, result.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Select(t => t.Value<int>()));
    }

    [Fact]
    public async Task MergeEmptyArrayWithArray_ReturnsConcatenated()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "[]",
            "[1]");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JArray result = AssertValidJsonArray(block);
        _ = Assert.Single(result);
        Assert.Equal(1, result[0]!.Value<int>());
    }

    [Fact]
    public async Task MergeArraysOfObjects_Concatenated()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """[{"a":1}]""",
            """[{"b":2}]""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JArray result = AssertValidJsonArray(block);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0]!["a"]!.Value<int>());
        Assert.Equal(2, result[1]!["b"]!.Value<int>());
    }

    [Fact]
    public async Task MergeObjectWithArray_CollectedIntoArray()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""",
            "[1,2]");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JArray result = AssertValidJsonArray(block);
        Assert.Equal(2, result.Count);
        Assert.Equal(JTokenType.Object, result[0]!.Type);
        Assert.Equal(JTokenType.Array, result[1]!.Type);
    }

    [Fact]
    public async Task MergeObjectWithPrimitive_CollectedIntoArray()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""",
            "42");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JArray result = AssertValidJsonArray(block);
        Assert.Equal(2, result.Count);
        Assert.Equal(JTokenType.Object, result[0]!.Type);
        Assert.Equal(42, result[1]!.Value<int>());
    }

    [Fact]
    public async Task MergeMixedPrimitives_CollectedIntoArray()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "42",
            "true",
            "\"hello\"");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JArray result = AssertValidJsonArray(block);
        Assert.Equal(3, result.Count);
        Assert.Equal(42, result[0]!.Value<int>());
        Assert.True(result[1]!.Value<bool>());
        Assert.Equal("hello", result[2]!.Value<string>());
    }

    [Fact]
    public async Task DeepMerge_EmptyStringDoesNotOverwriteExistingValue()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"Cash":{"IP-1":9238}}""",
            """{"Cash":{"IP0":10728,"IP-1":""}}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var cash = (JObject)result["Cash"]!;
        Assert.Equal(10728, cash["IP0"]!.Value<int>());
        Assert.Equal(9238, cash["IP-1"]!.Value<int>());
    }

    [Fact]
    public async Task DeepMerge_EmptyStringWrittenWhenNoExistingValue()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""",
            """{"b":""}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Equal("", result["b"]!.Value<string>());
    }

    [Fact]
    public async Task SingleInput_ReturnsSameJson()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1,"b":[2,3]}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        var parsed = JToken.Parse(block.BlockResponse.RawResponse!);
        Assert.Equal(JTokenType.Object, parsed.Type);
        Assert.Equal(1, parsed["a"]!.Value<int>());
    }

    [Fact]
    public async Task EmptyObjects_ReturnsEmptyObject()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "{}",
            "{}");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Empty(result.Properties());
    }

    [Fact]
    public async Task NullJsonValues_PreservedInMerge()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":null}""",
            """{"b":1}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(JTokenType.Null, result["a"]!.Type);
        Assert.Equal(1, result["b"]!.Value<int>());
    }

    [Fact]
    public async Task UnicodeKeys_MergedCorrectly()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"日本語":1}""",
            """{"中文":2}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["日本語"]!.Value<int>());
        Assert.Equal(2, result["中文"]!.Value<int>());
    }

    [Fact]
    public async Task DeeplyNestedJson_HandledCorrectly()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"l1":{"l2":{"l3":{"l4":{"l5":"deep"}}}}}""",
            """{"l1":{"l2":{"l3":{"l4":{"l5b":"also deep"}}}}}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        var l4 = (JObject)result["l1"]!["l2"]!["l3"]!["l4"]!;
        Assert.Equal("deep", l4["l5"]!.Value<string>());
        Assert.Equal("also deep", l4["l5b"]!.Value<string>());
    }

    [Fact]
    public async Task InvalidJsonInput_SkippedWithWarning()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "not json",
            """{"a":1}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
    }

    [Fact]
    public async Task AllInvalidInputs_ReturnsEmptyObject()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "not json",
            "also not json");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
        Assert.Null(block.ExecutionState?.Error);
    }

    [Fact]
    public async Task AllInvalidInputs_Critical_FailsTheBlock()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "not json",
            "",
            "null");
        block.Properties.IsCritical = true;

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
        Assert.NotNull(block.ExecutionState);
        Assert.Equal(
            "The Merge JSON block 'MergeJson Test' had no valid JSON input, so it failed. "
            + "Check that the blocks feeding it return JSON.",
            block.ExecutionState.Error);
    }

    [Fact]
    public async Task SomeInvalidInputs_Critical_SkipsThemAndMergesTheRest()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "No document was uploaded.",
            """{"a": 1}""");
        block.Properties.IsCritical = true;

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Null(block.ExecutionState?.Error);
    }

    [Fact]
    public async Task EmptyStringInput_Skipped()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "",
            """{"a":1}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
    }

    [Fact]
    public async Task WhitespaceOnlyInput_Skipped()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "   ",
            """{"a":1}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
    }

    [Fact]
    public async Task NullLiteralInput_Skipped()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            "null",
            """{"a":1}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
    }

    [Fact]
    public async Task NoJsonInputs_ReturnsEmptyObject()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task NullExecutionState_InitializesAndReturnsEmptyObject()
    {
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
            ExecutionState = null
        };
        // No JsonInputs configured — block has no inputs

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.ExecutionState);
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("{}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task WrongPropertiesType_Throws()
    {
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
            Properties = new MergeProperties(),
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OutputSetOnBothRawAndJsonResponse()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""",
            """{"b":2}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.NotNull(block.BlockResponse.JsonResponse);
        Assert.Equal(block.BlockResponse.RawResponse, block.BlockResponse.JsonResponse);
    }

    [Fact]
    public async Task OutputIsValidJson()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""",
            """{"b":2}""",
            "[1,2]");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse?.RawResponse);
        var parsed = JToken.Parse(block.BlockResponse.RawResponse);
        Assert.NotNull(parsed);
    }

    [Fact]
    public async Task CancellationRequested_ThrowsOperationCancelled()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""");

        using CancellationTokenSource cts = new();
        cts.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => strategy.ExecuteBlockAsync(block, context, cts.Token));
    }

    // ── Pre-parse transforms ────────────────────────────────────────────────────

    [Fact]
    public async Task WithoutTransform_GroupedNumberInput_SilentlyDropped()
    {
        // Documents the bug: a number with thousands separators is invalid JSON, so the whole input is dropped.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"Total": 1,54,03,04,063}""",
            """{"a":1}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
        Assert.Null(result["Total"]);
    }

    [Fact]
    public async Task PreParseTransform_StripsDigitGroupSeparators_MakesInvalidJsonValid()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: null,
            """{"Total": 1,54,03,04,063}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1540304063L, result["Total"]!.Value<long>());
    }

    [Fact]
    public async Task PreParseTransform_PreservesPropertySeparatorCommas()
    {
        // The digit-anchored regex must NOT eat the comma separating properties a and b.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: null,
            """{"a":1,54,000,"b":2}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(154000, result["a"]!.Value<int>());
        Assert.Equal(2, result["b"]!.Value<int>());
    }

    [Fact]
    public async Task PreParseTransform_PreservesDecimalPoint()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: null,
            """{"x": 1,234.56}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1234.56, result["x"]!.Value<double>());
    }

    [Fact]
    public async Task PreParseTransform_PreservesNegativeSign()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: null,
            """{"x": -1,234}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(-1234, result["x"]!.Value<int>());
    }

    [Fact]
    public async Task PreParseTransform_AppliedToEveryInput()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: null,
            """{"a": 1,00,000}""",
            """{"b": 2,00,000}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(100000, result["a"]!.Value<int>());
        Assert.Equal(200000, result["b"]!.Value<int>());
    }

    [Fact]
    public async Task PreParseTransforms_AppliedInOrder()
    {
        // First swaps ';' for ',', then the digit strip removes inter-digit commas.
        var swapSemicolons = new MergeJsonPreParseTransform
        {
            Find = ";",
            ReplaceWith = ",",
            IsRegex = false,
            CaseSensitive = true,
        };

        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [swapSemicolons, DigitGroupStrip()],
            extractions: null,
            """{"a": 1;234;567}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1234567, result["a"]!.Value<int>());
    }

    [Fact]
    public async Task PreParseTransform_LiteralReplace()
    {
        var literal = new MergeJsonPreParseTransform
        {
            Find = "PLACEHOLDER",
            ReplaceWith = "42",
            IsRegex = false,
            CaseSensitive = true,
        };

        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [literal],
            extractions: null,
            """{"a": PLACEHOLDER}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(42, result["a"]!.Value<int>());
    }

    [Fact]
    public async Task PreParseTransform_AppliedAfterXmlTagExtraction()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: [new MergeJsonInputExtraction { Mode = JsonExtractionMode.XmlTagPattern, Pattern = "*" }],
            """<page1>{"Total": 1,54,03,04,063}</page1>""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1540304063L, result["Total"]!.Value<long>());
    }

    [Fact]
    public async Task PreParseTransform_AppliedAfterRegexExtraction()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: [new MergeJsonInputExtraction { Mode = JsonExtractionMode.Regex, Pattern = @"RESULT:(\{[\s\S]*\})" }],
            """RESULT:{"Total": 1,54,03,04,063}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1540304063L, result["Total"]!.Value<long>());
    }

    [Fact]
    public async Task PreParseTransform_InvalidRegexRuleSkipped_RemainingRulesApply()
    {
        var brokenRule = new MergeJsonPreParseTransform
        {
            Find = "(unclosed",
            ReplaceWith = "",
            IsRegex = true,
            CaseSensitive = true,
        };

        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [brokenRule, DigitGroupStrip()],
            extractions: null,
            """{"Total": 1,54,03,04,063}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1540304063L, result["Total"]!.Value<long>());
    }

    [Fact]
    public void PreParseTransforms_SurviveNewtonsoftRoundTrip()
    {
        var props = new MergeJsonProperties();
        props.JsonInputs.Add("{}");
        props.PreParseTransforms.Add(DigitGroupStrip());

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(props);
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<MergeJsonProperties>(json)!;

        MergeJsonPreParseTransform rule = Assert.Single(restored.PreParseTransforms);
        Assert.Equal(@"(?<=\d),(?=\d)", rule.Find);
        Assert.Equal("", rule.ReplaceWith);
        Assert.True(rule.IsRegex);
        Assert.True(rule.CaseSensitive);
    }

    [Fact]
    public void PreParseTransforms_SurviveSystemTextJsonRoundTrip()
    {
        var props = new MergeJsonProperties();
        props.PreParseTransforms.Add(DigitGroupStrip());

        string json = System.Text.Json.JsonSerializer.Serialize(props);
        var restored = System.Text.Json.JsonSerializer.Deserialize<MergeJsonProperties>(json)!;

        MergeJsonPreParseTransform rule = Assert.Single(restored.PreParseTransforms);
        Assert.Equal(@"(?<=\d),(?=\d)", rule.Find);
    }

    [Fact]
    public void Clone_DeepCopiesPreParseTransforms()
    {
        var props = new MergeJsonProperties();
        props.PreParseTransforms.Add(DigitGroupStrip());

        var clone = (MergeJsonProperties)props.Clone();
        clone.PreParseTransforms[0].Find = "CHANGED";

        Assert.Equal(@"(?<=\d),(?=\d)", props.PreParseTransforms[0].Find);
        Assert.Equal("CHANGED", clone.PreParseTransforms[0].Find);
    }

    [Fact]
    public async Task PreParseTransform_DigitStrip_CollapsesNumberArray_DocumentedHazard()
    {
        // Characterizes the known hazard: the digit-group strip is JSON-blind, so a number array's
        // element separator is removed just like an inter-digit grouping comma. Locks in the behavior
        // the doc-comment warns about.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: null,
            "[1,234]");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JArray result = AssertValidJsonArray(block);
        _ = Assert.Single(result);
        Assert.Equal(1234, result[0]!.Value<int>());
    }

    [Fact]
    public async Task PreParseTransform_DigitStrip_RewritesCommasInsideStringValues_DocumentedHazard()
    {
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTransformSetup(
            [DigitGroupStrip()],
            extractions: null,
            """{"note":"ref 1,234"}""");

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal("ref 1234", result["note"]!.Value<string>());
    }

    [Fact]
    public async Task NullPreParseTransforms_DoesNotThrow()
    {
        // A persisted/deserialized explicit "PreParseTransforms": null must not crash the block.
        (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) = CreateTestSetup(
            """{"a":1}""");
        ((MergeJsonProperties)block.Properties!).ExecutionState!.PreParseTransforms = null!;

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        JObject result = AssertValidJsonObject(block);
        Assert.Equal(1, result["a"]!.Value<int>());
    }

    [Fact]
    public void ExplicitNullLists_DeserializeCloneAndSetExecutionState_DoNotThrow()
    {
        // A hand-crafted/foreign workflow JSON with explicit null lists must not NPE at load/clone time
        // (these run before the strategy's runtime null-guard).
        const string json = """{"Type":"MergeJson","JsonInputs":["{a}"],"InputExtractions":null,"PreParseTransforms":null}""";
        var props = Newtonsoft.Json.JsonConvert.DeserializeObject<MergeJsonProperties>(json)!;

        var clone = (MergeJsonProperties)props.Clone();
        Assert.NotNull(clone.PreParseTransforms);
        Assert.Empty(clone.PreParseTransforms);
        Assert.Empty(clone.InputExtractions);

        props.SetExecutionState(asPlaceHolder: false, [], StringComparison.InvariantCultureIgnoreCase);
        Assert.NotNull(props.ExecutionState);
        Assert.Empty(props.ExecutionState!.PreParseTransforms);
        Assert.Empty(props.ExecutionState.InputExtractions);
    }

    private static MergeJsonPreParseTransform DigitGroupStrip()
    {
        return new MergeJsonPreParseTransform
        {
            Find = @"(?<=\d),(?=\d)",
            ReplaceWith = "",
            IsRegex = true,
            CaseSensitive = true,
        };
    }

    private (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) CreateTransformSetup(
        IReadOnlyList<MergeJsonPreParseTransform> transforms,
        IReadOnlyList<MergeJsonInputExtraction>? extractions,
        params string[] jsonInputs)
    {
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
        };

        var props = (MergeJsonProperties)block.Properties!;
        props.JsonInputs.AddRange(jsonInputs);
        props.PreParseTransforms.AddRange(transforms);
        if (extractions is not null)
        {
            props.InputExtractions.AddRange(extractions);
        }

        props.SetExecutionState(asPlaceHolder: false, [], StringComparison.InvariantCultureIgnoreCase);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeJsonStrategy();

        return (block, context, strategy);
    }

    private (Block block, BlockExecutionContext context, MergeJsonStrategy strategy) CreateTestSetup(
        params string[] jsonInputs)
    {
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.MergeJson, workflow)
        {
            Name = "MergeJson Test",
        };

        var props = (MergeJsonProperties)block.Properties!;
        props.JsonInputs.AddRange(jsonInputs);
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

    private static JArray AssertValidJsonArray(Block block)
    {
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        var parsed = JToken.Parse(block.BlockResponse.RawResponse);
        _ = Assert.IsType<JArray>(parsed);
        return (JArray)parsed;
    }
}
