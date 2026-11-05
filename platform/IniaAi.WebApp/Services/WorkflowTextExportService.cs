using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Globalization;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Exports a workflow / section / block to clipboard-friendly markdown. Static
/// configuration only — runtime execution-state, token usage, and signature
/// override values are intentionally excluded so a clipboard paste into a
/// third-party LLM chat does not carry per-run or per-tenant data.
/// </summary>
public class WorkflowTextExportService
{
    private const string NO_DEFAULT = "(no default)";
    private const string EMPTY_JSON_ARRAY = "[]";
    private const string RUNTIME_DEFAULT = "(runtime default)";


    // TextInputs whose tag (case-insensitive) contains any of these substrings have their
    // value passed through SecretMasker before export, so a workflow author who stored a
    // bearer token in a TextInput doesn't leak it via "Copy as text" into a third-party LLM.
    private static readonly string[] SENSITIVE_TAG_TOKENS =
    [
        "auth",
        "token",
        "secret",
        "password",
        "credential",
        "bearer",
        "apikey",
        "api_key",
        "header",
    ];

    public string ExportWorkflowToText(Workflow workflow)
    {
        var mb = new MarkdownBuilder();
        AppendWorkflow(mb, workflow);
        return mb.ToString();
    }

    public string ExportSectionToText(Section section)
    {
        var mb = new MarkdownBuilder();
        AppendSection(mb, section);
        return mb.ToString();
    }

    public string ExportBlockToText(Block block)
    {
        var mb = new MarkdownBuilder();
        AppendBlock(mb, block);
        return mb.ToString();
    }

    private static void AppendWorkflow(MarkdownBuilder mb, Workflow workflow)
    {
        string workflowName = workflow.Name ?? "Untitled Workflow";
        string workflowTitle = FirstNonEmpty(workflow.Title) ?? workflowName;

        mb.H1(workflowTitle)
          .Blank()
          .Line($"**Workflow Name:** {workflowName}")
          .Line($"**Workflow ID:** `{workflow.Id ?? "unknown"}`")
          .Blank();

        AppendVariables(mb, workflow.Variables);

        if (workflow.Sections.Count > 0)
        {
            foreach (Section section in workflow.Sections.Where(s => !s.IsSeparator).OrderBy(s => s.Order))
            {
                AppendSection(mb, section);
            }
        }
    }

    private static void AppendVariables(MarkdownBuilder mb, ICollection<WorkflowVariable>? variables)
    {
        if (variables is null || variables.Count == 0)
        {
            return;
        }

        mb.H2("Workflow Variables").Blank();

        foreach (WorkflowVariable variable in variables)
        {
            string displayName = variable.DisplayName ?? variable.ReplacementTag ?? "unknown";

            mb.H3(displayName)
              .BulletCode("Tag", variable.ReplacementTag ?? "unknown")
              .Bullet("Type", variable.Type)
              .Bullet("Required", variable.IsRequired ? "Yes" : "No")
              .BulletIfSet("Default", variable.DefaultValue)
              .BulletIfSet("Description", variable.Description);

            if (variable.Options?.Any() == true)
            {
                mb.Line("- **Options:**");
                foreach (KeyValuePair<string, string> option in variable.Options)
                {
                    mb.Line($"  - `{option.Key}`: {option.Value}");
                }
            }

            mb.Blank();
        }

        mb.Line("---").Blank();
    }

    private static void AppendSection(MarkdownBuilder mb, Section section)
    {
        if (section.IsSeparator)
        {
            return;
        }

        int sectionNumber = section.Order;
        string sectionTitle = FirstNonEmpty(section.Title, section.Name) ?? $"Section {sectionNumber}";

        mb.H1($"Section {sectionNumber}: {sectionTitle}")
          .Line($"**Section ID:** `{section.Id ?? "unknown"}`")
          .Blank();

        if (section.Blocks.Count == 0)
        {
            mb.Line("*No blocks in this section.*").Blank();
            return;
        }

        foreach (Block block in section.Blocks.OrderBy(b => b.Order))
        {
            AppendBlock(mb, block);
        }
    }

