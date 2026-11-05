import { describe, expect, it } from "bun:test";
import { ConfigError } from "./config-error.js";
import {
  ANTHROPIC_PROXY_PLACEHOLDER_API_KEY,
  assertLoopbackUrl,
  assertModelIdForProvider,
  assertNoProxyVariables,
  buildProviderSubprocessEnv,
  buildSubprocessEnv,
  EXECUTION_ID_HEADER,
  LOOPBACK_BASE_URL,
  LOOPBACK_PORT,
  MAX_OUTPUT_TOKENS,
  MODEL_PROVIDER_ENV_VAR,
  MODEL_PROVIDERS,
  parseAnthropicProxyKey,
  parseLoopbackListenAddress,
  parseModelProvider,
  parseModelProviderConfig,
} from "./model-provider.js";

const COMMON_HOST = {
  PATH: "/opt/venv/bin:/usr/bin",
  HOME: "/home/bun",
  NODE_PATH: "/opt/node/node_modules",
  CLAUDE_CONFIG_DIR: "/home/bun/.claude",
  CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
  ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
  AUTH_TOKEN: "secret",
  PLATFORM_BASE_URL: "http://platform",
  LOG_LEVEL: "info",
};

const FOUNDRY_HOST = {
  ...COMMON_HOST,
  [MODEL_PROVIDER_ENV_VAR]: "foundry",
  CLAUDE_CODE_USE_FOUNDRY: "1",
  ANTHROPIC_FOUNDRY_RESOURCE: "inia-foundry",
  ANTHROPIC_MODEL: "claude-sonnet-5",
  ENABLE_PROMPT_CACHING_1H: "1",
};

const ANTHROPIC_KEY = "sk-ant-api03-real-key";

const ANTHROPIC_HOST = {
  ...COMMON_HOST,
  [MODEL_PROVIDER_ENV_VAR]: "anthropic",
  ANTHROPIC_API_KEY: ANTHROPIC_KEY,
  ANTHROPIC_MODEL: "claude-sonnet-5-5",
};

const BEDROCK_HOST = {
  ...COMMON_HOST,
  CLAUDE_CODE_USE_BEDROCK: "1",
  ANTHROPIC_MODEL: "us.anthropic.claude-sonnet-4-6",
  AGENT_RUNTIME_METADATA_CREDENTIALS: "true",
  AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338",
  AWS_REGION: "us-west-2",
};

const EVERY_MODE_HOST = {
  ...BEDROCK_HOST,
  ...FOUNDRY_HOST,
};

function refusal(env: Record<string, string | undefined>): string {
  try {
    parseModelProviderConfig(env);
  } catch (error) {
    if (error instanceof ConfigError) {
      return error.message;
    }
    throw error;
  }
  throw new Error("expected parseModelProviderConfig to refuse");
}

describe("parseModelProvider", () => {
  it("defaults to bedrock when the setting is unset or empty — zero config keeps today's behaviour", () => {
    expect(parseModelProvider(undefined)).toBe("bedrock");
    expect(parseModelProvider("")).toBe("bedrock");
  });

  it("accepts exactly the declared modes", () => {
    expect(parseModelProvider("bedrock")).toBe("bedrock");
    expect(parseModelProvider("foundry")).toBe("foundry");
    expect(parseModelProvider("anthropic")).toBe("anthropic");
  });

  it("refuses anything else, naming the setting — a typo must not fall back to a mode", () => {
    for (const raw of ["Bedrock", "azure", "Anthropic", " foundry"]) {
      expect(() => parseModelProvider(raw)).toThrow(ConfigError);
      expect(() => parseModelProvider(raw)).toThrow(new RegExp(MODEL_PROVIDER_ENV_VAR));
    }
  });
});

