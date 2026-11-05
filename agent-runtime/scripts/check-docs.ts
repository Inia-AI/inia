import { existsSync, readdirSync, readFileSync } from "node:fs";
import { dirname, join, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { closedModules, closedPaths, isClosedPath, openLines } from "../src/closed-modules.js";
import { messageOf } from "../src/errors.js";

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const DOCS_ROOT = join(REPO_ROOT, "docs");
const ROOT_ENTRY_POINTS = ["README.md", "CLAUDE.md", "sandbox/README.md"];
const OWN_REPO_PREFIX = "agent-runtime:";
const PAGE_TYPES = new Set(["hub", "reference", "guide", "runbook", "explanation", "research", "historical"]);
const PAGE_STATUSES = new Set(["current", "historical", "planned"]);
const VERIFIED_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const SOURCE_PATTERN = /^([\w.-]+):(.+)$/;
const ADR_ROOT = join(DOCS_ROOT, "adr");
const ADR_FILENAME_PATTERN = /^(\d{4})-[a-z0-9]+(?:-[a-z0-9]+)*\.md$/;
const MARKDOWN_LINK_PATTERN = /\[[^\]]*\]\(([^)\s]+)\)/g;
const EXTERNAL_LINK_PATTERN = /^(https?:|mailto:|#)/;
const AWS_ACCOUNT_ID_CONTEXT = /arn:|account|aws/i;
const AWS_ACCOUNT_ID_PATTERN = /\b\d{12}\b/;
const LONGER_DIGIT_RUN = /\d{13,}/;
const RFC1918_PATTERN = /\b(10\.\d{1,3}|192\.168|172\.(1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}\b/;
const LOCAL_USERS_PATH = /\/Users\/\w+/;
const RULES_ROOT = join(REPO_ROOT, ".claude", "rules");
const ALWAYS_LOADED_FILE = "CLAUDE.md";
const ALWAYS_LOADED_MAX_BYTES = 32_768;
const NAMES_FILE = join(".claude", "internal-names.txt");
const NAME_RULE_PATTERN = /^([^:]+):\s*\/(.+)\/([a-z]*)$/;
const YAML_COMMENT_PATTERN = /^\s*#/;

type NameRule = { kind: string; pattern: RegExp };

type Frontmatter = {
  fields: Map<string, string>;
  sources: string[];
};

const failures: string[] = [];

function* markdownFiles(dir: string): Generator<string> {
  if (!existsSync(dir)) {
    return;
  }
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      yield* markdownFiles(full);
    } else if (entry.name.endsWith(".md")) {
      yield full;
    }
  }
}

