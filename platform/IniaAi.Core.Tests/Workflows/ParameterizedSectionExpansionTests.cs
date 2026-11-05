using IniaAi.Common.Enums;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;

namespace IniaAi.Core.Tests.Workflows;

public class ParameterizedSectionExpansionTests
{
    #region Shared Prompt Constants

    private const string SharedOperatingSystemPrompt =
        "You are a financial analyst. You are provided with the full {_PERIOD_DESC} income statement " +
        "and its related notes for a corporate entity. Your task is to populate the following " +
        "template line items {_PERIOD_YEARS}. Where values are not directly stated, you must derive " +
        "them using the notes and your analytical judgement. ";

    private const string SharedOperatingUserPrompt =
        """
        # INSTRUCTIONS

        Extract and normalise operating P&L line items from the provided {_PERIOD_DESC} income statement and accompanying notes into a standardised template. You must handle both "function of expense" and "nature of expense" presentation formats.

        If no explicit Operating Income / EBIT line exists, treat all items between
        Gross Profit and Finance Costs (or the first clearly non-operating line) as
        within the operating section.

        1. Core Business Revenues
           - The primary operating revenue from principal business activities

        2. Other Revenues
           - Non-core revenues WITHIN the revenue section or ABOVE the Gross Profit line
           - If no such item exists above Gross Profit, use ""

        3. Cost of Goods Sold (COGS)
           - If no explicit COGS line exists, attempt to derive it, otherwise use ""

        4. Selling, General and Administrative (SG&A)
           - If reported as separate lines, SUM them into a single SG&A value

        5. Salaries and Wages
           - Extract the employee cost / staff cost figure from the notes if available
           - Wages may be distributed across multiple cost categories. Review the notes to
             determine where wages are embedded, and deduct the appropriate portion from
             EACH affected line item to avoid double counting
           - If wages cannot be reliably separated from the parent line items, use ""

        6. Depreciation and Amortization (D&A)
           - Extract ONLY if disclosed on the FACE of the income statement.
           - If D&A appears only in the notes or cash flow statement, use "" (it will be derived from OCF downstream).
           - Do NOT include impairment charges unless they are bundled inseparably.

        7. Dividend Income
           - If bundled with Interest Income, use ""

        8. Other Operating Income
           - All other non-core income items that appear BELOW Gross Profit but WITHIN the operating section
           - EXCLUDE: any interest income, even if presented within "Other income" above or within the operating section. Interest income is extracted separately and including it here would cause double counting.

        9. Other Operating Expenses
           - All other non-core expense items that appear BELOW Gross Profit but WITHIN the operating section

        # VALIDATION & CONVENTIONS

        1. Double-Count Prevention:
           - The extracted line items will be input into a normalised income statement excel sheet individually.
           - Check you have handled any double counting

        2. Sign Convention:
           - POSITIVE: revenues and income items
           - NEGATIVE: all expense line items

        3. Value Rules:
           - Use 0 for actual zero values shown in the document
           - Use "" (empty string) ONLY for line items that are genuinely missing, not disclosed, or cannot be reliably derived
           - Do not use null
           - Extract numbers as found without adjusting units
        {_PERIOD_SELECTION}
        # OUTPUT FORMAT:
        - Return your reasoning within <reasoning> tags
        - Return your extracted values within <answer> tags.
        - Units must be as found in the financial statements provided, without any adjustment.

        <reasoning>
        [Provide your reasoning here, including calculations and adjustments made to normalise the extracted numbers without double counting]
        </reasoning>

        <answer>
        {
          "Core Business Revenues": { "[Year 1]": [value], "[Year 2]": [value] },
          "Other Revenues": { "[Year 1]": [value], "[Year 2]": [value] },
          "Cost of Goods Sold": { "[Year 1]": [value], "[Year 2]": [value] },
          "Selling, General and Administrative": { "[Year 1]": [value], "[Year 2]": [value] },
          "Salaries and Wages": { "[Year 1]": [value], "[Year 2]": [value] },
          "Depreciation and Amortization": { "[Year 1]": [value], "[Year 2]": [value] },
          "Dividend Income": { "[Year 1]": [value], "[Year 2]": [value] },
          "Other Operating Income": { "[Year 1]": [value], "[Year 2]": [value] },
          "Other Operating Expenses": { "[Year 1]": [value], "[Year 2]": [value] }
        }
        </answer>
        """;

    private const string SharedBelowOperatingSystemPrompt = SharedOperatingSystemPrompt;

    private const string SharedBelowOperatingUserPrompt =
        """
        # OPERATING SECTION EXTRACTION
        The following items have ALREADY been extracted from the operating section of the income statement by a prior step. Review this carefully to understand what has been captured and how classification decisions were made, so that you do not double count any item.

        <reasoning>
        {PL_Operating.reasoning}
        </reasoning>

        <extracted_values>
        {PL_Operating.answer}
        </extracted_values>

        # INSTRUCTIONS:

        Extract and normalise below-the-line P&L items from the provided {_PERIOD_DESC} income statement and accompanying notes into a standardised template. These items relate to the section of the income statement BELOW Operating Income / EBIT.

        1. Interest Income
           - GROSS interest earned, without netting against interest expense
           - If only a net figure is available: assign to the dominant component and use "" for the other

        2. Interest Expense
           - GROSS interest obligation on debt, without netting interest income
           - Apply same gross/net logic as Interest Income above

        3. Other Non-Operating Income (BELOW Operating Income)
           - EXCLUDE dividend income. It is extracted separately and including it here would cause double counting.
           - EXCLUDE any items already captured in the operating section extraction above (e.g., items classified as Other Operating Income)
           - If no Non-Operating Income exist, use ""

        4. Other Non-Operating Expenses
           - Expense items BELOW Operating Income that are NOT interest or tax related
           - EXCLUDE any items already captured in the operating section extraction above (e.g., items classified as Other Operating Expenses)
           - If no Non-Operating Expenses exist, use ""

        5. Income Tax (Expense) Benefit
           - TOTAL income tax charge or credit excluding any deferred provisions (see step 6)

        6. Deferred Tax Provision

        7. Net Income (Loss)
              - Profit/Loss before any Other Comprehensive Income (OCI) adjustments

        # VALIDATION & CONVENTIONS

        1. Double-Count Prevention:
           - The extracted line items will be input into a normalised income statement excel sheet individually, ALONGSIDE the operating section items already extracted above.
           - Cross-check each item you extract against the operating section extraction. If an income or expense item was already captured there (in any line item), do NOT include it again here.
           - Check you have handled any double counting

        2. Sign Convention:
           - POSITIVE: income items and tax benefits (credits)
           - NEGATIVE: expense items and tax charges

        3. Value Rules:
           - Use 0 for actual zero values shown in the document
           - Use "" (empty string) ONLY for line items that are genuinely missing, not disclosed, or cannot be reliably derived
           - Do not use null
           - Extract numbers as found without adjusting units

        4. Validation Check:
           - Verify that the sum of all extracted items plus Operating Income reconciles to Profit Before Tax (within rounding)
           - State the Net Income (Loss) figure observed (for downstream verification only — do not include in the answer JSON)
        {_PERIOD_SELECTION}
        # OUTPUT FORMAT:
        - Return your reasoning within <reasoning> tags
        - Return your extracted values within <answer> tags.
        - Units must be as found in the financial statements provided, without any adjustment.

        <reasoning>
        [Provide your reasoning here, including
        - How you cross-checked against the operating section extraction to avoid double counting
        - The validation reconciliation
        - Any gross/net decomposition performed]
        </reasoning>

        <answer>
        {
          "Interest Income": { "[Year 1]": [value], "[Year 2]": [value] },
          "Interest Expense": { "[Year 1]": [value], "[Year 2]": [value] },
          "Other Non-Operating Income": { "[Year 1]": [value], "[Year 2]": [value] },
          "Other Non-Operating Expenses": { "[Year 1]": [value], "[Year 2]": [value] },
          "Income Tax Expense Benefit": { "[Year 1]": [value], "[Year 2]": [value] },
          "Deferred Tax Provision": { "[Year 1]": [value], "[Year 2]": [value] },
          "Net Income (Loss)": { "[Year 1]": [value], "[Year 2]": [value] }
        }
        </answer>
        """;

