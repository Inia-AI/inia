import { describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { delimiter, join } from "node:path";
import { loadServerConfig } from "./config.js";
import { ConfigError } from "./config-error.js";
import {
  type ConfigProvider,
  envConfigProvider,
  resolveConfigValues,
  settingsFileProvider,
} from "./config-provider.js";

function fakeRemoteProvider(name: string, values: Record<string, string | undefined>): ConfigProvider {
  return { name, load: () => Promise.resolve(values) };
}

describe("envConfigProvider", () => {
  it("serves the environment it was given", async () => {
    const provider = envConfigProvider({ PORT: "9000", AUTH_TOKEN: "secret" });
    expect(provider.name).toBe("env");
    expect(await provider.load()).toEqual({ PORT: "9000", AUTH_TOKEN: "secret" });
  });
});

describe("resolveConfigValues", () => {
  it("lets an explicit env value beat the same key from a remote provider", async () => {
    const values = await resolveConfigValues([
      envConfigProvider({ AUTH_TOKEN: "from-env" }),
      fakeRemoteProvider("vault", { AUTH_TOKEN: "from-vault", PLATFORM_BASE_URL: "http://platform" }),
    ]);
    expect(values["AUTH_TOKEN"]).toBe("from-env");
    expect(values["PLATFORM_BASE_URL"]).toBe("http://platform");
  });

  it("resolves earlier-listed providers over later ones across two fakes", async () => {
    const values = await resolveConfigValues([
      fakeRemoteProvider("first", { SHARED: "first-wins", ONLY_FIRST: "a" }),
      fakeRemoteProvider("second", { SHARED: "second-loses", ONLY_SECOND: "b" }),
    ]);
    expect(values).toEqual({ SHARED: "first-wins", ONLY_FIRST: "a", ONLY_SECOND: "b" });
  });

  it("treats an undefined value as absent rather than masking lower-precedence providers", async () => {
    const values = await resolveConfigValues([
      envConfigProvider({ AUTH_TOKEN: undefined }),
      fakeRemoteProvider("vault", { AUTH_TOKEN: "from-vault" }),
    ]);
    expect(values["AUTH_TOKEN"]).toBe("from-vault");
  });

  it("keeps keys that collide with Object.prototype members instead of silently dropping them", async () => {
    const values = await resolveConfigValues([
      fakeRemoteProvider("vault", { toString: "x", constructor: "y", ["__proto__"]: "z" }),
    ]);
    expect(values["toString"]).toBe("x");
    expect(values["constructor"]).toBe("y");
    expect(Object.entries(values)).toContainEqual(["__proto__", "z"]);
  });

  it("fails closed when a provider rejects, naming the provider", async () => {
    const unreachable: ConfigProvider = {
      name: "vault",
      load: () => Promise.reject(new Error("connect timeout")),
    };
    const resolution = resolveConfigValues([envConfigProvider({}), unreachable]);
    await expect(resolution).rejects.toThrow(ConfigError);
    await expect(resolution).rejects.toThrow('config provider "vault" failed to load: connect timeout');
  });

  it("fails closed when a provider throws synchronously", async () => {
    const broken: ConfigProvider = {
      name: "ssm",
      load: () => {
        throw new Error("missing region");
      },
    };
    await expect(resolveConfigValues([broken])).rejects.toThrow(
      'config provider "ssm" failed to load: missing region',
    );
  });

  it("fails closed when a provider rejects with a non-Error value, still naming the provider", async () => {
    const throttled: ConfigProvider = {
      name: "ssm",
      load: () => Promise.reject("throttled"),
    };
    await expect(resolveConfigValues([throttled])).rejects.toThrow(ConfigError);
    await expect(resolveConfigValues([throttled])).rejects.toThrow(
      'config provider "ssm" failed to load: throttled',
    );
  });
});

describe("loadServerConfig through the provider seam", () => {
  it("behaves identically to reading the environment directly", async () => {
    const env = {
      CLAUDE_CODE_USE_BEDROCK: "1",
      ANTHROPIC_MODEL: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
      PORT: "9000",
      AUTH_TOKEN: "secret",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
      AWS_REGION: "us-west-2",
      AWS_SESSION_TOKEN: "",
      RANDOM_VAR: "x",
      EMPTY_VAR: undefined,
    };
    const resolved = await resolveConfigValues([envConfigProvider(env)]);
    expect(resolved["AWS_SESSION_TOKEN"]).toBe("");
    expect(Object.hasOwn(resolved, "EMPTY_VAR")).toBe(false);
    expect(loadServerConfig(resolved)).toEqual(loadServerConfig(env));
  });
});

function settingsFolder(files: Record<string, string>): string {
  const folder = mkdtempSync(join(tmpdir(), "settings-"));
  for (const [name, content] of Object.entries(files)) {
    writeFileSync(join(folder, name), content);
  }
  return folder;
}

describe("settingsFileProvider", () => {
  it("reads each file in the named folders as a setting, without one trailing newline", async () => {
    const first = settingsFolder({ AUTH_TOKEN: "token-from-file\n", EMPTY: "\n" });
    const second = settingsFolder({ ANTHROPIC_API_KEY: "key-from-file" });
    const provider = settingsFileProvider(`${first}${delimiter}${second}`);
    expect(await provider.load()).toEqual({
      AUTH_TOKEN: "token-from-file",
      EMPTY: "",
      ANTHROPIC_API_KEY: "key-from-file",
    });
  });

  it("skips a folder inside a named folder", async () => {
    const folder = settingsFolder({ AUTH_TOKEN: "token" });
    mkdirSync(join(folder, "nested"));
    expect(await settingsFileProvider(folder).load()).toEqual({ AUTH_TOKEN: "token" });
  });

  it("serves nothing when no folder is named", async () => {
    expect(await settingsFileProvider(undefined).load()).toEqual({});
    expect(await settingsFileProvider("").load()).toEqual({});
  });

  it("fails the start when a named folder does not exist, naming the folder", async () => {
    const missing = join(tmpdir(), "settings-that-do-not-exist");
    const resolution = resolveConfigValues([envConfigProvider({}), settingsFileProvider(missing)]);
    await expect(resolution).rejects.toThrow(ConfigError);
    await expect(resolution).rejects.toThrow(`SETTINGS_DIRECTORIES names '${missing}', which does not exist`);
  });

  it("loses to the environment for a key both hold", async () => {
    const folder = settingsFolder({ AUTH_TOKEN: "from-file", ANTHROPIC_API_KEY: "key-from-file" });
    const values = await resolveConfigValues([
      envConfigProvider({ AUTH_TOKEN: "from-env" }),
      settingsFileProvider(folder),
    ]);
    expect(values["AUTH_TOKEN"]).toBe("from-env");
    expect(values["ANTHROPIC_API_KEY"]).toBe("key-from-file");
  });
});
