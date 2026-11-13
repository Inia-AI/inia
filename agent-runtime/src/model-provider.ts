import { type AgentIdentityConfig, parseAgentIdentity } from "./agent-identity.js";
import { STATIC_CREDENTIAL_VARS } from "./aws-profile.js";
import { ConfigError, nonEmpty } from "./config-error.js";

export const MODEL_PROVIDER_ENV_VAR = "AGENT_RUNTIME_MODEL_PROVIDER";
export const MODEL_PROVIDERS = ["bedrock", "foundry", "anthropic"] as const;
export type ModelProvider = (typeof MODEL_PROVIDERS)[number];

// The address of the loopback model process — the gateway shim or the Anthropic key holder; the
// modes are exclusive — is code-owned, never a deploy-time knob: a configurable URL is a URL a
// deploy could point anywhere. Loopback beside the run API port.
export const LOOPBACK_HOST = "127.0.0.1";
export const LOOPBACK_PORT = 8131;
export const LOOPBACK_BASE_URL = `http://${LOOPBACK_HOST}:${LOOPBACK_PORT}`;
export const EXECUTION_ID_HEADER = "x-agent-runtime-execution-id";
// How the supervisor tells a loopback process where to listen; tests use a free port, deployment
// the constant above. Both are validated back to loopback by the process's entry point.
export const LOOPBACK_HOST_ENV_VAR = "AGENT_RUNTIME_LOOPBACK_HOST";
export const LOOPBACK_PORT_ENV_VAR = "AGENT_RUNTIME_LOOPBACK_PORT";

// The anthropic mode's key holder (ADR 0151) holds the plain API key alone; the run child reaches
// the Anthropic API through it with a placeholder credential.
export const ANTHROPIC_PROXY_PLACEHOLDER_API_KEY = "agent-runtime-anthropic-proxy";
export const ANTHROPIC_API_URL = "https://api.anthropic.com";
export const ANTHROPIC_API_KEY_ENV_VAR = "ANTHROPIC_API_KEY";

export type AnthropicProxyConfig = { apiKey: string };

export type ModelProviderConfig =
  | { provider: "bedrock"; identity: AgentIdentityConfig }
  | { provider: "foundry" }
  | { provider: "anthropic"; proxy: AnthropicProxyConfig };

const BEDROCK_SUBPROCESS_ENV_ALLOWLIST: readonly string[] = [
  "PATH",
  "HOME",
  "NODE_PATH",
  "CLAUDE_CONFIG_DIR",
  "CLAUDE_CODE_USE_BEDROCK",
  "CLAUDE_CODE_DISABLE_AUTO_MEMORY",
  "ANTHROPIC_MODEL",
  "ANTHROPIC_DEFAULT_HAIKU_MODEL",
  "ENABLE_PROMPT_CACHING_1H",
  "AWS_REGION",
  "AWS_DEFAULT_REGION",
  "AWS_EC2_METADATA_SERVICE_ENDPOINT",
  "AWS_ACCESS_KEY_ID",
  "AWS_SECRET_ACCESS_KEY",
  "AWS_SESSION_TOKEN",
  "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI",
  "AWS_CONTAINER_CREDENTIALS_FULL_URI",
];

// AZURE_CLIENT_ID selects a user-assigned managed identity in the Azure default chain; it names
// an identity and carries no secret. Every secret-bearing AZURE_* variable is refused at boot.
const FOUNDRY_SUBPROCESS_ENV_ALLOWLIST: readonly string[] = [
  "PATH",
  "HOME",
  "NODE_PATH",
  "CLAUDE_CONFIG_DIR",
  "CLAUDE_CODE_USE_FOUNDRY",
  "CLAUDE_CODE_DISABLE_AUTO_MEMORY",
  "ANTHROPIC_MODEL",
  "ANTHROPIC_DEFAULT_HAIKU_MODEL",
  "ENABLE_PROMPT_CACHING_1H",
  "ANTHROPIC_FOUNDRY_RESOURCE",
  "ANTHROPIC_FOUNDRY_BASE_URL",
  "AZURE_CLIENT_ID",
];

