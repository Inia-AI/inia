using System.Text;
using System.Text.Json;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

public sealed class EditorAgentStepLabellerTests
{
    private const string REPORT_ID = "8b33956f-c334-4c2c-b65a-720750d81805";
    private const string WF = "python3 \"$SKILL_ASSETS_DIR/workflow-guide/wf.py\"";
    private const string WRITEUP_ID = "0149674a181a4725b51e1d34631d8d91";
    private const string WRITEUP_NAME = "Financial Position Writeup";

    private readonly EditorAgentStepLabeller _labeller;

    public EditorAgentStepLabellerTests()
    {
        Workflow workflow = EditorAgentWorkflows.Build();
        Section section = workflow.Sections.Single();
        section.Title = "Financial Position";
        _ = new Block(BlockTypes.AiQuery, section, "BalanceSheet_Writeup")
        {
            Name = WRITEUP_NAME,
            StableBlockId = WRITEUP_ID,
            Order = 2,
            Properties = new AiQueryProperties { UserPromptTemplate = "Describe {revenue.answer} in 150 words." },
        };
        _ = new WorkflowVariable(workflow, "EBITDA");

        _labeller = new EditorAgentStepLabeller(
            workflow,
            [new EditorAgentReportSummary(REPORT_ID, new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), "Acme", "completed", "Report", [])]);
    }

    [Theory]
    [InlineData(WF + " find \"word limit\"", "Searched the prompts for \"word limit\"", EditorAgentStepIcons.SEARCH)]
    [InlineData(WF + " find revenue --field name", "Searched the block names for \"revenue\"", EditorAgentStepIcons.SEARCH)]
    [InlineData(WF + " find '{\"Sections\"' --field any", "Searched the workflow", EditorAgentStepIcons.SEARCH)]
    [InlineData(WF + " block " + WRITEUP_ID, "Opened " + WRITEUP_NAME, EditorAgentStepIcons.OPEN)]
    [InlineData(WF + " block BalanceSheet_Writeup --full", "Opened " + WRITEUP_NAME, EditorAgentStepIcons.OPEN)]
    [InlineData(WF + " block revenue summary BalanceSheet_Writeup", "Opened Revenue breakdown, Summary and " + WRITEUP_NAME, EditorAgentStepIcons.OPEN)]
    [InlineData(WF + " block 99999999999999999999", "Opened a block", EditorAgentStepIcons.OPEN)]
    [InlineData(WF + " section 0", "Opened the Financial Position section", EditorAgentStepIcons.OPEN)]
    [InlineData(WF + " readers EBITDA", "Checked what reads EBITDA", EditorAgentStepIcons.LINKS)]
    [InlineData(WF + " feeds \"Financial Position Writeup\"", "Checked what feeds " + WRITEUP_NAME, EditorAgentStepIcons.LINKS)]
    [InlineData(WF + " report " + REPORT_ID, "Looked at the 3 Oct report", EditorAgentStepIcons.REPORT)]
    [InlineData(WF + " report " + REPORT_ID + " summary", "Looked at Summary in the 3 Oct report", EditorAgentStepIcons.REPORT)]
    [InlineData(WF + " report 11111111-2222-3333-4444-555555555555", "Looked at a past report", EditorAgentStepIcons.REPORT)]
    [InlineData(WF + " reports", "Read the list of past reports", EditorAgentStepIcons.REPORT)]
    [InlineData("WF=\"$SKILL_ASSETS_DIR/workflow-guide/wf.py\"\npython3 \"$WF\" block summary; python3 \"$WF\" readers summary",
        "Opened Summary, then checked what reads Summary", EditorAgentStepIcons.OPEN)]
    [InlineData("WF=\"$SKILL_ASSETS_DIR/workflow-guide/wf.py\"\npython3 \"$WF\" find 0123456789abcdef0123456789abcdef; python3 \"$WF\" find fedcba9876543210fedcba9876543210",
        "Searched the workflow ×2", EditorAgentStepIcons.SEARCH)]
    [InlineData("WF=\"$SKILL_ASSETS_DIR/workflow-guide/wf.py\"\npython3 \"$WF\" readers summary; python3 \"$WF\" readers summary; python3 \"$WF\" block summary",
        "Checked what reads Summary ×2, then opened Summary", EditorAgentStepIcons.LINKS)]
    [InlineData("cat /workspaces/run-1/docs/outline.txt", "Read the outline of the workflow", EditorAgentStepIcons.READ)]
    [InlineData("jq '.edges[]' docs/dependency-map.json", "Looked at how blocks connect", EditorAgentStepIcons.LINKS)]
    [InlineData("python3 -c \"import json; wf = json.load(open('docs/workflow.json')); print(wf.keys())\"", "Looked through the workflow", EditorAgentStepIcons.SEARCH)]
    [InlineData("python3 \"$SKILL_ASSETS_DIR/workflow-guide/check_workflow.py\" out/workflow.json schema.json", "Checked the proposed changes", EditorAgentStepIcons.CHECK)]
    [InlineData("python3 - <<'EOF'\nimport json\njson.dump(wf, open('out/workflow.json', 'w'))\nEOF", "Prepared the proposed changes", EditorAgentStepIcons.EDIT)]
    [InlineData("echo '{\"ids\": [\"x\"]}' > out/open-reports.json", "Asked for a past report", EditorAgentStepIcons.REPORT)]
    [InlineData("cat docs/report-" + REPORT_ID + ".md", "Looked at the 3 Oct report", EditorAgentStepIcons.REPORT)]
    [InlineData("grep -c right docs/report-" + REPORT_ID + ".workflow.json", "Looked at the 3 Oct report", EditorAgentStepIcons.REPORT)]
    [InlineData(WF + " find --report " + REPORT_ID + " \"right-of-use\"", "Searched the 3 Oct report for \"right-of-use\"", EditorAgentStepIcons.REPORT)]
    [InlineData("ls -la", "Looked through the workflow", EditorAgentStepIcons.SEARCH)]
    public void ABashStep_IsNamedInPlainWords(string command, string label, string icon)
    {
        AgentActivityStep step = _labeller.LabelToolCall(new AgentActivityToolCall(AgentTraceToolNames.BASH, command, null))!;

        Assert.Equal(label, step.Label);
        Assert.Equal(icon, step.Icon);
        Assert.DoesNotContain("/", step.Label, StringComparison.Ordinal);
        Assert.DoesNotContain(WRITEUP_ID, step.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadStep_IsNamedByTheFileItRead()
    {
        Assert.Equal(EditorAgentStepLabeller.READ_VERSIONS, _labeller.LabelToolCall(
            new AgentActivityToolCall(AgentTraceToolNames.READ, null, "/workspaces/run-1/docs/versions.md"))!.Label);
        Assert.Null(_labeller.LabelToolCall(new AgentActivityToolCall("Skill", null, null)));
    }

    [Theory]
    [InlineData("Now let me check what reads the **writeup** in [Financial Position Writeup](block:0149674a181a4725b51e1d34631d8d91).",
        "Now let me check what reads the writeup in Financial Position Writeup.")]
    [InlineData("Let me look at `docs/outline.txt` first.", "")]
    [InlineData("I'll run: python3 -c \"print(1)\"", "")]
    [InlineData("{\"Sections\": []}", "")]
    public void ARemark_IsShownAsPlainText_AndOneThatCarriesCodeOrAPathIsHidden(string text, string shown)
    {
        AgentActivityStep step = _labeller.LabelRemark(text)!;

        Assert.Equal(shown, step.Label);
        Assert.Equal(shown.Length == 0 ? null : EditorAgentStepIcons.REMARK, step.Icon);
    }

    /// <summary>
    /// A turn projected with the editor's labeller: every step is named without the tidier, the
    /// remark between two steps shows, and the answer after the last step does not.
    /// </summary>
    [Fact]
    public void AProjectedTurn_NamesItsStepsWithoutTheTidier_AndLeavesTheAnswerOut()
    {
        byte[] trace = Trace(
            Stage("2026-10-06T10:00:00Z", "run.started"),
            Tool("2026-10-06T10:00:02Z", "Bash", new { command = "cat docs/outline.txt" }),
            Text("2026-10-06T10:00:03Z", "The length rules live in the writeups; checking them."),
            Tool("2026-10-06T10:00:05Z", "Bash", new { command = WF + " find \"30 words\"", description = "Search prompts for word limits" }),
            Tool("2026-10-06T10:00:07Z", "Bash", new { command = WF + " block BalanceSheet_Writeup" }),
            Text("2026-10-06T10:00:20Z", "**The writeups are long because** each one asks for every line item."),
            Stage("2026-10-06T10:00:21Z", "run.completed"));
        var options = new AgentActivityProjectionOptions { StepLabeller = _labeller, RemarksBetweenToolCallsOnly = true };

        AgentActivityDigest digest = AgentActivityProjector.Project(trace, options);
        IReadOnlyList<AgentNarrationLine> forTidier = AgentActivityProjector.ExtractNarration(trace, options);

        Assert.Equal(
            [
                EditorAgentStepLabeller.READ_OUTLINE,
                "The length rules live in the writeups; checking them.",
                "Searched the prompts for \"30 words\"",
                "Opened " + WRITEUP_NAME,
            ],
            digest.Beats.Select(static beat => beat.Label));
        Assert.Equal(EditorAgentStepIcons.REMARK, digest.Beats[1].Icon);
        Assert.Equal(21, digest.Receipt.DurationSeconds);
        Assert.Empty(forTidier);
    }

    private static byte[] Trace(params string[] lines)
    {
        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    private static string Stage(string at, string stageEvent)
    {
        return JsonSerializer.Serialize(new { uuid = Guid.NewGuid().ToString(), at, type = "stage", @event = stageEvent });
    }

    private static string Tool(string at, string toolName, object input)
    {
        return JsonSerializer.Serialize(new
        {
            uuid = Guid.NewGuid().ToString(),
            at,
            type = "tool_call",
            toolName,
            inputTruncated = JsonSerializer.Serialize(input),
        });
    }

    private static string Text(string at, string text)
    {
        return JsonSerializer.Serialize(new
        {
            uuid = Guid.NewGuid().ToString(),
            at,
            type = "message",
            message = new { type = "assistant", message = new { role = "assistant", content = new[] { new { type = "text", text } } } },
        });
    }
}
