const WHITESPACE_RUN = /\s+/g;
const WORD_EDGE_PUNCTUATION = /^[^0-9A-Za-z]+|[^0-9A-Za-z]+$/g;
// Parentheses and minus signs on a figure ARE the figure: a statement prints
// negatives as (1,514,705), so stripping them would let a quote assert an
// invented sign. Only sign-free separator punctuation is dropped from tokens
// that carry digits.
const FIGURE_EDGE_PUNCTUATION = /^[^0-9A-Za-z()−–-]+|[^0-9A-Za-z()−–-]+$/g;
const CONTAINS_DIGIT = /[0-9]/;
const CONTAINS_LETTER = /[A-Za-z]/;
const NON_DIGIT = /[^0-9]/g;
const PARENTHESISED_FIGURE = /^\(.*\)$/;
const LEADING_MINUS = /^[-−–]/;
const TRAILING_LEDGER_MINUS = /[-−–]$/;

function normalize(text: string): string {
  return text.replace(WHITESPACE_RUN, " ").trim();
}

// One printed value renders many ways — (1,514,705), -1,514,705, −1514705, €1,514,705 —
// so letter-free digit-bearing tokens compare as sign plus digits. The sign survives
// canonicalisation deliberately: a flipped or dropped sign is still a miss, only the
// separators and symbols around the digits stop mattering. Tokens that mix letters and
// digits (FY2024, IFRS16) are labels, not figures, and keep their exact spelling.
function canonicalFigureOf(token: string): string | null {
  const trimmed = token.replace(FIGURE_EDGE_PUNCTUATION, "");
  if (!CONTAINS_DIGIT.test(trimmed) || CONTAINS_LETTER.test(trimmed)) {
    return null;
  }
  const digits = trimmed.replace(NON_DIGIT, "");
  if (digits === "") {
    return null;
  }
  const negative =
    PARENTHESISED_FIGURE.test(trimmed) || LEADING_MINUS.test(trimmed) || TRAILING_LEDGER_MINUS.test(trimmed);
  return negative ? `-${digits}` : digits;
}

function quotableTokenOf(token: string): string {
  const figure = canonicalFigureOf(token);
  if (figure !== null) {
    return figure;
  }
  return CONTAINS_DIGIT.test(token)
    ? token.replace(FIGURE_EDGE_PUNCTUATION, "")
    : token.replace(WORD_EDGE_PUNCTUATION, "");
}

function quotableTokensOf(text: string): string[] {
  return text
    .split(WHITESPACE_RUN)
    .map(quotableTokenOf)
    .filter((token) => token !== "");
}

function isTokenSubsequence(needle: string[], hay: string[]): boolean {
  let matched = 0;
  for (const token of hay) {
    if (token === needle[matched]) {
      matched += 1;
      if (matched === needle.length) {
        return true;
      }
    }
  }
  return false;
}

// A citation counts as present when it is a verbatim substring (whitespace
// aside), or when every quoted token appears in order on ONE printed line —
// models legitimately drop columns (the note number) and add separator
// punctuation, but anchoring to a single line still rejects invented tokens,
// reordering, and quotes stitched across lines.
function quotedOnOnePrintedLine(documentText: string, asPrinted: string): boolean {
  const needle = quotableTokensOf(asPrinted);
  if (needle.length === 0) {
    return false;
  }
  return documentText.split("\n").some((line) => isTokenSubsequence(needle, quotableTokensOf(line)));
}

// The substring fast-path sees "1,514,705" inside "(1,514,705)", so on its own it would
// hand a sign flip the acceptance the token path exists to refuse. A substring match
// therefore also requires every canonical figure of the quote to appear, sign intact,
// somewhere in the searched text.
function figuresAgreeInSign(documentText: string, asPrinted: string): boolean {
  const quotedFigures = canonicalFiguresOf(asPrinted);
  if (quotedFigures.length === 0) {
    return true;
  }
  const printedFigures = new Set(canonicalFiguresOf(documentText));
  return quotedFigures.every((figure) => printedFigures.has(figure));
}

export function citationPresent(documentText: string, asPrinted: string): boolean {
  const needle = normalize(asPrinted);
  if (needle === "") {
    return false;
  }
  if (normalize(documentText).includes(needle) && figuresAgreeInSign(documentText, asPrinted)) {
    return true;
  }
  return quotedOnOnePrintedLine(documentText, asPrinted);
}

const PDFTOTEXT_PAGE_SEPARATOR = "\f";

export function pageTextsOf(documentText: string): string[] {
  const chunks = documentText.split(PDFTOTEXT_PAGE_SEPARATOR);
  if (chunks.length > 1 && chunks[chunks.length - 1] === "") {
    chunks.pop();
  }
  return chunks;
}

export function citationPresentOnPage(pageTexts: string[], page: number, asPrinted: string): boolean {
  const pageText = pageTexts[page - 1];
  if (page < 1 || pageText === undefined) {
    return false;
  }
  return citationPresent(pageText, asPrinted);
}

function canonicalFiguresOf(text: string): string[] {
  return text
    .split(WHITESPACE_RUN)
    .map(canonicalFigureOf)
    .filter((figure): figure is string => figure !== null);
}

export function citationQuotesFigures(asPrinted: string): boolean {
  return canonicalFiguresOf(asPrinted).length > 0;
}

// A quote can prove where a figure came from only if it carries the figure: the
// reported value must appear, digit-canonically, among the quote's own figures.
// A quote with figures but not this one is quoting a different number than the
// extraction reported: a real line cited for an invented value.
export function citationCoversFigure(asPrinted: string, value: number): boolean {
  const canonicalValue = canonicalFigureOf(String(value));
  if (canonicalValue === null) {
    return false;
  }
  return canonicalFiguresOf(asPrinted).includes(canonicalValue);
}

export type CitationToCheck = {
  field: string;
  asPrinted: string;
  page: number | null;
  values: readonly (number | null)[];
};

// One verdict per citation, two independent questions: is the quote really printed
// where it claims (on its cited page, or anywhere in the document when no page is
// cited), and does the quote carry the very figures the extraction reported. Both
// are observational — they name suspects, they do not gate.
export function citationMissesOf(
  documentText: string,
  pageTexts: string[],
  citations: readonly CitationToCheck[],
): string[] {
  const misses: string[] = [];
  for (const citation of citations) {
    if (citation.page === null) {
      if (!citationPresent(documentText, citation.asPrinted)) {
        misses.push(citation.field);
      }
    } else if (!citationPresentOnPage(pageTexts, citation.page, citation.asPrinted)) {
      misses.push(`${citation.field}@page${citation.page}`);
    }
    const reported = citation.values.filter((value): value is number => value !== null);
    if (reported.length === 0) {
      continue;
    }
    if (!citationQuotesFigures(citation.asPrinted)) {
      misses.push(`${citation.field}: quote carries no figures`);
      continue;
    }
    for (const value of reported) {
      if (!citationCoversFigure(citation.asPrinted, value)) {
        misses.push(`${citation.field}: figure ${value} not in quote`);
      }
    }
  }
  return misses;
}
