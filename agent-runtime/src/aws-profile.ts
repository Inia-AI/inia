import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";

export const AWS_PROFILE_NAME = "agent-runtime";

const SESSION_NAME_MAX = 64;

const SESSION_NAME_MIN = 2;

// Container-credential URIs are stripped alongside static keys — both outrank
// a shared-config profile in the AWS chain, not just the AWS_* key trio.
export const STATIC_CREDENTIAL_VARS: readonly string[] = [
  "AWS_ACCESS_KEY_ID",
  "AWS_SECRET_ACCESS_KEY",
  "AWS_SESSION_TOKEN",
  "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI",
  "AWS_CONTAINER_CREDENTIALS_FULL_URI",
];

// EXECUTION_ID_PATTERN's charset is already a subset of what role session
// names accept — only length needs adjusting here.
export function toRoleSessionName(executionId: string): string {
  return executionId.slice(0, SESSION_NAME_MAX).padEnd(SESSION_NAME_MIN, "-");
}

function renderProfile(settings: readonly string[], region: string | undefined): string {
  const lines = [`[profile ${AWS_PROFILE_NAME}]`, ...settings];
  if (region !== undefined && region !== "") {
    lines.push(`region = ${region}`);
  }
  return `${lines.join("\n")}\n`;
}

export function renderAssumeRoleProfile(input: {
  roleArn: string;
  region?: string | undefined;
  sessionName: string;
}): string {
  return renderProfile(
    [
      `role_arn = ${input.roleArn}`,
      "credential_source = Ec2InstanceMetadata",
      `role_session_name = ${input.sessionName}`,
    ],
    input.region,
  );
}

export function renderCredentialsFileProfile(input: {
  credentialsFile: string;
  region?: string | undefined;
}): string {
  return renderProfile([`credential_process = /bin/cat ${input.credentialsFile}`], input.region);
}

export function withoutStaticCredentials(env: Record<string, string>): Record<string, string> {
  const filtered: Record<string, string> = {};
  for (const [name, value] of Object.entries(env)) {
    if (!STATIC_CREDENTIAL_VARS.includes(name)) {
      filtered[name] = value;
    }
  }
  return filtered;
}

// The host keeps the file current with the model role's short-lived credentials; the metadata
// service, which would hand out the host's own role, is never asked.
export function applyCredentialsFileProfile(input: {
  env: Record<string, string>;
  homeDir: string;
  credentialsFile: string;
}): Record<string, string> {
  const configPath = writePrivateConfig(
    input.homeDir,
    renderCredentialsFileProfile({ credentialsFile: input.credentialsFile, region: input.env["AWS_REGION"] }),
  );
  return {
    ...withoutStaticCredentials(input.env),
    AWS_CONFIG_FILE: configPath,
    AWS_PROFILE: AWS_PROFILE_NAME,
    AWS_EC2_METADATA_DISABLED: "true",
  };
}

function writePrivateConfig(homeDir: string, profile: string): string {
  const awsDir = join(homeDir, ".aws");
  mkdirSync(awsDir, { recursive: true, mode: 0o700 });
  const configPath = join(awsDir, "config");
  writeFileSync(configPath, profile, { mode: 0o600 });
  return configPath;
}

export function applyAssumeRoleProfile(input: {
  env: Record<string, string>;
  homeDir: string;
  roleArn: string;
  executionId: string;
}): Record<string, string> {
  const configPath = writePrivateConfig(
    input.homeDir,
    renderAssumeRoleProfile({
      roleArn: input.roleArn,
      region: input.env["AWS_REGION"],
      sessionName: toRoleSessionName(input.executionId),
    }),
  );
  return {
    ...withoutStaticCredentials(input.env),
    AWS_CONFIG_FILE: configPath,
    AWS_PROFILE: AWS_PROFILE_NAME,
  };
}
