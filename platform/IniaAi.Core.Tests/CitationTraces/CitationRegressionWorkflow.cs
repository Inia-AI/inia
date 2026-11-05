using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// A made-up fund review that sends a citation down every path a report can take: a figure a
/// block cites from a document page, a date a block cites from a web page in another form, a
/// summary that cites both blocks, so its citations resolve through two steps to the page or the
/// link, or stay on a block whose own figure no page states, a figure in millions read from a
/// table cell printed in thousands, and a draft with citations off that restates figures a
/// conversion block computed from a sterling page, so those figures cite the conversion block with
/// the page behind it.
/// </summary>
internal static class CitationRegressionWorkflow
{
    public const string FUND_FACTS_STEP = "fund-facts";
    public const string WEB_PROFILES_STEP = "web-profiles";
    public const string SUMMARY_STEP = "summary";
    public const string STATEMENT_FIGURES_STEP = "statement-figures";
    public const string USD_CONVERSION_STEP = "usd-conversion";
    public const string PERFORMANCE_DRAFT_STEP = "performance-draft";

    public const string FACTSHEET_NAME = "northwind-factsheet.pdf";
    public const string WEB_DOCUMENT_NAME = "northwind-web-research";
    public const string TEAM_URL = "https://example.org/northwind/team";
    public const string ABOUT_URL = "https://example.org/northwind/about";
    public const string ANNUAL_REPORT_NAME = "harbour-media-annual-report.pdf";
    public const string USD_CONVERSION_TITLE = "USD conversion";
    public const string STATEMENT_FIGURES_TITLE = "Statement figures";
    public const string PERFORMANCE_DRAFT_TITLE = "Performance draft";

    private const string FUND_FACTS_TAG = "fund_facts";
    private const string WEB_PROFILES_TAG = "web_profiles";
    private const string STATEMENT_FIGURES_TAG = "statement_figures";
    private const string USD_CONVERSION_TAG = "usd_conversion";
    private const string PERFORMANCE_DRAFT_TAG = "performance_draft";

    private const string FACTSHEET_KEY_FIGURES =
        "Northwind Opportunity Fund, LLC\n\n"
        + "Fund factsheet for the year ended 31 December 2025. Manager: Northwind Capital Partners LLC.\n\n"
        + "Key figures\n\n"
        + "Net asset value as at 31 December 2025: $89,997,355\n"
        + "Assets under management as at 31 December 2025: $96,159,302\n"
        + "Fund established: 9 March 2013, Delaware limited liability company\n"
        + "Management fee: 1.5% of net asset value per year\n\n"
        + "The fund invests in listed small-cap equities in North America.";

    private const string FACTSHEET_PRINCIPALS =
        "Principals\n\n"
        + "Alex Rivera is Founder and Chief Investment Officer.\n\n"
        + "Morgan Lee joined the manager as an Analyst in June 2005.\n\n"
        + "The manager employs 14 staff, of whom 5 are investment professionals.";

    private const string WEB_TEAM =
        "Our team\n\n"
        + "Alex Rivera | Managing Partner | start date: 2011-05-01\n"
        + "Morgan Lee | Partner | start date: 2016-01-15";

    private const string WEB_ABOUT =
        "About Northwind Capital Partners\n\n"
        + "Northwind Capital Partners was founded in 2013 in Wilmington, Delaware, and manages the "
        + "Northwind Opportunity Fund for pension plans and family offices.";

    private const string ANNUAL_REPORT_RESULTS =
        "Harbour Media Group plc\n\n"
        + "Annual report for the year ended 31 December 2025\n\n"
        + "Financial review\n\n"
        + "Revenue for the year was £41.5m (2024: £36.6m). The operating loss for the year was £2.4m "
        + "(2024: £0.6m). Cash at the year end was £3.5m.";

    private const string ANNUAL_REPORT_BALANCE_SHEET =
        "Consolidated statement of financial position\n"
        + "As at 31 December 2025\n\n"
        + "                                   2025      2024\n"
        + "                                  £'000     £'000\n"
        + "Contingent consideration           2,506     1,230\n"
        + "Trade and other payables           4,120     3,875\n";

