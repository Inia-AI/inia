using System.Text.Json;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class WorkflowVariableFingerprintTests
{
    private static readonly List<WorkflowVariable> Definitions =
    [
        new() { ReplacementTag = "Country", Type = WorkflowVariableTypes.String, DefaultValue = "UK" },
        new() { ReplacementTag = "Year", Type = WorkflowVariableTypes.Number },
        new() { ReplacementTag = "Sectors", Type = WorkflowVariableTypes.MultiSelect },
        new() { ReplacementTag = "Regions", Type = WorkflowVariableTypes.List, DefaultValue = "EMEA | APAC" },
        new() { ReplacementTag = "AsOf", Type = WorkflowVariableTypes.DateTime, DefaultValue = "2025-01-31" },
    ];

    private static Dictionary<string, JsonElement> Values(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    [Fact]
    public void Compute_SameValuesUnderDifferentKeyCasing_AreEqual()
    {
        WorkflowVariableFingerprint pascal = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Country":"FR","Year":2025,"Sectors":["a","b"]}"""));
        WorkflowVariableFingerprint camel = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"country":"FR","year":2025,"sectors":["a","b"]}"""));

        Assert.Empty(pascal.DifferencesFrom(camel));
    }

    [Fact]
    public void Compute_MissingValueFallsBackToTheTemplateDefault()
    {
        WorkflowVariableFingerprint explicitDefault = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Country":"UK","Year":2025}"""));
        WorkflowVariableFingerprint implied = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Year":2025}"""));

        Assert.Empty(explicitDefault.DifferencesFrom(implied));
    }

    [Fact]
    public void Compute_MissingValueFallsBackToTheDefaultInItsSubmittedShape()
    {
        WorkflowVariableFingerprint mergedBySubmit = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Regions":["EMEA","APAC"],"AsOf":"2025-01-31T00:00"}"""));
        WorkflowVariableFingerprint omittedByStart = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{}"""));

        Assert.Empty(mergedBySubmit.DifferencesFrom(omittedByStart));
    }

    [Fact]
    public void Compute_ListAsPipeStringOrArray_AreEqual()
    {
        WorkflowVariableFingerprint asArray = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Sectors":["a","b"]}"""));
        WorkflowVariableFingerprint asPipeString = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Sectors":"a | b"}"""));

        Assert.Empty(asArray.DifferencesFrom(asPipeString));
    }

    [Fact]
    public void Compute_CaseTwinKeys_BindTheExactTagAndDoNotThrow()
    {
        WorkflowVariableFingerprint twins = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"country":"FR","Country":"DE"}"""));
        WorkflowVariableFingerprint exact = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Country":"DE"}"""));

        Assert.Empty(twins.DifferencesFrom(exact));
    }

    [Fact]
    public void Compute_ExplicitEmptyString_DiffersFromTheOmittedDefault()
    {
        WorkflowVariableFingerprint explicitEmpty = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Country":""}"""));
        WorkflowVariableFingerprint omitted = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{}"""));

        Assert.Equal(["Country"], explicitEmpty.DifferencesFrom(omitted));
    }

    [Fact]
    public void Compute_DateFromEditorOrWebApp_AreEqual()
    {
        WorkflowVariableFingerprint fromEditor = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"AsOf":"2026-09-25"}"""));
        WorkflowVariableFingerprint fromWebApp = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"AsOf":"2026-09-25T17:45"}"""));

        Assert.Empty(fromEditor.DifferencesFrom(fromWebApp));
    }

    [Fact]
    public void Compute_NumberAsStringOrNumber_AreEqual()
    {
        WorkflowVariableFingerprint asNumber = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Year":2025}"""));
        WorkflowVariableFingerprint asString = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Year":"2025"}"""));

        Assert.Empty(asNumber.DifferencesFrom(asString));
    }

    [Fact]
    public void Compute_UnknownKeysAreIgnored()
    {
        WorkflowVariableFingerprint bare = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Year":2025}"""));
        WorkflowVariableFingerprint withExtra = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Year":2025,"Unrelated":"x"}"""));

        Assert.Empty(bare.DifferencesFrom(withExtra));
    }

    [Fact]
    public void DifferencesFrom_NamesEveryTagWhoseValueDiffers()
    {
        WorkflowVariableFingerprint left = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Country":"FR","Year":2025,"Sectors":["a","b"]}"""));
        WorkflowVariableFingerprint right = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Country":"DE","Year":2025,"Sectors":["b","a"]}"""));

        Assert.Equal(["Country", "Sectors"], left.DifferencesFrom(right));
    }

    [Fact]
    public void Compute_NullValues_UsesDefaultsOnly()
    {
        WorkflowVariableFingerprint fromNull = WorkflowVariableFingerprint.Compute(Definitions, null);
        WorkflowVariableFingerprint fromDefaults = WorkflowVariableFingerprint.Compute(
            Definitions, Values("""{"Country":"UK"}"""));

        Assert.Empty(fromDefaults.DifferencesFrom(fromNull));
        Assert.Equal(["AsOf", "Country", "Regions"], fromNull.CanonicalValues.Keys.Order(StringComparer.Ordinal));
    }
}
