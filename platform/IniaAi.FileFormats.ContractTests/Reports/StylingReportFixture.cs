using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.FileFormats.ContractTests.Reports;

// Invented report output in the shape of a full credit analysis run; used as the styling fixture.
// It describes no real company or person.
public static class StylingReportFixture
{
    public const string REPORT_TITLE = "Fernhollow Petrochemical — Credit Analysis";

    public const string S30P0Content =
"""
Fernhollow Petrochemical Co., Ltd. was founded on <fact id="c0">14 June 2001</fact> and is listed on a regional stock exchange, with its head office and main production base on an integrated coastal site. The company refines imported crude oil into fuels and aromatics and converts most of its aromatics output into purified terephthalic acid and polyester products for textile and packaging customers.

The company operates across three reportable segments: a Refining segment covering crude distillation, reforming and aromatics; a Chemicals segment covering purified terephthalic acid and specialty polymers; and a Polyester segment covering fibre, film and bottle-grade resin. Refining contributes about <fact id="c4">58%</fact> of revenue but less than a third of gross profit.

The founding family holds, directly and through holding companies, a total of <fact id="c10">3,184,000,000</fact> shares, or <fact id="c11">61.2%</fact> of share capital. No other shareholder holds more than <fact id="c12">5%</fact>.

Note: All figures converted to USD at the FY 31-Dec-<fact id="c16">2025</fact> local currency/USD rate of 0.1390.
""";

    public const string S30P1Content =
"""
Strengths:
- Free cash flow generation has improved sharply, rising from (<fact id="c0">$1.1 billion</fact>) in <fact id="c1">FY2023</fact> to <fact id="c2">$0.9 billion</fact> in <fact id="c3">FY2025</fact>, as capital expenditure fell after the polyester expansion was completed.

- Profitability improved despite lower revenue, with gross profit rising to <fact id="c6">$1.7 billion</fact> as cost of goods sold declined <fact id="c7">16.2%</fact>, faster than the <fact id="c8">12.8%</fact> fall in revenue.

- Leverage is on a credible deleveraging path, with Net Debt/EBITDA improving from <fact id="c11">5.9x</fact> to <fact id="c12">4.8x</fact> as net debt was repaid from free cash flow.

Weaknesses:
- Liquidity is weak (sources-to-uses ratio of <fact id="c16">0.48x</fact>, deficit of <fact id="c17">$3.9 billion</fact>), driven by short-term bank borrowing that must be rolled over each year.

- The capital structure is debt-heavy, with Total Debt/Total Capitalization of <fact id="c20">64.8%</fact> and Debt/Equity of <fact id="c21">1.84x</fact>.

- Crude supply depends on spot purchases from a small number of traders, which exposes margins to freight and supply disruption.
""";

    public const string S32P0Content =
"""
Fernhollow Petrochemical's governance profile presents a moderate credit concern, shaped mainly by concentrated founding-family control that reaches the board, the executive team and the related-party supply chain.

The executive team is credibly staffed across key roles. The General Manager (appointed <fact id="c1">2022</fact>, a chemical engineer) brings long operating experience from the company's own plants, and the Chief Financial Officer joined from an audit firm in <fact id="c2">2019</fact>.

The current leadership reflects an orderly transition in early <fact id="c7">2025</fact> rather than a settled structure: three of seven directors are new, and the board has not yet completed a full annual cycle together.

The governance framework meets the applicable listing rules, and the audit outcome is clean. The company has an audit committee chaired by an independent director and publishes related-party transactions each quarter.

Three governance risks warrant credit committee attention:

1. **Founding-family key-person concentration** — the founding family controls <fact id="c0">61.2%</fact> of shares and holds the chair and two executive roles, so strategy and financing decisions rest with a small group.

<fact id="c14">2</fact>. **Related-party purchasing** — about <fact id="c15">18%</fact> of feedstock is bought through a trading company owned by the founding family, which makes pricing hard to verify independently.

3. **Auditor tier and tenure** — a regional audit firm has audited the accounts for nine consecutive years, which raises modest independence considerations.

Note: All figures converted to USD at the 31-Dec-<fact id="c7">2025</fact> local currency/USD rate of 0.1390.
""";

