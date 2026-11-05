import { describe, expect, it } from "bun:test";
import { parseAgentIdentity } from "./agent-identity.js";
import {
  CHILD_GRACE_MS,
  DEADLINE_ITERATOR_CLOSE_TIMEOUT_MS,
  DEFAULT_BODY_LIMIT_BYTES,
  DEFAULT_CHAT_BORROW_HEADROOM_SLOTS,
  DEFAULT_CHAT_MAX_SLOTS,
  DEFAULT_CHAT_RESERVED_SLOTS,
  DEFAULT_CHECKPOINT_ROOT,
  DEFAULT_DOCUMENT_ROOT,
  DEFAULT_MAX_BUDGET_USD,
  DEFAULT_MAX_TURNS,
  DEFAULT_PAUSE_GRACE_MINUTES,
  DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
  DEFAULT_RUN_DEADLINE_MS,
  FILES_LISTING_RESIDENT_BYTES,
  LISTING_KIND_FIELD_BYTES,
  loadServerConfig,
  MAX_ANSWER_CHARS,
  MAX_ARTIFACTS_MANIFEST_BYTES,
  MAX_CHECKPOINT_BUNDLE_BYTES,
  MAX_CHILD_FRAME_LENGTH,
  MAX_DOCUMENT_META_FIELD_CHARS,
  MAX_DOCUMENTS_PER_RUN,
  MAX_FILES_LISTING_BYTES,
  MAX_INFLIGHT_UPLOAD_BYTES,
  MAX_OUTPUT_FORMAT_CHARS,
  MAX_PROMPT_CHARS,
  MAX_PUBLISHED_OUT_BYTES,
  MAX_PUBLISHED_OUT_FILES,
  MAX_RECARRIED_OUT_FILES,
  MAX_RECARRIED_OUT_PATH_CHARS,
  MAX_RETAINED_OUTPUT_BYTES,
  MAX_SKILL_ASSETS_PER_SKILL,
  MAX_SKILL_DESCRIPTION_CHARS,
  MAX_SYSTEM_PROMPT_CHARS,
  MAX_TOTAL_DOCUMENT_BYTES,
  MAX_UTF8_BYTES_PER_JS_CHAR,
  MAX_WIRE_FRAME_LENGTH,
  parseAdmissionClassConfig,
  parseAllowedModels,
  parseAuth,
  parseBuildIdentity,
  parseCheckpointRoot,
  parseDocumentRoot,
  parseLogLevel,
  parseMaxBudgetUsd,
  parseMaxTurns,
  parsePauseGraceMinutes,
  parsePort,
  parseRunDeadlineMs,
  RETAINED_OUTPUT_WORST_CASE_BYTES,
  RUN_BUDGET_CEILING_USD,
  RUN_DEADLINE_CEILING_MS,
  RUN_INPUT_BODY_LIMIT_BYTES,
  RUN_TURNS_CEILING,
  TRACE_PACK_ALLOWANCE_BYTES,
  UID_POOL_BASE,
  UID_POOL_SIZE,
  UNDECLARED_BODY_PROVISIONAL_BYTES,
  UNKNOWN_BUILD_FIELD,
} from "./config.js";
import { ConfigError } from "./config-error.js";
import { measureHtmlInertJson } from "./html-inert-json.js";
import { assertBedrockPinned, buildSubprocessEnv } from "./model-provider.js";

const PINNED_ENV = {
  CLAUDE_CODE_USE_BEDROCK: "1",
  ANTHROPIC_MODEL: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
};

describe("parseBuildIdentity", () => {
  it("reports unknown when the build stamps are absent or blank", () => {
    expect(parseBuildIdentity(undefined, undefined)).toEqual({
      version: UNKNOWN_BUILD_FIELD,
      revision: UNKNOWN_BUILD_FIELD,
    });
    expect(parseBuildIdentity("", "   ")).toEqual({
      version: UNKNOWN_BUILD_FIELD,
      revision: UNKNOWN_BUILD_FIELD,
    });
  });

  it("carries the stamped version and revision through, trimmed", () => {
    expect(parseBuildIdentity(" 0.2.4 ", "1e096ffcafe0f00dbaddeed5a1e0b0b0c0ffee11")).toEqual({
      version: "0.2.4",
      revision: "1e096ffcafe0f00dbaddeed5a1e0b0b0c0ffee11",
    });
  });

  it("refuses a stamp that is not a plain build token, rather than serving it", () => {
    for (const bad of ["0.2.4 stale", "<script>", "0.2.4\nstale", '0.2.4"', "x".repeat(65)]) {
      expect(() => parseBuildIdentity(bad, undefined)).toThrow(ConfigError);
      expect(() => parseBuildIdentity(undefined, bad)).toThrow(ConfigError);
    }
  });

  it("names the variable, the constraint that failed, and the value it got", () => {
    expect(() => parseBuildIdentity("0.2.4 stale", undefined)).toThrow(
      /AGENT_RUNTIME_VERSION.*characters outside.*"0\.2\.4 stale"/,
    );
    expect(() => parseBuildIdentity(undefined, "x".repeat(65))).toThrow(
      /AGENT_RUNTIME_REVISION.*at most 64 characters.*got 65/,
    );
  });

  it("escapes and truncates the rejected value so a control character cannot forge a log line", () => {
    const forged = parseBuildFieldMessageFor("0.2.4\nFATAL: forged");
    expect(forged).not.toContain("\n");
    expect(forged).toContain("\\n");
    const long = parseBuildFieldMessageFor("y".repeat(200));
    expect(long).toContain("…");
    expect(long.length).toBeLessThan(140);
  });
});

function parseBuildFieldMessageFor(value: string): string {
  try {
    parseBuildIdentity(value, undefined);
    return "";
  } catch (caught) {
    return caught instanceof Error ? caught.message : "";
  }
}

describe("parsePort", () => {
  it("defaults when unset or empty", () => {
    expect(parsePort(undefined)).toBe(8130);
    expect(parsePort("")).toBe(8130);
  });

  it("parses a valid port", () => {
    expect(parsePort("9000")).toBe(9000);
  });

  it("rejects non-numeric, fractional, and out-of-range values", () => {
    for (const bad of ["garbage", "80.5", "0", "65536", "-1"]) {
      expect(() => parsePort(bad)).toThrow(ConfigError);
    }
  });
});

describe("parseLogLevel", () => {
  it("defaults to info when unset or empty", () => {
    expect(parseLogLevel(undefined)).toBe("info");
    expect(parseLogLevel("")).toBe("info");
  });

  it("accepts each named level", () => {
    expect(parseLogLevel("debug")).toBe("debug");
    expect(parseLogLevel("info")).toBe("info");
    expect(parseLogLevel("warn")).toBe("warn");
    expect(parseLogLevel("error")).toBe("error");
  });

  it("refuses an unknown level fail-closed instead of guessing", () => {
    for (const bad of ["verbose", "DEBUG", "trace", "0"]) {
      expect(() => parseLogLevel(bad)).toThrow(ConfigError);
    }
  });
});

