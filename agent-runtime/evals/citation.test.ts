import { describe, expect, it } from "bun:test";
import {
  citationCoversFigure,
  citationMissesOf,
  citationPresent,
  citationPresentOnPage,
  citationQuotesFigures,
  pageTextsOf,
} from "./citation.js";

const CANNED_PDFTOTEXT_PAGE = `
CONSOLIDATED STATEMENT OF FINANCIAL POSITION

                                          2025            2024 (restated)
Trade receivables                   37 397 885          37 575 319
Inventories                         22 629 482          34 123 039

Total current assets               860 082 705         731 016 239
`;

describe("citationPresent", () => {
  it("finds an as-printed figure despite whitespace differences", () => {
    expect(citationPresent(CANNED_PDFTOTEXT_PAGE, "Trade receivables 37 397 885")).toBe(true);
  });

  it("survives line wrapping in the canned text", () => {
    const wrapped = "Total current\nassets     860 082 705";
    expect(citationPresent(wrapped, "Total current assets 860 082 705")).toBe(true);
  });

  it("rejects a citation that never appears", () => {
    expect(citationPresent(CANNED_PDFTOTEXT_PAGE, "Trade receivables 37 397 886")).toBe(false);
  });

  it("rejects an empty as-printed string instead of trivially matching", () => {
    expect(citationPresent(CANNED_PDFTOTEXT_PAGE, "   ")).toBe(false);
  });
});

const TWO_PAGE_DOCUMENT = `page one\nRevenue 12 345\f${CANNED_PDFTOTEXT_PAGE}`;

describe("pageTextsOf", () => {
  it("splits canned pdftotext text into pages on form feeds", () => {
    const pages = pageTextsOf(TWO_PAGE_DOCUMENT);
    expect(pages).toHaveLength(2);
    expect(pages[0]).toContain("Revenue 12 345");
    expect(pages[1]).toContain("Trade receivables");
  });

  it("counts pages, not chunks: pdftotext ends its output with a form feed", () => {
    const pages = pageTextsOf(`${TWO_PAGE_DOCUMENT}\f`);
    expect(pages).toHaveLength(2);
    expect(pages[1]).toContain("Trade receivables");
  });

  it("treats text without form feeds as a single page", () => {
    expect(pageTextsOf("just one page")).toHaveLength(1);
  });
});

describe("citationPresentOnPage", () => {
  it("finds an as-printed line on the cited page", () => {
    expect(citationPresentOnPage(pageTextsOf(TWO_PAGE_DOCUMENT), 2, "Trade receivables 37 397 885")).toBe(
      true,
    );
  });

  it("flags a cited page that does not contain the line even when another page does", () => {
    expect(citationPresentOnPage(pageTextsOf(TWO_PAGE_DOCUMENT), 1, "Trade receivables 37 397 885")).toBe(
      false,
    );
  });

  it("flags a cited page outside the document", () => {
    const pages = pageTextsOf(TWO_PAGE_DOCUMENT);
    expect(citationPresentOnPage(pages, 0, "Revenue 12 345")).toBe(false);
    expect(citationPresentOnPage(pages, 3, "Revenue 12 345")).toBe(false);
  });
});

const NOTE_COLUMN_PAGE = [
  "  CURRENT ASSETS:",
  "    Trade receivables                18       37,397,885       37,575,319",
  "    Cash and cash equivalents        22      204,454,491      326,818,129",
  "  Total current assets                       860,082,705      731,016,239",
  "    Bank loans                       25      424,903,910      153,725,756",
  "    Bond loans                       25       32,500,771       48,785,070",
].join("\n");

describe("citationPresent — quoted tokens anchored to one printed line", () => {
  it("accepts a quoted line that omits the note-column number", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Cash and cash equivalents 204,454,491 326,818,129")).toBe(true);
  });

  it("accepts model-invented separator punctuation around printed tokens", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Cash and cash equivalents | 204,454,491 | 326,818,129")).toBe(
      true,
    );
  });

  it("rejects a quote whose annotations are not printed on the line", () => {
    expect(
      citationPresent(NOTE_COLUMN_PAGE, "Cash and cash equivalents: 204,454,491 (2025), 326,818,129 (2024)"),
    ).toBe(false);
  });

  it("rejects a quote stitched from two printed lines", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Bank loans 424,903,910 Bond loans 32,500,771")).toBe(false);
  });

  it("rejects reordered figures", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Cash and cash equivalents 326,818,129 204,454,491")).toBe(
      false,
    );
  });

  it("rejects a figure that is not printed on the quoted line", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Cash and cash equivalents 204,454,999")).toBe(false);
  });

  it("rejects a quote that is only punctuation", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "| / — |")).toBe(false);
  });
});

