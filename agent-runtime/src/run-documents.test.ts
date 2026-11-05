import { afterAll, describe, expect, it } from "bun:test";
import { createHash } from "node:crypto";
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Readable, Writable } from "node:stream";
import { countCompetitorTurns } from "./concurrency-test-helpers.js";
import {
  MAX_DOCUMENT_META_FIELD_CHARS,
  MAX_DOCUMENTS_PER_RUN,
  MAX_RECARRIED_OUT_FILES,
  MAX_SKILL_ASSETS_PER_SKILL,
  MAX_SKILL_DESCRIPTION_CHARS,
  MAX_TOTAL_DOCUMENT_BYTES,
} from "./config.js";
import { type CargoReceiver, createCargoReceiver, writeCargo } from "./document-channel.js";
import { createNarrower, type Narrower } from "./narrow.js";
import {
  BASE64_DECODE_CHUNK_CHARS,
  cargoManifestOf,
  createDocumentBudget,
  decodeRunDocuments,
  parseDocumentMetas,
  parseRecarriedOutPayloads,
  parseRunDocumentPayloads,
  parseSkillFileMetas,
  parseSkillFilePayloads,
  RunDocumentBudgetError,
  type StagedFileMeta,
  stageStreamedInputs,
  summarizeStagedFileMetas,
} from "./run-documents.js";

class TestParseError extends Error {}

const parse: Narrower = createNarrower((message) => new TestParseError(message));

function shaOf(text: string): string {
  return createHash("sha256").update(Buffer.from(text)).digest("hex");
}

function payloadFor(text: string, name = "report.pdf"): Record<string, unknown> {
  return { name, contentBase64: Buffer.from(text).toString("base64") };
}

describe("parseRunDocumentPayloads", () => {
  it("accepts a well-formed document with the optional fields carried through", () => {
    const payloads = parseRunDocumentPayloads(
      [
        {
          ...payloadFor("hello"),
          mediaType: "application/pdf",
          documentClass: "AnnualReport",
          sha256: shaOf("hello"),
        },
      ],
      parse,
    );
    expect(payloads).toEqual([
      {
        name: "report.pdf",
        contentBase64: Buffer.from("hello").toString("base64"),
        mediaType: "application/pdf",
        documentClass: "AnnualReport",
        sha256: shaOf("hello"),
      },
    ]);
  });

  it("accepts names using the full safe charset and normalizes an uppercase sha", () => {
    const payloads = parseRunDocumentPayloads(
      [{ ...payloadFor("x", "Annual Report_2025-v1.0.pdf"), sha256: shaOf("x").toUpperCase() }],
      parse,
    );
    expect(payloads[0]?.name).toBe("Annual Report_2025-v1.0.pdf");
    expect(payloads[0]?.sha256).toBe(shaOf("x"));
  });

  it("rejects names outside the charset, path-special names, and the reserved manifest name", () => {
    for (const name of [
      "",
      ".",
      "..",
      "manifest.json",
      "a/b.pdf",
      "../escape",
      "a\\b",
      "ünïcode.pdf",
      "a\n.pdf",
      "x".repeat(201),
    ]) {
      expect(() => parseRunDocumentPayloads([payloadFor("x", name)], parse)).toThrow(TestParseError);
    }
  });

  it("rejects duplicate names within one request", () => {
    expect(() => parseRunDocumentPayloads([payloadFor("a"), payloadFor("b")], parse)).toThrow(
      /repeat the name/,
    );
  });

  it("rejects unknown fields, missing content, and malformed optional fields", () => {
    for (const bad of [
      "not-an-array",
      [null],
      ["text"],
      [{ contentBase64: "aGk=" }],
      [{ name: "a.pdf" }],
      [{ name: "a.pdf", contentBase64: "" }],
      [{ name: "a.pdf", contentBase64: "aGk=", extra: true }],
      [{ name: "a.pdf", contentBase64: "aGk=", mediaType: 7 }],
      [{ name: "a.pdf", contentBase64: "aGk=", documentClass: "" }],
      [{ name: "a.pdf", contentBase64: "aGk=", sha256: "zz" }],
      [{ name: "a.pdf", contentBase64: "aGk=", sha256: 7 }],
    ]) {
      expect(() => parseRunDocumentPayloads(bad, parse)).toThrow(TestParseError);
    }
  });

  it("rejects more documents than one run may carry and accepts exactly the cap", () => {
    const overCap = Array.from({ length: MAX_DOCUMENTS_PER_RUN + 1 }, (_, i) =>
      payloadFor("x", `doc-${i}.pdf`),
    );
    expect(() => parseRunDocumentPayloads(overCap, parse)).toThrow(TestParseError);
    const atCap = Array.from({ length: MAX_DOCUMENTS_PER_RUN }, (_, i) => payloadFor("x", `doc-${i}.pdf`));
    expect(parseRunDocumentPayloads(atCap, parse)).toHaveLength(MAX_DOCUMENTS_PER_RUN);
  });

  it("requires the sha256 when the caller demands it", () => {
    expect(() => parseRunDocumentPayloads([payloadFor("x")], parse, { requireSha256: true })).toThrow(
      TestParseError,
    );
  });
});

