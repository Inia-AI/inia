using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.InputRules;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IniaAi.Domain.Tests.InputRules;

/// <summary>
/// The server side of the shared input rule cases. The run form in the web app runs the same file,
/// so a case that passes here and there means the server and the form refuse a submission with
/// the same words.
/// </summary>
public sealed class VariableInputRuleCasesTests
{
    private const string FIXTURE_PATH = "InputRules/Fixtures/variable-input-rule-cases.json";

    // The SHA-256 of the fixture. IniaAi.Web pins the same hash for its copy (src/lib/inputRules),
    // so a change to the cases fails here until both copies and both pins are updated together.
    private const string FIXTURE_SHA256 = "a8d1ca2d17ec03d9a4ee52660dc67021cc3a8acf0e0368713a5878690d644e4a";

    private const string WEB_COPY_FROM_REPOSITORY_ROOT = "../IniaAi.Web/src/lib/inputRules/variable-input-rule-cases.json";

    private static readonly JObject _fixture = JObject.Parse(File.ReadAllText(FixturePath()));

    public static TheoryData<string> CaseNames()
    {
        TheoryData<string> names = [];
        foreach (JToken testCase in _fixture["cases"]!)
        {
            names.Add((string)testCase["name"]!);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Case_GivesTheExpectedViolationsAndSkippedRules(string name)
    {
        JObject testCase = (JObject)_fixture["cases"]!.First(candidate => (string)candidate["name"]! == name);
        Workflow workflow = WorkflowOf((string)testCase["workflow"]!);
        Dictionary<string, JsonElement> values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(testCase["values"]!.ToString())!;
        List<string?> attached = [.. testCase["attached"]!.Select(key => (string?)key)];

        InputRuleOutcome outcome = VariableInputRuleCheck.Check(workflow, values, attached);

        Assert.Equal(
            [.. testCase["violations"]!.Select(violation => ((string)violation["variable"]!, (string)violation["message"]!))],
            outcome.Violations.Select(violation => (violation.ReplacementTag, violation.Message)).ToList());
        Assert.Equal(
            [.. testCase["skipped"]!.Select(skipped => ((string)skipped["variable"]!, (string)skipped["kind"]!, (string)skipped["reason"]!))],
            outcome.Skipped.Select(skipped => (skipped.ReplacementTag, skipped.Kind.ToString(), skipped.Reason)).ToList());
    }

    [Fact]
    public void DisplayNames_LoseTheirLeadingNumberingOnly()
    {
        foreach (JToken name in _fixture["names"]!)
        {
            Assert.Equal((string)name["shown"]!, VariableInputRuleCheck.WithoutNumbering((string)name["displayName"]!));
        }
    }

    [Fact]
    public void TheCases_MatchThePinnedHashAndTheWebCopy()
    {
        string cases = TextOf(FixturePath());
        Assert.Equal(FIXTURE_SHA256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cases))));

        // A checkout of the web app beside this one is compared too; CI has none.
        string? webCopy = WebCopyPath();
        if (webCopy is not null)
        {
            Assert.Equal(cases, TextOf(webCopy));
        }
    }

    private static Workflow WorkflowOf(string key)
    {
        JObject definition = (JObject)_fixture["workflows"]![key]!.DeepClone();
        definition["Name"] = key;
        definition["Sections"] = new JArray();
        return Workflow.FromJson(definition.ToString())!;
    }

    private static string TextOf(string path)
    {
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FixturePath()
    {
        return Path.Combine(AppContext.BaseDirectory, FIXTURE_PATH);
    }

    private static string? WebCopyPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IniaAi.Platform.slnx")))
            {
                string candidate = Path.GetFullPath(Path.Combine(directory.FullName, WEB_COPY_FROM_REPOSITORY_ROOT));
                return File.Exists(candidate) ? candidate : null;
            }
        }

        return null;
    }
}