describe("the loopback process's address is a code-owned loopback constant", () => {
  it("names loopback on a port beside the run API", () => {
    expect(LOOPBACK_BASE_URL).toBe(`http://127.0.0.1:${LOOPBACK_PORT}`);
    expect(() => assertLoopbackUrl(LOOPBACK_BASE_URL, "shim")).not.toThrow();
  });

  it("assertLoopbackUrl admits only loopback hosts over http", () => {
    for (const url of ["http://127.0.0.1:8131", "http://localhost:1", "http://[::1]:8131"]) {
      expect(() => assertLoopbackUrl(url, "shim")).not.toThrow();
    }
    for (const url of [
      "http://10.0.0.1:8131",
      "https://api.anthropic.com",
      "http://127.0.0.1.evil.example:8131",
      "http://localhost.evil.example",
      "not a url",
      "ftp://127.0.0.1",
    ]) {
      expect(() => assertLoopbackUrl(url, "shim")).toThrow(ConfigError);
    }
  });
});

describe("buildProviderSubprocessEnv", () => {
  it("bedrock is buildSubprocessEnv plus the output-token ceiling", () => {
    const source = { ...BEDROCK_HOST, AWS_ACCESS_KEY_ID: "AKIA", ANTHROPIC_FOUNDRY_RESOURCE: "res" };
    expect(buildProviderSubprocessEnv("bedrock", source)).toEqual({
      ...buildSubprocessEnv(source),
      CLAUDE_CODE_MAX_OUTPUT_TOKENS: MAX_OUTPUT_TOKENS,
      ENABLE_TOOL_SEARCH: "false",
    });
  });

  it("every mode raises the output-token ceiling, and a host value never overrides it", () => {
    for (const provider of MODEL_PROVIDERS) {
      const env = buildProviderSubprocessEnv(provider, {
        ...EVERY_MODE_HOST,
        CLAUDE_CODE_MAX_OUTPUT_TOKENS: "1000",
      });
      expect(env["CLAUDE_CODE_MAX_OUTPUT_TOKENS"]).toBe("64000");
    }
  });

  it("every mode turns deferred tool loading off, and a host value never turns it back on", () => {
    for (const provider of MODEL_PROVIDERS) {
      const env = buildProviderSubprocessEnv(provider, {
        ...EVERY_MODE_HOST,
        ENABLE_TOOL_SEARCH: "true",
      });
      expect(env["ENABLE_TOOL_SEARCH"]).toBe("false");
    }
  });

  it("foundry carries exactly the Foundry variables and never an AWS one or a Foundry secret", () => {
    expect(
      buildProviderSubprocessEnv("foundry", {
        ...FOUNDRY_HOST,
        AZURE_CLIENT_ID: "11111111-2222-3333-4444-555555555555",
        ANTHROPIC_FOUNDRY_API_KEY: "k",
        ANTHROPIC_FOUNDRY_AUTH_TOKEN: "t",
        AZURE_CLIENT_SECRET: "s",
        AWS_REGION: "us-west-2",
        AWS_ACCESS_KEY_ID: "AKIA",
        AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338",
        CLAUDE_CODE_USE_BEDROCK: "1",
        ANTHROPIC_BASE_URL: "http://127.0.0.1:1",
      }),
    ).toEqual({
      PATH: "/opt/venv/bin:/usr/bin",
      HOME: "/home/bun",
      NODE_PATH: "/opt/node/node_modules",
      CLAUDE_CONFIG_DIR: "/home/bun/.claude",
      CLAUDE_CODE_USE_FOUNDRY: "1",
      CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
      ANTHROPIC_MODEL: "claude-sonnet-5",
      ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
      ENABLE_PROMPT_CACHING_1H: "1",
      ANTHROPIC_FOUNDRY_RESOURCE: "inia-foundry",
      AZURE_CLIENT_ID: "11111111-2222-3333-4444-555555555555",
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
      CLAUDE_CODE_MAX_OUTPUT_TOKENS: MAX_OUTPUT_TOKENS,
      ENABLE_TOOL_SEARCH: "false",
    });
  });

  it("foundry passes the base-URL form through when that is what the deployment set", () => {
    const env = buildProviderSubprocessEnv("foundry", {
      ...FOUNDRY_HOST,
      ANTHROPIC_FOUNDRY_RESOURCE: undefined,
      ANTHROPIC_FOUNDRY_BASE_URL: "https://inia-foundry.services.ai.azure.com/anthropic",
    });
    expect(env["ANTHROPIC_FOUNDRY_BASE_URL"]).toBe("https://inia-foundry.services.ai.azure.com/anthropic");
    expect("ANTHROPIC_FOUNDRY_RESOURCE" in env).toBe(false);
  });

  it("anthropic points the child at the key holder with a placeholder credential, never the key", () => {
    expect(
      buildProviderSubprocessEnv("anthropic", {
        ...ANTHROPIC_HOST,
        ENABLE_PROMPT_CACHING_1H: "1",
        AWS_REGION: "us-west-2",
        AWS_ACCESS_KEY_ID: "AKIA",
        CLAUDE_CODE_USE_BEDROCK: "1",
        ANTHROPIC_BASE_URL: "http://10.0.0.1:1",
        ANTHROPIC_AUTH_TOKEN: "real-token",
        ANTHROPIC_CUSTOM_HEADERS: "x: y",
      }),
    ).toEqual({
      PATH: "/opt/venv/bin:/usr/bin",
      HOME: "/home/bun",
      NODE_PATH: "/opt/node/node_modules",
      CLAUDE_CONFIG_DIR: "/home/bun/.claude",
      CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
      ANTHROPIC_MODEL: "claude-sonnet-5-5",
      ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
      ENABLE_PROMPT_CACHING_1H: "1",
      ANTHROPIC_BASE_URL: LOOPBACK_BASE_URL,
      ANTHROPIC_API_KEY: ANTHROPIC_PROXY_PLACEHOLDER_API_KEY,
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
      CLAUDE_CODE_MAX_OUTPUT_TOKENS: MAX_OUTPUT_TOKENS,
      ENABLE_TOOL_SEARCH: "false",
    });
  });

  it("no mode lets a gateway credential, AUTH_TOKEN, the gateway settings or an Anthropic key reach the child", () => {
    for (const provider of MODEL_PROVIDERS) {
      const env = buildProviderSubprocessEnv(provider, {
        ...EVERY_MODE_HOST,
        ...ANTHROPIC_HOST,
      });
      for (const name of Object.keys(env)) {
        expect(name.startsWith("AGENT_RUNTIME_GATEWAY_")).toBe(false);
      }
      expect(env["AUTH_TOKEN"]).toBeUndefined();
      expect(env["PLATFORM_BASE_URL"]).toBeUndefined();
      expect(env["LOG_LEVEL"]).toBeUndefined();
      expect(Object.values(env)).not.toContain(ANTHROPIC_KEY);
    }
  });
});