// Authored in every non-Bedrock mode: an Azure VM, a gateway host and any other
// machine alike send nothing beyond the model calls the mode routes.
const NONESSENTIAL_TRAFFIC_SWITCH: Readonly<Record<string, string>> = {
  CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
};

// Authored in every mode. Without it the CLI caps each answer at 32,000 output tokens, thinking
// included, for a model id it cannot resolve; a cut answer resumes into a Bedrock refusal (ADR 0148).
export const MAX_OUTPUT_TOKENS = "64000";
const OUTPUT_TOKEN_CEILING: Readonly<Record<string, string>> = {
  CLAUDE_CODE_MAX_OUTPUT_TOKENS: MAX_OUTPUT_TOKENS,
};

// Authored in every mode. `tools` is a run's whole surface, so no tool is ever deferred; without
// it CLI 2.1.308 adds a deferred placeholder tool to the request on newer models outside Bedrock
// mode, a shape the gateway was never measured with (ADR 0154).
export const DEFERRED_TOOL_LOADING_OFF: Readonly<Record<string, string>> = {
  ENABLE_TOOL_SEARCH: "false",
};

const ANTHROPIC_SUBPROCESS_ENV_ALLOWLIST: readonly string[] = [
  "PATH",
  "HOME",
  "NODE_PATH",
  "CLAUDE_CONFIG_DIR",
  "CLAUDE_CODE_DISABLE_AUTO_MEMORY",
  "ANTHROPIC_MODEL",
  "ANTHROPIC_DEFAULT_HAIKU_MODEL",
  "ENABLE_PROMPT_CACHING_1H",
];

const ANTHROPIC_AUTHORED_ENV: Readonly<Record<string, string>> = {
  ANTHROPIC_BASE_URL: LOOPBACK_BASE_URL,
  ANTHROPIC_API_KEY: ANTHROPIC_PROXY_PLACEHOLDER_API_KEY,
  ...NONESSENTIAL_TRAFFIC_SWITCH,
};

const AWS_MODE_VARS: readonly string[] = [
  "CLAUDE_CODE_USE_BEDROCK",
  "AGENT_RUNTIME_ROLE_ARN",
  "AGENT_RUNTIME_METADATA_CREDENTIALS",
  "AGENT_RUNTIME_AMBIENT_CREDENTIALS",
  "AGENT_RUNTIME_CREDENTIALS_FILE",
  "AWS_EC2_METADATA_SERVICE_ENDPOINT",
  ...STATIC_CREDENTIAL_VARS,
];

export const FOUNDRY_TOKEN_ENDPOINT_ENV_VAR = "AGENT_RUNTIME_FOUNDRY_TOKEN_ENDPOINT";

// The host endpoint serves the runtime identity's Foundry token and nothing else, so the header the
// Azure credential chain's App Service source requires guards nothing and is a fixed value.
export const FOUNDRY_TOKEN_ENDPOINT_HEADER = "inia-agent-runtime";

const FOUNDRY_MODE_VARS: readonly string[] = [
  "CLAUDE_CODE_USE_FOUNDRY",
  "ANTHROPIC_FOUNDRY_RESOURCE",
  "ANTHROPIC_FOUNDRY_BASE_URL",
  FOUNDRY_TOKEN_ENDPOINT_ENV_VAR,
];

const FOUNDRY_SECRET_VARS: readonly string[] = [
  "ANTHROPIC_FOUNDRY_API_KEY",
  "ANTHROPIC_FOUNDRY_AUTH_TOKEN",
  "CLAUDE_CODE_SKIP_FOUNDRY_AUTH",
];