    public static Workflow Build()
    {
        Workflow root = new() { Id = "citation-regression-root", Title = "Northwind citation regression" };
        Section section = new("Northwind review", root) { Id = "citation-regression-section", Order = 0 };

        Block fundFacts = AiBlock(
            section,
            order: 0,
            FUND_FACTS_TAG,
            "Fund facts",
            FUND_FACTS_STEP,
            "List the fund's net asset value, assets under management and management fee from the factsheet, "
            + "each with its figure exactly as the factsheet writes it. Then add a line headed 'Derived figures' "
            + "that states, as one dollar amount you compute yourself, the excess of assets under management "
            + "over net asset value.");
        fundFacts.Documents.Add(Document(FACTSHEET_NAME, (FACTSHEET_KEY_FIGURES, null), (FACTSHEET_PRINCIPALS, null)));
        _ = new RagSettings(fundFacts) { Type = RagTypes.WholeDocument };

        Block webProfiles = AiBlock(
            section,
            order: 1,
            WEB_PROFILES_TAG,
            "Web profiles",
            WEB_PROFILES_STEP,
            "From the web pages, state when each principal took their current role. Write each date as a month "
            + "and a year in words, for example 'March 2014', never in the numeric form the pages use.");
        webProfiles.Documents.Add(Document(WEB_DOCUMENT_NAME, (WEB_TEAM, TEAM_URL), (WEB_ABOUT, ABOUT_URL)));
        _ = new RagSettings(webProfiles) { Type = RagTypes.WholeDocument };

        _ = AiBlock(
            section,
            order: 2,
            "summary",
            "Summary",
            SUMMARY_STEP,
            "Write a three-sentence summary of the fund for a credit committee. Give the net asset value and the "
            + "excess of assets under management over net asset value exactly as the fund facts state them, and "
            + "the month and year Alex Rivera became Managing Partner as the web profiles state it. Cite the "
            + "material below for each figure and date.\n\n"
            + $"{{{FUND_FACTS_TAG}}}\n\n{{{WEB_PROFILES_TAG}}}");

        Block statementFigures = AiBlock(
            section,
            order: 3,
            STATEMENT_FIGURES_TAG,
            STATEMENT_FIGURES_TITLE,
            STATEMENT_FIGURES_STEP,
            "List the company's revenue, operating loss and year-end cash for 2025 from the annual report, each "
            + "in pounds exactly as the report writes it. Then give the 2025 contingent consideration from the "
            + "statement of financial position, written only in millions of pounds to three decimal places, for "
            + "example '£1.234m', never in thousands.");
        statementFigures.Documents.Add(Document(
            ANNUAL_REPORT_NAME,
            (ANNUAL_REPORT_RESULTS, null),
            (ANNUAL_REPORT_BALANCE_SHEET, null)));
        _ = new RagSettings(statementFigures) { Type = RagTypes.WholeDocument };

        _ = AiBlock(
            section,
            order: 4,
            USD_CONVERSION_TAG,
            USD_CONVERSION_TITLE,
            USD_CONVERSION_STEP,
            "Convert each sterling figure below to US dollars at 1.3270 dollars per pound. Write one line per "
            + "figure: the sterling figure, then the dollar figure in millions to one decimal place in brackets, "
            + "for example 'Revenue: £10.0m ($13.3m)'.\n\n"
            + $"{{{STATEMENT_FIGURES_TAG}}}");

        _ = AiBlock(
            section,
            order: 5,
            PERFORMANCE_DRAFT_TAG,
            PERFORMANCE_DRAFT_TITLE,
            PERFORMANCE_DRAFT_STEP,
            "Write two sentences on the company's 2025 performance for a credit committee. Give revenue, the "
            + "operating loss and year-end cash in US dollars only, exactly as the conversion below writes them.\n\n"
            + $"{{{USD_CONVERSION_TAG}}}",
            citesTheMaterial: false);

        return root;
    }

    private static Block AiBlock(
        Section section,
        int order,
        string tag,
        string name,
        string step,
        string prompt,
        bool citesTheMaterial = true)
    {
        return new Block(BlockTypes.AiQuery, section, replacementTag: tag)
        {
            Id = $"citation-regression-{tag}",
            Name = name,
            Order = order,
            IsOutputSource = true,
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = prompt,
                SystemPromptTemplate = $"{RecordedModelAnswers.StepMarker(step)} You are a credit analyst. "
                    + (citesTheMaterial ? "Cite the provided material for every figure and date you state. " : string.Empty)
                    + "Be concise.",
                MaxTokens = 800,
                ShouldUseAdvancedModel = false,
                ShouldGetCitationsFromModel = citesTheMaterial,
            },
        };
    }

    private static Document Document(string name, params (string Text, string? SourceUrl)[] pages)
    {
        var document = new Document(new FileMetadata(
            "IniaTest",
            name,
            pages.Sum(page => page.Text.Length),
            StorageProvider.Database,
            $"citation-regression-{name}"), MediaType.TextPlain);

        for (int index = 0; index < pages.Length; index++)
        {
            _ = new Page(document, index + 1, index + 1)
            {
                RawText = pages[index].Text,
                SourceUrl = pages[index].SourceUrl,
            };
        }

        return document;
    }
}