describe("parseAuth", () => {
  it("returns token mode when only AUTH_TOKEN is set", () => {
    expect(parseAuth("secret", undefined)).toEqual({ token: "secret" });
  });

  it("returns disabled mode only on an explicit opt-out", () => {
    expect(parseAuth(undefined, "true")).toEqual({ disabled: true });
  });

  it("fails closed when neither is set", () => {
    expect(() => parseAuth(undefined, undefined)).toThrow(ConfigError);
    expect(() => parseAuth("", undefined)).toThrow(ConfigError);
    expect(() => parseAuth(undefined, "false")).toThrow(ConfigError);
  });

  it("rejects setting both a token and the opt-out", () => {
    expect(() => parseAuth("secret", "true")).toThrow(ConfigError);
  });
});

describe("assertBedrockPinned", () => {
  it("accepts a pinned bedrock environment", () => {
    expect(() => assertBedrockPinned(PINNED_ENV)).not.toThrow();
  });

  it("rejects a missing bedrock flag or model pin", () => {
    expect(() => assertBedrockPinned({ ANTHROPIC_MODEL: "m" })).toThrow(ConfigError);
    expect(() => assertBedrockPinned({ CLAUDE_CODE_USE_BEDROCK: "1" })).toThrow(ConfigError);
  });
});

describe("parseAllowedModels", () => {
  const PINNED = "us.anthropic.claude-sonnet-4-6";

  it("defaults to exactly the pinned model when unset or empty — zero config keeps today's behavior", () => {
    expect(parseAllowedModels(undefined, PINNED)).toEqual([PINNED]);
    expect(parseAllowedModels("", PINNED)).toEqual([PINNED]);
  });

  it("splits on commas, trimming whitespace and dropping empty entries", () => {
    expect(parseAllowedModels(` other-model , ${PINNED} ,third,`, PINNED)).toEqual([
      "other-model",
      PINNED,
      "third",
    ]);
  });

  it("refuses to boot on an allowlist that does not contain the pinned default model", () => {
    expect(() => parseAllowedModels("other-model", PINNED)).toThrow(ConfigError);
    expect(() => parseAllowedModels("other-model", PINNED)).toThrow(/ANTHROPIC_MODEL/);
  });

  it("refuses to boot on an allowlist that is empty after trimming", () => {
    expect(() => parseAllowedModels(" , ,", PINNED)).toThrow(ConfigError);
  });
});

describe("run fuses", () => {
  // Measured on the first real report extraction: 50 turns for $1.05. Deliberately
  // the cheapest turn ever observed, so the ordering guard below stays the strictest
  // reading of the arithmetic (ADR 0048).
  const OBSERVED_USD_PER_TURN = 0.021;

  it("keeps the turn fuse above where the budget fuse bites, so budget is the binding ceiling", () => {
    expect(RUN_TURNS_CEILING * OBSERVED_USD_PER_TURN).toBeGreaterThan(RUN_BUDGET_CEILING_USD);
  });

  it("pins the ceilings a request may declare at twice the workload they were re-derived from", () => {
    expect(RUN_BUDGET_CEILING_USD).toBe(5);
    expect(RUN_DEADLINE_CEILING_MS).toBe(30 * 60 * 1000);
  });

  it("holds each default below its ceiling, so a run declaring nothing is not sized for the outliers", () => {
    expect(DEFAULT_MAX_BUDGET_USD).toBe(2.5);
    expect(DEFAULT_RUN_DEADLINE_MS).toBe(15 * 60 * 1000);
    expect(DEFAULT_MAX_BUDGET_USD).toBeLessThan(RUN_BUDGET_CEILING_USD);
    expect(DEFAULT_RUN_DEADLINE_MS).toBeLessThan(RUN_DEADLINE_CEILING_MS);
  });
});

