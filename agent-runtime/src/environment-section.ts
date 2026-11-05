import { readFileSync } from "node:fs";
import { READ_TOOL_NAME } from "./read-gate.js";
import { DOCUMENTS_MANIFEST_NAME } from "./run-documents.js";
import { BASH_TOOL_NAME } from "./run-tool-names.js";
import { SKILL_ASSETS_ENV_VAR, SKILL_PLUGIN_NAME, SKILL_TOOL_NAME } from "./skill-plugin.js";
import { WORKSPACE_DOCUMENTS_DIR, WORKSPACE_OUT_DIR } from "./workspace-layout.js";

export const WORKSPACE_SCRATCH_DIR = "work";

const TOOL_KINDS = ["CLI", "Python", "Node"] as const;
export type ToolKind = (typeof TOOL_KINDS)[number];
export type ToolListEntry = { name: string; kind: ToolKind; purpose: string };

const TOOL_LIST_URL = new URL("environment-tools.txt", import.meta.url);

const TOOL_KIND_ALTERNATION = TOOL_KINDS.join("|");
const TOOL_LINE_PATTERN = new RegExp(`^- (\\S+) \\((${TOOL_KIND_ALTERNATION})\\): (\\S.*)$`);

function isToolKind(value: string): value is ToolKind {
  return TOOL_KINDS.some((kind) => kind === value);
}

export function parseToolList(text: string): ToolListEntry[] {
  return text
    .split("\n")
    .filter((line) => line !== "")
    .map((line, index) => {
      const match = TOOL_LINE_PATTERN.exec(line);
      const [, name, kind, purpose] = match ?? [];
      if (name === undefined || kind === undefined || purpose === undefined || !isToolKind(kind)) {
        throw new Error(
          `tool list line ${index + 1} is not "- <name> (${TOOL_KIND_ALTERNATION}): <purpose>": ${line}`,
        );
      }
      return { name, kind, purpose };
    });
}

export const TOOL_LIST_TEXT = readFileSync(TOOL_LIST_URL, "utf8").trimEnd();

export const TOOL_LIST: readonly ToolListEntry[] = parseToolList(TOOL_LIST_TEXT);

const DOCS = `${WORKSPACE_DOCUMENTS_DIR}/`;
const OUT = `${WORKSPACE_OUT_DIR}/`;
const WORK = `${WORKSPACE_SCRATCH_DIR}/`;
const MANIFEST = `${DOCS}${DOCUMENTS_MANIFEST_NAME}`;
const ASSET_PATH = `$${SKILL_ASSETS_ENV_VAR}/<skill>/<asset>`;

export const ENVIRONMENT_SECTION_OPEN_TAG = "<environment>";
export const ENVIRONMENT_SECTION_CLOSE_TAG = "</environment>";

export const RUNTIME_ENVIRONMENT_SECTION = [
  ENVIRONMENT_SECTION_OPEN_TAG,
  "Added automatically by the system.",
  "",
  "- You work in a sandboxed Linux workspace with no network access. Commands cannot reach the internet or any other host, and nothing can be installed.",
  "- The working directory is this run's workspace. Run pwd to see its absolute path.",
  `- ${DOCS} holds the staged input documents. ${MANIFEST} lists each file with its declared document class (documentClass), media type (mediaType), sha256 and size in bytes (sizeBytes).`,
  `- ${READ_TOOL_NAME} takes an absolute path and serves the staged documents in ${DOCS} only. Use ${BASH_TOOL_NAME} for every other file.`,
  `- ${DOCS} is writable from ${BASH_TOOL_NAME}, but treat the input documents as read-only. ${READ_TOOL_NAME} serves each document only as it was staged and never shows a change made to it with ${BASH_TOOL_NAME}.`,
  `- ${OUT} is retained output. Every file in it is kept after the run and counts against the retained-output cap, so write only deliverables there.`,
  `- Put scratch files in ${WORK} (create it with mkdir -p ${WORKSPACE_SCRATCH_DIR}). Files there are not retained output. They are saved with the session so a paused run can resume, and that saved copy has a size limit, so delete large intermediate files when you no longer need them.`,
  `- /tmp is empty at the start of every ${BASH_TOOL_NAME} call and is not kept between calls.`,
  `- When the run has skills, invoke one with the ${SKILL_TOOL_NAME} tool and its full name, ${SKILL_PLUGIN_NAME}:<name>.`,
  `- A skill's files are at ${ASSET_PATH}. They are read-only. Run them through an interpreter, for example python3 "${ASSET_PATH}", not as ./<asset>.`,
  `- Reading an image, or a PDF page as an image, costs far more context than reading its text. Extract text first (pdftotext -layout, pdfplumber, markitdown). When the layout matters, ${READ_TOOL_NAME} the staged PDF in ${DOCS} with the pages parameter to see those pages. ${READ_TOOL_NAME} cannot open an image you render into ${WORK}.`,
  "",
  "Tools:",
  "Import the Python packages from python3. bun and node resolve the Node packages with require().",
  "",
  TOOL_LIST_TEXT,
  ENVIRONMENT_SECTION_CLOSE_TAG,
].join("\n");

const SECTION_SEPARATOR = "\n\n";

export type ComposableSystemPrompt = string | string[];

export function composeSystemPrompt(callerPrompt: string): string;
export function composeSystemPrompt(callerPrompt: ComposableSystemPrompt | undefined): ComposableSystemPrompt;
export function composeSystemPrompt(
  callerPrompt: ComposableSystemPrompt | undefined,
): ComposableSystemPrompt {
  if (callerPrompt === undefined) {
    return RUNTIME_ENVIRONMENT_SECTION;
  }
  if (typeof callerPrompt === "string") {
    return `${RUNTIME_ENVIRONMENT_SECTION}${SECTION_SEPARATOR}${callerPrompt}`;
  }
  return [RUNTIME_ENVIRONMENT_SECTION, ...callerPrompt];
}