    private const string QrPeriodSelectionRule =
        "\n\n4. Period Selection:\n" +
        "   - Extract ONLY the \"{InterimPeriod} months ended\" figures matching " +
        "{QR_FS_ReportingDate.reporting_date} (MM-DD-YYYY) and their comparative. " +
        "Ignore any other period columns. The annual report year-end date is " +
        "{FS_Currency_Units_ReportingDate.reporting_date} — do not extract this period " +
        "as it is captured separately.";

    #endregion

    #region Test Helpers

    private static (Workflow parentWorkflow, Section templateSection) CreateTemplateSection()
    {
        var parent = new Workflow("CSCorporates1New");

        var section = new Section("", parent)
        {
            Title = "Extract - P&L (NEW)",
            Order = 9,
        };

        // Block 0: PL_Operating
        var block0 = new Block(BlockTypes.AiQuery, section, 0, "PL_Operating")
        {
            Name = "P&L Operating Section (Extract)",
            ShouldDisplayInLogs = true,
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = SharedOperatingUserPrompt,
                SystemPromptTemplate = SharedOperatingSystemPrompt,
                ShouldUseAdvancedModel = true,
                ModelIdOverride = "us.anthropic.claude-opus-4-6-v1",
                ShouldUsePageImages = true,
                ShouldUseThinkingMode = true,
                ThinkingBudget = 5000,
                MaxTokens = 12000,
            },
        };
        _ = new RagSettings(block0)
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "{IS_Pages.answer},{revenue_notes.answer},{costs_notes.answer},{interest_tax_notes.answer}",
            PageAdjustmentInput = "0",
        };

        // Block 1: PL_Below_Operating
        var block1 = new Block(BlockTypes.AiQuery, section, 1, "PL_Below_Operating")
        {
            Name = "P&L Below Operating Income (Extract)",
            ShouldDisplayInLogs = true,
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = SharedBelowOperatingUserPrompt,
                SystemPromptTemplate = SharedBelowOperatingSystemPrompt,
                ShouldUseAdvancedModel = true,
                ModelIdOverride = "us.anthropic.claude-opus-4-6-v1",
                ShouldUsePageImages = true,
                ShouldUseThinkingMode = true,
                ThinkingBudget = 5000,
                MaxTokens = 12000,
            },
        };
        _ = new RagSettings(block1)
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "{IS_Pages.answer},{revenue_notes.answer},{costs_notes.answer},{interest_tax_notes.answer}",
            PageAdjustmentInput = "0",
        };

        return (parent, section);
    }

    private static ParameterizedSectionConfig CreatePnLConfig()
    {
        const string arPeriodYears = "for the years {CorporationAssessmentYear} and {CorporationAssessmentYear-1}";

        return new ParameterizedSectionConfig(
        [
            new SectionVariant(
                Key: "AR",
                Label: "Annual Report",
                Params: new() { ["_PERIOD_DESC"] = "", ["_PERIOD_SELECTION"] = "", ["_PERIOD_YEARS"] = arPeriodYears },
                SectionSupportedDocumentClasses: ["Annual Report"]
            ),
            new SectionVariant(
                Key: "PAR",
                Label: "Past Annual Report",
                Params: new() { ["_PERIOD_DESC"] = "", ["_PERIOD_SELECTION"] = "", ["_PERIOD_YEARS"] = arPeriodYears },
                SectionStopGate: "{verify_past_annual_report.stop}",
                SectionSupportedDocumentClasses: ["Past Annual Report"]
            ),
            new SectionVariant(
                Key: "QR",
                Label: "Quarterly Financials",
                Params: new()
                {
                    ["_PERIOD_DESC"] = "{InterimPeriod} month",
                    ["_PERIOD_SELECTION"] = QrPeriodSelectionRule,
                    ["_PERIOD_YEARS"] = "for ALL available periods",
                },
                SectionStopGate: "{verify_quarterly_report.stop}",
                SectionSupportedDocumentClasses: ["Quarterly Financials"]
            ),
        ]);
    }

    private static List<Section> ExpandSection(Workflow parentWorkflow, Section templateSection, ParameterizedSectionConfig config)
    {
        Section template = AsSection(parentWorkflow, templateSection);
        return ParameterizedSectionExpander.Expand(template, config);
    }

    private static Section AsSection(Workflow parent, Section childSection)
    {
        return parent.Sections.First(s => s.Id == childSection.Id);
    }

    private static AiQueryProperties GetAiQueryProperties(Block block) =>
        (AiQueryProperties)block.Properties!;

    #endregion

    #region Section-Level Tests

    [Fact]
    public void Expand_ProducesCorrectNumberOfSections()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);

        Assert.Equal(3, expanded.Count);
    }

    [Fact]
    public void Expand_SectionTitlesAreSuffixedWithVariantKey()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        Section template = AsSection(parent, section);
        template.Title = "Extract - P&L (NEW)";
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, config);

        Assert.Equal("Extract - P&L (NEW) - AR", expanded[0].Title);
        Assert.Equal("Extract - P&L (NEW) - PAR", expanded[1].Title);
        Assert.Equal("Extract - P&L (NEW) - QR", expanded[2].Title);
    }

    [Fact]
    public void Expand_GivesEachVariantsDataOutputItsOwnName()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        Section template = AsSection(parent, section);
        template.Blocks.OrderBy(block => block.Order).First().DataOutputName = "pnl";
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, config);

        Assert.Equal(
            ["pnl_AR", "pnl_PAR", "pnl_QR"],
            expanded.SelectMany(variant => variant.Blocks).Where(block => block.IsDataOutput).Select(block => block.DataOutputName));
    }

    [Fact]
    public void Expand_EveryVariantCarriesTheWordExportSettingOfItsTemplate()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        Section template = AsSection(parent, section);
        template.Properties.ExcludeFromWordExport = true;
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, config);

        Assert.All(expanded, variant => Assert.True(variant.Properties.ExcludeFromWordExport));
    }

    [Fact]
    public void Expand_SharedComponentLinkFieldsCarryUnchangedIntoEveryVariant()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        Section template = AsSection(parent, section);
        template.SharedComponentId = "component-section";
        template.SharedComponentLinkState = SharedComponentLinkState.Linked;
        template.SharedComponentVersionHash = new string('a', 64);
        template.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "instance" };
        template.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["slot"] = SharedComponentSlotState.Hidden,
        };
        Block templateBlock = template.Blocks.OrderBy(b => b.Order).First();
        templateBlock.SharedComponentId = "component-block";
        templateBlock.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
        templateBlock.SharedComponentVersionHash = new string('b', 64);
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, config);

        Assert.All(expanded, variant =>
        {
            Assert.Equal("component-section", variant.SharedComponentId);
            Assert.Equal(SharedComponentLinkState.Linked, variant.SharedComponentLinkState);
            Assert.Equal(new string('a', 64), variant.SharedComponentVersionHash);
            Assert.NotNull(variant.SharedComponentBindings);
            Assert.Equal("instance", variant.SharedComponentBindings["slot"]);
            Assert.NotNull(variant.SharedComponentSlotStates);
            Assert.NotSame(template.SharedComponentSlotStates, variant.SharedComponentSlotStates);
            Assert.Equal(SharedComponentSlotState.Hidden, variant.SharedComponentSlotStates["slot"]);
            Block variantBlock = variant.Blocks.OrderBy(b => b.Order).First();
            Assert.Equal("component-block", variantBlock.SharedComponentId);
            Assert.Equal(SharedComponentLinkState.BrokenOut, variantBlock.SharedComponentLinkState);
            Assert.Equal(new string('b', 64), variantBlock.SharedComponentVersionHash);
        });
    }

    [Fact]
    public void Expand_BindingsAreIndependentInstancesPerVariant()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        Section template = AsSection(parent, section);
        template.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "instance" };
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, config);
        expanded[0].SharedComponentBindings!["slot"] = "mutated";

        Assert.Equal("instance", template.SharedComponentBindings["slot"]);
        Assert.Equal("instance", expanded[1].SharedComponentBindings!["slot"]);
    }

    [Fact]
    public void Expand_SectionOrderIsPreserved()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);

        Assert.True(expanded[0].Order < expanded[1].Order);
        Assert.True(expanded[1].Order < expanded[2].Order);
    }

    [Fact]
    public void Expand_EachSectionHasCorrectBlockCount()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);

        Assert.All(expanded, s => Assert.Equal(2, s.Blocks.Count));
    }

    #endregion

    #region Block ReplacementTag Tests

    [Fact]
    public void Expand_AR_BlockTagsAreSuffixed()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        var arBlocks = expanded[0].Blocks.OrderBy(b => b.Order).ToList();

        Assert.Equal("PL_Operating_AR", arBlocks[0].ReplacementTag);
        Assert.Equal("PL_Below_Operating_AR", arBlocks[1].ReplacementTag);
    }

    [Fact]
    public void Expand_QR_BlockTagsAreSuffixed()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        var qrBlocks = expanded[2].Blocks.OrderBy(b => b.Order).ToList();

        Assert.Equal("PL_Operating_QR", qrBlocks[0].ReplacementTag);
        Assert.Equal("PL_Below_Operating_QR", qrBlocks[1].ReplacementTag);
    }

    #endregion

    #region RAG Internal Tag Suffixing Tests

    [Fact]
    public void Expand_AR_RagExactIndexesAreUnchangedForExternalRefs()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block arBlock0 = expanded[0].Blocks.OrderBy(b => b.Order).First();

        // External block references (IS_Pages, revenue_notes etc.) are NOT suffixed —
        // only internal cross-references between blocks within the section are suffixed
        Assert.Equal(
            "{IS_Pages.answer},{revenue_notes.answer},{costs_notes.answer},{interest_tax_notes.answer}",
            arBlock0.RagSettings.First().ExactIndexesInput);
    }

    [Fact]
    public void Expand_QR_RagExactIndexesAreUnchangedForExternalRefs()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block qrBlock0 = expanded[2].Blocks.OrderBy(b => b.Order).First();

        Assert.Equal(
            "{IS_Pages.answer},{revenue_notes.answer},{costs_notes.answer},{interest_tax_notes.answer}",
            qrBlock0.RagSettings.First().ExactIndexesInput);
    }

    #endregion

    #region Internal Cross-Reference Tests

    [Fact]
    public void Expand_AR_InternalCrossRefsAreSuffixed()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block arBlock1 = expanded[0].Blocks.OrderBy(b => b.Order).Last();
        string userPrompt = GetAiQueryProperties(arBlock1).UserPromptTemplate!;

        Assert.Contains("{PL_Operating_AR.reasoning}", userPrompt);
        Assert.Contains("{PL_Operating_AR.answer}", userPrompt);
        Assert.DoesNotContain("{PL_Operating.reasoning}", userPrompt);
    }

    [Fact]
    public void Expand_QR_InternalCrossRefsAreSuffixed()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block qrBlock1 = expanded[2].Blocks.OrderBy(b => b.Order).Last();
        string userPrompt = GetAiQueryProperties(qrBlock1).UserPromptTemplate!;

        Assert.Contains("{PL_Operating_QR.reasoning}", userPrompt);
        Assert.Contains("{PL_Operating_QR.answer}", userPrompt);
    }

    #endregion

    #region Parameter Resolution Tests

    [Fact]
    public void Expand_AR_ParamsResolvedToEmptyString()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block arBlock0 = expanded[0].Blocks.OrderBy(b => b.Order).First();
        string userPrompt = GetAiQueryProperties(arBlock0).UserPromptTemplate!;

        Assert.DoesNotContain("{_PERIOD_DESC}", userPrompt);
        Assert.DoesNotContain("{_PERIOD_SELECTION}", userPrompt);
    }

    [Fact]
    public void Expand_QR_PeriodDescParamResolved()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block qrBlock0 = expanded[2].Blocks.OrderBy(b => b.Order).First();
        string userPrompt = GetAiQueryProperties(qrBlock0).UserPromptTemplate!;

        Assert.Contains("{InterimPeriod} month income statement", userPrompt);
        Assert.DoesNotContain("{_PERIOD_DESC}", userPrompt);
    }

    [Fact]
    public void Expand_QR_PeriodSelectionRuleAppearsInPrompt()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block qrBlock0 = expanded[2].Blocks.OrderBy(b => b.Order).First();
        string userPrompt = GetAiQueryProperties(qrBlock0).UserPromptTemplate!;

        Assert.Contains("Period Selection:", userPrompt);
        Assert.Contains("{QR_FS_ReportingDate.reporting_date}", userPrompt);
    }

    [Fact]
    public void Expand_AR_DoesNotContainPeriodSelectionRule()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block arBlock0 = expanded[0].Blocks.OrderBy(b => b.Order).First();
        string userPrompt = GetAiQueryProperties(arBlock0).UserPromptTemplate!;

        Assert.DoesNotContain("Period Selection:", userPrompt);
    }

    #endregion

    #region System Prompt Param Resolution Tests

    [Fact]
    public void Expand_QR_SystemPromptHasQrPeriodParams()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block qrBlock0 = expanded[2].Blocks.OrderBy(b => b.Order).First();
        string systemPrompt = GetAiQueryProperties(qrBlock0).SystemPromptTemplate!;

        Assert.Contains("{InterimPeriod} month income statement", systemPrompt);
        Assert.Contains("for ALL available periods", systemPrompt);
        Assert.DoesNotContain("{_PERIOD_DESC}", systemPrompt);
        Assert.DoesNotContain("{_PERIOD_YEARS}", systemPrompt);
    }

    [Fact]
    public void Expand_AR_SystemPromptHasArPeriodParams()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block arBlock0 = expanded[0].Blocks.OrderBy(b => b.Order).First();
        string systemPrompt = GetAiQueryProperties(arBlock0).SystemPromptTemplate!;

        Assert.Contains("{CorporationAssessmentYear}", systemPrompt);
        Assert.Contains("{CorporationAssessmentYear-1}", systemPrompt);
        Assert.DoesNotContain("{InterimPeriod}", systemPrompt);
        Assert.DoesNotContain("{_PERIOD_YEARS}", systemPrompt);
    }

    #endregion

    #region Global Variables Untouched Tests

    [Fact]
    public void Expand_GlobalVariablesRemainUntouched()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block arBlock0 = expanded[0].Blocks.OrderBy(b => b.Order).First();
        string systemPrompt = GetAiQueryProperties(arBlock0).SystemPromptTemplate!;

        Assert.Contains("{CorporationAssessmentYear}", systemPrompt);
        Assert.Contains("{CorporationAssessmentYear-1}", systemPrompt);
    }

    [Fact]
    public void Expand_QR_InterimPeriodVariableRemainsForLaterResolution()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block qrBlock0 = expanded[2].Blocks.OrderBy(b => b.Order).First();
        string userPrompt = GetAiQueryProperties(qrBlock0).UserPromptTemplate!;

        // {InterimPeriod} should be present (from param resolution) but not yet resolved
        Assert.Contains("{InterimPeriod}", userPrompt);
    }

    #endregion

    #region SupportedDocumentClasses Tests

    [Fact]
    public void Expand_LegacyPnLConfig_AfterNormalization_BlocksHaveVariantClasses()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        Section template = AsSection(parent, section);
        template.ParameterizedConfig = Newtonsoft.Json.JsonConvert.SerializeObject(CreatePnLConfig());

        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(template);
        Assert.NotNull(config);
        List<Section> expanded = ParameterizedSectionExpander.Expand(template, config);

        Assert.All(expanded[0].Blocks, block =>
            Assert.Equal(["Annual Report"], block.SupportedDocumentClasses));
        Assert.All(expanded[1].Blocks, block =>
            Assert.Equal(["Past Annual Report"], block.SupportedDocumentClasses));
        Assert.All(expanded[2].Blocks, block =>
            Assert.Equal(["Quarterly Financials"], block.SupportedDocumentClasses));
    }

    [Fact]
    public void Expand_VariantWithoutClassMap_KeepsBlockClasses()
    {
        var parent = new Workflow("Parent");
        var section = new Section("", parent) { Title = "Test Section", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "Block")
        {
            Name = "Block",
            SupportedDocumentClasses = ["Annual Report"],
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "V1", Label: "Variant 1"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Equal(["Annual Report"], expanded[0].Blocks.Single().SupportedDocumentClasses);
    }

    #endregion

    #region Stop Gate Tests

    [Fact]
    public void Expand_AR_HasNoStopGate()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);

        Assert.Null(expanded[0].TextInputTemplate);
    }

    [Fact]
    public void Expand_PAR_HasCorrectStopGate()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);

        Assert.Equal("{verify_past_annual_report.stop}", expanded[1].TextInputTemplate);
    }

    [Fact]
    public void Expand_QR_HasCorrectStopGate()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);

        Assert.Equal("{verify_quarterly_report.stop}", expanded[2].TextInputTemplate);
    }

    #endregion

    #region Period Selection In Below Operating Tests

    [Fact]
    public void Expand_QR_BelowOperatingBlockHasPeriodSelectionFromParam()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        Block qrBlock1 = expanded[2].Blocks.OrderBy(b => b.Order).Last();
        string userPrompt = GetAiQueryProperties(qrBlock1).UserPromptTemplate!;

        Assert.Contains("Period Selection:", userPrompt);
        Assert.Contains("{QR_FS_ReportingDate.reporting_date}", userPrompt);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Expand_BlockWithEmptyReplacementTag_DoesNotCorruptOtherRefs()
    {
        var parent = new Workflow("TestWorkflow");
        var section = new Section("", parent) { Title = "Test Section", Order = 1 };

        // Block with empty tag (no ReplacementTag set)
        _ = new Block(BlockTypes.AiQuery, section, 0, "")
        {
            Name = "Empty Tag Block",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Use {ExternalRef.answer} here",
            },
        };

        // Block with a real tag
        _ = new Block(BlockTypes.AiQuery, section, 1, "RealBlock")
        {
            Name = "Real Block",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reference {ExternalRef.answer} and {RealBlock.answer}",
            },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "V1", Label: "Variant 1"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        Block expandedReal = expanded[0].Blocks.OrderBy(b => b.Order).Last();
        AiQueryProperties expandedProps = Assert.IsType<AiQueryProperties>(expandedReal.Properties);

        // {ExternalRef.answer} should NOT be corrupted by the empty tag
        Assert.Contains("{ExternalRef.answer}", expandedProps.UserPromptTemplate);
        // Internal ref {RealBlock.answer} should be suffixed
        Assert.Contains("{RealBlock_V1.answer}", expandedProps.UserPromptTemplate);
    }

    [Fact]
    public void Expand_MalformedConfig_ThrowsJsonException()
    {
        // ParameterizedSectionConfig.FromJson is called by WorkflowHandler which now has try-catch.
        // The Expand method itself receives a parsed config, so this tests the deserialization layer.
        _ = Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<ParameterizedSectionConfig>("{ invalid json }"));
    }

    /// <summary>
    /// Tests that ReplaceInAllTextFields transforms ALL placeholder-bearing properties
    /// on ALL block types. The source of truth for which properties contain placeholders
    /// is each BlockProperties subclass's ReplaceAll method.
    /// </summary>
    [Fact]
    public void Expand_AllBlockTypes_AllPlaceholderFieldsAreSuffixed()
    {
        var parent = new Workflow("TestWorkflow");
        var section = new Section("", parent) { Title = "Mixed Section", Order = 1 };

        // Source block that all others reference
        _ = new Block(BlockTypes.AiQuery, section, 0, "Src")
        {
            Name = "Source",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Extract data",
            },
        };

        // AiQuery — UserPromptTemplate, SystemPromptTemplate, TextToCache
        _ = new Block(BlockTypes.AiQuery, section, 1, "AiBlock")
        {
            Name = "AI",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "{Src.answer}",
                SystemPromptTemplate = "{Src.system}",
                TextToCache = "{Src.cache}",
            },
        };

        // Merge — TextTemplate
        _ = new Block(BlockTypes.Merge, section, 2, "MergeBlock")
        {
            Name = "Merge",
            Properties = new MergeProperties
            {
                TextTemplate = "{Src.answer}",
            },
        };

        // CalculateFormula — FormulaTemplate
        _ = new Block(BlockTypes.CalculateNumber, section, 3, "CalcBlock")
        {
            Name = "Calc",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "{Src.score}",
            },
        };

        // BuildTopics — TopicsModelTemplate
        _ = new Block(BlockTypes.BuildTopicsForAllDocuments, section, 4, "TopicsBlock")
        {
            Name = "Topics",
            Properties = new BuildTopicsProperties
            {
                TopicsModelTemplate = "{Src.topics}",
            },
        };

        // LoopStart — LoopedListTemplate
        _ = new Block(BlockTypes.LoopStart, section, 5, "LoopBlock")
        {
            Name = "Loop",
            Properties = new LoopStartProperties
            {
                LoopedListTemplate = "{Src.items}",
            },
        };

        // SwitchCase — InputTemplate, DefaultValue, Cases values
        _ = new Block(BlockTypes.SwitchCase, section, 6, "SwitchBlock")
        {
            Name = "Switch",
            Properties = new SwitchCaseProperties
            {
                InputTemplate = "{Src.category}",
                DefaultValue = "{Src.fallback}",
                Cases = new Dictionary<string, string>
                {
                    ["keyA"] = "{Src.valueA}",
                    ["keyB"] = "{Src.valueB}",
                },
            },
        };

        // IntelligentSearch — SearchTermTemplate
        _ = new Block(BlockTypes.IntelligentSearch, section, 7, "SearchBlock")
        {
            Name = "Search",
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "{Src.query}",
            },
        };

        // WebApiCall — UrlTemplate, RequestPayloadTemplate
        _ = new Block(BlockTypes.WebApiCall, section, 8, "ApiBlock")
        {
            Name = "API",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/{Src.id}",
                RequestPayloadTemplate = "{Src.body}",
            },
        };

        // DynamicChart — DataSourceJson
        _ = new Block(BlockTypes.DynamicChart, section, 9, "ChartBlock")
        {
            Name = "Chart",
            Properties = new DynamicChartProperties
            {
                DataSourceJson = "{Src.data}",
            },
        };

        // Json2Xlsx — JsonInputTemplate, OutputFileName
        _ = new Block(BlockTypes.Json2Xlsx, section, 10, "XlsxBlock")
        {
            Name = "Xlsx",
            Properties = new Json2XlsxProperties
            {
                JsonInputTemplate = "{Src.json}",
                OutputFileName = "{Src.name}",
            },
        };

        // ExchangeRate — FromCurrency, ToCurrency
        _ = new Block(BlockTypes.ExchangeRate, section, 11, "FxBlock")
        {
            Name = "FX",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "{Src.from}",
                ToCurrency = "{Src.to}",
            },
        };

        // StringManipulation: InputTemplate, Find, ReplaceWith, TrimCharacters, Separator
        _ = new Block(BlockTypes.StringManipulation, section, 12, "StringBlock")
        {
            Name = "String",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "{Src.input}",
                Find = "{Src.find}",
                ReplaceWith = "{Src.replace}",
                TrimCharacters = "{Src.trim}",
                Separator = "{Src.sep}",
            },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "V1", Label: "Variant 1"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        var blocks = expanded[0].Blocks.OrderBy(b => b.Order).ToList();

        // AiQuery: all three template fields
        var aiProps = Assert.IsType<AiQueryProperties>(blocks[1].Properties);
        Assert.Contains("{Src_V1.answer}", aiProps.UserPromptTemplate);
        Assert.Contains("{Src_V1.system}", aiProps.SystemPromptTemplate);
        Assert.Contains("{Src_V1.cache}", aiProps.TextToCache);

        // Merge: TextTemplate
        var mergeProps = Assert.IsType<MergeProperties>(blocks[2].Properties);
        Assert.Contains("{Src_V1.answer}", mergeProps.TextTemplate);

        // CalculateFormula: FormulaTemplate
        var calcProps = Assert.IsType<CalculateFormulaProperties>(blocks[3].Properties);
        Assert.Contains("{Src_V1.score}", calcProps.FormulaTemplate);

        // BuildTopics: TopicsModelTemplate
        var topicsProps = Assert.IsType<BuildTopicsProperties>(blocks[4].Properties);
        Assert.Contains("{Src_V1.topics}", topicsProps.TopicsModelTemplate);

        // LoopStart: LoopedListTemplate
        var loopProps = Assert.IsType<LoopStartProperties>(blocks[5].Properties);
        Assert.Contains("{Src_V1.items}", loopProps.LoopedListTemplate);

        // SwitchCase: InputTemplate, DefaultValue, Cases values
        var switchProps = Assert.IsType<SwitchCaseProperties>(blocks[6].Properties);
        Assert.Contains("{Src_V1.category}", switchProps.InputTemplate);
        Assert.Contains("{Src_V1.fallback}", switchProps.DefaultValue);
        Assert.Contains("{Src_V1.valueA}", switchProps.Cases["keyA"]);
        Assert.Contains("{Src_V1.valueB}", switchProps.Cases["keyB"]);

        // IntelligentSearch: SearchTermTemplate
        var searchProps = Assert.IsType<IntelligentSearchProperties>(blocks[7].Properties);
        Assert.Contains("{Src_V1.query}", searchProps.SearchTermTemplate);

        // WebApiCall: UrlTemplate, RequestPayloadTemplate
        var apiProps = Assert.IsType<WebApiCallProperties>(blocks[8].Properties);
        Assert.Contains("{Src_V1.id}", apiProps.UrlTemplate);
        Assert.Contains("{Src_V1.body}", apiProps.RequestPayloadTemplate);

        // DynamicChart: DataSourceJson
        var chartProps = Assert.IsType<DynamicChartProperties>(blocks[9].Properties);
        Assert.Contains("{Src_V1.data}", chartProps.DataSourceJson);

        // Json2Xlsx: JsonInputTemplate, OutputFileName
        var xlsxProps = Assert.IsType<Json2XlsxProperties>(blocks[10].Properties);
        Assert.Contains("{Src_V1.json}", xlsxProps.JsonInputTemplate);
        Assert.Contains("{Src_V1.name}", xlsxProps.OutputFileName);

        // ExchangeRate: FromCurrency, ToCurrency
        var fxProps = Assert.IsType<ExchangeRateProperties>(blocks[11].Properties);
        Assert.Contains("{Src_V1.from}", fxProps.FromCurrency);
        Assert.Contains("{Src_V1.to}", fxProps.ToCurrency);

        // StringManipulation: InputTemplate, Find, ReplaceWith, TrimCharacters, Separator
        var stringProps = Assert.IsType<StringManipulationProperties>(blocks[12].Properties);
        Assert.Contains("{Src_V1.input}", stringProps.InputTemplate);
        Assert.Contains("{Src_V1.find}", stringProps.Find);
        Assert.Contains("{Src_V1.replace}", stringProps.ReplaceWith);
        Assert.Contains("{Src_V1.trim}", stringProps.TrimCharacters);
        Assert.Contains("{Src_V1.sep}", stringProps.Separator);
    }

    #endregion

    #region On-Demand Expansion Guard Tests

    [Fact]
    public void Expand_ExpandedSectionsDoNotHaveParameterizedConfig()
    {
        // Ensures the recursion guard works: expanded sections must not carry
        // ParameterizedConfig, otherwise ExecuteSectionAsync would infinitely recurse.
        (Workflow? parent, Section? section) = CreateTemplateSection();
        section.ParameterizedConfig = Newtonsoft.Json.JsonConvert.SerializeObject(CreatePnLConfig());
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);

        Assert.All(expanded, s => Assert.True(string.IsNullOrWhiteSpace(s.ParameterizedConfig)));
    }

    [Fact]
    public void Expand_ExpandedSectionsCarryVariantKeyMarker()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);

        Assert.Equal(config.Variants.Count, expanded.Count);
        for (int i = 0; i < expanded.Count; i++)
        {
            Assert.Equal(config.Variants[i].Key, expanded[i].ExpandedFromParameterizedKey);
        }
    }

    [Fact]
    public void GetExpandedParameterizedSectionTitles_ReturnsExpandedVariantTitles()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        parent.ReplaceSectionWithExpanded(AsSection(parent, section), expanded);

        IReadOnlyList<string> titles = parent.GetExpandedParameterizedSectionTitles();

        Assert.Equal(config.Variants.Count, titles.Count);
        Assert.Equal(expanded.Select(s => s.Title).OrderBy(t => t), titles.OrderBy(t => t));
    }

    [Fact]
    public void GetExpandedParameterizedSectionTitles_CleanTree_ReturnsEmpty()
    {
        (Workflow? parent, Section? _) = CreateTemplateSection();

        IReadOnlyList<string> titles = parent.GetExpandedParameterizedSectionTitles();

        Assert.Empty(titles);
    }

    [Fact]
    public void GetExpandedParameterizedSectionTitles_TemplateWithConfigNotExpanded_ReturnsEmpty()
    {
        // A parameterized template that has NOT been expanded carries ParameterizedConfig
        // but no marker, so it must save to core normally (not be flagged).
        (Workflow? parent, Section? section) = CreateTemplateSection();
        section.ParameterizedConfig = Newtonsoft.Json.JsonConvert.SerializeObject(CreatePnLConfig());

        IReadOnlyList<string> titles = parent.GetExpandedParameterizedSectionTitles();

        Assert.Empty(titles);
    }

    [Fact]
    public void CloneTo_PreservesExpandedFromParameterizedKey()
    {
        // The upload path runs the tree through CloneTo before SaveCoreWorkflow inspects
        // the marker, so the marker must survive cloning or the guard sees a clean tree.
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();
        List<Section> expanded = ExpandSection(parent, section, config);
        parent.ReplaceSectionWithExpanded(AsSection(parent, section), expanded);

        var clone = new Workflow();
        parent.CloneTo(clone);

        Assert.Equal(
            config.Variants.Select(v => v.Key).OrderBy(k => k),
            clone.Sections.Select(s => s.ExpandedFromParameterizedKey).OrderBy(k => k));
    }

    [Fact]
    public void CloneToPreservingIds_PreservesExpandedFromParameterizedKey()
    {
        // Same-env re-upload uses CloneToPreservingIds; the marker must survive it too.
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();
        List<Section> expanded = ExpandSection(parent, section, config);
        parent.ReplaceSectionWithExpanded(AsSection(parent, section), expanded);

        var clone = new Workflow();
        parent.CloneToPreservingIds(clone);

        Assert.Equal(
            config.Variants.Select(v => v.Key).OrderBy(k => k),
            clone.Sections.Select(s => s.ExpandedFromParameterizedKey).OrderBy(k => k));
    }

    [Fact]
    public void ExpandedFromParameterizedKey_SurvivesJsonRoundTrip()
    {
        // The .workflow file path serializes the tree to JSON and back; the marker must
        // survive so an uploaded executed file is still detected as expanded.
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();
        List<Section> expanded = ExpandSection(parent, section, config);
        parent.ReplaceSectionWithExpanded(AsSection(parent, section), expanded);

        string json = parent.ToJson(pretty: true);
        var restored = Workflow.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(
            parent.GetExpandedParameterizedSectionTitles().OrderBy(t => t),
            restored.GetExpandedParameterizedSectionTitles().OrderBy(t => t));
    }

    [Fact]
    public void Expand_EmptyVariants_ReturnsEmptyList()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        var config = new ParameterizedSectionConfig([]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Empty(expanded);
    }

    [Fact]
    public void Expand_ExpandedSectionsHaveCorrectParentAfterReplacement()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        List<Section> expanded = ExpandSection(parent, section, config);
        parent.ReplaceSectionWithExpanded(AsSection(parent, section), expanded);

        Assert.All(expanded, s =>
        {
            Assert.Equal(parent, s.Workflow);
            Assert.Equal(parent.Id, s.WorkflowId);
        });
    }

    [Fact]
    public void Expand_OriginalSectionRemovedFromTreeAfterReplacement()
    {
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();
        Section templateSection = AsSection(parent, section);

        List<Section> expanded = ExpandSection(parent, section, config);
        parent.ReplaceSectionWithExpanded(templateSection, expanded);

        Assert.DoesNotContain(templateSection, parent.Sections);
    }

    [Fact]
    public void Expand_VariantStopGatesAreIndependent()
    {
        // Verifies each variant gets its own stop gate, so only PQR skips with AF11
        // while AR/PAR/QR run normally.
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "AR", Label: "AR", SectionStopGate: null),
            new SectionVariant(Key: "PAR", Label: "PAR", SectionStopGate: "{verify_par.stop}"),
            new SectionVariant(Key: "PQR", Label: "PQR", SectionStopGate: "{verify_pqr.stop}"),
        ]);

        (Workflow? parent, Section? section) = CreateTemplateSection();
        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Null(expanded[0].TextInputTemplate);
        Assert.Equal("{verify_par.stop}", expanded[1].TextInputTemplate);
        Assert.Equal("{verify_pqr.stop}", expanded[2].TextInputTemplate);
    }

    [Fact]
    public void Expand_AllBlocksInTreeAccessibleViaGetAllBlocks()
    {
        // After expansion and replacement, FindBlocksByReplacementTag should find
        // blocks from both expanded sections and other existing sections.
        (Workflow? parent, Section? section) = CreateTemplateSection();
        ParameterizedSectionConfig config = CreatePnLConfig();

        // Add another section with a block (simulating "AR - Extract F/S Page No.")
        var otherSection = new Section("", parent) { Title = "Other Section", Order = 0 };
        _ = new Block(BlockTypes.Merge, otherSection, 0, "IS_Pages_AR")
        {
            Name = "IS Pages AR",
        };

        List<Section> expanded = ExpandSection(parent, section, config);
        parent.ReplaceSectionWithExpanded(AsSection(parent, section), expanded);

        // Should find blocks from expanded sections
        Assert.NotNull(parent.FindBlockByReplacementTag("PL_Operating_AR"));
        Assert.NotNull(parent.FindBlockByReplacementTag("PL_Operating_QR"));

        // Should also find blocks from other sections
        Assert.NotNull(parent.FindBlockByReplacementTag("IS_Pages_AR"));
    }

    #endregion

    #region Document Pool Helpers

    private static Document CreateDoc(string? documentClass = null)
    {
        var fileMetadata = new FileMetadata("org", "test.pdf", 0, StorageProvider.Database);
        return new Document(fileMetadata, MediaType.ApplicationPdf) { Class = documentClass };
    }

    private static Section CreateVariantSectionWithBlock(List<string> supportedClasses)
    {
        var section = new Section { Title = "Variant" };
        _ = new Block(BlockTypes.AiQuery, section, 0, "tag")
        {
            Name = "Block",
            SupportedDocumentClasses = supportedClasses,
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        return section;
    }

    #endregion

    #region CollectDocumentsFromTree Tests

    [Fact]
    public void CollectDocumentsFromTree_IncludesChildSectionBlocksDocuments()
    {
        var root = new Workflow("Root");
        var section = new Section("", root) { Title = "Child Section" };
        var block = new Block(BlockTypes.AiQuery, section, 0, "Child")
        {
            Name = "Child Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        Document doc = CreateDoc();
        block.Documents.Add(doc);

        IReadOnlyList<Document> collected = ParameterizedSectionExpander.CollectDocumentsFromTree(root);

        Assert.Single(collected);
        Assert.Equal(doc.Id, collected[0].Id);
    }

    [Fact]
    public void CollectDocumentsFromTree_DoesNotDuplicateSameReference()
    {
        var root = new Workflow("Root");
        var section1 = new Section("", root) { Title = "S1" };
        var section2 = new Section("", root) { Title = "S2" };
        var block1 = new Block(BlockTypes.AiQuery, section1, 0, "B1")
        {
            Name = "B1",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        var block2 = new Block(BlockTypes.AiQuery, section2, 0, "B2")
        {
            Name = "B2",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        Document sharedDoc = CreateDoc();
        block1.Documents.Add(sharedDoc);
        block2.Documents.Add(sharedDoc);

        IReadOnlyList<Document> collected = ParameterizedSectionExpander.CollectDocumentsFromTree(root);

        Assert.Single(collected);
    }

    [Fact]
    public void CollectDocumentsFromTree_NoDocsAttached_ReturnsEmpty()
    {
        var root = new Workflow("Root");
        var section = new Section("", root) { Title = "S" };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        IReadOnlyList<Document> collected = ParameterizedSectionExpander.CollectDocumentsFromTree(root);

        Assert.Empty(collected);
    }

    #endregion

    #region DistributeDocumentsToVariants Tests

    [Fact]
    public void DistributeDocumentsToVariants_VariantBlockWithEmptyClasses_GetsAllDocs()
    {
        Section variant = CreateVariantSectionWithBlock([]);
        List<Document> docs = [CreateDoc("Annual Report"), CreateDoc("Quarterly Financials"), CreateDoc()];

        ParameterizedSectionExpander.DistributeDocumentsToVariants([variant], docs);

        HashSet<string> attachedIds = [.. variant.Blocks.Single().Documents.Select(d => d.Id)];
        HashSet<string> expectedIds = [.. docs.Select(d => d.Id)];
        Assert.Equal(expectedIds, attachedIds);
    }

    [Fact]
    public void DistributeDocumentsToVariants_VariantBlockWithAnyClass_GetsAllDocs()
    {
        Section variant = CreateVariantSectionWithBlock(["Any"]);
        List<Document> docs = [CreateDoc("Annual Report"), CreateDoc("Quarterly Financials")];

        ParameterizedSectionExpander.DistributeDocumentsToVariants([variant], docs);

        HashSet<string> attachedIds = [.. variant.Blocks.Single().Documents.Select(d => d.Id)];
        HashSet<string> expectedIds = [.. docs.Select(d => d.Id)];
        Assert.Equal(expectedIds, attachedIds);
    }

    [Fact]
    public void DistributeDocumentsToVariants_VariantBlockWithNoneClass_GetsNoDocs()
    {
        Section variant = CreateVariantSectionWithBlock(["None"]);
        List<Document> docs = [CreateDoc("Annual Report"), CreateDoc("Quarterly Financials")];

        ParameterizedSectionExpander.DistributeDocumentsToVariants([variant], docs);

        Assert.Empty(variant.Blocks.Single().Documents);
    }

    [Fact]
    public void DistributeDocumentsToVariants_VariantBlockWithSpecificClass_GetsOnlyMatchingDocs()
    {
        Section variant = CreateVariantSectionWithBlock(["Annual Report"]);
        Document arDoc = CreateDoc("Annual Report");
        Document qrDoc = CreateDoc("Quarterly Financials");

        ParameterizedSectionExpander.DistributeDocumentsToVariants([variant], [arDoc, qrDoc]);

        List<Document> attached = [.. variant.Blocks.Single().Documents];
        Assert.Single(attached);
        Assert.Equal(arDoc.Id, attached[0].Id);
    }

    [Fact]
    public void DistributeDocumentsToVariants_DocsWithNullClass_NotMatchedBySpecificClassFilter()
    {
        Section variant = CreateVariantSectionWithBlock(["Annual Report"]);
        Document unclassified = CreateDoc(null);
        Document arDoc = CreateDoc("Annual Report");

        ParameterizedSectionExpander.DistributeDocumentsToVariants([variant], [unclassified, arDoc]);

        List<Document> attached = [.. variant.Blocks.Single().Documents];
        Assert.Single(attached);
        Assert.Equal(arDoc.Id, attached[0].Id);
    }

    [Fact]
    public void DistributeDocumentsToVariants_MultipleVariants_FilteredIndependently()
    {
        Section arVariant = CreateVariantSectionWithBlock(["Annual Report"]);
        Section qrVariant = CreateVariantSectionWithBlock(["Quarterly Financials"]);
        Document arDoc = CreateDoc("Annual Report");
        Document qrDoc = CreateDoc("Quarterly Financials");

        ParameterizedSectionExpander.DistributeDocumentsToVariants([arVariant, qrVariant], [arDoc, qrDoc]);

        Assert.Equal(arDoc.Id, arVariant.Blocks.Single().Documents.Single().Id);
        Assert.Equal(qrDoc.Id, qrVariant.Blocks.Single().Documents.Single().Id);
    }

    #endregion

    #region Stable Block Id Suffix Determinism Tests

    [Fact]
    public void Expand_StableBlockId_SuffixedWithVariantKey()
    {
        var parent = new Workflow("StableIdWorkflow");
        var section = new Section("", parent) { Title = "Stable Section", Order = 1 };
        var block = new Block(BlockTypes.AiQuery, section, 0, "TheBlock")
        {
            Name = "The Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        block.StableBlockId = "stable-src";

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "AR", Label: "Annual Report"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Equal("stable-src_AR", expanded[0].Blocks.Single().StableBlockId);
    }

    [Fact]
    public void Expand_StableBlockId_IsDeterministicAcrossRepeatedExpansions()
    {
        var parent = new Workflow("StableIdWorkflow");
        var section = new Section("", parent) { Title = "Stable Section", Order = 1 };
        var block = new Block(BlockTypes.AiQuery, section, 0, "TheBlock")
        {
            Name = "The Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        block.StableBlockId = "stable-src";

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "AR", Label: "Annual Report"),
        ]);

        // Expanding the SAME template twice must produce identical suffixed stable ids,
        // because the suffix derives from the source stable id (not a fresh GUID).
        List<Section> first = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        List<Section> second = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        string firstId = first[0].Blocks.Single().StableBlockId!;
        string secondId = second[0].Blocks.Single().StableBlockId!;

        Assert.Equal("stable-src_AR", firstId);
        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public void Expand_StableBlockId_NullSource_LeftUnsuffixed()
    {
        var parent = new Workflow("NullStableWorkflow");
        var section = new Section("", parent) { Title = "Null Stable Section", Order = 1 };
        var block = new Block(BlockTypes.AiQuery, section, 0, "TheBlock")
        {
            Name = "The Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        block.StableBlockId = null;

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "AR", Label: "Annual Report"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Null(expanded[0].Blocks.Single().StableBlockId);
    }

    [Fact]
    public void Expand_StableBlockId_EmptySource_LeftUnsuffixed()
    {
        var parent = new Workflow("EmptyStableWorkflow");
        var section = new Section("", parent) { Title = "Empty Stable Section", Order = 1 };
        var block = new Block(BlockTypes.AiQuery, section, 0, "TheBlock")
        {
            Name = "The Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        block.StableBlockId = "";

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "AR", Label: "Annual Report"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.True(string.IsNullOrEmpty(expanded[0].Blocks.Single().StableBlockId));
    }

    #endregion

    #region Stable Section Id Suffix Tests

    [Fact]
    public void Expand_StableSectionId_SuffixedWithVariantKey()
    {
        var parent = new Workflow("StableSectionIdWorkflow");
        var section = new Section("", parent) { Title = "Stable Section", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "TheBlock")
        {
            Name = "The Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        section.StableSectionId = "stable-sec";

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "AR", Label: "Annual Report"),
            new SectionVariant(Key: "QR", Label: "Quarterly Financials"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Equal("stable-sec_AR", expanded[0].StableSectionId);
        Assert.Equal("stable-sec_QR", expanded[1].StableSectionId);
    }

    [Fact]
    public void Expand_StableSectionId_NullSource_LeftNull()
    {
        var parent = new Workflow("NullStableSectionWorkflow");
        var section = new Section("", parent) { Title = "Null Stable Section", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "TheBlock")
        {
            Name = "The Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        section.StableSectionId = null;

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "AR", Label: "Annual Report"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Null(expanded[0].StableSectionId);
    }

    #endregion

    #region ClassMap Helpers

    private static (Workflow parent, Section section) CreateMultiClassTemplateSection()
    {
        var parent = new Workflow("MultiClassWorkflow");
        var section = new Section("", parent) { Title = "Doc Verification", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "verify_AF")
        {
            Name = "Verify Audited Financials",
            SupportedDocumentClasses = ["Fund1_AuditedFinancials"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        _ = new Block(BlockTypes.AiQuery, section, 1, "verify_OM")
        {
            Name = "Verify Offering Memo",
            SupportedDocumentClasses = ["Fund1_OfferingMemo"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        return (parent, section);
    }

    #endregion

    #region ClassMap Mapping Tests

    [Fact]
    public void Expand_ClassMap_MapsEachBlocksOwnClasses()
    {
        (Workflow parent, Section section) = CreateMultiClassTemplateSection();
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = ["Fund2_AuditedFinancials"],
                ["Fund1_OfferingMemo"] = ["Fund2_OfferingMemo"],
            }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        var blocks = expanded[0].Blocks.OrderBy(b => b.Order).ToList();

        Assert.Equal(["Fund2_AuditedFinancials"], blocks[0].SupportedDocumentClasses);
        Assert.Equal(["Fund2_OfferingMemo"], blocks[1].SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_SourceFansOutToMultipleTargets()
    {
        var parent = new Workflow("FanOutWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            SupportedDocumentClasses = ["Fund1_AuditedFinancials"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = ["Fund2_AuditedFinancials", "Fund2_UnauditedFinancials"],
            }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Equal(
            ["Fund2_AuditedFinancials", "Fund2_UnauditedFinancials"],
            expanded[0].Blocks.Single().SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_EmptyTargetList_PassesThroughUnchanged()
    {
        var parent = new Workflow("EmptyTargetsWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            SupportedDocumentClasses = ["Fund1_AuditedFinancials"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        // Emptying the classes would mean "all documents" at distribution; pass through instead.
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = [],
            }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Equal(["Fund1_AuditedFinancials"], expanded[0].Blocks.Single().SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_UnmappedClassPassesThroughUnchanged()
    {
        (Workflow parent, Section section) = CreateMultiClassTemplateSection();
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = ["Fund2_AuditedFinancials"],
            }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        var blocks = expanded[0].Blocks.OrderBy(b => b.Order).ToList();

        Assert.Equal(["Fund2_AuditedFinancials"], blocks[0].SupportedDocumentClasses);
        Assert.Equal(["Fund1_OfferingMemo"], blocks[1].SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_NoneSentinelPassesThroughUnchanged()
    {
        var parent = new Workflow("NoneWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            SupportedDocumentClasses = ["None"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = ["Fund2_AuditedFinancials"],
            }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Equal(["None"], expanded[0].Blocks.Single().SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_SentinelKeyInMap_IsNotRewritten()
    {
        var parent = new Workflow("SentinelKeyWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            SupportedDocumentClasses = ["None"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        // A hand-authored map listing a sentinel must not strip the block's None status.
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["None"] = ["Fund2_AuditedFinancials"],
            }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Equal(["None"], expanded[0].Blocks.Single().SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_TwoSourcesMappingToSameTarget_Deduplicates()
    {
        var parent = new Workflow("DedupWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            SupportedDocumentClasses = ["Fund1_AuditedFinancials", "Fund1_UnauditedFinancials"],
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = ["Fund2_Financials"],
                ["Fund1_UnauditedFinancials"] = ["Fund2_Financials"],
            }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Equal(["Fund2_Financials"], expanded[0].Blocks.Single().SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_PresentIgnoresSectionSupportedDocumentClasses()
    {
        (Workflow parent, Section section) = CreateMultiClassTemplateSection();
        var config = new ParameterizedSectionConfig([
            new SectionVariant(
                Key: "F2",
                Label: "Fund 2",
                SectionSupportedDocumentClasses: ["ShouldBeIgnored"],
                ClassMap: new() { ["Fund1_AuditedFinancials"] = ["Fund2_AuditedFinancials"] }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        var blocks = expanded[0].Blocks.OrderBy(b => b.Order).ToList();

        Assert.Equal(["Fund2_AuditedFinancials"], blocks[0].SupportedDocumentClasses);
        Assert.Equal(["Fund1_OfferingMemo"], blocks[1].SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_EmptyDictionary_KeepsBlockClassesUnchanged()
    {
        (Workflow parent, Section section) = CreateMultiClassTemplateSection();
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        var blocks = expanded[0].Blocks.OrderBy(b => b.Order).ToList();

        Assert.Equal(["Fund1_AuditedFinancials"], blocks[0].SupportedDocumentClasses);
        Assert.Equal(["Fund1_OfferingMemo"], blocks[1].SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_BlockWithEmptyClasses_StaysEmpty()
    {
        var parent = new Workflow("EmptyClassesWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = ["Fund2_AuditedFinancials"],
            }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Empty(expanded[0].Blocks.Single().SupportedDocumentClasses);
    }

    [Fact]
    public void Expand_ClassMap_TemplateBlocksKeepTheirOwnClasses()
    {
        (Workflow parent, Section section) = CreateMultiClassTemplateSection();
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = ["Fund2_AuditedFinancials"],
            }),
        ]);

        _ = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        var templateBlocks = AsSection(parent, section).Blocks.OrderBy(b => b.Order).ToList();

        Assert.Equal(["Fund1_AuditedFinancials"], templateBlocks[0].SupportedDocumentClasses);
        Assert.Equal(["Fund1_OfferingMemo"], templateBlocks[1].SupportedDocumentClasses);
    }

    [Fact]
    public void DistributeDocumentsToVariants_ClassMappedBlock_GetsDocsOfMappedClass()
    {
        (Workflow parent, Section section) = CreateMultiClassTemplateSection();
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", ClassMap: new()
            {
                ["Fund1_AuditedFinancials"] = ["Fund2_AuditedFinancials"],
                ["Fund1_OfferingMemo"] = ["Fund2_OfferingMemo"],
            }),
        ]);
        Document fund1Doc = CreateDoc("Fund1_AuditedFinancials");
        Document fund2Doc = CreateDoc("Fund2_AuditedFinancials");

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        ParameterizedSectionExpander.DistributeDocumentsToVariants(expanded, [fund1Doc, fund2Doc]);

        Block verifyAf = expanded[0].Blocks.OrderBy(b => b.Order).First();
        List<Document> attached = [.. verifyAf.Documents];
        Assert.Single(attached);
        Assert.Equal(fund2Doc.Id, attached[0].Id);
    }

    #endregion

    #region Section Properties And Configuration Deep Copy Tests

    private static (Workflow parent, Section section) CreateGridLayoutTemplateSection()
    {
        var parent = new Workflow("GridWorkflow");
        var section = new Section("", parent)
        {
            Title = "Dashboard",
            Order = 1,
            Configuration = new SectionConfiguration
            {
                GridLayout = new GridLayoutSettings
                {
                    Mode = "grid",
                    Columns = 5,
                    Elements =
                    [
                        new GridElementPosition { BlockOrder = 0, Row = 1, Column = 2, RowSpan = 1, ColumnSpan = 3 },
                    ],
                },
            },
        };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        return (parent, section);
    }

    [Fact]
    public void Expand_SectionConfigurationIsCopiedToEveryVariant()
    {
        (Workflow parent, Section section) = CreateGridLayoutTemplateSection();
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F1", Label: "Fund 1"),
            new SectionVariant(Key: "F2", Label: "Fund 2"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.All(expanded, variant =>
        {
            Assert.NotNull(variant.Configuration);
            Assert.NotNull(variant.Configuration!.GridLayout);
            Assert.Equal("grid", variant.Configuration.GridLayout!.Mode);
            Assert.Equal(5, variant.Configuration.GridLayout.Columns);
            GridElementPosition element = Assert.Single(variant.Configuration.GridLayout.Elements!);
            Assert.Equal(0, element.BlockOrder);
            Assert.Equal(1, element.Row);
            Assert.Equal(2, element.Column);
            Assert.Equal(1, element.RowSpan);
            Assert.Equal(3, element.ColumnSpan);
        });
    }

    [Fact]
    public void Expand_SectionConfigurationIsDeepCopy_NoSharedReferences()
    {
        (Workflow parent, Section section) = CreateGridLayoutTemplateSection();
        Section template = AsSection(parent, section);
        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F1", Label: "Fund 1"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, config);
        Section variant = expanded[0];

        Assert.NotSame(template.Configuration, variant.Configuration);
        Assert.NotSame(template.Configuration!.GridLayout, variant.Configuration!.GridLayout);
        Assert.NotSame(template.Configuration.GridLayout!.Elements![0], variant.Configuration.GridLayout!.Elements![0]);

        variant.Configuration.GridLayout.Columns = 12;
        variant.Configuration.GridLayout.Elements[0].Row = 9;
        Assert.Equal(5, template.Configuration.GridLayout.Columns);
        Assert.Equal(1, template.Configuration.GridLayout.Elements[0].Row);
    }

    [Fact]
    public void Expand_SectionPropertiesAreCopied_IsSeparatorSurvives()
    {
        var parent = new Workflow("SeparatorWorkflow");
        var section = new Section("", parent) { Title = "---", Order = 1 };
        section.Properties.IsSeparator = true;

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F1", Label: "Fund 1"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.True(expanded[0].IsSeparator);
    }

    [Fact]
    public void Expand_SectionPropertiesAreDeepCopy_NoSharedReference()
    {
        var parent = new Workflow("PropsWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };
        Section template = AsSection(parent, section);

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F1", Label: "Fund 1"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, config);
        Section variant = expanded[0];

        Assert.NotSame(template.Properties, variant.Properties);

        variant.Properties.IsSeparator = true;
        Assert.False(template.Properties.IsSeparator);
    }

    [Fact]
    public void Expand_NullConfiguration_StaysNull()
    {
        var parent = new Workflow("NullConfigWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "B")
        {
            Name = "B",
            Properties = new AiQueryProperties { UserPromptTemplate = "x" },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F1", Label: "Fund 1"),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);

        Assert.Null(expanded[0].Configuration);
    }

    #endregion

    #region StringManipulation Rewriting Tests

    [Fact]
    public void Expand_StringManipulation_AllTemplateFieldsAreSuffixedAndParamResolved()
    {
        var parent = new Workflow("StringWorkflow");
        var section = new Section("", parent) { Title = "S", Order = 1 };
        _ = new Block(BlockTypes.AiQuery, section, 0, "Src")
        {
            Name = "Source",
            Properties = new AiQueryProperties { UserPromptTemplate = "extract" },
        };
        _ = new Block(BlockTypes.StringManipulation, section, 1, "StringBlock")
        {
            Name = "String",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "{Src.answer} {_FUND_KEY}",
                Find = "{Src.find}",
                ReplaceWith = "{_FUND_KEY}",
                TrimCharacters = "{Src.trim}",
                Separator = "{Src.sep}",
            },
        };

        var config = new ParameterizedSectionConfig([
            new SectionVariant(Key: "F2", Label: "Fund 2", Params: new() { ["_FUND_KEY"] = "Fund2" }),
        ]);

        List<Section> expanded = ParameterizedSectionExpander.Expand(AsSection(parent, section), config);
        Block stringBlock = expanded[0].Blocks.OrderBy(b => b.Order).Last();
        var stringProps = Assert.IsType<StringManipulationProperties>(stringBlock.Properties);

        Assert.Equal("{Src_F2.answer} Fund2", stringProps.InputTemplate);
        Assert.Equal("{Src_F2.find}", stringProps.Find);
        Assert.Equal("Fund2", stringProps.ReplaceWith);
        Assert.Equal("{Src_F2.trim}", stringProps.TrimCharacters);
        Assert.Equal("{Src_F2.sep}", stringProps.Separator);
    }

    #endregion
}
