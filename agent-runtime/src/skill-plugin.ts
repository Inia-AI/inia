import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { PRIVATE_DIR_MODE, PRIVATE_FILE_MODE } from "./private-fs.js";
import { isSafePathSegment } from "./safe-path.js";

// With no plugin manifest, the SDK names the plugin after its directory basename — this same constant, used below as both.
export const SKILL_PLUGIN_NAME = "skills";
export const SKILL_TOOL_NAME = "Skill";
const PLUGIN_SKILLS_DIR = "skills";
const SKILL_MD_FILE_NAME = "SKILL.md";

export const MAX_SKILL_NAME_CHARS = 64;

const MARKDOWN_EXTENSION_PATTERN = /\.(?:md|markdown)$/i;
const NON_SKILL_NAME_CHARS_PATTERN = /[^a-z0-9]+/g;
const HYPHEN_EDGES_PATTERN = /^-+|-+$/g;
const NORMALIZED_SKILL_NAME_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

export function normalizeSkillName(rawName: string): string | undefined {
  const normalized = rawName
    .replace(MARKDOWN_EXTENSION_PATTERN, "")
    .toLowerCase()
    .replace(NON_SKILL_NAME_CHARS_PATTERN, "-")
    .replace(HYPHEN_EDGES_PATTERN, "")
    .slice(0, MAX_SKILL_NAME_CHARS)
    .replace(HYPHEN_EDGES_PATTERN, "");
  return NORMALIZED_SKILL_NAME_PATTERN.test(normalized) ? normalized : undefined;
}

export type SkillIdentityProblem =
  | { kind: "no-identity"; name: string; index: number }
  | { kind: "collision"; name: string; index: number; priorName: string; identity: string };

export function assertUniqueSkillIdentities<T extends { name: string }>(
  skillFiles: readonly T[],
  fail: (problem: SkillIdentityProblem) => never,
): { skillFile: T; identity: string }[] {
  const priorNames = new Map<string, string>();
  return skillFiles.map((skillFile, index) => {
    const identity = normalizeSkillName(skillFile.name);
    if (identity === undefined) {
      fail({ kind: "no-identity", name: skillFile.name, index });
    }
    const priorName = priorNames.get(identity);
    if (priorName !== undefined) {
      fail({ kind: "collision", name: skillFile.name, index, priorName, identity });
    }
    priorNames.set(identity, skillFile.name);
    return { skillFile, identity };
  });
}

export function qualifiedSkillName(name: string): string {
  return `${SKILL_PLUGIN_NAME}:${name}`;
}

// JSON string escaping is a strict subset of YAML double-quoted scalar escaping, so JSON.stringify is safe here.
function yamlQuoted(value: string): string {
  return JSON.stringify(value);
}

export function composeSkillMd(input: {
  name: string;
  description?: string;
  content: Uint8Array;
}): Uint8Array {
  const frontmatter = [
    "---",
    `name: ${yamlQuoted(input.name)}`,
    ...(input.description === undefined ? [] : [`description: ${yamlQuoted(input.description)}`]),
    "---",
    "",
    "",
  ].join("\n");
  return Buffer.concat([Buffer.from(frontmatter, "utf8"), Buffer.from(input.content)]);
}

export type StagedSkillAsset = {
  name: string;
  bytes: Uint8Array;
};

export type StagedSkillFile = {
  name: string;
  description?: string | undefined;
  bytes: Uint8Array;
  assets?: readonly StagedSkillAsset[] | undefined;
};

// The env var sandboxed Bash finds the staged assets under; the same module that
// stages the files owns the name the run addresses them by.
export const SKILL_ASSETS_ENV_VAR = "SKILL_ASSETS_DIR";

// Read + execute, never write: the ro-bind is the boundary, the mode is the belt. The
// exec bit helps only where the backing mount allows exec — the dev compose /workspaces
// tmpfs is noexec — so the supported invocation is through an interpreter
// (python3/bash <file>), which needs read alone.
const SKILL_ASSET_FILE_MODE = 0o500;

export type SkillPluginBinding = {
  path: string;
  skillNames: string[];
  assetsPath?: string;
};

function stageSkillAssets(
  assetsRoot: string | undefined,
  skillName: string,
  identity: string,
  assets: readonly StagedSkillAsset[],
): void {
  if (assetsRoot === undefined) {
    throw new Error(`skill "${skillName}" ships assets but no assets root was provided`);
  }
  const skillAssetsDir = join(assetsRoot, identity);
  mkdirSync(skillAssetsDir, { recursive: true, mode: PRIVATE_DIR_MODE });
  const seenNames = new Set<string>();
  for (const asset of assets) {
    if (!isSafePathSegment(asset.name)) {
      throw new Error(`skill "${skillName}" asset name "${asset.name}" is not a safe file name`);
    }
    if (seenNames.has(asset.name)) {
      throw new Error(`skill "${skillName}" repeats the asset name "${asset.name}"`);
    }
    seenNames.add(asset.name);
    writeFileSync(join(skillAssetsDir, asset.name), asset.bytes, { mode: SKILL_ASSET_FILE_MODE });
  }
}

export function stageSkillPlugin(
  privateHome: string,
  skillFiles: readonly StagedSkillFile[],
  assetsRoot?: string,
): SkillPluginBinding | undefined {
  if (skillFiles.length === 0) {
    return undefined;
  }
  const pluginRoot = join(privateHome, SKILL_PLUGIN_NAME);
  const skillNames: string[] = [];
  const staged = assertUniqueSkillIdentities(skillFiles, (problem) => {
    throw new Error(
      problem.kind === "no-identity"
        ? `skill file name "${problem.name}" keeps no skill identity after normalization`
        : `skill files "${problem.priorName}" and "${problem.name}" collide on the skill identity "${problem.identity}"`,
    );
  });
  let assetsStaged = false;
  for (const { skillFile, identity: name } of staged) {
    const skillDir = join(pluginRoot, PLUGIN_SKILLS_DIR, name);
    mkdirSync(skillDir, { recursive: true, mode: PRIVATE_DIR_MODE });
    writeFileSync(
      join(skillDir, SKILL_MD_FILE_NAME),
      composeSkillMd({
        name,
        ...(skillFile.description === undefined ? {} : { description: skillFile.description }),
        content: skillFile.bytes,
      }),
      { mode: PRIVATE_FILE_MODE },
    );
    const assets = skillFile.assets ?? [];
    if (assets.length > 0) {
      stageSkillAssets(assetsRoot, skillFile.name, name, assets);
      assetsStaged = true;
    }
    skillNames.push(qualifiedSkillName(name));
  }
  return {
    path: pluginRoot,
    skillNames,
    ...(assetsStaged && assetsRoot !== undefined ? { assetsPath: assetsRoot } : {}),
  };
}