describe("parseModelProviderConfig — bedrock", () => {
  it("keeps today's identity tri-state under the provider record", () => {
    expect(parseModelProviderConfig(BEDROCK_HOST)).toEqual({
      provider: "bedrock",
      identity: { metadataCredentials: true },
    });
    expect(parseModelProviderConfig({ ...BEDROCK_HOST, [MODEL_PROVIDER_ENV_VAR]: "bedrock" })).toEqual({
      provider: "bedrock",
      identity: { metadataCredentials: true },
    });
  });

  it("still refuses an unpinned or un-flagged Bedrock environment", () => {
    expect(() => parseModelProviderConfig({ ...BEDROCK_HOST, CLAUDE_CODE_USE_BEDROCK: undefined })).toThrow(
      ConfigError,
    );
    expect(() => parseModelProviderConfig({ ...BEDROCK_HOST, ANTHROPIC_MODEL: undefined })).toThrow(
      ConfigError,
    );
  });

  it("refuses a Foundry variable present without the mode, naming it", () => {
    expect(refusal({ ...BEDROCK_HOST, CLAUDE_CODE_USE_FOUNDRY: "1" })).toContain("CLAUDE_CODE_USE_FOUNDRY");
    expect(refusal({ ...BEDROCK_HOST, ANTHROPIC_FOUNDRY_RESOURCE: "r" })).toContain(
      "ANTHROPIC_FOUNDRY_RESOURCE",
    );
    expect(refusal({ ...BEDROCK_HOST, ANTHROPIC_FOUNDRY_API_KEY: "k" })).toContain(
      "ANTHROPIC_FOUNDRY_API_KEY",
    );
  });

  it("refuses a gateway setting or a direct-API variable present without the mode, naming it", () => {
    expect(refusal({ ...BEDROCK_HOST, AGENT_RUNTIME_LOOPBACK_HOST: "127.0.0.1" })).toContain(
      "AGENT_RUNTIME_LOOPBACK_HOST",
    );
    expect(refusal({ ...BEDROCK_HOST, AGENT_RUNTIME_LOOPBACK_PORT: "8131" })).toContain(
      "AGENT_RUNTIME_LOOPBACK_PORT",
    );
    expect(refusal({ ...BEDROCK_HOST, ANTHROPIC_BASE_URL: "http://127.0.0.1:1" })).toContain(
      "ANTHROPIC_BASE_URL",
    );
    expect(refusal({ ...BEDROCK_HOST, ANTHROPIC_API_KEY: "k" })).toContain("ANTHROPIC_API_KEY");
  });
});