describe("buildSubprocessEnv", () => {
  it("passes only the allowlisted variables through to the SDK subprocess", () => {
    const env = buildSubprocessEnv({
      PATH: "/usr/bin",
      HOME: "/home/bun",
      CLAUDE_CODE_USE_BEDROCK: "1",
      ANTHROPIC_MODEL: "model",
      AWS_REGION: "us-west-2",
      AUTH_TOKEN: "secret",
      PLATFORM_BASE_URL: "http://platform",
      RANDOM_VAR: "x",
    });
    expect(env["PATH"]).toBe("/usr/bin");
    expect(env["CLAUDE_CODE_USE_BEDROCK"]).toBe("1");
    expect(env["ANTHROPIC_MODEL"]).toBe("model");
    expect(env["AWS_REGION"]).toBe("us-west-2");
    expect(env["AUTH_TOKEN"]).toBeUndefined();
    expect(env["PLATFORM_BASE_URL"]).toBeUndefined();
    expect(env["RANDOM_VAR"]).toBeUndefined();
  });

  it("passes ENABLE_PROMPT_CACHING_1H through for worker runs", () => {
    const env = buildSubprocessEnv({ ENABLE_PROMPT_CACHING_1H: "1" });
    expect(env["ENABLE_PROMPT_CACHING_1H"]).toBe("1");
  });

  it("passes NODE_PATH through, or the toolbelt's node packages do not resolve in a run", () => {
    const env = buildSubprocessEnv({ NODE_PATH: "/opt/node/node_modules" });
    expect(env["NODE_PATH"]).toBe("/opt/node/node_modules");
  });

  it("passes the metadata endpoint override through, so dev runs can reach the IMDS mock", () => {
    const env = buildSubprocessEnv({ AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338" });
    expect(env["AWS_EC2_METADATA_SERVICE_ENDPOINT"]).toBe("http://imds-mock:1338");
  });
});

describe("the Bedrock child env, pinned byte for byte", () => {
  // Every allowlisted name present, beside the secrets and the other providers' variables a seam
  // could be tempted to widen toward. The expected object is spelled out literally rather than
  // derived: a key reaching the SDK subprocess in the default mode that is not in this list is a
  // Bedrock-invariance break, whatever its intent.
  const HOST_ENV = {
    PATH: "/opt/venv/bin:/usr/bin",
    HOME: "/home/bun",
    NODE_PATH: "/opt/node/node_modules",
    CLAUDE_CONFIG_DIR: "/home/bun/.claude",
    CLAUDE_CODE_USE_BEDROCK: "1",
    CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
    ANTHROPIC_MODEL: "us.anthropic.claude-sonnet-4-6",
    ANTHROPIC_DEFAULT_HAIKU_MODEL: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
    ENABLE_PROMPT_CACHING_1H: "1",
    AWS_REGION: "us-west-2",
    AWS_DEFAULT_REGION: "us-west-2",
    AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338",
    AWS_ACCESS_KEY_ID: "AKIA",
    AWS_SECRET_ACCESS_KEY: "sk",
    AWS_SESSION_TOKEN: "st",
    AWS_CONTAINER_CREDENTIALS_RELATIVE_URI: "/creds",
    AWS_CONTAINER_CREDENTIALS_FULL_URI: "http://169.254.170.2/creds",
    AUTH_TOKEN: "secret",
    PLATFORM_BASE_URL: "http://platform",
    AGENT_RUNTIME_ROLE_ARN: "arn:aws:iam::1:role/agent",
    AGENT_RUNTIME_VERSION: "0.3.1",
    LOG_LEVEL: "debug",
    ANTHROPIC_ALLOWED_MODELS: "us.anthropic.claude-sonnet-4-6",
  };
  const EXPECTED_BEDROCK_SUBPROCESS_ENV = {
    PATH: "/opt/venv/bin:/usr/bin",
    HOME: "/home/bun",
    NODE_PATH: "/opt/node/node_modules",
    CLAUDE_CONFIG_DIR: "/home/bun/.claude",
    CLAUDE_CODE_USE_BEDROCK: "1",
    CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
    ANTHROPIC_MODEL: "us.anthropic.claude-sonnet-4-6",
    ANTHROPIC_DEFAULT_HAIKU_MODEL: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
    ENABLE_PROMPT_CACHING_1H: "1",
    AWS_REGION: "us-west-2",
    AWS_DEFAULT_REGION: "us-west-2",
    AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338",
    AWS_ACCESS_KEY_ID: "AKIA",
    AWS_SECRET_ACCESS_KEY: "sk",
    AWS_SESSION_TOKEN: "st",
    AWS_CONTAINER_CREDENTIALS_RELATIVE_URI: "/creds",
    AWS_CONTAINER_CREDENTIALS_FULL_URI: "http://169.254.170.2/creds",
  };

  it("buildSubprocessEnv emits exactly the historical allowlist and nothing a provider seam adds", () => {
    expect(
      buildSubprocessEnv({
        ...HOST_ENV,
        CLAUDE_CODE_USE_FOUNDRY: "1",
        ANTHROPIC_FOUNDRY_RESOURCE: "res",
        ANTHROPIC_FOUNDRY_BASE_URL: "https://res.services.ai.azure.com/anthropic",
        ANTHROPIC_FOUNDRY_API_KEY: "k",
        ANTHROPIC_FOUNDRY_AUTH_TOKEN: "t",
        ANTHROPIC_BASE_URL: "http://127.0.0.1:1",
        ANTHROPIC_API_KEY: "k",
        ANTHROPIC_AUTH_TOKEN: "t",
        ANTHROPIC_CUSTOM_HEADERS: "x: y",
        CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
        AZURE_CLIENT_ID: "id",
        AGENT_RUNTIME_MODEL_PROVIDER: "bedrock",
      }),
    ).toEqual(EXPECTED_BEDROCK_SUBPROCESS_ENV);
  });

  it("loadServerConfig in the default mode hands the spawner that env plus the output-token ceiling and the tool-search switch, in that key order", () => {
    const config = loadServerConfig(HOST_ENV);
    const expected = {
      ...EXPECTED_BEDROCK_SUBPROCESS_ENV,
      CLAUDE_CODE_MAX_OUTPUT_TOKENS: "64000",
      ENABLE_TOOL_SEARCH: "false",
    };
    expect(config.subprocessEnv).toEqual(expected);
    expect(Object.keys(config.subprocessEnv)).toEqual(Object.keys(expected));
    expect(config.pinnedModel).toBe("us.anthropic.claude-sonnet-4-6");
    expect(config.allowedModels).toEqual(["us.anthropic.claude-sonnet-4-6"]);
  });
});

describe("loadServerConfig", () => {
  it("builds a full config from a valid environment", () => {
    const config = loadServerConfig({
      ...PINNED_ENV,
      PORT: "8130",
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
    });
    expect(config.port).toBe(8130);
    expect(config.auth).toEqual({ disabled: true });
    expect(config.bodyLimitBytes).toBeGreaterThan(0);
    expect(config.requestPhaseTimeoutMs).toBeGreaterThan(0);
    expect(config.fuses.deadlineMs.defaultValue).toBeGreaterThan(0);
    expect(config.fuses.maxBudgetUsd.defaultValue).toBeGreaterThan(0);
    expect(config.subprocessEnv["CLAUDE_CODE_USE_BEDROCK"]).toBe("1");
    expect(config.subprocessEnv["AUTH_TOKEN"]).toBeUndefined();
    expect(config.pauseGraceMinutes).toBe(DEFAULT_PAUSE_GRACE_MINUTES);
    expect(config.admissionClasses).toEqual({
      chatReservedSlots: DEFAULT_CHAT_RESERVED_SLOTS,
      chatMaxSlots: DEFAULT_CHAT_MAX_SLOTS,
      chatBorrowHeadroomSlots: DEFAULT_CHAT_BORROW_HEADROOM_SLOTS,
    });
    expect(config.checkpointRoot).toBe(DEFAULT_CHECKPOINT_ROOT);
    expect(config.allowedModels).toEqual([PINNED_ENV.ANTHROPIC_MODEL]);
    expect(config.logLevel).toBe("info");
  });

  it("threads a configured LOG_LEVEL through", () => {
    const config = loadServerConfig({
      ...PINNED_ENV,
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
      LOG_LEVEL: "debug",
    });
    expect(config.logLevel).toBe("debug");
  });

  it("threads the image's baked build stamps through, defaulting to unknown for unstamped builds", () => {
    const stamped = loadServerConfig({
      ...PINNED_ENV,
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
      AGENT_RUNTIME_VERSION: "0.2.4",
      AGENT_RUNTIME_REVISION: "45d7d7e",
    });
    expect(stamped.buildIdentity).toEqual({ version: "0.2.4", revision: "45d7d7e" });
    const unstamped = loadServerConfig({
      ...PINNED_ENV,
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
    });
    expect(unstamped.buildIdentity).toEqual({
      version: UNKNOWN_BUILD_FIELD,
      revision: UNKNOWN_BUILD_FIELD,
    });
  });

  it("threads the model allowlist through and refuses one missing the pinned model", () => {
    const config = loadServerConfig({
      ...PINNED_ENV,
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
      ANTHROPIC_ALLOWED_MODELS: `${PINNED_ENV.ANTHROPIC_MODEL},us.anthropic.claude-sonnet-4-6`,
    });
    expect(config.allowedModels).toEqual([PINNED_ENV.ANTHROPIC_MODEL, "us.anthropic.claude-sonnet-4-6"]);
    expect(() =>
      loadServerConfig({
        ...PINNED_ENV,
        AUTH_DISABLED: "true",
        AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
        ANTHROPIC_ALLOWED_MODELS: "us.anthropic.claude-sonnet-4-6",
      }),
    ).toThrow(ConfigError);
  });

  it("threads the pause grace window and checkpoint root through from the environment", () => {
    const config = loadServerConfig({
      ...PINNED_ENV,
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
      AGENT_RUNTIME_PAUSE_GRACE_MINUTES: "0",
      AGENT_RUNTIME_CHECKPOINT_ROOT: "/workspaces/alt-checkpoints",
    });
    expect(config.pauseGraceMinutes).toBe(0);
    expect(config.checkpointRoot).toBe("/workspaces/alt-checkpoints");
  });

  it("refuses to start when the agent identity is neither configured nor waived", () => {
    expect(() => loadServerConfig({ ...PINNED_ENV, PORT: "8130", AUTH_DISABLED: "true" })).toThrow(
      ConfigError,
    );
  });

  it("refuses a document root outside every sandbox-denied read root — the Read fix rests on it", () => {
    expect(() =>
      loadServerConfig({
        ...PINNED_ENV,
        AUTH_DISABLED: "true",
        AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
        AGENT_RUNTIME_DOCUMENT_ROOT: "/var/lib/agent-documents",
      }),
    ).toThrow(ConfigError);
    const contained = loadServerConfig({
      ...PINNED_ENV,
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
      AGENT_RUNTIME_DOCUMENT_ROOT: "/workspaces/alt-documents",
    });
    expect(contained.documentRoot).toBe("/workspaces/alt-documents");
  });

  it("refuses a path that only shares a denied root's spelling as a prefix", () => {
    expect(() =>
      loadServerConfig({
        ...PINNED_ENV,
        AUTH_DISABLED: "true",
        AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
        AGENT_RUNTIME_DOCUMENT_ROOT: "/workspaces-adjacent/documents",
      }),
    ).toThrow(ConfigError);
  });
});

describe("loadServerConfig per provider mode", () => {
  const FOUNDRY_ENV = {
    AUTH_DISABLED: "true",
    AGENT_RUNTIME_MODEL_PROVIDER: "foundry",
    CLAUDE_CODE_USE_FOUNDRY: "1",
    ANTHROPIC_FOUNDRY_RESOURCE: "inia-foundry",
    ANTHROPIC_MODEL: "claude-sonnet-5",
    ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
    PATH: "/usr/bin",
    HOME: "/home/bun",
  };

  it("foundry: carries the provider record, the Foundry child env, and no identity tri-state", () => {
    const config = loadServerConfig(FOUNDRY_ENV);
    expect(config.modelProvider).toEqual({ provider: "foundry" });
    expect(config.subprocessEnv).toEqual({
      PATH: "/usr/bin",
      HOME: "/home/bun",
      CLAUDE_CODE_USE_FOUNDRY: "1",
      ANTHROPIC_MODEL: "claude-sonnet-5",
      ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
      ANTHROPIC_FOUNDRY_RESOURCE: "inia-foundry",
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
      CLAUDE_CODE_MAX_OUTPUT_TOKENS: "64000",
      ENABLE_TOOL_SEARCH: "false",
    });
    expect(config.pinnedModel).toBe("claude-sonnet-5");
    expect(config.allowedModels).toEqual(["claude-sonnet-5"]);
  });

  it("foundry: the model allowlist must be deployment names too", () => {
    expect(
      loadServerConfig({ ...FOUNDRY_ENV, ANTHROPIC_ALLOWED_MODELS: "claude-sonnet-5,claude-opus-4-8" })
        .allowedModels,
    ).toEqual(["claude-sonnet-5", "claude-opus-4-8"]);
    expect(() =>
      loadServerConfig({
        ...FOUNDRY_ENV,
        ANTHROPIC_ALLOWED_MODELS: "claude-sonnet-5,us.anthropic.claude-opus-4-8",
      }),
    ).toThrow(/ANTHROPIC_ALLOWED_MODELS/);
  });

  const ANTHROPIC_ENV = {
    AUTH_DISABLED: "true",
    AGENT_RUNTIME_MODEL_PROVIDER: "anthropic",
    ANTHROPIC_API_KEY: "sk-ant-api03-real-key",
    ANTHROPIC_MODEL: "claude-sonnet-5-5",
    PATH: "/usr/bin",
    HOME: "/home/bun",
  };

  it("anthropic: hands the key to the key holder alone and points the child env at it", () => {
    const config = loadServerConfig(ANTHROPIC_ENV);
    expect(config.modelProvider).toEqual({
      provider: "anthropic",
      proxy: { apiKey: "sk-ant-api03-real-key" },
    });
    expect(config.subprocessEnv).toEqual({
      PATH: "/usr/bin",
      HOME: "/home/bun",
      ANTHROPIC_MODEL: "claude-sonnet-5-5",
      ANTHROPIC_BASE_URL: "http://127.0.0.1:8131",
      ANTHROPIC_API_KEY: "agent-runtime-anthropic-proxy",
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
      CLAUDE_CODE_MAX_OUTPUT_TOKENS: "64000",
      ENABLE_TOOL_SEARCH: "false",
    });
    expect(config.pinnedModel).toBe("claude-sonnet-5-5");
    expect(config.allowedModels).toEqual(["claude-sonnet-5-5"]);
  });

  it("anthropic: the model allowlist must be Anthropic API ids too — a Bedrock id or ARN refuses boot", () => {
    expect(
      loadServerConfig({ ...ANTHROPIC_ENV, ANTHROPIC_ALLOWED_MODELS: "claude-sonnet-5-5,claude-haiku-4-5" })
        .allowedModels,
    ).toEqual(["claude-sonnet-5-5", "claude-haiku-4-5"]);
    for (const foreign of [
      "us.anthropic.claude-opus-4-8",
      "arn:aws:bedrock:us-west-2:123456789012:inference-profile/us.anthropic.claude-opus-4-8",
    ]) {
      expect(() =>
        loadServerConfig({ ...ANTHROPIC_ENV, ANTHROPIC_ALLOWED_MODELS: `claude-sonnet-5-5,${foreign}` }),
      ).toThrow(/ANTHROPIC_ALLOWED_MODELS/);
    }
  });

  it("anthropic: a Bedrock switch beside the mode refuses boot, naming it", () => {
    expect(() => loadServerConfig({ ...ANTHROPIC_ENV, CLAUDE_CODE_USE_BEDROCK: "1" })).toThrow(
      /CLAUDE_CODE_USE_BEDROCK/,
    );
  });

  it("neither non-Bedrock mode needs, or accepts, the AWS identity tri-state", () => {
    expect(() => loadServerConfig({ ...FOUNDRY_ENV, AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true" })).toThrow(
      /AGENT_RUNTIME_AMBIENT_CREDENTIALS/,
    );
  });
});

describe("loadServerConfig metadata mode", () => {
  it("refuses metadata mode without a usable metadata endpoint, which would silently target real IMDS", () => {
    for (const endpoint of [undefined, "", "not-a-url", "ftp://imds-mock:1338"]) {
      expect(() =>
        loadServerConfig({
          ...PINNED_ENV,
          AUTH_DISABLED: "true",
          AGENT_RUNTIME_METADATA_CREDENTIALS: "true",
          AWS_EC2_METADATA_SERVICE_ENDPOINT: endpoint,
        }),
      ).toThrow(ConfigError);
    }
  });

  it("boots metadata mode when the endpoint names an http URL", () => {
    const config = loadServerConfig({
      ...PINNED_ENV,
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_METADATA_CREDENTIALS: "true",
      AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338",
    });
    expect(config.modelProvider).toEqual({ provider: "bedrock", identity: { metadataCredentials: true } });
    expect(config.subprocessEnv["AWS_EC2_METADATA_SERVICE_ENDPOINT"]).toBe("http://imds-mock:1338");
  });
});

describe("parseAgentIdentity", () => {
  const ROLE = "arn:aws:iam::1:role/agent";

  it("returns the role when one is configured", () => {
    expect(parseAgentIdentity({ roleArn: ROLE, ambientFlag: undefined, metadataFlag: undefined })).toEqual({
      roleArn: ROLE,
    });
  });

  it("returns ambient mode only on an explicit opt-out", () => {
    expect(parseAgentIdentity({ roleArn: undefined, ambientFlag: "true", metadataFlag: undefined })).toEqual({
      ambient: true,
    });
  });

  it("returns metadata mode only on an explicit opt-in", () => {
    expect(
      parseAgentIdentity({
        roleArn: undefined,
        ambientFlag: undefined,
        metadataFlag: "true",
        metadataEndpoint: "http://imds-mock:1338",
      }),
    ).toEqual({ metadataCredentials: true });
  });

  it("fails closed when none is set, so a lost variable cannot silently widen the identity", () => {
    expect(() =>
      parseAgentIdentity({ roleArn: undefined, ambientFlag: undefined, metadataFlag: undefined }),
    ).toThrow(ConfigError);
    expect(() =>
      parseAgentIdentity({ roleArn: "", ambientFlag: undefined, metadataFlag: undefined }),
    ).toThrow(ConfigError);
  });

  it("rejects setting more than one, which would hide which one is in force", () => {
    expect(() => parseAgentIdentity({ roleArn: ROLE, ambientFlag: "true", metadataFlag: undefined })).toThrow(
      ConfigError,
    );
    expect(() => parseAgentIdentity({ roleArn: ROLE, ambientFlag: undefined, metadataFlag: "true" })).toThrow(
      ConfigError,
    );
    expect(() =>
      parseAgentIdentity({ roleArn: undefined, ambientFlag: "true", metadataFlag: "true" }),
    ).toThrow(ConfigError);
  });
});

describe("parseAdmissionClassConfig", () => {
  it("reserves nothing for chat, caps it at eight and keeps four slots of headroom when unset or empty", () => {
    const expected = { chatReservedSlots: 0, chatMaxSlots: 8, chatBorrowHeadroomSlots: 4 };
    expect(parseAdmissionClassConfig(undefined, undefined, undefined)).toEqual(expected);
    expect(parseAdmissionClassConfig("", "", "")).toEqual(expected);
  });

  it("accepts the recommended production values", () => {
    expect(parseAdmissionClassConfig("4", "8", "4")).toEqual({
      chatReservedSlots: 4,
      chatMaxSlots: 8,
      chatBorrowHeadroomSlots: 4,
    });
  });

  it("accepts a zero cap, which switches chat admission off", () => {
    expect(parseAdmissionClassConfig("0", "0", "0")).toEqual({
      chatReservedSlots: 0,
      chatMaxSlots: 0,
      chatBorrowHeadroomSlots: 0,
    });
  });

  it("refuses to start on slot counts that are not whole numbers within the pool", () => {
    for (const bad of ["-1", "1.5", "abc", "NaN", String(UID_POOL_SIZE + 1)]) {
      expect(() => parseAdmissionClassConfig(bad, undefined, undefined)).toThrow(ConfigError);
      expect(() => parseAdmissionClassConfig(undefined, bad, undefined)).toThrow(ConfigError);
      expect(() => parseAdmissionClassConfig(undefined, undefined, bad)).toThrow(ConfigError);
    }
  });

  it("refuses a reserve that leaves workflow runs no slot", () => {
    expect(() => parseAdmissionClassConfig(String(UID_POOL_SIZE), String(UID_POOL_SIZE), undefined)).toThrow(
      ConfigError,
    );
  });

  it("refuses a headroom that would leave chat unable ever to borrow", () => {
    expect(() => parseAdmissionClassConfig("0", "8", String(UID_POOL_SIZE))).toThrow(ConfigError);
    expect(parseAdmissionClassConfig("0", "8", String(UID_POOL_SIZE - 1)).chatBorrowHeadroomSlots).toBe(
      UID_POOL_SIZE - 1,
    );
  });

  it("refuses a cap below the reserve", () => {
    expect(() => parseAdmissionClassConfig("4", "3", undefined)).toThrow(ConfigError);
  });
});

describe("parsePauseGraceMinutes", () => {
  it("defaults when unset or empty", () => {
    expect(parsePauseGraceMinutes(undefined)).toBe(DEFAULT_PAUSE_GRACE_MINUTES);
    expect(parsePauseGraceMinutes("")).toBe(DEFAULT_PAUSE_GRACE_MINUTES);
  });

  it("accepts zero, meaning kill the paused child immediately", () => {
    expect(parsePauseGraceMinutes("0")).toBe(0);
  });

  it("accepts whole minutes within a day", () => {
    expect(parsePauseGraceMinutes("30")).toBe(30);
    expect(parsePauseGraceMinutes("1440")).toBe(1440);
  });

  it("refuses to start on values that are not whole non-negative minutes", () => {
    for (const bad of ["-1", "1.5", "abc", "NaN", "Infinity", "1441"]) {
      expect(() => parsePauseGraceMinutes(bad)).toThrow(ConfigError);
    }
  });
});

describe("size caps", () => {
  function base64LengthOf(byteLength: number): number {
    return Math.ceil(byteLength / 3) * 4;
  }

  it("keeps the child frame cap large enough to carry a maximum-size checkpoint bundle in base64", () => {
    // Only the child→supervisor direction still frames bundle bytes: a pause or terminal
    // checkpoint has no channel back but stdout.
    expect(MAX_CHILD_FRAME_LENGTH).toBeGreaterThan(base64LengthOf(MAX_CHECKPOINT_BUNDLE_BYTES));
    expect(Number.isSafeInteger(MAX_CHILD_FRAME_LENGTH)).toBe(true);
    expect(MAX_CHECKPOINT_BUNDLE_BYTES).toBeGreaterThan(0);
  });

  it("keeps the supervisor→child frame cap below bundle scale — the bundle rides the cargo channel, not the frame", () => {
    expect(MAX_WIRE_FRAME_LENGTH).toBeLessThan(base64LengthOf(MAX_CHECKPOINT_BUNDLE_BYTES));
    expect(Number.isSafeInteger(MAX_WIRE_FRAME_LENGTH)).toBe(true);
  });

  it("keeps the child grace wide enough to contain the deadline stream close plus the publish work (ADR 0125)", () => {
    // The post-deadline exit spends the close bound first, then publishes deliverables and
    // serializes the terminal bundle; the allowance names what the grace leaves for that
    // work plus the deadline-arm skew workspace prep introduces.
    const publishAndSerializeAllowanceMs = 5_000;
    expect(CHILD_GRACE_MS).toBeGreaterThanOrEqual(
      DEADLINE_ITERATOR_CLOSE_TIMEOUT_MS + publishAndSerializeAllowanceMs,
    );
  });

  it("keeps both frame caps below document scale — no frame carries document bytes in either direction", () => {
    expect(MAX_WIRE_FRAME_LENGTH).toBeLessThan(base64LengthOf(MAX_TOTAL_DOCUMENT_BYTES));
    expect(MAX_CHILD_FRAME_LENGTH).toBeLessThan(base64LengthOf(MAX_TOTAL_DOCUMENT_BYTES));
  });

  // Documents ride the fd-3 cargo channel, so the frame carries their metadata only.
  // This allowance is not a wish: the arithmetic test below proves the worst-case
  // metadata for MAX_DOCUMENTS_PER_RUN entries fits inside it, and the parse
  // boundaries bound every free-text field the sum leans on.
  const jsonEnvelopeAndMetadataAllowanceBytes = 2 * 1024 * 1024;
  // The splitter caps serialized JS characters; JSON escaping expands a character to
  // at most six (\uXXXX). Names need no expansion (their pattern admits no escapes);
  // free-text fields get the full factor.
  const jsonEscapeExpansion = 6;
  const perFieldJsonScaffoldingChars = 24;
  // Every sized rider of a resume frame at its cap: outputFormat and systemPrompt both
  // travel on every start AND resume frame, so an uncounted one would break the proofs
  // that lean on this sum. The bundle contributes only its descriptor (inside the
  // metadata allowance) — its bytes ride the cargo channel. With the giant base64 rider
  // gone, the free-text riders carry the JSON escape factor the old sum could ignore.
  const maximalResumeRiderChars =
    (MAX_ANSWER_CHARS + MAX_PROMPT_CHARS + MAX_OUTPUT_FORMAT_CHARS + MAX_SYSTEM_PROMPT_CHARS) *
      jsonEscapeExpansion +
    jsonEnvelopeAndMetadataAllowanceBytes;

  it("proves a maximal cold-resume frame — every sized rider at its cap — fits the wire cap", () => {
    expect(maximalResumeRiderChars).toBeLessThanOrEqual(MAX_WIRE_FRAME_LENGTH);
  });

  it("proves worst-case staged-input metadata at every cap fits the frame's metadata allowance", () => {
    const nameChars = 200;
    const shaChars = 64;
    const sizeDigits = 16;
    const worstAssetMetaChars = nameChars + shaChars + sizeDigits + 3 * perFieldJsonScaffoldingChars;
    const worstEntryChars =
      nameChars +
      shaChars +
      sizeDigits +
      2 * MAX_DOCUMENT_META_FIELD_CHARS * jsonEscapeExpansion +
      MAX_SKILL_DESCRIPTION_CHARS * jsonEscapeExpansion +
      MAX_SKILL_ASSETS_PER_SKILL * worstAssetMetaChars +
      8 * perFieldJsonScaffoldingChars;
    expect(MAX_DOCUMENTS_PER_RUN * worstEntryChars).toBeLessThanOrEqual(
      jsonEnvelopeAndMetadataAllowanceBytes,
    );
  });

  // A re-carried entry serializes as {"name":…,"sha256":…,"sizeBytes":…}; the name is an
  // out/-relative path — free text up to its own char cap, so it gets the full JSON escape
  // factor; the digest and size do not.
  const worstRecarriedEntryChars =
    MAX_RECARRIED_OUT_PATH_CHARS * jsonEscapeExpansion + 64 + 16 + 3 * perFieldJsonScaffoldingChars;
  const worstRecarriedMetadataChars = MAX_RECARRIED_OUT_FILES * worstRecarriedEntryChars;

  it("proves a maximal continuation frame — descriptor, answer, and every re-carried meta at cap — fits the wire cap", () => {
    // The continuation is a resume frame, so every sized rider of the cold-resume proof
    // travels here too, plus the re-carried out/ metadata (the bytes cross on fd-3).
    expect(maximalResumeRiderChars + worstRecarriedMetadataChars).toBeLessThanOrEqual(MAX_WIRE_FRAME_LENGTH);
  });

  it("proves a maximal continuation body — full shared budget, bundle, prompt, and re-carry overhead — fits the body limit", () => {
    // The re-carried bytes charge the shared document budget, so base64 of that budget
    // already covers them; what the continuation adds on top is the bundle's base64, the
    // per-entry path/digest overhead, and the revision turn.
    expect(
      base64LengthOf(MAX_TOTAL_DOCUMENT_BYTES) +
        base64LengthOf(MAX_CHECKPOINT_BUNDLE_BYTES) +
        MAX_PROMPT_CHARS * MAX_UTF8_BYTES_PER_JS_CHAR +
        worstRecarriedMetadataChars +
        jsonEnvelopeAndMetadataAllowanceBytes,
    ).toBeLessThanOrEqual(DEFAULT_BODY_LIMIT_BYTES);
  });

  it("keeps the re-carry ceiling strictly between the documents cap and the publish cap", () => {
    expect(MAX_RECARRIED_OUT_FILES).toBeGreaterThan(MAX_DOCUMENTS_PER_RUN);
    expect(MAX_RECARRIED_OUT_FILES).toBeLessThan(MAX_PUBLISHED_OUT_FILES);
  });

  it("keeps the input route's body cap large enough for a maximal answer in any script", () => {
    expect(RUN_INPUT_BODY_LIMIT_BYTES).toBeGreaterThan(MAX_ANSWER_CHARS * MAX_UTF8_BYTES_PER_JS_CHAR);
    expect(RUN_INPUT_BODY_LIMIT_BYTES).toBeLessThan(DEFAULT_BODY_LIMIT_BYTES);
  });

  it("keeps the body limit large enough for a full document budget in base64 with headroom", () => {
    expect(DEFAULT_BODY_LIMIT_BYTES).toBe(384 * 1024 * 1024);
    expect(DEFAULT_BODY_LIMIT_BYTES).toBeGreaterThan(base64LengthOf(MAX_TOTAL_DOCUMENT_BYTES));
  });
});

describe("files listing bounds", () => {
  const worstListingOf = (entryCount: number, pathBytes: number): number => {
    const entries = Array.from({ length: entryCount }, (_, index) => ({
      path: `out/${String(index).padStart(pathBytes, "<")}`,
      sizeBytes: 1,
      sha256: "0".repeat(64),
      kind: "artifact",
    }));
    return measureHtmlInertJson({ files: entries }).renderedBytes;
  };

  it("leaves the listing room for a kind field on every entry a publish may carry", () => {
    expect(LISTING_KIND_FIELD_BYTES).toBe(',"kind":"artifact"'.length);
    expect(MAX_FILES_LISTING_BYTES - MAX_ARTIFACTS_MANIFEST_BYTES).toBeGreaterThan(
      MAX_PUBLISHED_OUT_FILES * LISTING_KIND_FIELD_BYTES,
    );
  });

  it("renders a manifest at the publish cap inside the listing cap", () => {
    const entryCount = 2_000;
    const pathBytes = 512;
    const listingBytes = worstListingOf(entryCount, pathBytes);
    const manifestBytes = listingBytes - entryCount * LISTING_KIND_FIELD_BYTES;
    expect(manifestBytes).toBeLessThanOrEqual(MAX_ARTIFACTS_MANIFEST_BYTES);
    expect(listingBytes).toBeLessThanOrEqual(MAX_FILES_LISTING_BYTES);
  });

  it("charges a listing the copies it holds at once rather than the one it sends", () => {
    expect(FILES_LISTING_RESIDENT_BYTES).toBe(3 * MAX_ARTIFACTS_MANIFEST_BYTES + 2 * MAX_FILES_LISTING_BYTES);
    expect(FILES_LISTING_RESIDENT_BYTES).toBeGreaterThan(MAX_FILES_LISTING_BYTES);
    expect(MAX_INFLIGHT_UPLOAD_BYTES).toBeGreaterThan(FILES_LISTING_RESIDENT_BYTES);
  });
});

describe("in-flight upload budget", () => {
  it("pins the budget to exactly one maximum body so a ratio change is a conscious decision", () => {
    expect(MAX_INFLIGHT_UPLOAD_BYTES).toBe(DEFAULT_BODY_LIMIT_BYTES);
  });

  it("sizes the undeclared-body provisional charge so a full uid pool of starts can upload at once", () => {
    expect(UNDECLARED_BODY_PROVISIONAL_BYTES).toBe(64 * 1024);
    expect(Math.floor(MAX_INFLIGHT_UPLOAD_BYTES / UNDECLARED_BODY_PROVISIONAL_BYTES)).toBeGreaterThanOrEqual(
      UID_POOL_SIZE,
    );
  });
});

describe("retained output budget", () => {
  it("pins the publish ceiling to the document budget so a raise is a conscious decision", () => {
    expect(MAX_PUBLISHED_OUT_BYTES).toBe(MAX_TOTAL_DOCUMENT_BYTES);
    expect(MAX_PUBLISHED_OUT_BYTES).toBe(200 * 1024 * 1024);
  });

  it("charges a run's worst case as a full publish plus its trace pack", () => {
    expect(TRACE_PACK_ALLOWANCE_BYTES).toBeGreaterThan(0);
    expect(RETAINED_OUTPUT_WORST_CASE_BYTES).toBe(MAX_PUBLISHED_OUT_BYTES + TRACE_PACK_ALLOWANCE_BYTES);
    expect(MAX_RETAINED_OUTPUT_BYTES).toBe(3 * RETAINED_OUTPUT_WORST_CASE_BYTES);
  });

  it("sizes the default declared reservation so the uid pool binds before the output budget", () => {
    expect(DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES).toBe(24 * 1024 * 1024);
    expect(DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES).toBeLessThanOrEqual(RETAINED_OUTPUT_WORST_CASE_BYTES);
    expect(
      Math.floor(MAX_RETAINED_OUTPUT_BYTES / DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES),
    ).toBeGreaterThanOrEqual(UID_POOL_SIZE);
  });

  it("threads the retained-output budget into the server config", () => {
    const config = loadServerConfig({
      ...PINNED_ENV,
      AUTH_DISABLED: "true",
      AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
    });
    expect(config.retainedOutputBudgetBytes).toBe(MAX_RETAINED_OUTPUT_BYTES);
  });
});

describe("run uid pool", () => {
  it("carries enough slots for the platform's two-uids-per-block validator loop", () => {
    expect(UID_POOL_SIZE).toBeGreaterThanOrEqual(32);
  });

  it("keeps every slot clear of the image's own users and inside the numeric band", () => {
    // Only constraints checked here: no collision with the supervisor's own
    // uid, and the last slot still being an ordinary uid.
    expect(UID_POOL_BASE).toBeGreaterThan(1000);
    expect(UID_POOL_BASE + UID_POOL_SIZE - 1).toBeLessThan(60000);
  });
});

describe("parseMaxBudgetUsd", () => {
  it("defaults to the built-in default when unset or empty", () => {
    expect(parseMaxBudgetUsd(undefined)).toBe(DEFAULT_MAX_BUDGET_USD);
    expect(parseMaxBudgetUsd("")).toBe(DEFAULT_MAX_BUDGET_USD);
  });

  it("accepts an override that lowers the default, fractions included", () => {
    expect(parseMaxBudgetUsd("0.25")).toBe(0.25);
    expect(parseMaxBudgetUsd(String(DEFAULT_MAX_BUDGET_USD))).toBe(DEFAULT_MAX_BUDGET_USD);
  });

  it("refuses a default above the built-in default, and anything that is not a positive number", () => {
    for (const raw of [String(DEFAULT_MAX_BUDGET_USD + 0.01), "1000", "0", "-1", "abc", "NaN", "Infinity"]) {
      expect(() => parseMaxBudgetUsd(raw)).toThrow(ConfigError);
    }
  });

  it("names both the built-in default and the refused value when a default would exceed it", () => {
    const overDefault = String(DEFAULT_MAX_BUDGET_USD + 0.01);
    expect(() => parseMaxBudgetUsd(overDefault)).toThrow(
      new RegExp(`${DEFAULT_MAX_BUDGET_USD}.*${overDefault.replace(".", "\\.")}`),
    );
  });

  it("stays tighten-only now the ceiling sits above the default — it may not raise into the gap", () => {
    // The one configuration that can tell the two bounds apart. A knob that could
    // raise a fleet-wide spend default is a knob a typo can bill you with (ADR 0054),
    // so the gap ADR 0117 opened is reachable by a request, never by this variable.
    const betweenDefaultAndCeiling = (DEFAULT_MAX_BUDGET_USD + RUN_BUDGET_CEILING_USD) / 2;
    expect(betweenDefaultAndCeiling).toBeGreaterThan(DEFAULT_MAX_BUDGET_USD);
    expect(betweenDefaultAndCeiling).toBeLessThan(RUN_BUDGET_CEILING_USD);
    expect(() => parseMaxBudgetUsd(String(betweenDefaultAndCeiling))).toThrow(ConfigError);
    expect(() => parseMaxBudgetUsd(String(RUN_BUDGET_CEILING_USD))).toThrow(ConfigError);
  });
});

describe("parseMaxTurns", () => {
  it("defaults to the built-in default when unset or empty", () => {
    expect(parseMaxTurns(undefined)).toBe(DEFAULT_MAX_TURNS);
    expect(parseMaxTurns("")).toBe(DEFAULT_MAX_TURNS);
  });

  it("accepts an override that lowers the default turn cap", () => {
    expect(parseMaxTurns("50")).toBe(50);
    expect(parseMaxTurns(String(RUN_TURNS_CEILING))).toBe(RUN_TURNS_CEILING);
  });

  it("refuses a default above the ceiling, and refuses anything that is not a positive integer", () => {
    for (const raw of [String(RUN_TURNS_CEILING + 1), "1000", "0", "-1", "1.5", "abc", "NaN", "Infinity"]) {
      expect(() => parseMaxTurns(raw)).toThrow(ConfigError);
    }
  });
});

describe("parseRunDeadlineMs", () => {
  it("defaults to the built-in default when unset or empty", () => {
    expect(parseRunDeadlineMs(undefined)).toBe(DEFAULT_RUN_DEADLINE_MS);
    expect(parseRunDeadlineMs("")).toBe(DEFAULT_RUN_DEADLINE_MS);
  });

  it("accepts an override that shortens the default deadline", () => {
    expect(parseRunDeadlineMs("60000")).toBe(60_000);
    expect(parseRunDeadlineMs(String(DEFAULT_RUN_DEADLINE_MS))).toBe(DEFAULT_RUN_DEADLINE_MS);
  });

  it("refuses a default above the built-in default, and anything that is not a positive integer", () => {
    for (const raw of [String(DEFAULT_RUN_DEADLINE_MS + 1), "0", "-1", "1.5", "abc", "Infinity"]) {
      expect(() => parseRunDeadlineMs(raw)).toThrow(ConfigError);
    }
  });

  it("stays tighten-only now the ceiling sits above the default — it may not raise into the gap", () => {
    const betweenDefaultAndCeiling = (DEFAULT_RUN_DEADLINE_MS + RUN_DEADLINE_CEILING_MS) / 2;
    expect(betweenDefaultAndCeiling).toBeGreaterThan(DEFAULT_RUN_DEADLINE_MS);
    expect(betweenDefaultAndCeiling).toBeLessThan(RUN_DEADLINE_CEILING_MS);
    expect(() => parseRunDeadlineMs(String(betweenDefaultAndCeiling))).toThrow(ConfigError);
    expect(() => parseRunDeadlineMs(String(RUN_DEADLINE_CEILING_MS))).toThrow(ConfigError);
  });
});

describe("loadServerConfig fuses", () => {
  const bootable = { ...PINNED_ENV, AUTH_DISABLED: "true", AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true" };

  it("moves only the defaults under tightened env overrides — the ceilings stay code-owned", () => {
    const tightened = loadServerConfig({
      ...bootable,
      AGENT_RUNTIME_MAX_BUDGET_USD: "0.5",
      AGENT_RUNTIME_RUN_DEADLINE_MS: "90000",
      AGENT_RUNTIME_MAX_TURNS: "100",
    });
    expect(tightened.fuses.maxBudgetUsd).toEqual({ defaultValue: 0.5, ceiling: RUN_BUDGET_CEILING_USD });
    expect(tightened.fuses.deadlineMs).toEqual({ defaultValue: 90_000, ceiling: RUN_DEADLINE_CEILING_MS });
    expect(tightened.fuses.maxTurns).toEqual({ defaultValue: 100, ceiling: RUN_TURNS_CEILING });
  });

  it("refuses to boot on a deployed default raised into the gap below the ceiling", () => {
    // A request may declare anything up to the ceiling; this variable may not hand the
    // same value to every silent run on the fleet (ADR 0054, ADR 0117).
    expect(() =>
      loadServerConfig({
        ...bootable,
        AGENT_RUNTIME_MAX_BUDGET_USD: String((DEFAULT_MAX_BUDGET_USD + RUN_BUDGET_CEILING_USD) / 2),
      }),
    ).toThrow(ConfigError);
    expect(() =>
      loadServerConfig({
        ...bootable,
        AGENT_RUNTIME_RUN_DEADLINE_MS: String((DEFAULT_RUN_DEADLINE_MS + RUN_DEADLINE_CEILING_MS) / 2),
      }),
    ).toThrow(ConfigError);
  });

  it("serves the built-in defaults when no override is set", () => {
    const defaults = loadServerConfig(bootable);
    expect(defaults.fuses.maxBudgetUsd).toEqual({
      defaultValue: DEFAULT_MAX_BUDGET_USD,
      ceiling: RUN_BUDGET_CEILING_USD,
    });
    expect(defaults.fuses.deadlineMs).toEqual({
      defaultValue: DEFAULT_RUN_DEADLINE_MS,
      ceiling: RUN_DEADLINE_CEILING_MS,
    });
    expect(defaults.fuses.maxTurns).toEqual({
      defaultValue: DEFAULT_MAX_TURNS,
      ceiling: RUN_TURNS_CEILING,
    });
  });

  it("carries the retained-output pair in the same default/ceiling shape as the other three", () => {
    const defaults = loadServerConfig(bootable);
    expect(defaults.fuses.maxRetainedOutputBytes).toEqual({
      defaultValue: DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
      ceiling: RETAINED_OUTPUT_WORST_CASE_BYTES,
    });
  });

  it("refuses to boot when a deployed default exceeds its bound", () => {
    expect(() =>
      loadServerConfig({ ...bootable, AGENT_RUNTIME_MAX_BUDGET_USD: String(RUN_BUDGET_CEILING_USD + 0.5) }),
    ).toThrow(ConfigError);
    expect(() =>
      loadServerConfig({ ...bootable, AGENT_RUNTIME_RUN_DEADLINE_MS: String(RUN_DEADLINE_CEILING_MS + 1) }),
    ).toThrow(ConfigError);
    expect(() =>
      loadServerConfig({ ...bootable, AGENT_RUNTIME_MAX_TURNS: String(RUN_TURNS_CEILING + 1) }),
    ).toThrow(ConfigError);
  });
});

describe("parseDocumentRoot", () => {
  it("defaults when unset or empty", () => {
    expect(parseDocumentRoot(undefined)).toBe(DEFAULT_DOCUMENT_ROOT);
    expect(parseDocumentRoot("")).toBe(DEFAULT_DOCUMENT_ROOT);
  });

  it("accepts an absolute path under a sandbox-denied root and refuses relative ones", () => {
    expect(parseDocumentRoot("/workspaces/documents")).toBe("/workspaces/documents");
    expect(() => parseDocumentRoot("documents")).toThrow(ConfigError);
  });

  it("refuses an absolute path outside every sandbox-denied read root", () => {
    expect(() => parseDocumentRoot("/var/documents")).toThrow(ConfigError);
  });
});

describe("parseCheckpointRoot", () => {
  it("defaults when unset or empty", () => {
    expect(parseCheckpointRoot(undefined)).toBe(DEFAULT_CHECKPOINT_ROOT);
    expect(parseCheckpointRoot("")).toBe(DEFAULT_CHECKPOINT_ROOT);
  });

  it("accepts an absolute path under a sandbox-denied root", () => {
    expect(parseCheckpointRoot("/workspaces/alt-checkpoints")).toBe("/workspaces/alt-checkpoints");
  });

  it("refuses a checkpoint root outside every sandbox-denied read root", () => {
    expect(() => parseCheckpointRoot("/var/checkpoints")).toThrow(ConfigError);
  });

  it("refuses relative and non-normalized paths", () => {
    expect(() => parseCheckpointRoot("checkpoints")).toThrow(ConfigError);
    expect(() => parseCheckpointRoot("./checkpoints")).toThrow(ConfigError);
    expect(() => parseCheckpointRoot("/workspaces/../var/checkpoints")).toThrow(ConfigError);
  });
});