// Secret carriers and endpoint redirectors of the bundled Azure credential chain (measured in the
// CLI binary): a value here would either hand the chain a secret or point it away from the VM's
// own identity endpoint.
const AZURE_SECRET_VARS: readonly string[] = [
  "AZURE_CLIENT_SECRET",
  "AZURE_CLIENT_CERTIFICATE_PATH",
  "AZURE_CLIENT_CERTIFICATE_PASSWORD",
  "AZURE_USERNAME",
  "AZURE_PASSWORD",
  "AZURE_FEDERATED_TOKEN_FILE",
  "AZURE_AUTHORITY_HOST",
  "AZURE_POD_IDENTITY_AUTHORITY_HOST",
  "IDENTITY_ENDPOINT",
  "IDENTITY_HEADER",
  "IMDS_ENDPOINT",
  "MSI_ENDPOINT",
  "MSI_SECRET",
];
const FOUNDRY_RESOURCE_PATTERN = /^[A-Za-z0-9-]+$/;

// Authored by the supervisor into a loopback process's own environment; never a deploy setting.
const LOOPBACK_LISTEN_VARS: readonly string[] = [LOOPBACK_HOST_ENV_VAR, LOOPBACK_PORT_ENV_VAR];

const DIRECT_API_REDIRECT_VARS: readonly string[] = [
  "ANTHROPIC_BASE_URL",
  "ANTHROPIC_AUTH_TOKEN",
  "ANTHROPIC_CUSTOM_HEADERS",
];

const DIRECT_API_VARS: readonly string[] = [...DIRECT_API_REDIRECT_VARS, ANTHROPIC_API_KEY_ENV_VAR];

const LOOPBACK_HOSTNAMES: readonly string[] = ["127.0.0.1", "localhost", "[::1]"];
// A Foundry deployment name and an Anthropic API model id share this shape; neither carries the
// `:` version suffix or the `arn:` prefix a Bedrock id can.
const BARE_MODEL_NAME_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;

function isBareNonBedrockModelName(model: string): boolean {
  return BARE_MODEL_NAME_PATTERN.test(model) && !BEDROCK_MODEL_ID_PATTERN.test(model);
}
const BEDROCK_MODEL_ID_PATTERN = /^(?:[a-z]{2,6}\.)?anthropic\./;
const DECIMAL_PORT_PATTERN = /^[0-9]{1,5}$/;

type Env = Record<string, string | undefined>;

function allowlisted(source: Env, names: readonly string[]): Record<string, string> {
  const env: Record<string, string> = {};
  for (const name of names) {
    const value = source[name];
    if (value !== undefined) {
      env[name] = value;
    }
  }
  return env;
}

export function buildSubprocessEnv(source: Env): Record<string, string> {
  return allowlisted(source, BEDROCK_SUBPROCESS_ENV_ALLOWLIST);
}

// The credential chain's App Service source fetches and refreshes the token from this endpoint, in
// place of the metadata service the host closes to the container (ADR 0166).
function foundryTokenEndpointEnv(source: Env): Record<string, string> {
  const endpoint = nonEmpty(source[FOUNDRY_TOKEN_ENDPOINT_ENV_VAR]);
  return endpoint === undefined
    ? {}
    : { IDENTITY_ENDPOINT: endpoint, IDENTITY_HEADER: FOUNDRY_TOKEN_ENDPOINT_HEADER };
}

export function buildProviderSubprocessEnv(provider: ModelProvider, source: Env): Record<string, string> {
  switch (provider) {
    case "bedrock":
      return { ...buildSubprocessEnv(source), ...OUTPUT_TOKEN_CEILING, ...DEFERRED_TOOL_LOADING_OFF };
    case "foundry":
      return {
        ...allowlisted(source, FOUNDRY_SUBPROCESS_ENV_ALLOWLIST),
        ...foundryTokenEndpointEnv(source),
        ...NONESSENTIAL_TRAFFIC_SWITCH,
        ...OUTPUT_TOKEN_CEILING,
        ...DEFERRED_TOOL_LOADING_OFF,
      };
    case "anthropic":
      return {
        ...allowlisted(source, ANTHROPIC_SUBPROCESS_ENV_ALLOWLIST),
        ...ANTHROPIC_AUTHORED_ENV,
        ...OUTPUT_TOKEN_CEILING,
        ...DEFERRED_TOOL_LOADING_OFF,
      };
  }
}