describe("decodeRunDocuments", () => {
  it("decodes bytes once, computes the sha256 itself, and carries the metadata", async () => {
    const payloads = parseRunDocumentPayloads(
      [{ ...payloadFor("the document body"), mediaType: "text/plain", documentClass: "Note" }],
      parse,
    );
    const documents = await decodeRunDocuments(
      payloads,
      parse,
      createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
    );
    expect(documents).toHaveLength(1);
    expect(Buffer.from(documents[0]?.bytes ?? new Uint8Array()).toString("utf8")).toBe("the document body");
    expect(documents[0]?.sha256).toBe(shaOf("the document body"));
    expect(documents[0]?.mediaType).toBe("text/plain");
    expect(documents[0]?.documentClass).toBe("Note");
  });

  it("rejects content that is not canonical base64", async () => {
    for (const contentBase64 of ["not base64!!", "aGk", "aG k=", "aGk=extra", "====", "aGk=\n"]) {
      await expect(
        decodeRunDocuments(
          [{ name: "a.pdf", contentBase64 }],
          parse,
          createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
        ),
      ).rejects.toThrow(TestParseError);
    }
  });

  it("accepts a valid multi-megabyte document, well past any regex backtracking limit", async () => {
    const bytes = Buffer.alloc(8 * 1024 * 1024, 0xab);
    const documents = await decodeRunDocuments(
      [{ name: "big.pdf", contentBase64: bytes.toString("base64") }],
      parse,
      createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
    );
    expect(documents).toHaveLength(1);
    expect(documents[0]?.bytes.byteLength).toBe(bytes.byteLength);
  });

  it("rejects a declared sha256 that does not match the decoded bytes", async () => {
    const payload = {
      name: "a.pdf",
      contentBase64: Buffer.from("real").toString("base64"),
      sha256: shaOf("fake"),
    };
    await expect(
      decodeRunDocuments([payload], parse, createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES)),
    ).rejects.toThrow(/sha256/);
  });

  it("fails the budget from decoded sizes summed across all documents", async () => {
    const chunk = Buffer.alloc(40, 7).toString("base64");
    const payloads = [1, 2, 3].map((i) => ({ name: `part-${i}.bin`, contentBase64: chunk }));
    await expect(decodeRunDocuments(payloads, parse, createDocumentBudget(100))).rejects.toThrow(
      RunDocumentBudgetError,
    );
  });

  it("accepts documents that together sit exactly on the budget", async () => {
    const payloads = [1, 2].map((i) => ({
      name: `part-${i}.bin`,
      contentBase64: Buffer.alloc(50, 1).toString("base64"),
    }));
    const documents = await decodeRunDocuments(payloads, parse, createDocumentBudget(100));
    expect(documents[0]?.bytes.byteLength).toBe(50);
    expect(documents[1]?.bytes.byteLength).toBe(50);
  });

  it("has no default budget: every decode names the authority it decodes under", () => {
    expect(MAX_TOTAL_DOCUMENT_BYTES).toBe(200 * 1024 * 1024);
    expect(decodeRunDocuments.length).toBe(3);
  });

  const DECODE_CHUNK_BYTES = (BASE64_DECODE_CHUNK_CHARS / 4) * 3;

  function patternedBytes(size: number): Buffer {
    const bytes = Buffer.alloc(size);
    const period = Math.min(size, 256);
    for (let i = 0; i < period; i += 1) {
      bytes[i] = (i * 31 + 7) & 0xff;
    }
    for (let filled = period; filled < size; filled += filled) {
      bytes.copyWithin(filled, 0, Math.min(filled, size - filled));
    }
    return bytes;
  }

  it("keeps the decode chunk on base64's four-character quantum", () => {
    expect(BASE64_DECODE_CHUNK_CHARS % 4).toBe(0);
    expect(BASE64_DECODE_CHUNK_CHARS).toBeGreaterThan(0);
  });

  it("decodes a document spanning several chunks byte-for-byte at a non-chunk-aligned length", async () => {
    const cases: { size: number; padding: string; chunkAligned: boolean }[] = [
      { size: DECODE_CHUNK_BYTES * 2 + 1001, padding: "=", chunkAligned: false },
      { size: DECODE_CHUNK_BYTES * 2 + 1000, padding: "==", chunkAligned: false },
      { size: DECODE_CHUNK_BYTES * 2, padding: "", chunkAligned: true },
    ];
    for (const { size, padding, chunkAligned } of cases) {
      const bytes = patternedBytes(size);
      const contentBase64 = bytes.toString("base64");
      expect(contentBase64.match(/=*$/)?.[0]).toBe(padding);
      expect(contentBase64.length % BASE64_DECODE_CHUNK_CHARS === 0).toBe(chunkAligned);
      const [document] = await decodeRunDocuments(
        [{ name: "spanning.bin", contentBase64 }],
        parse,
        createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
      );
      if (document === undefined) {
        throw new Error("expected one decoded document");
      }
      expect(document.bytes.byteLength).toBe(size);
      expect(Buffer.from(document.bytes).equals(bytes)).toBe(true);
      expect(document.sha256).toBe(createHash("sha256").update(bytes).digest("hex"));
    }
  });

  it("charges a multi-chunk document by its projected size, against the shared budget", async () => {
    const size = DECODE_CHUNK_BYTES * 2 + 1001;
    const contentBase64 = patternedBytes(size).toString("base64");
    await expect(
      decodeRunDocuments([{ name: "over.bin", contentBase64 }], parse, createDocumentBudget(size - 1)),
    ).rejects.toThrow(RunDocumentBudgetError);
    const budget = createDocumentBudget(size);
    const [document] = await decodeRunDocuments([{ name: "exact.bin", contentBase64 }], parse, budget);
    expect(document?.bytes.byteLength).toBe(size);
    await expect(
      decodeRunDocuments([{ name: "one-more.bin", contentBase64: "AAAA" }], parse, budget),
    ).rejects.toThrow(RunDocumentBudgetError);
  });

  it("rejects padding that lands at an interior chunk boundary, exactly like the whole-string pass", async () => {
    const contentBase64 = `${"A".repeat(BASE64_DECODE_CHUNK_CHARS - 2)}==AAAA`;
    await expect(
      decodeRunDocuments(
        [{ name: "midpad.bin", contentBase64 }],
        parse,
        createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
      ),
    ).rejects.toThrow(/canonical/);
  });

  it("hands the event loop back within one document, not only between documents", async () => {
    const chunkCount = 4;
    const contentBase64 = patternedBytes(DECODE_CHUNK_BYTES * chunkCount).toString("base64");
    const competitorTurns = await countCompetitorTurns(() =>
      decodeRunDocuments(
        [{ name: "large.bin", contentBase64 }],
        parse,
        createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
      ),
    );
    expect(competitorTurns).toBeGreaterThanOrEqual(chunkCount);
  });

  it("reports non-canonical content ahead of the budget when both would refuse", async () => {
    const contentBase64 = `${"A".repeat(200)}!!!!`;
    await expect(
      decodeRunDocuments([{ name: "bad.bin", contentBase64 }], parse, createDocumentBudget(100)),
    ).rejects.toThrow(/canonical/);
  });
});

