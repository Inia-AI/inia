import { describe, expect, it } from "bun:test";
import {
  HTML_ACTIVE_ESCAPE_BYTES,
  measureHtmlInertJson,
  renderHtmlInertJson,
  toHtmlInertJson,
} from "./html-inert-json.js";

describe("html-inert JSON", () => {
  it("escapes the characters an HTML parser would act on, and nothing else", () => {
    expect(toHtmlInertJson({ path: "a<b>&c" })).toBe('{"path":"a\\u003cb\\u003e\\u0026c"}');
  });

  it("stays the same document to a JSON parser", () => {
    const body = { files: [{ path: "out/<script>&.csv", sizeBytes: 1 }] };
    expect(JSON.parse(toHtmlInertJson(body))).toEqual(body);
  });

  it("measures the rendered bytes without building the rendering", () => {
    const body = { path: "a<b>&c" };
    const measured = measureHtmlInertJson(body);
    expect(measured.json).toBe(JSON.stringify(body));
    expect(measured.renderedBytes).toBe(Buffer.byteLength(renderHtmlInertJson(measured.json)));
  });

  it("counts an escape at its rendered cost, not at the character it replaced", () => {
    const plain = measureHtmlInertJson({ path: "aaa" });
    const active = measureHtmlInertJson({ path: "<<<" });
    expect(active.renderedBytes - plain.renderedBytes).toBe(3 * (HTML_ACTIVE_ESCAPE_BYTES - 1));
  });

  it("counts exactly the characters it escapes, across the whole basic multilingual plane", () => {
    const everyCharacter = Array.from({ length: 0x10000 }, (_, code) => String.fromCharCode(code)).join("");
    const measured = measureHtmlInertJson({ path: everyCharacter });
    expect(measured.renderedBytes).toBe(Buffer.byteLength(renderHtmlInertJson(measured.json)));
  });

  it("measures multi-byte characters as the bytes they serialize to", () => {
    const measured = measureHtmlInertJson({ path: "é<" });
    expect(measured.renderedBytes).toBe(Buffer.byteLength(renderHtmlInertJson(measured.json)));
  });
});