export function parseModelProvider(raw: string | undefined): ModelProvider {
  const value = nonEmpty(raw);
  if (value === undefined) {
    return "bedrock";
  }
  const provider = MODEL_PROVIDERS.find((candidate) => candidate === value);
  if (provider === undefined) {
    throw new ConfigError(
      `${MODEL_PROVIDER_ENV_VAR} must be one of ${MODEL_PROVIDERS.join(", ")}, got "${raw}"`,
    );
  }
  return provider;
}

export function assertBedrockPinned(env: Env): string {
  if (env["CLAUDE_CODE_USE_BEDROCK"] !== "1") {
    throw new ConfigError("CLAUDE_CODE_USE_BEDROCK must be set to 1 — this service is Bedrock-only");
  }
  const model = env["ANTHROPIC_MODEL"];
  if (model === undefined || model === "") {
    throw new ConfigError(
      "ANTHROPIC_MODEL must pin the model — an unpinned SDK falls back to expensive defaults",
    );
  }
  return model;
}

export function assertLoopbackUrl(raw: string, label: string): void {
  let parsed: URL;
  try {
    parsed = new URL(raw);
  } catch {
    throw new ConfigError(`${label} must be a URL, got "${raw}"`);
  }
  if (parsed.protocol !== "http:" || !LOOPBACK_HOSTNAMES.includes(parsed.hostname)) {
    throw new ConfigError(
      `${label} must be an http URL on loopback (${LOOPBACK_HOSTNAMES.join(", ")}), got "${raw}"`,
    );
  }
}

function refuseForeign(env: Env, provider: ModelProvider, names: readonly string[], why: string): void {
  for (const name of names) {
    if (nonEmpty(env[name]) !== undefined) {
      throw new ConfigError(`${name} is set while ${MODEL_PROVIDER_ENV_VAR} is "${provider}": ${why}`);
    }
  }
}

function requireSetting(env: Env, name: string, provider: ModelProvider): string {
  const value = nonEmpty(env[name]);
  if (value === undefined) {
    throw new ConfigError(`${name} must be set when ${MODEL_PROVIDER_ENV_VAR} is "${provider}"`);
  }
  return value;
}

function assertHttpUrl(raw: string, name: string, allowedProtocols: readonly string[]): URL {
  let parsed: URL;
  try {
    parsed = new URL(raw);
  } catch {
    throw new ConfigError(`${name} must be a URL, got "${raw}"`);
  }
  if (!allowedProtocols.includes(parsed.protocol)) {
    throw new ConfigError(`${name} must be a ${allowedProtocols.join(" or ")} URL, got "${raw}"`);
  }
  // A credential in a URL would ride every error message and log line that names the URL.
  if (parsed.username !== "" || parsed.password !== "") {
    throw new ConfigError(`${name} must not carry userinfo`);
  }
  return parsed;
}

type ModelIdShape = { accepts: (model: string) => boolean; expected: string };

// A Record, not a switch: a new mode that forgets its row fails to compile, where a void switch
// would accept any model id in silence.
const MODEL_ID_SHAPES: Readonly<Record<ModelProvider, ModelIdShape | undefined>> = {
  bedrock: undefined,
  foundry: {
    accepts: isBareNonBedrockModelName,
    expected: "a Foundry deployment name (such as claude-sonnet-5)",
  },
  anthropic: {
    accepts: isBareNonBedrockModelName,
    expected: "an Anthropic API model id such as claude-sonnet-5-5, not a Bedrock id or ARN,",
  },
};

export function assertModelIdForProvider(provider: ModelProvider, model: string, label: string): void {
  const shape = MODEL_ID_SHAPES[provider];
  if (shape !== undefined && !shape.accepts(model)) {
    throw new ConfigError(
      `${label} must be ${shape.expected} when ${MODEL_PROVIDER_ENV_VAR} is "${provider}", got "${model}"`,
    );
  }
}

