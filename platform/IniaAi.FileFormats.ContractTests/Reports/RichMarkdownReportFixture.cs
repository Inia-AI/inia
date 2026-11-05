using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.FileFormats.ContractTests.Reports;

/// <summary>An invented credit analysis in rich Markdown: headings, emphasis, lists, a table, a quote, a rule and links.</summary>
public static class RichMarkdownReportFixture
{
    public const string EXECUTIVE_SUMMARY = """
        # Executive Summary

        **Acme Industries Ltd.** is a mid-cap manufacturer of industrial components. This
        report assesses the counterparty's creditworthiness ahead of a proposed *$25M
        revolving facility*. Our overall recommendation is ***conditional approval***,
        subject to the covenants outlined in Section 3.

        ## Key Highlights

        - Revenue grew **18% year-over-year** to $312M
        - EBITDA margin expanded to `22.4%`, up from 19.1%
        - Net leverage of **2.1x** remains within policy limits
        - One area of concern: ~~declining~~ *volatile* free cash flow conversion

        ### Rating Snapshot

        The internal model assigns an indicative rating of **BBB-**. Key drivers:

        1. Stable end-market demand
        2. Diversified customer base (no client > 8% of revenue)
        3. Experienced management team with average tenure of 11 years

        > **Analyst note:** The rating incorporates a one-notch downward adjustment for
        > the cyclicality of the industrials sector. See [methodology](https://example.com/rating-methodology)
        > for details.

        ---

        Further detail follows in subsequent sections.
        """;

    public const string FINANCIAL_ANALYSIS = """
        # Financial Analysis

        ## Income Statement Trends

        Revenue and profitability have improved across the review period, as summarized below.

        | Metric            | FY2023 | FY2024 | FY2025 | Trend |
        |-------------------|-------:|-------:|-------:|:-----:|
        | Revenue ($M)      |   245  |   264  |   312  |   ▲   |
        | Gross Margin (%)  |  34.1  |  35.0  |  37.2  |   ▲   |
        | EBITDA ($M)       |  46.8  |  52.1  |  69.9  |   ▲   |
        | Net Income ($M)   |  18.2  |  21.4  |  29.6  |   ▲   |

        Key observations:

        - **Top-line growth** accelerated in FY2025, driven by the launch of the *Series-7*
          product line and price increases of approximately `4.5%`.
        - Operating leverage improved as fixed-cost absorption increased with volume.

        ## Balance Sheet & Liquidity

        The company maintains adequate liquidity:

        1. Cash and equivalents of **$41M** at period end
        2. Undrawn committed lines of $60M
        3. No material near-term maturities until **2028**

        ### Covenant Headroom

        Computed headroom against the proposed covenant package:

        - Net Leverage: **2.1x** vs. covenant of `3.5x` — comfortable headroom
        - Interest Coverage: **8.4x** vs. covenant of `3.0x`
        - Minimum Liquidity: $41M vs. `$15M` floor

        > Stress testing a 20% EBITDA decline still leaves leverage below 3.0x.
        """;

    public const string RISKS = """
        # Risks & Considerations

        ## Primary Risks

        - **Cyclicality** — the industrials sector is exposed to macroeconomic swings
        - **Customer concentration** — while diversified, the top 10 clients represent ~45%
        - **FX exposure** — roughly 30% of revenue is EUR-denominated and *unhedged*

        ## Proposed Covenants

        The facility should include the following financial maintenance covenants:

        1. **Maximum Net Leverage** of 3.5x, tested quarterly
        2. **Minimum Interest Coverage** of 3.0x
        3. **Minimum Liquidity** of $15M at all times

        ### Conditions Precedent

        - [x] Satisfactory legal due diligence
        - [x] Audited FY2025 financial statements
        - [ ] Updated insurance certificates
        - [ ] Intercreditor agreement executed

        ## Recommendation

        We recommend ***conditional approval*** of the $25M revolving facility. The credit
        profile is consistent with a **BBB-** rating, and the proposed covenant package
        provides adequate downside protection. For escalation, contact the
        [credit committee](mailto:credit@example.com).
        """;

    public static WorkflowReport Build()
    {
        var report = new WorkflowReport
        {
            Title = "Acme Industries — Credit Analysis Report",
            AnalystName = CreditReviewReportFixture.ANALYST,
            ReviewDate = new DateTime(2026, 6, 17),
        };

        foreach ((string title, string content) in new[] { ("1. Executive Summary", EXECUTIVE_SUMMARY), ("2. Financial Analysis", FINANCIAL_ANALYSIS), ("3. Risks & Considerations", RISKS) })
        {
            var section = new ReportSection { Title = title };
            report.Add(section);
            section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = content });
        }

        return report;
    }
}
