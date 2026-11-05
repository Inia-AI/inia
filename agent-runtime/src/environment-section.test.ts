import { describe, expect, it } from "bun:test";
import { execFileSync, spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import {
  composeSystemPrompt,
  ENVIRONMENT_SECTION_CLOSE_TAG,
  ENVIRONMENT_SECTION_OPEN_TAG,
  parseToolList,
  RUNTIME_ENVIRONMENT_SECTION,
  TOOL_LIST,
  type ToolKind,
  WORKSPACE_SCRATCH_DIR,
} from "./environment-section.js";
import { READ_TOOL_NAME } from "./read-gate.js";
import { DOCUMENTS_MANIFEST_NAME } from "./run-documents.js";
import { RUN_TOOLS } from "./run-report.js";
import { qualifiedSkillName, SKILL_ASSETS_ENV_VAR, SKILL_TOOL_NAME } from "./skill-plugin.js";
import { RESERVED_WORKSPACE_DIRS, WORKSPACE_DOCUMENTS_DIR, WORKSPACE_OUT_DIR } from "./workspace-layout.js";

const PYPROJECT_URL = new URL("../toolbelt/python/pyproject.toml", import.meta.url);
const NODE_PACKAGE_URL = new URL("../toolbelt/node/package.json", import.meta.url);

const EM_OR_EN_DASH_PATTERN = /[–—]/;
const PYPROJECT_DEPENDENCIES_PATTERN = /^dependencies = \[\n([\s\S]*?)\n\]/m;
const PYPROJECT_REQUIREMENT_PATTERN = /^\s*"([A-Za-z0-9._-]+)/;

function pythonDistributionName(name: string): string {
  return name.toLowerCase().replace(/[._]+/g, "-");
}

function declaredPythonPackages(): string[] {
  const pyproject = readFileSync(PYPROJECT_URL, "utf8");
  const block = PYPROJECT_DEPENDENCIES_PATTERN.exec(pyproject)?.[1];
  if (block === undefined) {
    throw new Error("toolbelt/python/pyproject.toml has no dependencies array");
  }
  return block
    .split("\n")
    .map((line) => PYPROJECT_REQUIREMENT_PATTERN.exec(line)?.[1])
    .filter((name) => name !== undefined)
    .map(pythonDistributionName);
}

function declaredNodePackages(): string[] {
  const manifest: unknown = JSON.parse(readFileSync(NODE_PACKAGE_URL, "utf8"));
  if (typeof manifest !== "object" || manifest === null || !("dependencies" in manifest)) {
    throw new Error("toolbelt/node/package.json has no dependencies");
  }
  const dependencies = manifest.dependencies;
  if (typeof dependencies !== "object" || dependencies === null) {
    throw new Error("toolbelt/node/package.json dependencies is not an object");
  }
  return Object.keys(dependencies);
}

function listedNames(kind: ToolKind): string[] {
  return TOOL_LIST.filter((entry) => entry.kind === kind).map((entry) => entry.name);
}

describe("the runtime environment section", () => {
  it("is composed from the constants the runtime enforces", () => {
    expect(
      RUNTIME_ENVIRONMENT_SECTION.startsWith(
        `${ENVIRONMENT_SECTION_OPEN_TAG}\nAdded automatically by the system.\n`,
      ),
    ).toBe(true);
    expect(RESERVED_WORKSPACE_DIRS).not.toContain(WORKSPACE_SCRATCH_DIR);
    for (const fragment of [
      `${WORKSPACE_DOCUMENTS_DIR}/${DOCUMENTS_MANIFEST_NAME}`,
      `${WORKSPACE_OUT_DIR}/ is retained output`,
      `${WORKSPACE_SCRATCH_DIR}/`,
      `$${SKILL_ASSETS_ENV_VAR}/<skill>/<asset>`,
      qualifiedSkillName("<name>"),
      `the ${SKILL_TOOL_NAME} tool`,
      `${READ_TOOL_NAME} takes an absolute path`,
      ...RUN_TOOLS,
      "no network access",
    ]) {
      expect(RUNTIME_ENVIRONMENT_SECTION).toContain(fragment);
    }
  });

  it("carries no em or en dash", () => {
    expect(EM_OR_EN_DASH_PATTERN.test(RUNTIME_ENVIRONMENT_SECTION)).toBe(false);
  });

  it("ends with the hand-written tool list, one line per tool, then the closing tag", () => {
    const lines = TOOL_LIST.map((entry) => `- ${entry.name} (${entry.kind}): ${entry.purpose}`);
    expect(
      RUNTIME_ENVIRONMENT_SECTION.endsWith(`${lines.join("\n")}\n${ENVIRONMENT_SECTION_CLOSE_TAG}`),
    ).toBe(true);
  });

  it("uses no markdown heading, so an authored prompt's headings stay unambiguous", () => {
    expect(RUNTIME_ENVIRONMENT_SECTION.split("\n").filter((line) => line.startsWith("#"))).toEqual([]);
  });

  it("refuses a tool list line out of shape", () => {
    expect(() => parseToolList("- pandas: no kind")).toThrow("tool list line 1");
  });
});

describe("composeSystemPrompt", () => {
  it("is the whole system prompt when the caller sends none", () => {
    expect(composeSystemPrompt(undefined)).toBe(RUNTIME_ENVIRONMENT_SECTION);
  });

  it("sits above a caller's string prompt, which follows verbatim", () => {
    expect(composeSystemPrompt("You are a credit analyst.")).toBe(
      `${RUNTIME_ENVIRONMENT_SECTION}\n\nYou are a credit analyst.`,
    );
  });

  it("is its own leading block above a caller's block array, which keeps every block", () => {
    const blocks = ["static role and rules", "__SYSTEM_PROMPT_DYNAMIC_BOUNDARY__", "per-pass context"];
    expect(composeSystemPrompt(blocks)).toEqual([RUNTIME_ENVIRONMENT_SECTION, ...blocks]);
  });
});

describe("the tool list against the toolbelt", () => {
  it("names every Python package toolbelt/python/pyproject.toml declares", () => {
    const listed = listedNames("Python").map(pythonDistributionName);
    const declared = declaredPythonPackages();
    expect(declared.length).toBeGreaterThan(0);
    expect(declared.filter((name) => !listed.includes(name))).toEqual([]);
  });

  it("names every Node package toolbelt/node/package.json declares", () => {
    const listed = listedNames("Node");
    const declared = declaredNodePackages();
    expect(declared.length).toBeGreaterThan(0);
    expect(declared.filter((name) => !listed.includes(name))).toEqual([]);
  });

  it("names only Python packages installed in the toolbelt venv", () => {
    const script = [
      "import importlib.metadata, sys",
      "missing = []",
      "for name in sys.argv[1:]:",
      "    try:",
      "        importlib.metadata.version(name)",
      "    except importlib.metadata.PackageNotFoundError:",
      "        missing.append(name)",
      "print(','.join(missing))",
    ].join("\n");
    const output = execFileSync("python3", ["-c", script, ...listedNames("Python")], { encoding: "utf8" });
    expect(output.trim()).toBe("");
  });

  it("names only Node packages installed on NODE_PATH", () => {
    const nodePath = process.env["NODE_PATH"] ?? "";
    expect(nodePath).not.toBe("");
    const missing = listedNames("Node").filter((name) => !existsSync(join(nodePath, name, "package.json")));
    expect(missing).toEqual([]);
  });

  it("names only commands on PATH", () => {
    const missing = listedNames("CLI").filter(
      (name) => spawnSync("sh", ["-c", `command -v ${name}`]).status !== 0,
    );
    expect(missing).toEqual([]);
  });
});
