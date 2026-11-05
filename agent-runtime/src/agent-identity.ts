import { ConfigError, nonEmpty } from "./config-error.js";

export type AgentIdentityConfig = { roleArn: string } | { ambient: true } | { metadataCredentials: true };

// Validated here rather than beside the call: a metadata-mode identity that was
// never checked for a usable endpoint must not be constructible.
export function parseAgentIdentity(input: {
  roleArn: string | undefined;
  ambientFlag: string | undefined;
  metadataFlag: string | undefined;
  metadataEndpoint?: string | undefined;
}): AgentIdentityConfig {
  const roleArn = nonEmpty(input.roleArn);
  const ambient = input.ambientFlag === "true";
  const metadata = input.metadataFlag === "true";
  const chosenModes = [roleArn !== undefined, ambient, metadata].filter(Boolean).length;
  if (chosenModes !== 1) {
    throw new ConfigError(
      "agent identity is fail-closed: set exactly one of AGENT_RUNTIME_ROLE_ARN, " +
        "AGENT_RUNTIME_METADATA_CREDENTIALS=true, or AGENT_RUNTIME_AMBIENT_CREDENTIALS=true",
    );
  }
  if (roleArn !== undefined) {
    return { roleArn };
  }
  if (metadata) {
    assertMetadataEndpoint(input.metadataEndpoint);
    return { metadataCredentials: true };
  }
  return { ambient: true };
}

// An unset endpoint targets real IMDS (unroutable in dev, a 3s chain timeout);
// an empty one is worse — the SDK treats "" as absent and falls back to real
// IMDS silently — so metadata mode refuses to boot without a usable URL.
function assertMetadataEndpoint(raw: string | undefined): void {
  if (raw === undefined || raw === "") {
    throw new ConfigError(
      "AGENT_RUNTIME_METADATA_CREDENTIALS=true requires AWS_EC2_METADATA_SERVICE_ENDPOINT to name the metadata endpoint",
    );
  }
  let parsed: URL;
  try {
    parsed = new URL(raw);
  } catch {
    throw new ConfigError(`AWS_EC2_METADATA_SERVICE_ENDPOINT must be a URL, got "${raw}"`);
  }
  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    throw new ConfigError(`AWS_EC2_METADATA_SERVICE_ENDPOINT must be an http(s) URL, got "${raw}"`);
  }
}