    private static void AppendBlock(MarkdownBuilder mb, Block block)
    {
        mb.H2($"Block {block.Order}: {block.Name ?? "Unnamed Block"}")
          .Line($"**Block ID:** `{block.Id ?? "unknown"}`")
          .Line($"**Type:** `{block.Type}`")
          .LineIf(!string.IsNullOrEmpty(block.ReplacementTag), $"**Replacement Tag:** `{block.ReplacementTag}`")
          .Blank();

        AppendBlockBody(mb, block);
        AppendTextInputs(mb, block.TextInputs);

        mb.Line("---").Blank();
    }

    /// <summary>
    /// Per-type emission: template fields + type-specific configuration. One case per
    /// concrete <see cref="BlockProperties"/> subclass. Types without a case emit only the
    /// header from <see cref="AppendBlock"/> — add a case here when the type grows
    /// authorable static config worth exporting.
    /// </summary>
    private static void AppendBlockBody(MarkdownBuilder mb, Block block)
    {
        switch (block.Properties)
        {
            case AiQueryProperties p:
                mb.FencedSection("System Prompt Template", p.SystemPromptTemplate)
                  .FencedSection("User Prompt Template", p.UserPromptTemplate);
                AppendAiConfiguration(mb, p);
                AppendOutputFormat(mb, p);
                mb.FencedSection("Conditional Tag Template", p.ConditionalTagTemplate);
                AppendSignatureFields(mb, p.SignatureFields);
                break;

            case MergeProperties p:
                mb.FencedSection("Text Template", p.TextTemplate);
                AppendSignatureFields(mb, p.SignatureFields);
                break;

            case CalculateFormulaProperties p:
                mb.FencedSection("Text Template", p.FormulaTemplate);
                break;

            case IntelligentSearchProperties p:
                mb.FencedSection("Search Term Template", p.SearchTermTemplate);
                break;

            case LoopStartProperties p:
                mb.FencedSection("Text Template", p.LoopedListTemplate);
                break;

            case BuildTopicsProperties p:
                mb.FencedSection("Text Template", p.TopicsModelTemplate);
                break;

            case WebApiCallProperties p:
                mb.FencedSection("Text Template", p.UrlTemplate)
                  .FencedSection("Request Payload Template", p.RequestPayloadTemplate);
                break;

            case Json2XlsxProperties p:
                mb.FencedSection("Text Template", p.JsonInputTemplate);
                break;

            case SwitchCaseProperties p:
                mb.FencedSection("Text Template", p.InputTemplate);
                AppendSwitchCases(mb, p);
                break;

            case StringManipulationProperties p:
                mb.FencedSection("Input Template", p.InputTemplate);
                AppendStringManipulation(mb, p);
                break;

            case MergeJsonProperties p:
                AppendMergeJson(mb, p);
                break;

            case ExchangeRateProperties p:
                AppendExchangeRate(mb, p);
                break;

            case DynamicChartProperties p:
                AppendDynamicChart(mb, p);
                break;

            case RetrieveDataFromDocumentsProperties p:
                AppendRetrieveDataFromDocuments(mb, p);
                break;

            // The agent family shares one arm through IAgentRunProperties so a future agent-family
            // block type is carried without another edit. Verification and Agent each add their own
            // extra (the verified group; the output schema), so their concrete arms run first and
            // delegate the shared part.
            case VerificationProperties p:
                AppendVerificationConfiguration(mb, p);
                break;

            case AgentProperties p:
                AppendAgentRunConfiguration(mb, p);
                mb.FencedSection("Output Schema", p.OutputSchema)
                  .BulletCodeIfSet("Output Files", p.OutputFiles.Count > 0 ? string.Join(", ", p.OutputFiles) : null);
                break;

            case IAgentRunProperties p:
                AppendAgentRunConfiguration(mb, p);
                break;
        }
    }