describe("parseSkillFilePayloads", () => {
  it("accepts a minimal skill file and normalizes its declared sha", () => {
    const payloads = parseSkillFilePayloads(
      [{ ...payloadFor("# how to grade", "grading.md"), sha256: shaOf("# how to grade").toUpperCase() }],
      parse,
    );
    expect(payloads).toEqual([
      {
        name: "grading.md",
        contentBase64: Buffer.from("# how to grade").toString("base64"),
        sha256: shaOf("# how to grade"),
      },
    ]);
  });

  it("rejects the document-only metadata fields — skill files stay minimal", () => {
    for (const bad of [
      [{ ...payloadFor("x", "a.md"), mediaType: "text/markdown" }],
      [{ ...payloadFor("x", "a.md"), documentClass: "Skill" }],
      [{ ...payloadFor("x", "a.md"), unexpected: true }],
    ]) {
      expect(() => parseSkillFilePayloads(bad, parse)).toThrow(TestParseError);
    }
  });

  it("accepts an optional description and carries it through", () => {
    const payloads = parseSkillFilePayloads(
      [{ ...payloadFor("# how to grade", "grading.md"), description: "How to grade extractions" }],
      parse,
    );
    expect(payloads[0]?.description).toBe("How to grade extractions");
    expect(parseSkillFilePayloads([payloadFor("x", "a.md")], parse)[0]?.description).toBeUndefined();
  });

  it("bounds the description and refuses one on a document payload", () => {
    expect(() =>
      parseSkillFilePayloads(
        [{ ...payloadFor("x", "a.md"), description: "d".repeat(MAX_SKILL_DESCRIPTION_CHARS + 1) }],
        parse,
      ),
    ).toThrow(TestParseError);
    expect(() => parseSkillFilePayloads([{ ...payloadFor("x", "a.md"), description: 7 }], parse)).toThrow(
      TestParseError,
    );
    expect(() =>
      parseRunDocumentPayloads([{ ...payloadFor("x"), description: "not a document field" }], parse),
    ).toThrow(TestParseError);
  });

  it("rejects a name that keeps no skill identity after normalization", () => {
    expect(() => parseSkillFilePayloads([payloadFor("x", "---")], parse)).toThrow(/identity/);
  });

  it("rejects two names that collide on the same skill identity", () => {
    expect(() =>
      parseSkillFilePayloads([payloadFor("a", "huh.md"), payloadFor("b", "HUH.md")], parse),
    ).toThrow(/identity/);
  });

  it("reports a structural failure ahead of a cross-entry identity collision", () => {
    const collidingThenMalformed = [
      payloadFor("a", "huh.md"),
      payloadFor("b", "HUH.md"),
      { ...payloadFor("c", "third.md"), contentBase64: 42 },
    ];
    expect(() => parseSkillFilePayloads(collidingThenMalformed, parse)).toThrow(/skillFiles\[2\]/);
  });

  it("locates an identity failure at the entry that caused it", () => {
    expect(() => parseSkillFilePayloads([payloadFor("a", "ok.md"), payloadFor("b", "---")], parse)).toThrow(
      /skillFiles\[1\]\.name/,
    );
    expect(() =>
      parseSkillFilePayloads(
        [payloadFor("a", "one.md"), payloadFor("b", "huh.md"), payloadFor("c", "HUH.md")],
        parse,
      ),
    ).toThrow(/skillFiles\[2\]\.name/);
  });

  it("labels its failures with the skillFiles field, not documents", () => {
    expect(() => parseSkillFilePayloads([payloadFor("x", "../escape")], parse)).toThrow(/skillFiles\[0\]/);
    expect(() => parseSkillFilePayloads([payloadFor("a", "s.md"), payloadFor("b", "s.md")], parse)).toThrow(
      /skillFiles must not repeat/,
    );
    expect(() => parseSkillFilePayloads("not-an-array", parse)).toThrow(/skillFiles/);
  });

  it("enforces the same entry cap and sha requirement as documents", () => {
    const overCap = Array.from({ length: MAX_DOCUMENTS_PER_RUN + 1 }, (_, i) =>
      payloadFor("x", `skill-${i}.md`),
    );
    expect(() => parseSkillFilePayloads(overCap, parse)).toThrow(TestParseError);
    expect(() => parseSkillFilePayloads([payloadFor("x", "s.md")], parse, { requireSha256: true })).toThrow(
      TestParseError,
    );
  });
});