const NEGATIVE_LINE = "    Issuance premiums deducted from costs        23     (1,514,705)     (1,514,705)";

describe("citationPresent — negative figures keep their sign", () => {
  it("accepts a quote keeping the printed parenthesised negatives while omitting the note column", () => {
    expect(
      citationPresent(NEGATIVE_LINE, "Issuance premiums deducted from costs (1,514,705) (1,514,705)"),
    ).toBe(true);
  });

  it("rejects a quote asserting a positive where the statement prints a negative", () => {
    expect(citationPresent(NEGATIVE_LINE, "Issuance premiums deducted from costs 1,514,705 1,514,705")).toBe(
      false,
    );
  });

  it("rejects a minus sign invented on a printed positive", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Cash and cash equivalents -204,454,491 326,818,129")).toBe(
      false,
    );
  });

  it("still drops separator punctuation around signed figures", () => {
    expect(
      citationPresent(NEGATIVE_LINE, "Issuance premiums deducted from costs | (1,514,705) | (1,514,705)"),
    ).toBe(true);
  });
});

describe("citationPresent — the substring fast-path cannot bypass sign integrity", () => {
  it("rejects a bare positive quoted from a parenthesised negative even as a substring", () => {
    expect(citationPresent("Row 23 (1,514,705)", "1,514,705")).toBe(false);
  });

  it("still matches a wrapped line whose figures agree in sign", () => {
    expect(citationPresent("Total current\nassets     860 082 705", "Total current assets 860 082 705")).toBe(
      true,
    );
  });
});

describe("citationPresent — en-dash and ledger trailing minus carry the sign", () => {
  it("accepts an en-dash negative quoted as a hyphen minus", () => {
    expect(citationPresent("Costs \u20131,514,705", "Costs -1514705")).toBe(true);
  });

  it("rejects a sign dropped from an en-dash negative", () => {
    expect(citationPresent("Costs \u20131,514,705", "Costs 1514705")).toBe(false);
  });

  it("rejects a minus invented over a printed positive via en-dash", () => {
    expect(citationPresent("Costs 1,514,705", "Costs \u20131,514,705")).toBe(false);
  });

  it("reads a ledger-style trailing minus as the figure's sign", () => {
    expect(citationPresent("Costs 2,431-", "Costs -2431")).toBe(true);
    expect(citationCoversFigure("Costs 2,431-", -2431)).toBe(true);
    expect(citationCoversFigure("Costs 2,431-", 2431)).toBe(false);
  });
});

describe("citationPresent — decimal points canonicalise away, a recorded tradeoff", () => {
  it("conflates decimal renderings of one figure because European thousands dots are indistinguishable", () => {
    expect(citationPresent("Total 1.514.705", "Total 1,514,705")).toBe(true);
    expect(citationPresent("Ratio 3.14", "Ratio 314")).toBe(true);
  });
});

describe("citationPresent — reformatted figures match digit-canonically", () => {
  it("accepts a quote whose figures drop the thousands separators", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Total current assets 860082705 731016239")).toBe(true);
  });

  it("accepts a parenthesised negative rendered as a minus sign", () => {
    expect(
      citationPresent(NEGATIVE_LINE, "Issuance premiums deducted from costs -1,514,705 -1,514,705"),
    ).toBe(true);
  });

  it("accepts a parenthesised negative rendered as a bare minus with separators stripped", () => {
    expect(citationPresent(NEGATIVE_LINE, "Issuance premiums deducted from costs -1514705 -1514705")).toBe(
      true,
    );
  });

  it("accepts a parenthesised negative rendered with a unicode minus", () => {
    expect(
      citationPresent(NEGATIVE_LINE, "Issuance premiums deducted from costs \u22121,514,705 \u22121,514,705"),
    ).toBe(true);
  });

  it("accepts a currency symbol prefixed to a separator-stripped figure", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Total current assets \u20ac860082705 \u20ac731016239")).toBe(
      true,
    );
  });

  it("rejects wrong digits even with the separators stripped", () => {
    expect(citationPresent(NOTE_COLUMN_PAGE, "Total current assets 860082706 731016239")).toBe(false);
  });

  it("does not canonicalise tokens that mix letters and digits", () => {
    expect(citationPresent("Revenue FY2024 1,000", "Revenue 2024 1,000")).toBe(false);
  });
});

describe("citationPresentOnPage — reformatted figures match digit-canonically", () => {
  it("accepts separator-stripped figures on the cited page", () => {
    const pages = pageTextsOf(`irrelevant first page\f${NOTE_COLUMN_PAGE}`);
    expect(citationPresentOnPage(pages, 2, "Total current assets 860082705 731016239")).toBe(true);
  });
});