    private static void AppendAgentRunConfiguration(MarkdownBuilder mb, IAgentRunProperties p)
    {
        mb.FencedSection("User Prompt Template", p.PromptTemplate)
          .FencedSection("System Prompt Template", p.SystemPromptTemplate);

        // Every fuse prints a line: a declared value, or the runtime default it falls back to when
        // left off the start request — never a blank or a zero that reads as "capped at nothing".
        mb.H3("Agent Configuration")
          .BulletCodeIfSet("Model", p.Model)
          .Bullet("ShouldUseAdvancedModel", p.ShouldUseAdvancedModel)
          .BulletCodeIfSet("Effort", p.Effort)
          .Bullet("Deadline (minutes)", FuseValue(p.DeadlineMinutes))
          .Bullet("Max Budget (USD)", FuseValue(p.MaxBudgetUsd))
          .Bullet("Max Turns", FuseValue(p.MaxTurns))
          .Bullet("Max Retained Output (MiB)", FuseValue(p.MaxRetainedOutputMib))
          .Blank();

        AppendWorkerSkills(mb, p.WorkerSkills);
    }

    private static void AppendVerificationConfiguration(MarkdownBuilder mb, VerificationProperties p)
    {
        AppendAgentRunConfiguration(mb, p);

        mb.H3("Verification").Bullet("Pass Cap", p.PassCap).Blank();

        if (p.Members.Count > 0)
        {
            mb.H4("Members");
            foreach (VerificationMember member in p.Members)
            {
                mb.Line($"- `{member.MemberTag}` — redo mode `{member.RedoMode}`");
            }

            mb.Blank();
        }

        if (p.Criteria.Count > 0)
        {
            mb.H4("Criteria");
            foreach (VerificationCriterion criterion in p.Criteria)
            {
                string scope = criterion.AppliesTo.Count == 0
                    ? "group"
                    : string.Join(", ", criterion.AppliesTo);
                mb.Line($"- `{criterion.Id}`: {criterion.Label} (applies to: {scope})");
            }

            mb.Blank();
        }
    }

    private static void AppendWorkerSkills(MarkdownBuilder mb, List<AgentWorkerSkill> skills)
    {
        if (skills.Count == 0)
        {
            return;
        }

        mb.H3("Worker Skills");
        foreach (AgentWorkerSkill skill in skills)
        {
            mb.H4(FirstNonEmpty(skill.Name) ?? "(unnamed skill)")
              .BulletIfSet("Description", skill.Description);
            if (!string.IsNullOrEmpty(skill.Content))
            {
                mb.Fenced(skill.Content);
            }
        }

        mb.Blank();
    }

    private static string FuseValue<T>(T? value)
        where T : struct, IFormattable
    {
        return value?.ToString(null, CultureInfo.InvariantCulture) ?? RUNTIME_DEFAULT;
    }

    private static void AppendAiConfiguration(MarkdownBuilder mb, AiQueryProperties p)
    {
        mb.H3("AI Configuration")
          .Bullet("MaxTokens", p.MaxTokens)
          .Bullet("ShouldUseAdvancedModel", p.ShouldUseAdvancedModel)
          .BulletCodeIfSet("ModelIdOverride", p.ModelIdOverride)
          .Bullet("ShouldUseThinkingMode", p.ShouldUseThinkingMode)
          .BulletIf(p.ShouldUseThinkingMode, "ThinkingBudget", p.ThinkingBudget)
          .BulletIfSet("Effort", p.Effort?.ToString())
          .Blank();
    }

    private static void AppendOutputFormat(MarkdownBuilder mb, AiQueryProperties p)
    {
        if (p.OutputFormatType == OutputFormatType.None)
        {
            return;
        }

        bool isTagFormat = p.OutputFormatType is OutputFormatType.SingleTag or OutputFormatType.JsonInTag;
        bool isJsonFormat = p.OutputFormatType is OutputFormatType.Json or OutputFormatType.JsonInTag;

        mb.H3("Output Format Validation")
          .Bullet("OutputFormatType", p.OutputFormatType)
          .BulletCodeIfSet("OutputFormatTagName", isTagFormat ? p.OutputFormatTagName : null)
          .BulletCodeIfSet("OutputFormatTagNames", p.OutputFormatType == OutputFormatType.MultiTag ? p.OutputFormatTagNames : null)
          .Blank()
          .FencedSectionH4("Output Format JSON Schema", isJsonFormat ? p.OutputFormatJsonSchema : null);
    }