describe("createDocumentBudget", () => {
  it("is one authority across successive decodes — documents and skill files share the budget", async () => {
    const budget = createDocumentBudget(100);
    const documents = await decodeRunDocuments(
      [{ name: "doc.bin", contentBase64: Buffer.alloc(60, 1).toString("base64") }],
      parse,
      budget,
    );
    expect(documents[0]?.bytes.byteLength).toBe(60);
    await expect(
      decodeRunDocuments(
        [{ name: "skill.md", contentBase64: Buffer.alloc(60, 2).toString("base64") }],
        parse,
        budget,
      ),
    ).rejects.toThrow(RunDocumentBudgetError);
  });
});

const STAGE_ROOTS: string[] = [];

afterAll(() => {
  for (const root of STAGE_ROOTS) {
    rmSync(root, { recursive: true, force: true });
  }
});

describe("skill file assets", () => {
  function skillWithAssets(assets: unknown[]): Record<string, unknown> {
    return { ...payloadFor("# Verifier skill\n", "verify-skill.md"), assets };
  }

  function assetFor(text: string, name = "verify.py"): Record<string, unknown> {
    return { name, contentBase64: Buffer.from(text).toString("base64"), sha256: shaOf(text) };
  }

  it("accepts a skill file carrying assets and carries them through", () => {
    const payloads = parseSkillFilePayloads(
      [skillWithAssets([assetFor("print('check')"), assetFor("data", "table.csv")])],
      parse,
    );
    expect(payloads[0]?.assets).toEqual([
      {
        name: "verify.py",
        contentBase64: Buffer.from("print('check')").toString("base64"),
        sha256: shaOf("print('check')"),
      },
      { name: "table.csv", contentBase64: Buffer.from("data").toString("base64"), sha256: shaOf("data") },
    ]);
  });

  it("treats an empty assets array as absent — one payload shape, one fingerprint", () => {
    const payloads = parseSkillFilePayloads([skillWithAssets([])], parse);
    expect(payloads[0]).not.toContainKey("assets");
  });

  it("rejects assets on a document payload — documents stay flat", () => {
    expect(() => parseRunDocumentPayloads([{ ...payloadFor("x"), assets: [assetFor("y")] }], parse)).toThrow(
      /unknown field "assets"/,
    );
  });

  it("rejects asset names that are not ordinary single-segment file names", () => {
    for (const name of ["../escape.py", "a/b.py", "/etc/passwd", ".", "..", "", "docs\\x"]) {
      expect(() => parseSkillFilePayloads([skillWithAssets([assetFor("x", name)])], parse)).toThrow(
        /assets\[0\]\.name/,
      );
    }
  });

  it("rejects duplicate asset names within one skill but allows the same name across skills", () => {
    expect(() => parseSkillFilePayloads([skillWithAssets([assetFor("a"), assetFor("b")])], parse)).toThrow(
      /must not repeat/,
    );
    const payloads = parseSkillFilePayloads(
      [
        { ...payloadFor("s1", "one.md"), assets: [assetFor("a")] },
        { ...payloadFor("s2", "two.md"), assets: [assetFor("b")] },
      ],
      parse,
    );
    expect(payloads).toHaveLength(2);
  });

  it("rejects more assets than the per-skill cap and accepts exactly the cap", () => {
    const atCap = Array.from({ length: MAX_SKILL_ASSETS_PER_SKILL }, (_, index) =>
      assetFor("x", `asset-${index}.py`),
    );
    expect(parseSkillFilePayloads([skillWithAssets(atCap)], parse)).toHaveLength(1);
    const overCap = [...atCap, assetFor("x", "one-more.py")];
    expect(() => parseSkillFilePayloads([skillWithAssets(overCap)], parse)).toThrow(
      new RegExp(`${MAX_SKILL_ASSETS_PER_SKILL}`),
    );
  });

  it("rejects unknown fields inside an asset entry", () => {
    expect(() =>
      parseSkillFilePayloads([skillWithAssets([{ ...assetFor("x"), description: "no" }])], parse),
    ).toThrow(/unknown field "description"/);
  });

  it("requires each asset sha256 when the caller demands it", () => {
    const missingSha = { name: "verify.py", contentBase64: Buffer.from("x").toString("base64") };
    expect(() =>
      parseSkillFilePayloads([skillWithAssets([missingSha])], parse, { requireSha256: true }),
    ).toThrow(/sha256 must be present/);
  });

  it("decodes assets, verifies their declared shas, and charges the shared budget", async () => {
    const budget = createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES);
    const payloads = parseSkillFilePayloads([skillWithAssets([assetFor("print('check')")])], parse);
    const decoded = await decodeRunDocuments(payloads, parse, budget);
    expect(decoded[0]?.assets).toEqual([
      {
        name: "verify.py",
        bytes: Buffer.from("print('check')"),
        sha256: shaOf("print('check')"),
      },
    ]);
  });

  it("fails the shared budget from asset bytes, not only skill bodies", async () => {
    const budget = createDocumentBudget(4);
    const payloads = parseSkillFilePayloads([skillWithAssets([assetFor("12345")])], parse);
    expect(decodeRunDocuments(payloads, parse, budget)).rejects.toThrow(RunDocumentBudgetError);
  });

  it("rejects an asset whose bytes do not match the declared sha", async () => {
    const budget = createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES);
    const lying = {
      name: "verify.py",
      contentBase64: Buffer.from("x").toString("base64"),
      sha256: shaOf("y"),
    };
    const payloads = parseSkillFilePayloads([skillWithAssets([lying])], parse);
    expect(decodeRunDocuments(payloads, parse, budget)).rejects.toThrow(/sha256/);
  });
});