export function pinnedModelOf(provider: ModelProvider, env: Env): string {
  if (provider === "bedrock") {
    return assertBedrockPinned(env);
  }
  const model = requireSetting(env, "ANTHROPIC_MODEL", provider);
  assertModelIdForProvider(provider, model, "ANTHROPIC_MODEL");
  return model;
}

function parseFoundry(env: Env): ModelProviderConfig {
  const provider = "foundry";
  refuseForeign(env, provider, AWS_MODE_VARS, "Foundry runs on the managed identity, never an AWS identity");
  refuseForeign(
    env,
    provider,
    FOUNDRY_SECRET_VARS,
    "authentication is the managed identity, never a key or token",
  );
  refuseForeign(
    env,
    provider,
    AZURE_SECRET_VARS,
    "the identity chain must resolve the managed identity, never a secret",
  );
  refuseForeign(
    env,
    provider,
    DIRECT_API_VARS,
    "a direct-API variable would redirect the CLI away from Foundry",
  );
  if (env["CLAUDE_CODE_USE_FOUNDRY"] !== "1") {
    throw new ConfigError(
      `CLAUDE_CODE_USE_FOUNDRY must be set to 1 when ${MODEL_PROVIDER_ENV_VAR} is "foundry"`,
    );
  }
  const resource = nonEmpty(env["ANTHROPIC_FOUNDRY_RESOURCE"]);
  const baseUrl = nonEmpty(env["ANTHROPIC_FOUNDRY_BASE_URL"]);
  if ((resource === undefined) === (baseUrl === undefined)) {
    throw new ConfigError(
      "exactly one of ANTHROPIC_FOUNDRY_RESOURCE or ANTHROPIC_FOUNDRY_BASE_URL must name the Foundry endpoint " +
        `when ${MODEL_PROVIDER_ENV_VAR} is "foundry"`,
    );
  }
  if (baseUrl !== undefined) {
    assertHttpUrl(baseUrl, "ANTHROPIC_FOUNDRY_BASE_URL", ["https:"]);
  }
  // The CLI composes `https://<resource>.services.ai.azure.com` from it (measured in the binary):
  // anything but a hostname label is a host the managed-identity token would be sent to.
  if (resource !== undefined && !FOUNDRY_RESOURCE_PATTERN.test(resource)) {
    throw new ConfigError(
      `ANTHROPIC_FOUNDRY_RESOURCE must be a Foundry resource name (letters, digits, hyphens), got "${resource}"`,
    );
  }
  const tokenEndpoint = nonEmpty(env[FOUNDRY_TOKEN_ENDPOINT_ENV_VAR]);
  if (tokenEndpoint !== undefined) {
    assertHttpUrl(tokenEndpoint, FOUNDRY_TOKEN_ENDPOINT_ENV_VAR, ["http:"]);
  }
  pinnedModelOf(provider, env);
  return { provider };
}

const MIN_PORT = 1;
const MAX_PORT = 65535;

// The supervisor authors both; the entry point re-validates them to loopback, and a missing port
// refuses rather than falling back to any default — the run API's port is exactly the wrong one.
export function parseLoopbackListenAddress(env: Env): { host: string; port: number } {
  const host = nonEmpty(env[LOOPBACK_HOST_ENV_VAR]) ?? LOOPBACK_HOST;
  const rawPort = nonEmpty(env[LOOPBACK_PORT_ENV_VAR]);
  if (rawPort === undefined) {
    throw new ConfigError(`${LOOPBACK_PORT_ENV_VAR} must be set by the supervisor`);
  }
  const port = DECIMAL_PORT_PATTERN.test(rawPort) ? Number(rawPort) : Number.NaN;
  if (!Number.isInteger(port) || port < MIN_PORT || port > MAX_PORT) {
    throw new ConfigError(`${LOOPBACK_PORT_ENV_VAR} must be a TCP port, got "${rawPort}"`);
  }
  assertLoopbackUrl(`http://${host}:${port}`, "the loopback listen address");
  return { host, port };
}