function parseFrontmatter(text: string): Frontmatter | null {
  if (!text.startsWith("---\n")) {
    return null;
  }
  const end = text.indexOf("\n---\n", 4);
  if (end === -1) {
    return null;
  }
  const fields = new Map<string, string>();
  const sources: string[] = [];
  let inSources = false;
  for (const line of text.slice(4, end).split("\n")) {
    if (YAML_COMMENT_PATTERN.test(line)) {
      continue;
    }
    const item = line.match(/^\s+-\s+(.+)$/);
    if (inSources && item?.[1] !== undefined) {
      sources.push(item[1].trim());
      continue;
    }
    inSources = false;
    const kv = line.match(/^(\w+):\s*(.*)$/);
    if (kv?.[1] !== undefined && kv[2] !== undefined) {
      if (kv[1] === "sources") {
        inSources = true;
      } else {
        fields.set(kv[1], kv[2].replace(/^["']|["']$/g, "").trim());
      }
    }
  }
  return { fields, sources };
}

function checkFrontmatter(rel: string, fm: Frontmatter | null): void {
  if (fm === null) {
    failures.push(`${rel}: no frontmatter`);
    return;
  }
  const type = fm.fields.get("type") ?? "";
  if (!PAGE_TYPES.has(type)) {
    failures.push(`${rel}: type "${type}" not in {${[...PAGE_TYPES].join(", ")}}`);
  }
  const status = fm.fields.get("status") ?? "";
  if (!PAGE_STATUSES.has(status)) {
    failures.push(`${rel}: status "${status}" not in {${[...PAGE_STATUSES].join(", ")}}`);
  }
  if ((fm.fields.get("description") ?? "") === "") {
    failures.push(`${rel}: missing description`);
  }
  const verified = fm.fields.get("verified");
  if (verified !== undefined && !VERIFIED_PATTERN.test(verified)) {
    failures.push(`${rel}: verified "${verified}" is not YYYY-MM-DD`);
  }
  for (const source of fm.sources) {
    const match = source.match(SOURCE_PATTERN);
    if (match?.[1] === undefined || match[2] === undefined) {
      failures.push(`${rel}: source "${source}" is not repo:path`);
      continue;
    }
    if (source.startsWith(OWN_REPO_PREFIX)) {
      const resolved = resolve(REPO_ROOT, match[2]);
      if (match[2].startsWith("/") || !resolved.startsWith(REPO_ROOT + sep) || !existsSync(resolved)) {
        failures.push(`${rel}: source path is not a tracked-repo-relative path: ${source}`);
      }
    }
  }
}

function checkLinks(rel: string, file: string, text: string): void {
  for (const match of text.matchAll(MARKDOWN_LINK_PATTERN)) {
    const target = match[1];
    if (target === undefined || EXTERNAL_LINK_PATTERN.test(target)) {
      continue;
    }
    const withoutAnchor = target.split("#")[0] ?? "";
    if (withoutAnchor === "") {
      continue;
    }
    if (withoutAnchor.startsWith("/")) {
      failures.push(`${rel}: absolute link (${target}) — use repo-relative links`);
      continue;
    }
    const resolved = resolve(dirname(file), withoutAnchor);
    if (!resolved.startsWith(REPO_ROOT + sep) || !existsSync(resolved)) {
      failures.push(`${rel}: dead link (${target})`);
    }
  }
}

function checkSensitiveContent(rel: string, text: string): void {
  const lines = text.split("\n");
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i] ?? "";
    const loc = `${rel}:${i + 1}`;
    if (
      AWS_ACCOUNT_ID_PATTERN.test(line) &&
      !LONGER_DIGIT_RUN.test(line) &&
      AWS_ACCOUNT_ID_CONTEXT.test(line)
    ) {
      failures.push(`${loc}: 12-digit number in an AWS context (account ID pattern)`);
    }
    if (RFC1918_PATTERN.test(line)) {
      failures.push(`${loc}: RFC1918 address`);
    }
    if (LOCAL_USERS_PATH.test(line)) {
      failures.push(`${loc}: local /Users/ path`);
    }
  }
}

function checkAdrDirectory(): void {
  if (!existsSync(ADR_ROOT)) {
    return;
  }
  const numbers: number[] = [];
  const names = new Set<string>();
  for (const entry of readdirSync(ADR_ROOT, { withFileTypes: true })) {
    const rel = join("docs", "adr", entry.name);
    if (entry.isDirectory()) {
      failures.push(`${rel}: docs/adr holds only NNNN-slug.md records, no subdirectories`);
      continue;
    }
    const match = entry.name.match(ADR_FILENAME_PATTERN);
    if (match?.[1] === undefined) {
      failures.push(`${rel}: filename is not NNNN-slug.md`);
      continue;
    }
    numbers.push(Number(match[1]));
    names.add(entry.name);
  }
  numbers.sort((a, b) => a - b);
  for (let i = 0; i < numbers.length; i++) {
    if (numbers[i] !== i + 1) {
      failures.push(
        `docs/adr: numbers must be unique and gapless from 0001 (found ${numbers[i]} at position ${i + 1})`,
      );
      break;
    }
  }
  for (const name of names) {
    const fm = parseFrontmatter(readFileSync(join(ADR_ROOT, name), "utf8"));
    const supersededBy = fm?.fields.get("superseded_by");
    if (supersededBy !== undefined && !names.has(supersededBy)) {
      failures.push(`docs/adr/${name}: superseded_by "${supersededBy}" names no ADR in docs/adr`);
    }
  }
}

function readNameRules(isPrivateTree: boolean): NameRule[] {
  const file = join(REPO_ROOT, NAMES_FILE);
  if (!existsSync(file)) {
    if (isPrivateTree) {
      failures.push(
        `${NAMES_FILE}: missing — a tree with closed modules checks its pages for internal names`,
      );
    }
    return [];
  }
  const rules: NameRule[] = [];
  readFileSync(file, "utf8")
    .split("\n")
    .forEach((line, index) => {
      const trimmed = line.trim();
      if (trimmed === "" || trimmed.startsWith("#")) {
        return;
      }
      const match = trimmed.match(NAME_RULE_PATTERN);
      if (match?.[1] === undefined || match[2] === undefined || match[3] === undefined) {
        failures.push(`${NAMES_FILE}:${index + 1}: not "<what it names>: /<pattern>/<flags>"`);
        return;
      }
      try {
        rules.push({ kind: match[1].trim(), pattern: new RegExp(match[2], match[3]) });
      } catch (error) {
        failures.push(`${NAMES_FILE}:${index + 1}: ${messageOf(error)}`);
      }
    });
  if (isPrivateTree && rules.length === 0) {
    failures.push(
      `${NAMES_FILE}: holds no rule — a tree with closed modules checks its pages for internal names`,
    );
  }
  return rules;
}

const modules = new Set(closedModules(REPO_ROOT));
const closed = closedPaths(REPO_ROOT);
const nameRules = readNameRules(modules.size > 0);

function checkInternalNames(rel: string, text: string): void {
  let lines: { number: number; text: string }[];
  try {
    lines = openLines(text, rel, modules);
  } catch (error) {
    failures.push(messageOf(error));
    return;
  }
  for (const { number, text: line } of lines) {
    for (const { kind, pattern } of nameRules) {
      const hit = line.match(pattern);
      if (hit !== null) {
        failures.push(
          `${rel}:${number}: ${kind} "${hit[0]}" — a page that ships in the public tree names none (writing rule 5 in docs/README.md)`,
        );
      }
    }
  }
}

function checkAlwaysLoadedSize(): void {
  const file = join(REPO_ROOT, ALWAYS_LOADED_FILE);
  if (existsSync(file)) {
    const bytes = readFileSync(file).byteLength;
    if (bytes > ALWAYS_LOADED_MAX_BYTES) {
      failures.push(
        `${ALWAYS_LOADED_FILE}: ${bytes} bytes, over the ${ALWAYS_LOADED_MAX_BYTES}-byte cap (Claude Code warns past 40,000 characters, Codex reads only the first 32 KiB) — move a section into a path-scoped .claude/rules file`,
      );
    }
  }
  if (!existsSync(RULES_ROOT)) {
    return;
  }
  for (const entry of readdirSync(RULES_ROOT, { withFileTypes: true })) {
    if (!(entry.isFile() && entry.name.endsWith(".md"))) {
      continue;
    }
    const fm = parseFrontmatter(readFileSync(join(RULES_ROOT, entry.name), "utf8"));
    if (fm?.fields.has("paths") !== true) {
      failures.push(
        `.claude/rules/${entry.name}: no paths: frontmatter, so it loads into every session like ${ALWAYS_LOADED_FILE} — scope it with paths:`,
      );
    }
  }
}

checkAlwaysLoadedSize();

let pageCount = 0;
for (const file of markdownFiles(DOCS_ROOT)) {
  pageCount++;
  const rel = file.slice(REPO_ROOT.length + 1);
  const text = readFileSync(file, "utf8");
  checkFrontmatter(rel, parseFrontmatter(text));
  checkLinks(rel, file, text);
  checkSensitiveContent(rel, text);
  if (!isClosedPath(closed, rel)) {
    checkInternalNames(rel, text);
  }
}

checkAdrDirectory();

let rootCount = 0;
for (const name of ROOT_ENTRY_POINTS) {
  const file = join(REPO_ROOT, name);
  if (!existsSync(file)) {
    continue;
  }
  rootCount++;
  const text = readFileSync(file, "utf8");
  checkLinks(name, file, text);
  checkSensitiveContent(name, text);
  if (!isClosedPath(closed, name)) {
    checkInternalNames(name, text);
  }
}

if (failures.length > 0) {
  console.error(`${failures.length} docs failure(s):`);
  for (const failure of failures) {
    console.error(`  ${failure}`);
  }
  process.exit(1);
}
console.log(`check-docs: ${pageCount} docs pages + ${rootCount} root entry points clean`);