describe("parseModelProviderConfig — foundry", () => {
  it("accepts the resource form and the base-URL form, and carries no identity record", () => {
    expect(parseModelProviderConfig(FOUNDRY_HOST)).toEqual({ provider: "foundry" });
    expect(
      parseModelProviderConfig({
        ...FOUNDRY_HOST,
        ANTHROPIC_FOUNDRY_RESOURCE: undefined,
        ANTHROPIC_FOUNDRY_BASE_URL: "https://inia-foundry.services.ai.azure.com/anthropic",
      }),
    ).toEqual({ provider: "foundry" });
  });

  it("refuses the mode without its variables, naming the missing piece", () => {
    expect(refusal({ ...FOUNDRY_HOST, CLAUDE_CODE_USE_FOUNDRY: undefined })).toContain(
      "CLAUDE_CODE_USE_FOUNDRY",
    );
    expect(refusal({ ...FOUNDRY_HOST, CLAUDE_CODE_USE_FOUNDRY: "true" })).toContain(
      "CLAUDE_CODE_USE_FOUNDRY",
    );
    const neither = refusal({ ...FOUNDRY_HOST, ANTHROPIC_FOUNDRY_RESOURCE: undefined });
    expect(neither).toContain("ANTHROPIC_FOUNDRY_RESOURCE");
    expect(neither).toContain("ANTHROPIC_FOUNDRY_BASE_URL");
    expect(refusal({ ...FOUNDRY_HOST, ANTHROPIC_MODEL: undefined })).toContain("ANTHROPIC_MODEL");
  });

  it("refuses both endpoint forms at once — the CLI would pick one silently", () => {
    const message = refusal({
      ...FOUNDRY_HOST,
      ANTHROPIC_FOUNDRY_BASE_URL: "https://inia-foundry.services.ai.azure.com/anthropic",
    });
    expect(message).toContain("ANTHROPIC_FOUNDRY_RESOURCE");
    expect(message).toContain("ANTHROPIC_FOUNDRY_BASE_URL");
  });

  it("refuses a base URL that is not https — the identity token must not cross plaintext", () => {
    expect(
      refusal({
        ...FOUNDRY_HOST,
        ANTHROPIC_FOUNDRY_RESOURCE: undefined,
        ANTHROPIC_FOUNDRY_BASE_URL: "http://inia-foundry.services.ai.azure.com/anthropic",
      }),
    ).toContain("ANTHROPIC_FOUNDRY_BASE_URL");
  });

  it("refuses a Foundry secret — authentication is the managed identity, decided, not a key", () => {
    expect(refusal({ ...FOUNDRY_HOST, ANTHROPIC_FOUNDRY_API_KEY: "k" })).toContain(
      "ANTHROPIC_FOUNDRY_API_KEY",
    );
    expect(refusal({ ...FOUNDRY_HOST, ANTHROPIC_FOUNDRY_AUTH_TOKEN: "t" })).toContain(
      "ANTHROPIC_FOUNDRY_AUTH_TOKEN",
    );
    expect(refusal({ ...FOUNDRY_HOST, CLAUDE_CODE_SKIP_FOUNDRY_AUTH: "1" })).toContain(
      "CLAUDE_CODE_SKIP_FOUNDRY_AUTH",
    );
  });

  it("refuses an Azure secret-bearing credential variable — the chain must resolve the identity, not a secret", () => {
    for (const name of [
      "AZURE_CLIENT_SECRET",
      "AZURE_CLIENT_CERTIFICATE_PATH",
      "AZURE_USERNAME",
      "AZURE_PASSWORD",
      "AZURE_FEDERATED_TOKEN_FILE",
    ]) {
      expect(refusal({ ...FOUNDRY_HOST, [name]: "x" })).toContain(name);
    }
  });

  it("refuses an AWS identity or credential variable beside the mode, naming it", () => {
    for (const name of [
      "CLAUDE_CODE_USE_BEDROCK",
      "AGENT_RUNTIME_ROLE_ARN",
      "AGENT_RUNTIME_METADATA_CREDENTIALS",
      "AGENT_RUNTIME_AMBIENT_CREDENTIALS",
      "AWS_ACCESS_KEY_ID",
      "AWS_SECRET_ACCESS_KEY",
      "AWS_SESSION_TOKEN",
      "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI",
      "AWS_CONTAINER_CREDENTIALS_FULL_URI",
      "AWS_EC2_METADATA_SERVICE_ENDPOINT",
    ]) {
      expect(refusal({ ...FOUNDRY_HOST, [name]: "x" })).toContain(name);
    }
  });

  it("refuses a Bedrock-shaped model id — Foundry ids are deployment names", () => {
    for (const model of [
      "us.anthropic.claude-sonnet-4-6",
      "anthropic.claude-haiku-4-5-20251001-v1:0",
      "global.anthropic.claude-opus-4-8",
      "claude sonnet",
    ]) {
      expect(refusal({ ...FOUNDRY_HOST, ANTHROPIC_MODEL: model })).toContain("ANTHROPIC_MODEL");
    }
    expect(() =>
      parseModelProviderConfig({ ...FOUNDRY_HOST, ANTHROPIC_MODEL: "claude-opus-4-8" }),
    ).not.toThrow();
  });
});