// The key rides an x-api-key header, and a header value cannot carry whitespace or a control
// character: a trailing newline from a secret file would boot and then fail every model call. The
// refusal never echoes the value.
const API_KEY_PATTERN = /^[\x21-\x7e]+$/;

export function parseAnthropicProxyKey(env: Env): string {
  const apiKey = requireSetting(env, ANTHROPIC_API_KEY_ENV_VAR, "anthropic");
  if (!API_KEY_PATTERN.test(apiKey)) {
    throw new ConfigError(
      `${ANTHROPIC_API_KEY_ENV_VAR} must be printable ASCII with no whitespace or control character — ` +
        "a trailing newline from a secret file is the usual cause",
    );
  }
  return apiKey;
}

// No loopback process uses a proxy — a model gateway and its token service are internal hosts, and
// the key holder reaches the Anthropic API directly — and their envs are authored without these,
// so one present means the authoring was bypassed.
const PROXY_ENV_VARS: readonly string[] = [
  "HTTP_PROXY",
  "HTTPS_PROXY",
  "ALL_PROXY",
  "http_proxy",
  "https_proxy",
  "all_proxy",
];

export function assertNoProxyVariables(env: Env, processLabel: string): void {
  for (const name of PROXY_ENV_VARS) {
    if (nonEmpty(env[name]) !== undefined) {
      throw new ConfigError(`${name} is set in the ${processLabel}'s environment; it never uses a proxy`);
    }
  }
}

function parseAnthropic(env: Env): ModelProviderConfig {
  const provider = "anthropic";
  refuseForeign(env, provider, AWS_MODE_VARS, "the anthropic mode holds no AWS identity");
  refuseForeign(env, provider, FOUNDRY_MODE_VARS, "the anthropic mode holds no Foundry endpoint");
  refuseForeign(env, provider, FOUNDRY_SECRET_VARS, "the anthropic mode holds no Foundry credential");
  refuseForeign(env, provider, AZURE_SECRET_VARS, "the anthropic mode holds no Azure credential");
  refuseForeign(
    env,
    provider,
    DIRECT_API_REDIRECT_VARS,
    `the runtime authors the child's Anthropic variables itself (the key holder at ${LOOPBACK_BASE_URL})`,
  );
  assertLoopbackUrl(LOOPBACK_BASE_URL, "the Anthropic key holder address");
  const apiKey = parseAnthropicProxyKey(env);
  pinnedModelOf(provider, env);
  return { provider, proxy: { apiKey } };
}

function parseBedrock(env: Env): ModelProviderConfig {
  const provider = "bedrock";
  refuseForeign(
    env,
    provider,
    FOUNDRY_MODE_VARS,
    "a Foundry variable without the Foundry mode is a misconfiguration",
  );
  refuseForeign(env, provider, FOUNDRY_SECRET_VARS, "a Foundry credential is never accepted");
  refuseForeign(
    env,
    provider,
    DIRECT_API_VARS,
    "a direct-API variable would redirect the CLI away from Bedrock",
  );
  assertBedrockPinned(env);
  return {
    provider,
    identity: parseAgentIdentity({
      roleArn: env["AGENT_RUNTIME_ROLE_ARN"],
      ambientFlag: env["AGENT_RUNTIME_AMBIENT_CREDENTIALS"],
      metadataFlag: env["AGENT_RUNTIME_METADATA_CREDENTIALS"],
      metadataEndpoint: env["AWS_EC2_METADATA_SERVICE_ENDPOINT"],
      credentialsFile: env["AGENT_RUNTIME_CREDENTIALS_FILE"],
    }),
  };
}

export function parseModelProviderConfig(env: Env): ModelProviderConfig {
  const provider = parseModelProvider(env[MODEL_PROVIDER_ENV_VAR]);
  refuseForeign(env, provider, LOOPBACK_LISTEN_VARS, "the loopback process's address is code-owned");
  switch (provider) {
    case "bedrock":
      return parseBedrock(env);
    case "foundry":
      return parseFoundry(env);
    case "anthropic":
      return parseAnthropic(env);
  }
}