describe("staged file metas over the wire", () => {
  function metaFor(text: string, name = "report.pdf"): Record<string, unknown> {
    return { name, sha256: shaOf(text), sizeBytes: Buffer.byteLength(text) };
  }

  it("accepts a well-formed document meta with the optional fields carried through", () => {
    const metas = parseDocumentMetas(
      [{ ...metaFor("hello"), mediaType: "application/pdf", documentClass: "AnnualReport" }],
      parse,
    );
    expect(metas).toEqual([
      {
        name: "report.pdf",
        sha256: shaOf("hello"),
        sizeBytes: 5,
        mediaType: "application/pdf",
        documentClass: "AnnualReport",
      },
    ]);
  });

  it("rejects inline content — document bytes never ride a frame", () => {
    expect(() =>
      parseDocumentMetas([{ ...metaFor("x"), contentBase64: Buffer.from("x").toString("base64") }], parse),
    ).toThrow(TestParseError);
  });

  it("requires the sha256", () => {
    expect(() => parseDocumentMetas([{ name: "a.pdf", sizeBytes: 3 }], parse)).toThrow(/sha256/);
  });

  it.each([[-1], [1.5], ["10"], [undefined]])("rejects sizeBytes %p", (sizeBytes) => {
    expect(() => parseDocumentMetas([{ name: "a.pdf", sha256: shaOf("a"), sizeBytes }], parse)).toThrow(
      TestParseError,
    );
  });

  it("rejects repeated names and unsafe names", () => {
    expect(() => parseDocumentMetas([metaFor("a", "same.pdf"), metaFor("b", "same.pdf")], parse)).toThrow(
      /repeat/,
    );
    expect(() => parseDocumentMetas([metaFor("a", "../escape")], parse)).toThrow(TestParseError);
  });

  it("caps the entry count", () => {
    const metas = Array.from({ length: MAX_DOCUMENTS_PER_RUN + 1 }, (_value, index) =>
      metaFor("body", `doc-${index}.pdf`),
    );
    expect(() => parseDocumentMetas(metas, parse)).toThrow(/more than/);
  });

  it("accepts skill metas with description and sized assets", () => {
    const metas = parseSkillFileMetas(
      [
        {
          ...metaFor("# skill", "grading.md"),
          description: "How to grade",
          assets: [{ name: "verify.py", sha256: shaOf("print()"), sizeBytes: 7 }],
        },
      ],
      parse,
    );
    expect(metas).toEqual([
      {
        name: "grading.md",
        sha256: shaOf("# skill"),
        sizeBytes: 7,
        description: "How to grade",
        assets: [{ name: "verify.py", sha256: shaOf("print()"), sizeBytes: 7 }],
      },
    ]);
  });

  it("rejects a skill asset without a size", () => {
    expect(() =>
      parseSkillFileMetas(
        [{ ...metaFor("# s", "s.md"), assets: [{ name: "a.py", sha256: shaOf("x") }] }],
        parse,
      ),
    ).toThrow(/sizeBytes/);
  });

  it("still enforces skill identity uniqueness on metas", () => {
    expect(() => parseSkillFileMetas([metaFor("a", "grade.md"), metaFor("b", "Grade.MD")], parse)).toThrow(
      /identity/,
    );
  });
});