    public const string S33P0Content =
"""
**Fernhollow Petrochemical Co., Ltd. — Industry Risk & Operating Environment**

**Integrated Refining-to-Polyester**

The operating environment is **high risk**. The binding constraint is the combination of crude price volatility and structural overcapacity in purified terephthalic acid, which together leave little margin buffer for producers that buy crude on the spot market.

**Cyclicality and Demand**

The refining-to-polyester chain is acutely cyclical. Crude oil represents <fact id="c0">70</fact>–<fact id="c1">80%</fact> of the cost of the main products, and polyester demand follows textile and packaging volumes, which move with consumer spending.

**Industry Structure, Barriers and Trends**

Entry barriers are high and protect incumbents. A new integrated complex costs several billion dollars and takes four to five years to build and commission.

Exit barriers are equally high, and this asymmetry is the root of the industry's chronic margin problem: refining and chemical assets are rarely closed, so capacity stays in the market through downturns.

The downstream polyester market is fragmented with dozens of producers, which compresses margins for volume players without integration.

The sovereign context is low severity: the home country is rated in the A range and the currency is managed against a basket.

**Key Risks**

The binding constraints from industry structure and the operating environment are:

1. **Crude price volatility and supply disruption** — the dominant profit driver for every operator; exposure is highest for producers without long-term supply contracts.
<fact id="c21">2</fact>. **Structural PTA and polyester overcapacity** — a multi-year condition that holds down revenue per tonne and is not self-correcting.
3. **Freight and shipping disruption** — longer routes and higher insurance costs raise the delivered cost of crude.
4. **Trade restrictions on export markets** — anti-dumping duties on polyester products limit sales into some regions.
<fact id="c22">5</fact>. **Demand headwinds in textiles and fuel** — textile exports declined <fact id="c2">3.1%</fact> year on year in <fact id="c4">2025</fact>, and fuel demand is flattening.

Note: All figures converted to USD at the FY 31-Dec-<fact id="c4">2025</fact> local currency/USD rate of 0.1390.
""";

    public const string S33P1Content =
"""
Fernhollow Petrochemical is a mid-sized integrated producer, distinguished by a short crude-to-polyester chain on one site but limited by its reliance on spot crude.

**Market Share and Pricing Power**

Pricing power differs across the portfolio. Specialty polymers are the strongest position, with about <fact id="c10">9%</fact> of domestic capacity and contracts that pass through most feedstock costs; bulk polyester fibre has almost no pricing power.

**Integration as Competitive Differentiator**

The main competitive advantage is the physical co-location of refining, aromatics and polyester units, which saves transport cost and lets the company shift output between products as margins change.

**Concentration, Diversification, and Sovereign Context**

Customer end-markets span textiles, packaging, construction and automotive; segment diversification is the main mitigant of polyester cyclicality.

Feedstock supplier concentration is the main structural weakness: about <fact id="c18">70%</fact> of crude is bought on the spot market from three traders.

**Key Risks from Competitive Positioning and Market Structure**

The following risks are structural to the company's competitive position:

1. **Spot crude dependence:** Without term supply contracts, the company pays more for crude in tight markets and has less security of supply than integrated peers.
<fact id="c30">2</fact>. **PTA overcapacity ceiling:** Overcapacity in purified terephthalic acid prevents the company from raising prices even in strong demand periods.
3. **Specialty polymer concentration:** Most of the margin advantage comes from one product family that is exposed to a single new competitor.
4. **Expansion commitment opacity:** The company has <fact id="c29">400,000</fact> t/yr of new capacity under construction, but has not disclosed contracted offtake for it.

Note: All figures converted to USD at the FY 31-Dec-<fact id="c19">2025</fact> local currency/USD rate of 0.1390.
""";

    public const string S34P0Content =
"""
**Rating:** Average / B

**Analysis:**
Fernhollow Petrochemical reported total net revenues of $14.2 billion in <fact id="c0">FY2025</fact>, a contraction of <fact id="c36">12.8%</fact> from $16.3 billion in the prior year, mainly because crude and product prices fell while volumes held steady.

Cost of goods sold declined <fact id="c41">16.2%</fact> to $12.5 billion from $14.9 billion, outpacing the revenue contraction and lifting gross margin.

Note: All figures converted to USD at the <fact id="c0">FY2025</fact> local currency/USD rate of 0.1390.

**Rating Rationale:**
Fernhollow Petrochemical's <fact id="c0">FY2025</fact> profitability shows a genuine improvement — gross margin expanded to <fact id="c44">12.0%</fact> from <fact id="c45">8.6%</fact> — but the improvement depends on a favourable crude price spread that may not last.

**Data Sufficiency:** High
""";

    public const string S34P1Content =
"""
**Rating:** Weak / C

**Rating Rationale:**
The liquidity sources-to-uses ratio of <fact id="c1">0.48x</fact> and a sources-minus-uses deficit of $3.9 billion both fall within the weak band.

The structural driver is a near-term debt maturity wall of about $6.2 billion — <fact id="c3">54.1%</fact> of total funded debt — that must be refinanced within twelve months.

No committed bank facilities exist and undrawn committed headroom is zero, so there is no structural backstop if bank markets tighten.

The recommended −1 qualitative overlay is supported by the concentrated maturities, the refinancing dependence and the absence of committed lines.

Note: All figures converted to USD at the FY 31-Dec-<fact id="c0">2025</fact> local currency/USD rate of 0.1390.

**Data Sufficiency:** Medium
""";

