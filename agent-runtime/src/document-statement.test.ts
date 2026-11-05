import { afterEach, beforeEach, describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { composeDocumentsStatement, composeUserTurn, statedDocumentPathsOf } from "./document-statement.js";
import { sha256Hex } from "./hash.js";
import type { ReadMirror } from "./read-gate.js";
import { evaluateReadRequest } from "./read-gate.js";
import {
  DOCUMENTS_AREA,
  SKILLS_AREA,
  type StagedFileMeta,
  type StagedInputSummary,
  summarizeStagedFileMetas,
} from "./run-documents.js";
import { WORKSPACE_DOCUMENTS_DIR } from "./workspace-layout.js";

const WORKSPACE = "/workspace/run-abc123";

function documentSummary(name: string, extra: Partial<StagedInputSummary> = {}): StagedInputSummary {
  return {
    area: DOCUMENTS_AREA,
    name,
    sha256: "ab".repeat(32),
    sizeBytes: 10,
    ...extra,
  };
}

const CALLER_PROMPT = "Summarize the annual report.\nCite every figure.";

describe("composeDocumentsStatement", () => {
  it("names every staged document's absolute path and document class, one line each", () => {
    const statement = composeDocumentsStatement(WORKSPACE, [
      documentSummary("notes.txt"),
      documentSummary("report.pdf", { documentClass: "AnnualReport" }),
    ]);
    expect(statement).toBe(
      [
        "Staged input documents for this run:",
        `- ${WORKSPACE}/${WORKSPACE_DOCUMENTS_DIR}/notes.txt`,
        `- ${WORKSPACE}/${WORKSPACE_DOCUMENTS_DIR}/report.pdf (documentClass: AnnualReport)`,
      ].join("\n"),
    );
  });

  it("composes no statement for a run with no documents", () => {
    expect(composeDocumentsStatement(WORKSPACE, [])).toBeUndefined();
  });

  it("covers documents only, never skill files", () => {
    const statement = composeDocumentsStatement(WORKSPACE, [
      documentSummary("report.pdf"),
      { area: SKILLS_AREA, name: "grading.md", sha256: "cd".repeat(32), sizeBytes: 5 },
    ]);
    expect(statement).toContain("report.pdf");
    expect(statement).not.toContain("grading.md");
  });

  it("composes no statement when only skill files are staged", () => {
    expect(
      composeDocumentsStatement(WORKSPACE, [
        { area: SKILLS_AREA, name: "grading.md", sha256: "cd".repeat(32), sizeBytes: 5 },
      ]),
    ).toBeUndefined();
  });

  it("renders a document without a documentClass as its bare path, with no empty label", () => {
    const statement = composeDocumentsStatement(WORKSPACE, [documentSummary("notes.txt")]);
    expect(statement).toBe(
      ["Staged input documents for this run:", `- ${WORKSPACE}/${WORKSPACE_DOCUMENTS_DIR}/notes.txt`].join(
        "\n",
      ),
    );
    expect(statement).not.toContain("(");
  });

  it("treats a whitespace-only documentClass as absent", () => {
    const statement = composeDocumentsStatement(WORKSPACE, [
      documentSummary("notes.txt", { documentClass: "  " }),
    ]);
    expect(statement).not.toContain("(");
  });

  it("keeps each document on one line when its documentClass carries line breaks", () => {
    const statement = composeDocumentsStatement(WORKSPACE, [
      documentSummary("notes.txt", { documentClass: "board\nminutes" }),
    ]);
    expect(statement).toContain("(documentClass: board minutes)");
  });

  it("re-composes the identical statement from the same documents presented in a different order", () => {
    const reportFirst: StagedFileMeta[] = [
      { name: "report.pdf", sha256: "ab".repeat(32), sizeBytes: 4, documentClass: "Report" },
      { name: "notes.txt", sha256: "cd".repeat(32), sizeBytes: 4 },
    ];
    const notesFirst: StagedFileMeta[] = [...reportFirst].reverse();
    const startStatement = composeDocumentsStatement(WORKSPACE, summarizeStagedFileMetas(reportFirst, []));
    const resumeStatement = composeDocumentsStatement(WORKSPACE, summarizeStagedFileMetas(notesFirst, []));
    expect(startStatement).toBeDefined();
    expect(resumeStatement).toBe(startStatement);
  });
});

describe("composeUserTurn", () => {
  it("rides the statement ahead of the caller's prompt, prompt verbatim below", () => {
    const summaries = [documentSummary("report.pdf", { documentClass: "AnnualReport" })];
    const turn = composeUserTurn(CALLER_PROMPT, WORKSPACE, summaries);
    const statement = composeDocumentsStatement(WORKSPACE, summaries);
    expect(turn).toBe(`${statement}\n\n${CALLER_PROMPT}`);
    expect(turn.endsWith(CALLER_PROMPT)).toBe(true);
  });

  it("returns the caller's prompt byte for byte unchanged when no documents are staged", () => {
    expect(composeUserTurn(CALLER_PROMPT, WORKSPACE, [])).toBe(CALLER_PROMPT);
  });
});

describe("stated paths against the Read gate", () => {
  let scratchRoot: string;
  let workspacePath: string;

  beforeEach(() => {
    scratchRoot = mkdtempSync(join(tmpdir(), "document-statement-"));
    workspacePath = join(scratchRoot, "run-a");
  });

  afterEach(() => {
    rmSync(scratchRoot, { recursive: true, force: true });
  });

  // One test covering both sides: the statement's paths and the gate's accepted paths share
  // a derivation, so a workspace-layout change cannot break either silently.
  it("states exactly the paths the Read gate accepts, for every staged document", () => {
    const files = [
      { name: "report.pdf", bytes: Buffer.from("%PDF-1.7 report body") },
      { name: "board notes.txt", bytes: Buffer.from("minutes of the board") },
    ];
    const docsDir = join(workspacePath, WORKSPACE_DOCUMENTS_DIR);
    mkdirSync(docsDir, { recursive: true });
    const mirrorDir = join(scratchRoot, "doc-mirror");
    mkdirSync(mirrorDir);
    const summaries: StagedInputSummary[] = files.map((file) => {
      writeFileSync(join(docsDir, file.name), file.bytes);
      return documentSummary(file.name, { sha256: sha256Hex(file.bytes), sizeBytes: file.bytes.length });
    });
    const mirror: ReadMirror = {
      dir: mirrorDir,
      documents: summaries.map((summary) => ({
        name: summary.name,
        sha256: summary.sha256,
        sizeBytes: summary.sizeBytes,
      })),
    };
    const statement = composeDocumentsStatement(workspacePath, summaries);
    const statedPaths = statedDocumentPathsOf(workspacePath, summaries);
    expect(statedPaths).toHaveLength(files.length);
    for (const statedPath of statedPaths) {
      expect(statement).toContain(statedPath);
      const decision = evaluateReadRequest({ file_path: statedPath }, workspacePath, mirror);
      if (!decision.allowed) {
        throw new Error(`the stated path ${statedPath} was refused by the Read gate: ${decision.reason}`);
      }
      const file = files.find((candidate) => statedPath.endsWith(`/${candidate.name}`));
      expect(file === undefined ? undefined : readFileSync(decision.resolvedPath)).toEqual(file?.bytes);
    }
  });
});