describe("document meta field bounds", () => {
  const OVERLONG = "x".repeat(MAX_DOCUMENT_META_FIELD_CHARS + 1);

  it("rejects an overlong mediaType or documentClass on an HTTP document payload", () => {
    expect(() => parseRunDocumentPayloads([{ ...payloadFor("hello"), mediaType: OVERLONG }], parse)).toThrow(
      /mediaType/,
    );
    expect(() =>
      parseRunDocumentPayloads([{ ...payloadFor("hello"), documentClass: OVERLONG }], parse),
    ).toThrow(/documentClass/);
  });

  it("rejects an overlong mediaType or documentClass on a wire document meta", () => {
    const meta = { name: "a.pdf", sha256: shaOf("a"), sizeBytes: 1 };
    expect(() => parseDocumentMetas([{ ...meta, mediaType: OVERLONG }], parse)).toThrow(/mediaType/);
    expect(() => parseDocumentMetas([{ ...meta, documentClass: OVERLONG }], parse)).toThrow(/documentClass/);
  });

  it("accepts both fields at exactly the cap", () => {
    const atCap = "y".repeat(MAX_DOCUMENT_META_FIELD_CHARS);
    const metas = parseDocumentMetas(
      [{ name: "a.pdf", sha256: shaOf("a"), sizeBytes: 1, mediaType: atCap, documentClass: atCap }],
      parse,
    );
    expect(metas[0]?.mediaType).toBe(atCap);
    expect(metas[0]?.documentClass).toBe(atCap);
  });
});

describe("cargoManifestOf", () => {
  it("orders documents first, then each skill file immediately followed by its assets", () => {
    const documents = [
      { name: "a.pdf", sha256: shaOf("a"), sizeBytes: 1 },
      { name: "b.pdf", sha256: shaOf("b"), sizeBytes: 1 },
    ];
    const skillFiles = [
      {
        name: "grade.md",
        sha256: shaOf("g"),
        sizeBytes: 1,
        assets: [
          { name: "verify.py", sha256: shaOf("v"), sizeBytes: 2 },
          { name: "table.csv", sha256: shaOf("t"), sizeBytes: 3 },
        ],
      },
      { name: "other.md", sha256: shaOf("o"), sizeBytes: 4 },
    ];
    expect(cargoManifestOf(documents, skillFiles).map((entry) => entry.name)).toEqual([
      "a.pdf",
      "b.pdf",
      "grade.md",
      "verify.py",
      "table.csv",
      "other.md",
    ]);
  });
});