describe("citationCoversFigure — the reported value appears among the quote's figures", () => {
  it("finds the reported integer despite thousands separators in the quote", () => {
    expect(citationCoversFigure("Trade receivables 37,397,885 37,575,319", 37397885)).toBe(true);
  });

  it("matches a parenthesised negative against a negative integer", () => {
    expect(citationCoversFigure("Impairment of right-of-use assets (2,431)", -2431)).toBe(true);
  });

  it("rejects a value no figure in the quote carries", () => {
    expect(citationCoversFigure("Trade receivables 37,397,885 37,575,319", 37397886)).toBe(false);
  });

  it("rejects a sign flip between the quote and the value", () => {
    expect(citationCoversFigure("Impairment of right-of-use assets (2,431)", 2431)).toBe(false);
  });

  it("rejects when the quote carries no figures at all", () => {
    expect(citationCoversFigure("Trade receivables", 37397885)).toBe(false);
  });
});

describe("citationQuotesFigures", () => {
  it("sees figures in a quoted statement row", () => {
    expect(citationQuotesFigures("Trade receivables 37,397,885 37,575,319")).toBe(true);
  });

  it("sees no figures in a row-label-only quote", () => {
    expect(citationQuotesFigures("Trade receivables")).toBe(false);
  });

  it("does not count letter-and-digit tokens as figures", () => {
    expect(citationQuotesFigures("Revenue FY2024")).toBe(false);
  });
});

describe("citationPresentOnPage — quoted tokens anchored to one printed line", () => {
  const pages = pageTextsOf(`irrelevant first page\f${NOTE_COLUMN_PAGE}`);

  it("accepts the note-column omission on the cited page", () => {
    expect(citationPresentOnPage(pages, 2, "Cash and cash equivalents 204,454,491 326,818,129")).toBe(true);
  });

  it("still flags a cited page that does not carry the line", () => {
    expect(citationPresentOnPage(pages, 1, "Cash and cash equivalents 204,454,491 326,818,129")).toBe(false);
  });
});

describe("citationMissesOf — one verdict per citation, including figure coverage", () => {
  const pages = pageTextsOf(`irrelevant first page\f${NOTE_COLUMN_PAGE}`);
  const doc = pages.join("\n");

  it("passes a citation whose quote is on its page and carries the reported values", () => {
    expect(
      citationMissesOf(doc, pages, [
        {
          field: "cashAndShortTermInvestments",
          asPrinted: "Cash and cash equivalents 204,454,491 326,818,129",
          page: 2,
          values: [204454491, 326818129],
        },
      ]),
    ).toEqual([]);
  });

  it("flags a quote absent from the cited page", () => {
    expect(
      citationMissesOf(doc, pages, [
        { field: "inventedField", asPrinted: "A line that is nowhere", page: 2, values: [] },
      ]),
    ).toEqual(["inventedField@page2"]);
  });

  it("flags a genuine quoted line cited for a value it does not carry", () => {
    expect(
      citationMissesOf(doc, pages, [
        {
          field: "rightOfUseImpairment",
          asPrinted: "Cash and cash equivalents 204,454,491 326,818,129",
          page: 2,
          values: [19800000],
        },
      ]),
    ).toEqual(["rightOfUseImpairment: figure 19800000 not in quote"]);
  });

  it("flags a row-label-only quote as carrying no figures to check", () => {
    expect(
      citationMissesOf(doc, pages, [
        {
          field: "cashAndShortTermInvestments",
          asPrinted: "Cash and cash equivalents",
          page: 2,
          values: [204454491],
        },
      ]),
    ).toEqual(["cashAndShortTermInvestments: quote carries no figures"]);
  });

  it("searches the whole document when the citation names no page", () => {
    expect(
      citationMissesOf(doc, pages, [
        {
          field: "cashAndShortTermInvestments",
          asPrinted: "Cash and cash equivalents 204,454,491 326,818,129",
          page: null,
          values: [204454491],
        },
      ]),
    ).toEqual([]);
    expect(
      citationMissesOf(doc, pages, [
        { field: "inventedField", asPrinted: "A line that is nowhere", page: null, values: [] },
      ]),
    ).toEqual(["inventedField"]);
  });

  it("does not demand figure coverage of a field whose reported values are all null", () => {
    expect(
      citationMissesOf(doc, pages, [
        {
          field: "cashAndShortTermInvestments",
          asPrinted: "Cash and cash equivalents",
          page: 2,
          values: [null, null],
        },
      ]),
    ).toEqual([]);
  });
});
