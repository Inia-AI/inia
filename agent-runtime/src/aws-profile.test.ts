import { describe, expect, it } from "bun:test";
import { mkdtempSync, readFileSync, rmSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  AWS_PROFILE_NAME,
  applyAssumeRoleProfile,
  renderAssumeRoleProfile,
  toRoleSessionName,
  withoutStaticCredentials,
} from "./aws-profile.js";

const ROLE_ARN = "arn:aws:iam::000000000000:role/example_agent_runtime_role";

describe("renderAssumeRoleProfile", () => {
  it("points the profile at the role and sources its credentials from the instance", () => {
    const ini = renderAssumeRoleProfile({
      roleArn: ROLE_ARN,
      region: "us-west-2",
      sessionName: "run-1",
    });
    expect(ini).toContain(`[profile ${AWS_PROFILE_NAME}]`);
    expect(ini).toContain(`role_arn = ${ROLE_ARN}`);
    expect(ini).toContain("credential_source = Ec2InstanceMetadata");
    expect(ini).toContain("role_session_name = run-1");
    expect(ini).toContain("region = us-west-2");
  });

  it("omits the region line when none is configured, rather than emitting an empty one", () => {
    const ini = renderAssumeRoleProfile({ roleArn: ROLE_ARN, sessionName: "run-1" });
    expect(ini).not.toContain("region =");
  });
});

describe("toRoleSessionName", () => {
  it("keeps an already-valid execution id so CloudTrail shows the run", () => {
    expect(toRoleSessionName("exec-42")).toBe("exec-42");
  });

  it("truncates to the 64-character limit", () => {
    expect(toRoleSessionName("e".repeat(200))).toHaveLength(64);
  });

  it("pads a too-short id up to the two-character minimum", () => {
    expect(toRoleSessionName("x").length).toBeGreaterThanOrEqual(2);
  });
});

describe("withoutStaticCredentials", () => {
  it("drops static keys so the assumed-role profile is not outranked by them", () => {
    const env = withoutStaticCredentials({
      PATH: "/usr/bin",
      AWS_REGION: "us-west-2",
      AWS_ACCESS_KEY_ID: "AKIA",
      AWS_SECRET_ACCESS_KEY: "secret",
      AWS_SESSION_TOKEN: "token",
    });
    expect(env["AWS_ACCESS_KEY_ID"]).toBeUndefined();
    expect(env["AWS_SECRET_ACCESS_KEY"]).toBeUndefined();
    expect(env["AWS_SESSION_TOKEN"]).toBeUndefined();
  });

  it("drops container-credential URIs, which outrank the metadata service in the chain", () => {
    const env = withoutStaticCredentials({
      PATH: "/usr/bin",
      AWS_CONTAINER_CREDENTIALS_RELATIVE_URI: "/creds",
      AWS_CONTAINER_CREDENTIALS_FULL_URI: "http://127.0.0.1:9/creds",
    });
    expect(env["AWS_CONTAINER_CREDENTIALS_RELATIVE_URI"]).toBeUndefined();
    expect(env["AWS_CONTAINER_CREDENTIALS_FULL_URI"]).toBeUndefined();
    expect(env["PATH"]).toBe("/usr/bin");
  });

  it("leaves everything else untouched", () => {
    const env = withoutStaticCredentials({ PATH: "/usr/bin", AWS_REGION: "us-west-2" });
    expect(env).toEqual({ PATH: "/usr/bin", AWS_REGION: "us-west-2" });
  });
});

describe("applyAssumeRoleProfile", () => {
  function inHome(env: Record<string, string>): {
    result: Record<string, string>;
    home: string;
  } {
    const home = mkdtempSync(join(tmpdir(), "assume-role-"));
    const result = applyAssumeRoleProfile({
      env,
      homeDir: home,
      roleArn: ROLE_ARN,
      executionId: "exec-9",
    });
    return { result, home };
  }

  it("writes a private config the SDK will read, and points the env at it", () => {
    const { result, home } = inHome({ PATH: "/usr/bin", AWS_REGION: "us-west-2" });
    try {
      expect(result["AWS_PROFILE"]).toBe(AWS_PROFILE_NAME);
      const configPath = result["AWS_CONFIG_FILE"] ?? "";
      expect(configPath).toBe(join(home, ".aws", "config"));
      const written = readFileSync(configPath, "utf8");
      expect(written).toContain(`role_arn = ${ROLE_ARN}`);
      expect(written).toContain("credential_source = Ec2InstanceMetadata");
      expect(written).toContain("role_session_name = exec-9");
      expect(written).toContain("region = us-west-2");
      expect(statSync(configPath).mode & 0o777).toBe(0o600);
    } finally {
      rmSync(home, { recursive: true, force: true });
    }
  });

  it("removes host credentials that would outrank the assumed role", () => {
    const { result, home } = inHome({
      PATH: "/usr/bin",
      AWS_ACCESS_KEY_ID: "AKIA",
      AWS_SECRET_ACCESS_KEY: "secret",
      AWS_SESSION_TOKEN: "token",
    });
    try {
      expect(result["AWS_ACCESS_KEY_ID"]).toBeUndefined();
      expect(result["AWS_SECRET_ACCESS_KEY"]).toBeUndefined();
      expect(result["AWS_SESSION_TOKEN"]).toBeUndefined();
      expect(result["PATH"]).toBe("/usr/bin");
    } finally {
      rmSync(home, { recursive: true, force: true });
    }
  });
});