    private static void AppendSignatureFields(MarkdownBuilder mb, Dictionary<string, SignatureFieldDefinition>? signatureFields)
    {
        if (signatureFields is null || signatureFields.Count == 0)
        {
            return;
        }

        mb.H3("Signature Fields");

        foreach (KeyValuePair<string, SignatureFieldDefinition> entry in signatureFields)
        {
            // A JSON deserialiser can land a null value even though SignatureFieldDefinition's
            // record params are non-nullable; guard so a malformed workflow doesn't NRE the export.
            if (entry.Value is null)
            {
                continue;
            }

            string defaultValue = string.IsNullOrEmpty(entry.Value.DefaultValue) ? NO_DEFAULT : entry.Value.DefaultValue;
            mb.Line($"- `{entry.Key}`: {defaultValue}");
        }

        mb.Blank();
    }

    private static void AppendStringManipulation(MarkdownBuilder mb, StringManipulationProperties p)
    {
        mb.H3("String Manipulation").Bullet("Operation", p.Operation);

        switch (p.Operation)
        {
            case StringManipulationOperation.Replace:
            case StringManipulationOperation.ReplaceUsingPattern:
                mb.BulletCodeIfSet("Find", p.Find)
                  .BulletCodeIfSet("ReplaceWith", p.ReplaceWith)
                  .Bullet("CaseSensitive", p.CaseSensitive);
                break;

            case StringManipulationOperation.Substring:
                mb.BulletIf(p.StartIndex.HasValue, "StartIndex", p.StartIndex ?? 0)
                  .BulletIf(p.Length.HasValue, "Length", p.Length ?? 0);
                break;

            case StringManipulationOperation.Trim:
                mb.Bullet("TrimSide", p.TrimSide)
                  .BulletCodeIfSet("TrimCharacters", p.TrimCharacters);
                break;

            case StringManipulationOperation.Split:
                mb.BulletCodeIfSet("Separator", p.Separator)
                  .BulletIf(p.SplitIndex.HasValue, "SplitIndex", p.SplitIndex ?? 0);
                break;

            case StringManipulationOperation.Join:
                mb.BulletCodeIfSet("Separator", p.Separator);
                break;

            case StringManipulationOperation.ChangeCase:
                mb.Bullet("CaseType", p.CaseType);
                break;

            case StringManipulationOperation.EscapeSpecialCharacters:
            case StringManipulationOperation.UnescapeSpecialCharacters:
                mb.Bullet("EscapeNewlines", p.EscapeNewlines)
                  .Bullet("EscapeTabs", p.EscapeTabs)
                  .Bullet("EscapeCarriageReturns", p.EscapeCarriageReturns)
                  .Bullet("EscapeBackslashes", p.EscapeBackslashes)
                  .Bullet("EscapeDoubleQuotes", p.EscapeDoubleQuotes);
                break;

            case StringManipulationOperation.Length:
            case StringManipulationOperation.Distinct:
                break;
        }

        mb.Blank();
    }

    private static void AppendMergeJson(MarkdownBuilder mb, MergeJsonProperties p)
    {
        if (p.JsonInputs is { Count: > 0 })
        {
            mb.H3("JSON Inputs");
            for (int i = 0; i < p.JsonInputs.Count; i++)
            {
                mb.H4($"Input {i}").Fenced(p.JsonInputs[i] ?? string.Empty);
            }

            mb.Blank();
        }

        if (p.InputExtractions is { Count: > 0 })
        {
            mb.H3("Input Extractions");
            for (int i = 0; i < p.InputExtractions.Count; i++)
            {
                MergeJsonInputExtraction extraction = p.InputExtractions[i];
                mb.Line($"- **Input {i}:** Mode=`{extraction.Mode}`, Pattern=`{extraction.Pattern}`");
            }

            mb.Blank();
        }
    }

    private static void AppendExchangeRate(MarkdownBuilder mb, ExchangeRateProperties p)
    {
        mb.H3("Exchange Rate")
          .Bullet("OperationMode", p.OperationMode)
          .BulletCodeIfSet("FromCurrency", p.FromCurrency)
          .BulletCodeIfSet("ToCurrency", p.ToCurrency)
          .Bullet("UseCurrentDate", p.UseCurrentDate)
          .BulletCodeIfSet("ExchangeDate", !p.UseCurrentDate ? p.ExchangeDate : null)
          .BulletCodeIfSet("InputValue", p.InputValue)
          .Blank();
    }

