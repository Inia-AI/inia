import { readdir, readFile, stat } from "node:fs/promises";
import { delimiter, join } from "node:path";
import { ConfigError } from "./config-error.js";
import { messageOf } from "./errors.js";

export const SETTINGS_DIRECTORIES = "SETTINGS_DIRECTORIES";
const TRAILING_NEWLINE = /\r?\n$/;

export type ConfigValues = Record<string, string | undefined>;

export type ConfigProvider = {
  name: string;
  load: () => Promise<ConfigValues>;
};

export function envConfigProvider(env: ConfigValues): ConfigProvider {
  const bootSnapshot = { ...env };
  return { name: "env", load: () => Promise.resolve(bootSnapshot) };
}

export function settingsFileProvider(directories: string | undefined): ConfigProvider {
  return { name: "settings files", load: () => readSettingsDirectories(directories) };
}

async function readSettingsDirectories(directories: string | undefined): Promise<ConfigValues> {
  const values: ConfigValues = {};
  for (const directory of (directories ?? "").split(delimiter).filter((entry) => entry !== "")) {
    const isDirectory = await stat(directory).then(
      (entry) => entry.isDirectory(),
      () => false,
    );
    if (!isDirectory) {
      throw new Error(`${SETTINGS_DIRECTORIES} names '${directory}', which does not exist`);
    }
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      if (entry.isFile()) {
        const content = await readFile(join(directory, entry.name), "utf8");
        values[entry.name] = content.replace(TRAILING_NEWLINE, "");
      }
    }
  }
  return values;
}

async function loadFailClosed(provider: ConfigProvider): Promise<ConfigValues> {
  try {
    return await provider.load();
  } catch (error) {
    throw new ConfigError(`config provider "${provider.name}" failed to load: ${messageOf(error)}`);
  }
}

export async function resolveConfigValues(
  providersInPrecedenceOrder: readonly ConfigProvider[],
): Promise<Record<string, string>> {
  // Map accumulator, not an object literal — a key like "__proto__" or "toString" would hit
  // inherited members on a plain object and be silently dropped instead of merged.
  const loaded = await Promise.all(providersInPrecedenceOrder.map(loadFailClosed));
  const resolved = new Map<string, string>();
  for (const values of loaded) {
    for (const [key, value] of Object.entries(values)) {
      if (value !== undefined && !resolved.has(key)) {
        resolved.set(key, value);
      }
    }
  }
  return Object.fromEntries(resolved);
}