describe("parseModelProviderConfig — anthropic", () => {
  it("accepts a plain key and a model, and hands the key to the key holder alone", () => {
    expect(parseModelProviderConfig(ANTHROPIC_HOST)).toEqual({
      provider: "anthropic",
      proxy: { apiKey: ANTHROPIC_KEY },
    });
  });

  it("refuses the mode without its key or its model, naming the missing one", () => {
    expect(refusal({ ...ANTHROPIC_HOST, ANTHROPIC_API_KEY: undefined })).toContain("ANTHROPIC_API_KEY");
    expect(refusal({ ...ANTHROPIC_HOST, ANTHROPIC_API_KEY: "" })).toContain("ANTHROPIC_API_KEY");
    expect(refusal({ ...ANTHROPIC_HOST, ANTHROPIC_MODEL: undefined })).toContain("ANTHROPIC_MODEL");
  });

  it("refuses every other mode's variable beside it, naming it", () => {
    for (const name of [
      "CLAUDE_CODE_USE_BEDROCK",
      "AGENT_RUNTIME_ROLE_ARN",
      "AGENT_RUNTIME_METADATA_CREDENTIALS",
      "AGENT_RUNTIME_AMBIENT_CREDENTIALS",
      "AWS_ACCESS_KEY_ID",
      "AWS_SECRET_ACCESS_KEY",
      "AWS_SESSION_TOKEN",
      "AWS_EC2_METADATA_SERVICE_ENDPOINT",
      "CLAUDE_CODE_USE_FOUNDRY",
      "ANTHROPIC_FOUNDRY_RESOURCE",
      "ANTHROPIC_FOUNDRY_BASE_URL",
      "ANTHROPIC_FOUNDRY_API_KEY",
      "AZURE_CLIENT_SECRET",
      "AGENT_RUNTIME_LOOPBACK_PORT",
    ]) {
      expect(refusal({ ...ANTHROPIC_HOST, [name]: "x" })).toContain(name);
    }
  });

  it("refuses a deploy-time base URL, bearer token or custom headers — the key holder's address is code-owned", () => {
    const message = refusal({ ...ANTHROPIC_HOST, ANTHROPIC_BASE_URL: "https://api.anthropic.com" });
    expect(message).toContain("ANTHROPIC_BASE_URL");
    expect(message).toContain(LOOPBACK_BASE_URL);
    expect(refusal({ ...ANTHROPIC_HOST, ANTHROPIC_AUTH_TOKEN: "t" })).toContain("ANTHROPIC_AUTH_TOKEN");
    expect(refusal({ ...ANTHROPIC_HOST, ANTHROPIC_CUSTOM_HEADERS: "x: y" })).toContain(
      "ANTHROPIC_CUSTOM_HEADERS",
    );
  });

  it("pins the model ids to Anthropic API names, never a Bedrock id or ARN", () => {
    for (const model of ["claude-sonnet-5-5", "claude-haiku-4-5-20251001", "claude-opus-4-1"]) {
      expect(() => parseModelProviderConfig({ ...ANTHROPIC_HOST, ANTHROPIC_MODEL: model })).not.toThrow();
      expect(() => assertModelIdForProvider("anthropic", model, "ANTHROPIC_ALLOWED_MODELS")).not.toThrow();
    }
    for (const model of [
      "us.anthropic.claude-sonnet-4-6",
      "anthropic.claude-haiku-4-5-20251001-v1:0",
      "arn:aws:bedrock:us-west-2:123456789012:inference-profile/us.anthropic.claude-sonnet-4-6",
      "claude sonnet",
    ]) {
      expect(refusal({ ...ANTHROPIC_HOST, ANTHROPIC_MODEL: model })).toContain("ANTHROPIC_MODEL");
      expect(() => assertModelIdForProvider("anthropic", model, "ANTHROPIC_ALLOWED_MODELS")).toThrow(
        /ANTHROPIC_ALLOWED_MODELS/,
      );
    }
  });
});

