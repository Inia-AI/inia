using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent.Evals;

/// <param name="Rounds">The most rounds any of the case's proposals took; 0 when it proposed nothing.</param>
internal sealed record EditorAgentEvalResult(
    string CaseId,
    string Category,
    string Model,
    bool Passed,
    IReadOnlyList<string> Failures,
    decimal CostUsd,
    double Seconds,
    int Rounds,
    string Answer);

/// <summary>
/// Collects one run's results: a line per case in <c>results.jsonl</c> as each finishes, failed or
/// not, and <c>summary.md</c> with the pass rate per category, how many of the set's cases ran,
/// and the total cost once the run ends.
/// The folder is <c>INIA_TEST_EDITOR_AGENT_EVAL_OUT</c>, or a dated folder under the temp directory.
/// </summary>
public sealed class EditorAgentEvalReport : IDisposable
{
    private const string OUT_ENV = "INIA_TEST_EDITOR_AGENT_EVAL_OUT";

    private readonly List<EditorAgentEvalResult> _results = [];
    private readonly Lock _gate = new();
    private readonly string _folder;

    public EditorAgentEvalReport()
    {
        string? configured = Environment.GetEnvironmentVariable(OUT_ENV);
        _folder = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "editor-agent-evals", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))
            : configured;
    }

    internal void Record(EditorAgentEvalResult result)
    {
        lock (_gate)
        {
            _results.Add(result);
            _ = Directory.CreateDirectory(_folder);
            File.AppendAllText(Path.Combine(_folder, "results.jsonl"), JsonConvert.SerializeObject(result) + Environment.NewLine);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_results.Count == 0)
            {
                return;
            }

            File.WriteAllText(Path.Combine(_folder, "summary.md"), Summary());
        }
    }

    private string Summary()
    {
        var text = new StringBuilder();
        int passed = _results.Count(static result => result.Passed);
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"# Editor agent evals, {_results[0].Model}");
        _ = text.AppendLine();
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"Passed {passed} of {_results.Count} cases run; the set has {EditorAgentEvalCases.All.Count}. A case that threw counts as failed.");
        _ = text.AppendLine();
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"Cost ${_results.Sum(static result => result.CostUsd):0.00}, {_results.Sum(static result => result.Seconds) / 60:0} minutes of turns.");
        _ = text.AppendLine();
        _ = text.AppendLine("| Category | Passed | Cost |");
        _ = text.AppendLine("|---|---|---|");
        foreach (IGrouping<string, EditorAgentEvalResult> category in _results.GroupBy(static result => result.Category))
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"| {category.Key} | {category.Count(static result => result.Passed)} of {category.Count()} | ${category.Sum(static result => result.CostUsd):0.00} |");
        }

        foreach (EditorAgentEvalResult failed in _results.Where(static result => !result.Passed))
        {
            _ = text.AppendLine();
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"## {failed.CaseId}");
            foreach (string failure in failed.Failures)
            {
                _ = text.AppendLine(CultureInfo.InvariantCulture, $"- {failure}");
            }
        }

        return text.ToString();
    }
}
