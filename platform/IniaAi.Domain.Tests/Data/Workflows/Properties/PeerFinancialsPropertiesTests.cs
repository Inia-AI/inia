using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class PeerFinancialsPropertiesTests
{
    private static PeerFinancialsProperties CreatePopulated()
    {
        return new PeerFinancialsProperties
        {
            TargetPeriodsJson = """{"LTM": "{ltm_end}", "FY": "{fy0_end}"}""",
            TargetCurrency = "{currency}",
            TargetUnits = "Millions",
            BasisMode = PeerBasisMode.Strict,
            PeriodPreferences =
            [
                new PeerPeriodPreference { Type = "LTM", ToleranceMonths = 9 },
                new PeerPeriodPreference { Type = "FY" },
            ],
            MetricKeys = ["net_sales", "gross_debt"],
            RatioMetricKeys = ["net_leverage"],
        };
    }

    [Fact]
    public void HasPlaceholder_ContainsInTargetPeriodsJson_ReturnsTrue()
    {
        Assert.True(CreatePopulated().HasPlaceholder("{ltm_end}"));
    }

    [Fact]
    public void SetExecutionState_ResolvesPlaceholders_LeavesTemplateIntact()
    {
        PeerFinancialsProperties props = CreatePopulated();

        props.SetExecutionState(
            asPlaceHolder: true,
            new Dictionary<string, string?>
            {
                ["ltm_end"] = "2025-06-30",
                ["fy0_end"] = "2024-12-31",
                ["currency"] = "EUR",
            });

        Assert.Contains("2025-06-30", props.ExecutionState!.TargetPeriodsJson);
        Assert.Equal("EUR", props.ExecutionState.TargetCurrency);
        Assert.Contains("{ltm_end}", props.TargetPeriodsJson);
    }

    [Fact]
    public void Clone_DeepCopiesCollections()
    {
        PeerFinancialsProperties props = CreatePopulated();

        var clone = (PeerFinancialsProperties)props.Clone();
        clone.PeriodPreferences[0].ToleranceMonths = 1;
        clone.MetricKeys.Add("extra");
        clone.RatioMetricKeys.Clear();

        Assert.Equal(9, props.PeriodPreferences[0].ToleranceMonths);
        Assert.Equal(2, props.MetricKeys.Count);
        Assert.Single(props.RatioMetricKeys);
        Assert.Equal(props.BasisMode, clone.BasisMode);
        Assert.Equal(props.TargetPeriodsJson, clone.TargetPeriodsJson);
    }

    [Fact]
    public void NewtonsoftDeserialize_WorkflowAuthoredBlockJson_YieldsTypedProperties()
    {
        const string json = """
            {
              "Id": "peer-block-1",
              "Type": "PeerFinancials",
              "Name": "Peer Financials",
              "ReplacementTag": "PeerFinancials",
              "Properties": {
                "Type": "PeerFinancials",
                "BasisMode": "Strict",
                "TargetPeriodsJson": "{\"LTM\": \"2025-06-30\"}",
                "TargetCurrency": "EUR",
                "TargetUnits": "Millions",
                "PeriodPreferences": [
                  { "Type": "LTM", "ToleranceMonths": 9 },
                  { "Type": "FY" }
                ],
                "MetricKeys": [ "net_sales", "gross_debt" ],
                "RatioMetricKeys": [ "net_leverage" ]
              }
            }
            """;

        var block = JsonConvert.DeserializeObject<Entities.Workflows.Blocks.Block>(json);

        Assert.NotNull(block);
        var restored = Assert.IsType<PeerFinancialsProperties>(block.Properties);
        Assert.Equal(BlockTypes.PeerFinancials, restored.Type);
        Assert.Equal(PeerBasisMode.Strict, restored.BasisMode);
        Assert.Equal(2, restored.PeriodPreferences.Count);
        Assert.Equal(9, restored.PeriodPreferences[0].ToleranceMonths);
        Assert.Null(restored.PeriodPreferences[1].ToleranceMonths);
        Assert.Equal(["net_sales", "gross_debt"], restored.MetricKeys);
        Assert.Single(restored.RatioMetricKeys);
        Assert.Equal(string.Empty, restored.PeerDataPath);
    }
}
