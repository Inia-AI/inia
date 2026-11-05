using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.FileFormats.ContractTests.Reports;

// Invented text; it describes no real company or person. Numbered-list ordinals wrapped in <fact> tags
// once broke ordered-list parsing on Word export.
public static class IndustryReportFixture
{
    public const string IndustryCompetitivePositionParagraph1 =
"""
**Industry Risk & Operating Environment — Harrowgate Aerostructures plc**

Aerostructures supply presents an operating environment of durable structural strengths — a small number of qualified suppliers, multi-year delivery schedules and high certification barriers — set against a current-cycle combination of cost, contractual and trade headwinds that is strong enough to outweigh those strengths in the near term. The binding constraint is the gap between contract escalation formulas and actual input cost inflation, which compresses margins on work priced before the current cycle.

**Cyclicality and Demand**

Long-cycle demand is strongly positive but currently limited by production constraints. Airframe order books exceeded <fact id="c0">12,400</fact> units as of <fact id="c1">2025</fact> — roughly <fact id="c2">11</fact> years of production at current rates — while industry output ran about <fact id="c3">24%</fact> below the <fact id="c4">2019</fact> peak. Demand for structural parts follows airframe build rates with a lag of six to nine months, so supplier volumes recover more slowly than orders.

**Structure, Barriers, and Operating Environment**

The aerostructures market is among the most defensible segments in manufacturing. The five largest suppliers hold about <fact id="c25">78%</fact> of the European market, and entry barriers span capital intensity (a new wing-rib line costs <fact id="c26">£40</fact>–90M over three years), certification by each airframe maker, and decade-long qualification records that new entrants cannot buy.

The trade regime is the most acute near-term disruption. A tariff increase on imported aluminium plate from <fact id="c35">10%</fact> to <fact id="c36">25%</fact> (effective <fact id="c38">1 March 2026</fact>) raised the landed cost of the main raw material across all product lines, while contract pass-through clauses lag the cost by up to two quarters.

Labour cost has reset rather than moved with the cycle: the main collective agreement — ratified in <fact id="c52">November 2025</fact> after a <fact id="c53">21</fact>-day dispute — raised base pay by <fact id="c54">14%</fact> over three years with pension contributions up to <fact id="c21">9%</fact>, affecting about <fact id="c57">2,600</fact> production staff.

**Key Risks**

The binding constraints from industry structure and the operating environment are:

1. **Escalation index mismatch:** The gap between index-linked contract escalation formulas and actual labour and commodity cost inflation compresses margins on legacy orders for any supplier that signed long-term fixed-price or escalation-capped delivery commitments before the current cost cycle.

2. **Tariff-driven cost dislocation:** The net effect is margin compression across all product lines, with no identified offset of comparable size. The currency hedge programme does not protect against tariff-driven input cost increases, so the full increase reaches cost of sales within two quarters.

3. **Customer concentration:** Two airframe makers take <fact id="c49">41%</fact> of output, and a delivery pause at either one would reduce volumes faster than costs can be adjusted.

4. **Fixed-price development exposure:** New programmes are increasingly let on fixed-price development terms, which moves cost-overrun risk from the airframe maker to the supplier and carries asymmetric downside.

<fact id="c66">5</fact>. **Titanium supply vulnerability:** Titanium supply chains have been disrupted by sourcing restrictions, adding requalification cost and schedule risk for suppliers that depend on a single mill.
""";

    public const string IndustryCompetitivePositionParagraph2 =
"""
**Harrowgate Aerostructures plc is a capable mid-tier supplier with a concentrated customer base** — one of four qualified European makers of machined wing ribs, a second-source supplier of landing-gear parts, and a growing provider of repair services. The dominant constraint is a margin squeeze from contracts priced before the current cost cycle, which has held back cash generation despite rising volumes.

**Market Share, Pricing Power, and Competitive Structure**

The company held about <fact id="c0">12%</fact> of the European wing-rib market in <fact id="c1">2025</fact> (<fact id="c2">3,480</fact> shipsets against a market of <fact id="c3">29,000</fact>), up from about <fact id="c4">10%</fact> in <fact id="c5">2023</fact> as two smaller competitors left the market.

In machined structures, multi-year contracts and qualification costs suppress direct price competition; suppliers compete on delivery reliability, quality record and the ability to absorb rate increases, and customers rarely move work once a part is qualified.

In repair services, the company ranks #<fact id="c64">7</fact> in Europe by revenue, with repair revenue of <fact id="c17">£94M</fact> — roughly a third of the market leader's <fact id="c18">£280M</fact>. Repair margins are higher and less cyclical than new-build work.

**Concentration, Diversification, and Sovereign Exposure**

Customer concentration is pronounced: about <fact id="c30">74%</fact> of the order book, by value, is held with three customers. Geographic exposure is low risk, with most revenue earned in the United Kingdom and the euro area.

Supplier concentration amplifies both cost and continuity risk. Aluminium plate comes from two mills and titanium forgings from one, and requalifying a new source takes up to <fact id="c67">18</fact> months.

**Key Risks from Competitive Positioning and Market Structure**

The following risks are identified from the company's competitive position and market structure:

1. **Delivery reliability:** Delivery reliability is the main competitive differentiator; a quality escape or a missed rate increase would put follow-on work at risk.

2. **Structural labour cost inflation:** The <fact id="c51">November 2025</fact> agreement raised base pay by <fact id="c53">14%</fact> over three years, and contract escalators recover only part of the increase.

3. **Fixed-price development loss risk:** The company bid on two fixed-price development packages to win share; if technical complexity raises costs, it cannot pass overruns to the customer.

<fact id="c70">4</fact>. **New-entrant displacement:** Low-cost suppliers outside Europe are qualifying on simpler parts, which could move volume away from the company's least specialised lines.

5. **Plant ramp-up risk:** The new plant opened in <fact id="c60">September 2025</fact> at a cost of <fact id="c61">£118M</fact>; until it reaches full output, fixed costs weigh on margins.
""";

    public static WorkflowReport Build()
    {
        var report = new WorkflowReport
        {
            Title = "Harrowgate Aerostructures — Industry & Competitive Position",
            AnalystName = CreditReviewReportFixture.ANALYST,
            ReviewDate = new DateTime(2026, 6, 17),
        };

        var section = new ReportSection { Title = "Industry & Competitive Position" };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = IndustryCompetitivePositionParagraph1 });
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = IndustryCompetitivePositionParagraph2 });
        return report;
    }
}