describe("the other modes still refuse a plain Anthropic key", () => {
  it("bedrock, foundry and gateway each refuse ANTHROPIC_API_KEY, naming it", () => {
    for (const host of [BEDROCK_HOST, FOUNDRY_HOST]) {
      expect(refusal({ ...host, ANTHROPIC_API_KEY: ANTHROPIC_KEY })).toContain("ANTHROPIC_API_KEY");
    }
  });

  it("an unset mode with a plain key is still bedrock, and refuses the key", () => {
    expect(
      refusal({ ...BEDROCK_HOST, [MODEL_PROVIDER_ENV_VAR]: undefined, ANTHROPIC_API_KEY: "k" }),
    ).toContain("ANTHROPIC_API_KEY");
  });
});

describe("the execution-id header the child tags its requests with", () => {
  it("is a lowercase custom header name", () => {
    expect(EXECUTION_ID_HEADER).toBe("x-agent-runtime-execution-id");
  });
});

describe("the shim entry's own boundary: listen address and proxy variables", () => {
  it("requires the port the supervisor authored — never the run API's default — and a loopback host", () => {
    expect(parseLoopbackListenAddress({ AGENT_RUNTIME_LOOPBACK_PORT: "41234" })).toEqual({
      host: "127.0.0.1",
      port: 41234,
    });
    expect(() => parseLoopbackListenAddress({})).toThrow(/AGENT_RUNTIME_LOOPBACK_PORT/);
    expect(() => parseLoopbackListenAddress({ AGENT_RUNTIME_LOOPBACK_PORT: "" })).toThrow(
      /AGENT_RUNTIME_LOOPBACK_PORT/,
    );
    expect(() =>
      parseLoopbackListenAddress({
        AGENT_RUNTIME_LOOPBACK_PORT: "8131",
        AGENT_RUNTIME_LOOPBACK_HOST: "0.0.0.0",
      }),
    ).toThrow(ConfigError);
  });

  it("reads the port as decimal digits only — Number() would admit hex, floats and signs", () => {
    for (const raw of ["0x1FCB", "8131.0", "+8131", " 8131", "8131 ", "1e3", "0", "65536"]) {
      expect(() => parseLoopbackListenAddress({ AGENT_RUNTIME_LOOPBACK_PORT: raw })).toThrow(
        /AGENT_RUNTIME_LOOPBACK_PORT/,
      );
    }
    expect(parseLoopbackListenAddress({ AGENT_RUNTIME_LOOPBACK_PORT: "08131" }).port).toBe(8131);
  });

  it("the Anthropic key holder needs its key, and nothing defaults it", () => {
    expect(parseAnthropicProxyKey({ ANTHROPIC_API_KEY: ANTHROPIC_KEY })).toBe(ANTHROPIC_KEY);
    expect(() => parseAnthropicProxyKey({})).toThrow(/ANTHROPIC_API_KEY/);
  });

  it("refuses a key that cannot ride a header — a trailing newline from a secret file — without echoing it", () => {
    for (const key of [`${ANTHROPIC_KEY}\n`, `${ANTHROPIC_KEY}\r\n`, `sk ant`, `${ANTHROPIC_KEY}\u007f`]) {
      const message = refusal({ ...ANTHROPIC_HOST, ANTHROPIC_API_KEY: key });
      expect(message).toContain("ANTHROPIC_API_KEY");
      expect(message).not.toContain(key.trim());
      expect(() => parseAnthropicProxyKey({ ANTHROPIC_API_KEY: key })).toThrow(/ANTHROPIC_API_KEY/);
    }
  });

  it("refuses any proxy variable in a loopback process's environment, naming the process", () => {
    expect(() =>
      assertNoProxyVariables({ PATH: "/usr/bin", NO_PROXY: "localhost" }, "loopback process"),
    ).not.toThrow();
    for (const name of ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"]) {
      expect(() => assertNoProxyVariables({ [name]: "http://proxy.corp:3128" }, "loopback process")).toThrow(
        new RegExp(name),
      );
    }
    expect(() =>
      assertNoProxyVariables({ HTTPS_PROXY: "http://proxy.corp:3128" }, "Anthropic key holder"),
    ).toThrow(/Anthropic key holder/);
  });
});

