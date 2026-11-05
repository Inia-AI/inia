// \uXXXX (not HTML-entity) escaping: stays valid JSON while an HTML parser sees no markup.
const HTML_ACTIVE_CHARACTERS = ["<", ">", "&"];
// A literal, not a RegExp built from the list above: code scanning recognises a literal
// meta-character replacement as the HTML escape this is, and cannot read a constructed one.
const HTML_ACTIVE_PATTERN = /[<>&]/g;
const HTML_ACTIVE_CHARACTER_CODES = new Set(
  HTML_ACTIVE_CHARACTERS.map((character) => character.charCodeAt(0)),
);

const HEX_RADIX = 16;
const ESCAPE_HEX_DIGITS = 4;
const ESCAPE_PREFIX = "\\u";
export const HTML_ACTIVE_ESCAPE_BYTES = ESCAPE_PREFIX.length + ESCAPE_HEX_DIGITS;

export type MeasuredHtmlInertJson = { json: string; renderedBytes: number };

// Counts escapes rather than building the escaped string: rendering to measure
// would let an unbounded string pay for itself before a cap could refuse it.
export function measureHtmlInertJson(body: unknown): MeasuredHtmlInertJson {
  const json = JSON.stringify(body);
  let activeCharacters = 0;
  for (let index = 0; index < json.length; index += 1) {
    if (HTML_ACTIVE_CHARACTER_CODES.has(json.charCodeAt(index))) {
      activeCharacters += 1;
    }
  }
  return {
    json,
    renderedBytes: Buffer.byteLength(json) + activeCharacters * (HTML_ACTIVE_ESCAPE_BYTES - 1),
  };
}

export function renderHtmlInertJson(json: string): string {
  return json.replace(
    HTML_ACTIVE_PATTERN,
    (character) =>
      `${ESCAPE_PREFIX}${character.charCodeAt(0).toString(HEX_RADIX).padStart(ESCAPE_HEX_DIGITS, "0")}`,
  );
}

export function toHtmlInertJson(body: unknown): string {
  return renderHtmlInertJson(measureHtmlInertJson(body).json);
}