describe("stageStreamedInputs", () => {
  function newWorkspace(): string {
    const workspace = mkdtempSync(join(tmpdir(), "stage-streamed-"));
    STAGE_ROOTS.push(workspace);
    return workspace;
  }

  function docMetaOf(bytes: Buffer, name: string, extra: Record<string, string> = {}): StagedFileMeta {
    return {
      name,
      sha256: createHash("sha256").update(bytes).digest("hex"),
      sizeBytes: bytes.byteLength,
      ...extra,
    };
  }

  async function receiverFor(bodies: Buffer[]): Promise<CargoReceiver> {
    const parts: Buffer[] = [];
    const collector = new Writable({
      write(chunk: Buffer, _encoding, callback) {
        parts.push(Buffer.from(chunk));
        callback();
      },
    });
    await writeCargo(
      bodies.map((body) => ({ sizeBytes: body.byteLength, open: () => Readable.from([body]) })),
      collector,
    );
    const wire = Buffer.concat(parts);
    let offset = 0;
    return createCargoReceiver({
      read: (into) => {
        const take = Math.min(into.byteLength, wire.byteLength - offset);
        wire.copy(into, 0, offset, offset + take);
        offset += take;
        return Promise.resolve(take);
      },
      close: () => {},
    });
  }

  it("streams documents to the docs area with their manifest, and returns buffered skill files", async () => {
    const workspace = newWorkspace();
    const docBytes = Buffer.from("annual report body");
    const skillBytes = Buffer.from("# grading skill");
    const assetBytes = Buffer.from("print('check')");
    const documents = [docMetaOf(docBytes, "report.pdf", { mediaType: "application/pdf" })];
    const skillFiles: StagedFileMeta[] = [
      {
        ...docMetaOf(skillBytes, "grading.md"),
        description: "How to grade",
        assets: [
          {
            name: "verify.py",
            sha256: createHash("sha256").update(assetBytes).digest("hex"),
            sizeBytes: assetBytes.byteLength,
          },
        ],
      },
    ];
    const skillInputs = await stageStreamedInputs({
      documents,
      skillFiles,
      recarriedOut: [],
      receiver: await receiverFor([docBytes, skillBytes, assetBytes]),
      parse,
      budget: createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
      workspacePath: workspace,
    });
    expect(readFileSync(join(workspace, "docs", "report.pdf")).equals(docBytes)).toBe(true);
    expect(JSON.parse(readFileSync(join(workspace, "docs", "manifest.json"), "utf8"))).toEqual([
      {
        name: "report.pdf",
        mediaType: "application/pdf",
        sizeBytes: docBytes.byteLength,
        sha256: createHash("sha256").update(docBytes).digest("hex"),
      },
    ]);
    expect(skillInputs).toHaveLength(1);
    expect(Buffer.from(skillInputs[0]?.bytes ?? new Uint8Array()).equals(skillBytes)).toBe(true);
    expect(skillInputs[0]?.description).toBe("How to grade");
    expect(Buffer.from(skillInputs[0]?.assets?.[0]?.bytes ?? new Uint8Array()).equals(assetBytes)).toBe(true);
  });

  it("creates no docs dir when the run carries no documents", async () => {
    const workspace = newWorkspace();
    const skillInputs = await stageStreamedInputs({
      documents: [],
      skillFiles: [],
      recarriedOut: [],
      receiver: await receiverFor([]),
      parse,
      budget: createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
      workspacePath: workspace,
    });
    expect(skillInputs).toEqual([]);
    expect(existsSync(join(workspace, "docs"))).toBe(false);
  });

  it("fails when the streamed bytes do not match the declared sha256", async () => {
    const workspace = newWorkspace();
    const bytes = Buffer.from("actual bytes");
    const lying = { ...docMetaOf(bytes, "report.pdf"), sha256: shaOf("something else") };
    expect(
      stageStreamedInputs({
        documents: [lying],
        skillFiles: [],
        recarriedOut: [],
        receiver: await receiverFor([bytes]),
        parse,
        budget: createDocumentBudget(MAX_TOTAL_DOCUMENT_BYTES),
        workspacePath: workspace,
      }),
    ).rejects.toThrow(/sha256/);
  });

  it("charges the budget from declared sizes before pulling any bytes", async () => {
    const workspace = newWorkspace();
    const bytes = Buffer.from("12345678");
    let bytesAsked = false;
    const receiver: CargoReceiver = {
      next: () => {
        bytesAsked = true;
        return Promise.resolve();
      },
    };
    await expect(
      stageStreamedInputs({
        documents: [docMetaOf(bytes, "report.pdf")],
        skillFiles: [],
        recarriedOut: [],
        receiver,
        parse,
        budget: createDocumentBudget(4),
        workspacePath: workspace,
      }),
    ).rejects.toThrow(RunDocumentBudgetError);
    expect(bytesAsked).toBe(false);
  });
});

describe("summarizeStagedFileMetas", () => {
  it("summarizes both areas sorted by area/name with assets carried through", () => {
    const summaries = summarizeStagedFileMetas(
      [{ name: "z.pdf", sha256: shaOf("z"), sizeBytes: 3 }],
      [
        {
          name: "grade.md",
          sha256: shaOf("g"),
          sizeBytes: 5,
          description: "d",
          assets: [{ name: "verify.py", sha256: shaOf("v"), sizeBytes: 7 }],
        },
      ],
    );
    expect(summaries).toEqual([
      { area: "documents", name: "z.pdf", sha256: shaOf("z"), sizeBytes: 3 },
      {
        area: "skills",
        name: "grade.md",
        sha256: shaOf("g"),
        sizeBytes: 5,
        description: "d",
        assets: [{ name: "verify.py", sha256: shaOf("v"), sizeBytes: 7 }],
      },
    ]);
  });
});