describe("URLs that would carry a credential or point the identity elsewhere refuse boot", () => {
  it("refuses userinfo in the gateway, token-service and Foundry URLs", () => {
    expect(
      refusal({
        ...FOUNDRY_HOST,
        ANTHROPIC_FOUNDRY_RESOURCE: undefined,
        ANTHROPIC_FOUNDRY_BASE_URL: "https://user:pw@inia-foundry.services.ai.azure.com/anthropic",
      }),
    ).toContain("ANTHROPIC_FOUNDRY_BASE_URL");
  });

  it("pins the Foundry resource to a hostname label — the CLI composes the endpoint host from it", () => {
    for (const resource of ["evil.example/#", "res.services.ai.azure.com", "res_1", "res name", ""]) {
      expect(refusal({ ...FOUNDRY_HOST, ANTHROPIC_FOUNDRY_RESOURCE: resource })).toContain(
        "ANTHROPIC_FOUNDRY_RESOURCE",
      );
    }
    expect(() =>
      parseModelProviderConfig({ ...FOUNDRY_HOST, ANTHROPIC_FOUNDRY_RESOURCE: "inia-foundry-01" }),
    ).not.toThrow();
  });

  it("refuses the Azure chain's secret carriers and endpoint redirectors beside the Foundry mode", () => {
    for (const name of [
      "IDENTITY_HEADER",
      "IDENTITY_ENDPOINT",
      "MSI_SECRET",
      "MSI_ENDPOINT",
      "IMDS_ENDPOINT",
      "AZURE_AUTHORITY_HOST",
      "AZURE_POD_IDENTITY_AUTHORITY_HOST",
    ]) {
      expect(refusal({ ...FOUNDRY_HOST, [name]: "x" })).toContain(name);
    }
  });
});