    private static void AppendDynamicChart(MarkdownBuilder mb, DynamicChartProperties p)
    {
        // Defaults below must stay in sync with DynamicChartProperties — only emit when the
        // author moved off the default so a chart at defaults produces almost no output.
        mb.H3("Chart Configuration")
          .BulletCode("ChartType", p.ChartType)
          .BulletCodeIfSet("ChartTitle", NonDefault(p.ChartTitle, "Dynamic Chart"))
          .BulletCodeIfSet("XAxisField", p.XAxisField)
          .BulletCodeIfSet("YAxisField", p.YAxisField)
          .BulletCodeIfSet("SeriesGroupField", p.SeriesGroupField)
          .BulletCodeIfSet("XAxisLabel", NonDefault(p.XAxisLabel, "X Axis"))
          .BulletCodeIfSet("YAxisLabel", NonDefault(p.YAxisLabel, "Y Axis"))
          .BulletCodeIfSet("Stacking", NonDefault(p.Stacking, "none"))
          .BulletIf(!p.ShowLegend, "ShowLegend", "false")
          .BulletIf(!p.ShowGridLines, "ShowGridLines", "false")
          .BulletIf(!p.EnableAnimations, "EnableAnimations", "false")
          .BulletCodeIfSet("DataFilters", NonDefault(p.DataFilters, EMPTY_JSON_ARRAY))
          .Blank()
          .FencedSection("Data Source JSON", p.DataSourceJson);
    }

    private static string? NonDefault(string? value, string defaultValue)
    {
        return string.IsNullOrEmpty(value) || string.Equals(value, defaultValue, StringComparison.Ordinal)
            ? null
            : value;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void AppendSwitchCases(MarkdownBuilder mb, SwitchCaseProperties p)
    {
        if ((p.Cases is null || p.Cases.Count == 0) && string.IsNullOrEmpty(p.DefaultValue))
        {
            return;
        }

        mb.H3("Switch Cases");

        if (p.Cases is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> entry in p.Cases)
            {
                mb.Line($"- `{entry.Key}` -> {entry.Value}");
            }
        }

        mb.LineIf(!string.IsNullOrEmpty(p.DefaultValue), $"- **Default:** {p.DefaultValue}").Blank();
    }

    private static void AppendRetrieveDataFromDocuments(MarkdownBuilder mb, RetrieveDataFromDocumentsProperties p)
    {
        if (!p.ShouldUseOverviews)
        {
            return;
        }

        mb.H3("Retrieval Configuration")
          .Bullet("ShouldUseOverviews", true)
          .Blank();
    }

    private static void AppendTextInputs(MarkdownBuilder mb, ICollection<TextInput>? textInputs)
    {
        if (textInputs is null || textInputs.Count == 0)
        {
            return;
        }

        mb.H3("Text Inputs (Reference Only)");

        foreach (TextInput input in textInputs.OrderBy(ti => ti.ReplacementTag ?? string.Empty))
        {
            string displayTag = DisplayTag(input.ReplacementTag ?? "unknown");
            string value = input.Value ?? string.Empty;
            string emitted = IsSensitiveTag(displayTag) ? SecretMasker.Mask(value) : value;
            mb.Line($"  {displayTag}: {emitted}");
        }

        mb.Blank();
    }

    /// <summary>
    /// Strip the "01000-" numeric prefix off a replacement tag when it follows the
    /// "{order}-{name}" convention. Bare tags pass through unchanged.
    /// </summary>
    private static string DisplayTag(string tag)
    {
        if (!tag.Contains('-'))
        {
            return tag;
        }

        string[] parts = tag.Split('-', 2);
        return parts.Length == 2 && int.TryParse(parts[0], out _) ? parts[1] : tag;
    }

    private static bool IsSensitiveTag(string displayTag)
    {
        if (string.IsNullOrEmpty(displayTag))
        {
            return false;
        }

        foreach (string token in SENSITIVE_TAG_TOKENS)
        {
            if (displayTag.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

}