describe("parseRecarriedOutPayloads", () => {
  function outPayloadFor(text: string, path: string): Record<string, unknown> {
    return { path, contentBase64: Buffer.from(text).toString("base64"), sha256: shaOf(text) };
  }

  it("accepts nested paths and requires the digest on every entry", () => {
    const payloads = parseRecarriedOutPayloads(
      [outPayloadFor("model", "checks/model.py"), outPayloadFor("draft", "draft.md")],
      parse,
    );
    expect(payloads.map((payload) => payload.name)).toEqual(["checks/model.py", "draft.md"]);
    expect(() =>
      parseRecarriedOutPayloads(
        [{ path: "a.md", contentBase64: Buffer.from("x").toString("base64") }],
        parse,
      ),
    ).toThrow(TestParseError);
  });

  it("refuses walking, empty, duplicate, and oversized paths", () => {
    for (const path of ["../escape.py", "a/../b.md", "a//b.md", "", ".", "a/.", `${"x".repeat(513)}`]) {
      expect(() => parseRecarriedOutPayloads([outPayloadFor("x", path)], parse)).toThrow(TestParseError);
    }
    expect(() =>
      parseRecarriedOutPayloads([outPayloadFor("x", "a.md"), outPayloadFor("y", "a.md")], parse),
    ).toThrow(TestParseError);
  });

  it("bounds the entry count at the re-carry ceiling", () => {
    const entries = Array.from({ length: MAX_RECARRIED_OUT_FILES + 1 }, (_, index) =>
      outPayloadFor("x", `file-${index}.md`),
    );
    expect(() => parseRecarriedOutPayloads(entries, parse)).toThrow(TestParseError);
  });

  it("refuses a path nested inside another entry's path, in either order", () => {
    for (const pair of [
      ["checks", "checks/model.py"],
      ["checks/model.py", "checks"],
      ["a/b/c.md", "a/b"],
    ]) {
      expect(() =>
        parseRecarriedOutPayloads(
          pair.map((path) => outPayloadFor("x", path)),
          parse,
        ),
      ).toThrow(/must not nest the path/);
    }
    const siblings = parseRecarriedOutPayloads(
      [outPayloadFor("x", "checks/model.py"), outPayloadFor("y", "checks/other.py")],
      parse,
    );
    expect(siblings).toHaveLength(2);
  });
});

describe("stageStreamedInputs recarried out/", () => {
  function newWorkspace(): string {
    const workspace = mkdtempSync(join(tmpdir(), "stage-recarried-"));
    STAGE_ROOTS.push(workspace);
    return workspace;
  }

  async function receiverFor(bodies: Buffer[]): Promise<CargoReceiver> {
    const parts: Buffer[] = [];
    const collector = new Writable({
      write(chunk: Buffer, _encoding, callback) {
        parts.push(Buffer.from(chunk));
        callback();
      },
    });
    await writeCargo(
      bodies.map((body) => ({ sizeBytes: body.byteLength, open: () => Readable.from([body]) })),
      collector,
    );
    const wire = Buffer.concat(parts);
    let offset = 0;
    return createCargoReceiver({
      read: (into) => {
        const take = Math.min(into.byteLength, wire.byteLength - offset);
        wire.copy(into, 0, offset, offset + take);
        offset += take;
        return Promise.resolve(take);
      },
      close: () => {},
    });
  }

  function recarriedMetaOf(bytes: Buffer, name: string): StagedFileMeta {
    return {
      name,
      sha256: createHash("sha256").update(bytes).digest("hex"),
      sizeBytes: bytes.byteLength,
    };
  }

  it("stages re-carried files under out/ at their nested paths after documents and skills, with no manifest", async () => {
    const workspace = newWorkspace();
    const docBytes = Buffer.from("annual report body");
    const draftBytes = Buffer.from("# draft so far");
    const modelBytes = Buffer.from("print('check')");
    await stageStreamedInputs({
      documents: [
        { name: "report.pdf", sha256: shaOf("annual report body"), sizeBytes: docBytes.byteLength },
      ],
      skillFiles: [],
      recarriedOut: [recarriedMetaOf(draftBytes, "draft.md"), recarriedMetaOf(modelBytes, "checks/model.py")],
      receiver: await receiverFor([docBytes, draftBytes, modelBytes]),
      parse,
      budget: createDocumentBudget(1024),
      workspacePath: workspace,
    });
    expect(readFileSync(join(workspace, "out", "draft.md"), "utf8")).toBe("# draft so far");
    expect(readFileSync(join(workspace, "out", "checks", "model.py"), "utf8")).toBe("print('check')");
    expect(readdirSync(join(workspace, "out")).sort()).toEqual(["checks", "draft.md"]);
    expect(readFileSync(join(workspace, "docs", "report.pdf"), "utf8")).toBe("annual report body");
  });

  it("refuses a re-carried file whose streamed bytes do not match the declared sha256", async () => {
    const workspace = newWorkspace();
    const bytes = Buffer.from("tampered");
    await expect(
      stageStreamedInputs({
        documents: [],
        skillFiles: [],
        recarriedOut: [{ name: "draft.md", sha256: "0".repeat(64), sizeBytes: bytes.byteLength }],
        receiver: await receiverFor([bytes]),
        parse,
        budget: createDocumentBudget(1024),
        workspacePath: workspace,
      }),
    ).rejects.toThrow(TestParseError);
  });

  it("charges the re-carried bytes to the shared budget before pulling them", async () => {
    const workspace = newWorkspace();
    const bytes = Buffer.from("well over the tiny budget");
    await expect(
      stageStreamedInputs({
        documents: [],
        skillFiles: [],
        recarriedOut: [recarriedMetaOf(bytes, "draft.md")],
        receiver: await receiverFor([bytes]),
        parse,
        budget: createDocumentBudget(4),
        workspacePath: workspace,
      }),
    ).rejects.toThrow(RunDocumentBudgetError);
  });
});