    public const string S34P2Content =
"""
**Rating:** Weak / C

**Rating Rationale:**
On a last-twelve-months basis to 31 March <fact id="c3">2026</fact>, the company converted EBITDA of $1.6 billion into operating cash flow of $1.9 billion, a conversion rate above 100%.

Operating cash flow exceeds reported EBITDA in both periods, which the cash flow reconciliation attributes mainly to a release of working capital as inventory values fell with crude prices.

Free cash flow in <fact id="c7">FY2025</fact> was used mainly to repay net debt of $1.1 billion.

Capital expenditure fell to $0.6 billion in <fact id="c7">FY2025</fact> from $1.5 billion in <fact id="c8">FY2024</fact>, a reduction of <fact id="c9">60%</fact>, as the polyester expansion was completed.

Interest coverage cannot be computed for any annual period because gross interest expense is not disclosed separately.

Free cash flow has improved in each of the three available years, from ($1.1 billion) in <fact id="c10">FY2023</fact> to $0.9 billion in <fact id="c7">FY2025</fact>.

Note: All figures converted to USD at the FY 31-Dec-<fact id="c0">2025</fact> local currency/USD rate of 0.1390.

**Data Sufficiency:** Low
""";

    public const string S34P3Content =
"""
**Rating:** Weak / C

**Rating Rationale:**
The anchor Net Debt/EBITDA of <fact id="c13">4.8x</fact> (<fact id="c14">FY2025</fact>) places the company in the C (Weak) band, although it has improved from <fact id="c15">5.9x</fact>.

The capital structure remains debt-heavy. Total Debt/Total Capitalization stood at <fact id="c6">64.8%</fact> and Debt/Equity at <fact id="c7">1.84x</fact>.

The maturity profile is a significant risk factor. About <fact id="c22">54</fact>–<fact id="c23">55%</fact> of funded debt matures within twelve months.

The distribution policy does not add to leverage concerns. The $210 million dividend paid in <fact id="c14">FY2025</fact> was covered by free cash flow.

No material off-balance-sheet items were identified. Pledged assets (fixed assets with a book value of about $420 million and land-use rights of about $180 million) secure part of the bank debt.

**Items requiring analyst attention:** (i) The ~$6.2 billion near-term maturity wall and its dependence on continuous bank market access; (ii) the related-party feedstock purchases; (iii) the undisclosed offtake for the new capacity.

Note: All figures converted to USD at the FY 31-Dec-<fact id="c0">2025</fact> local currency/USD rate of 0.1390.

**Data Sufficiency:** Medium
""";

    public static (int Order, string Title, (int Order, string Kind, string? Title, string? Subtitle, string ContentRef)[] Paragraphs)[] Sections =>
    [
        (30, "Counterparty Overview",
        [
            (0, "Paragraph", "Counterparty Overview", "", S30P0Content),
            (1, "Paragraph", "Strengths & Weaknesses", "Strengths & Weaknesses", S30P1Content)
        ]),
        (32, "Leadership & Governance",
        [
            (0, "Paragraph", "Leadership & Governance", "", S32P0Content)
        ]),
        (33, "Industry & Competitive Position",
        [
            (0, "Paragraph", "Industry & Operating Environment", "Industry & Operating Environment", S33P0Content),
            (1, "Paragraph", "Market Position", "Market Position", S33P1Content)
        ]),
        (34, "Financial Evaluation",
        [
            (0, "Paragraph", "Profitability", "Profitability", S34P0Content),
            (1, "Paragraph", "Liquidity", "Liquidity", S34P1Content),
            (2, "Paragraph", "Cashflow Protection", "Cashflow Protection", S34P2Content),
            (3, "Paragraph", "Financial Leverage & Capital Structure", "Financial Leverage & Capital Structure", S34P3Content)
        ]),
    ];

    public static WorkflowReport Build()
    {
        var report = new WorkflowReport { Title = REPORT_TITLE, AnalystName = "Test Analyst" };

        foreach ((int order, string title, (int Order, string Kind, string? Title, string? Subtitle, string ContentRef)[] paragraphs) in Sections)
        {
            var section = new ReportSection { Title = title, Order = order };
            report.Add(section);

            foreach ((int paragraphOrder, string kind, string? paragraphTitle, string? subtitle, string content) in paragraphs)
            {
                section.Add(new ReportParagraph
                {
                    Kind = Enum.Parse<ReportParagraphKind>(kind),
                    Title = paragraphTitle,
                    Subtitle = subtitle,
                    Content = content,
                    Order = paragraphOrder,
                });
            }
        }

        return report;
    }
}
