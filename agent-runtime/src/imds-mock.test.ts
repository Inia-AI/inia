import { describe, expect, it } from "bun:test";
import {
  createImdsMockHandler,
  IMDS_CREDENTIALS_PATH,
  IMDS_MAX_ISSUED_TOKENS,
  IMDS_ROLE_NAME,
  IMDS_TOKEN_HEADER,
  IMDS_TOKEN_PATH,
  IMDS_TOKEN_TTL_HEADER,
  readImdsMockCredentials,
} from "./imds-mock.js";

const CREDENTIALS = {
  accessKeyId: "AKIAEXAMPLE",
  secretAccessKey: "example-secret",
  sessionToken: "example-session-token",
  expiration: "2026-08-06T18:34:32+00:00",
};

// injected clock, not wall clock — CREDENTIALS.expiration is fixed and would start failing once it's past
const BEFORE_EXPIRATION = new Date("2026-08-06T12:00:00Z");

function newHandler(overrides: Parameters<typeof createImdsMockHandler>[0] = {}) {
  return createImdsMockHandler({
    credentials: CREDENTIALS,
    now: () => BEFORE_EXPIRATION,
    ...overrides,
  });
}

function issueToken(handler: ReturnType<typeof createImdsMockHandler>): string {
  const response = handler({
    method: "PUT",
    path: IMDS_TOKEN_PATH,
    headers: { [IMDS_TOKEN_TTL_HEADER]: "21600" },
  });
  expect(response.status).toBe(200);
  return response.body;
}

describe("createImdsMockHandler", () => {
  it("issues a session token on a PUT carrying the ttl header", () => {
    const handler = newHandler();
    const token = issueToken(handler);
    expect(token.length).toBeGreaterThan(0);
  });

  it("refuses a token PUT without the ttl header", () => {
    const handler = newHandler();
    const response = handler({ method: "PUT", path: IMDS_TOKEN_PATH, headers: {} });
    expect(response.status).toBe(400);
  });

  it("refuses a token PUT whose ttl is not a positive integer within the IMDS ceiling", () => {
    const handler = newHandler();
    for (const ttl of ["0", "-1", "21601", "abc", ""]) {
      const response = handler({
        method: "PUT",
        path: IMDS_TOKEN_PATH,
        headers: { [IMDS_TOKEN_TTL_HEADER]: ttl },
      });
      expect(response.status).toBe(400);
    }
  });

  it("refuses metadata reads without a token, so clients must use the IMDSv2 flow", () => {
    const handler = newHandler();
    const response = handler({ method: "GET", path: IMDS_CREDENTIALS_PATH, headers: {} });
    expect(response.status).toBe(401);
  });

  it("refuses a token once its requested ttl has elapsed", () => {
    let nowMs = Date.parse("2026-08-06T12:00:00Z");
    const handler = newHandler({ now: () => new Date(nowMs) });
    const issued = handler({
      method: "PUT",
      path: IMDS_TOKEN_PATH,
      headers: { [IMDS_TOKEN_TTL_HEADER]: "60" },
    });
    expect(issued.status).toBe(200);
    nowMs += 61_000;
    const response = handler({
      method: "GET",
      path: IMDS_CREDENTIALS_PATH,
      headers: { [IMDS_TOKEN_HEADER]: issued.body },
    });
    expect(response.status).toBe(401);
  });

  it("caps the issued-token set, expiring the oldest token first", () => {
    let counter = 0;
    const handler = newHandler({ issueToken: () => `token-${counter++}` });
    const first = issueToken(handler);
    for (let i = 0; i <= IMDS_MAX_ISSUED_TOKENS; i += 1) {
      issueToken(handler);
    }
    const response = handler({
      method: "GET",
      path: IMDS_CREDENTIALS_PATH,
      headers: { [IMDS_TOKEN_HEADER]: first },
    });
    expect(response.status).toBe(401);
  });

  it("refuses metadata reads carrying a token it never issued", () => {
    const handler = newHandler();
    const response = handler({
      method: "GET",
      path: IMDS_CREDENTIALS_PATH,
      headers: { [IMDS_TOKEN_HEADER]: "forged" },
    });
    expect(response.status).toBe(401);
  });

  it("lists the role name for a tokened credentials-list read", () => {
    const handler = newHandler();
    const token = issueToken(handler);
    const response = handler({
      method: "GET",
      path: IMDS_CREDENTIALS_PATH,
      headers: { [IMDS_TOKEN_HEADER]: token },
    });
    expect(response.status).toBe(200);
    expect(response.body).toBe(IMDS_ROLE_NAME);
  });

  it("serves the fed credentials as an instance-profile document", () => {
    const handler = newHandler();
    const token = issueToken(handler);
    const response = handler({
      method: "GET",
      path: `${IMDS_CREDENTIALS_PATH}${IMDS_ROLE_NAME}`,
      headers: { [IMDS_TOKEN_HEADER]: token },
    });
    expect(response.status).toBe(200);
    const document = JSON.parse(response.body) as Record<string, unknown>;
    expect(document["Code"]).toBe("Success");
    expect(document["AccessKeyId"]).toBe(CREDENTIALS.accessKeyId);
    expect(document["SecretAccessKey"]).toBe(CREDENTIALS.secretAccessKey);
    expect(document["Token"]).toBe(CREDENTIALS.sessionToken);
    expect(document["Expiration"]).toBe(CREDENTIALS.expiration);
  });

  it("derives an expiration when the fed credentials carry none", () => {
    const now = new Date("2026-08-06T12:00:00Z");
    const handler = newHandler({
      credentials: {
        accessKeyId: CREDENTIALS.accessKeyId,
        secretAccessKey: CREDENTIALS.secretAccessKey,
        sessionToken: CREDENTIALS.sessionToken,
      },
      now: () => now,
    });
    const token = issueToken(handler);
    const response = handler({
      method: "GET",
      path: `${IMDS_CREDENTIALS_PATH}${IMDS_ROLE_NAME}`,
      headers: { [IMDS_TOKEN_HEADER]: token },
    });
    const document = JSON.parse(response.body) as Record<string, unknown>;
    expect(Date.parse(String(document["Expiration"]))).toBeGreaterThan(now.getTime());
  });

  it("refuses to serve credentials that have already expired, so the chain fails at resolution", () => {
    const handler = newHandler({ now: () => new Date("2026-08-06T19:00:00Z") });
    const token = issueToken(handler);
    const response = handler({
      method: "GET",
      path: `${IMDS_CREDENTIALS_PATH}${IMDS_ROLE_NAME}`,
      headers: { [IMDS_TOKEN_HEADER]: token },
    });
    expect(response.status).toBe(503);
  });

  it("answers 503 on credential paths when no credentials were fed", () => {
    const handler = createImdsMockHandler({});
    const token = issueToken(handler);
    for (const path of [IMDS_CREDENTIALS_PATH, `${IMDS_CREDENTIALS_PATH}${IMDS_ROLE_NAME}`]) {
      const response = handler({ method: "GET", path, headers: { [IMDS_TOKEN_HEADER]: token } });
      expect(response.status).toBe(503);
    }
  });

  it("answers 404 for an unknown role and unknown paths", () => {
    const handler = newHandler();
    const token = issueToken(handler);
    for (const path of [`${IMDS_CREDENTIALS_PATH}other-role`, "/latest/meta-data/instance-id", "/"]) {
      const response = handler({ method: "GET", path, headers: { [IMDS_TOKEN_HEADER]: token } });
      expect(response.status).toBe(404);
    }
  });

  it("answers 405 for methods the path does not serve", () => {
    const handler = newHandler();
    const token = issueToken(handler);
    expect(handler({ method: "GET", path: IMDS_TOKEN_PATH, headers: {} }).status).toBe(405);
    expect(
      handler({
        method: "PUT",
        path: IMDS_CREDENTIALS_PATH,
        headers: { [IMDS_TOKEN_HEADER]: token },
      }).status,
    ).toBe(405);
  });

  it("matches token headers case-insensitively, as HTTP servers deliver them", () => {
    const handler = newHandler();
    const issued = handler({
      method: "PUT",
      path: IMDS_TOKEN_PATH,
      headers: { "X-Aws-Ec2-Metadata-Token-Ttl-Seconds": "60" },
    });
    expect(issued.status).toBe(200);
    const response = handler({
      method: "GET",
      path: IMDS_CREDENTIALS_PATH,
      headers: { "X-Aws-Ec2-Metadata-Token": issued.body },
    });
    expect(response.status).toBe(200);
  });
});

describe("readImdsMockCredentials", () => {
  const FED_ENV = {
    AWS_ACCESS_KEY_ID: "AKIAEXAMPLE",
    AWS_SECRET_ACCESS_KEY: "example-secret",
    AWS_SESSION_TOKEN: "example-session-token",
    AWS_CREDENTIAL_EXPIRATION: "2026-08-06T18:34:32+00:00",
  };

  it("reads the fed credential triplet and its expiration", () => {
    expect(readImdsMockCredentials(FED_ENV)).toEqual({
      accessKeyId: "AKIAEXAMPLE",
      secretAccessKey: "example-secret",
      sessionToken: "example-session-token",
      expiration: "2026-08-06T18:34:32+00:00",
    });
  });

  it("omits the expiration when none was fed", () => {
    const { AWS_CREDENTIAL_EXPIRATION: _unused, ...withoutExpiration } = FED_ENV;
    expect(readImdsMockCredentials(withoutExpiration)).toEqual({
      accessKeyId: "AKIAEXAMPLE",
      secretAccessKey: "example-secret",
      sessionToken: "example-session-token",
    });
  });

  it("drops an expiration that does not parse as a date, rather than serving garbage", () => {
    expect(readImdsMockCredentials({ ...FED_ENV, AWS_CREDENTIAL_EXPIRATION: "not-a-date" })).toEqual({
      accessKeyId: "AKIAEXAMPLE",
      secretAccessKey: "example-secret",
      sessionToken: "example-session-token",
    });
  });

  it("returns undefined when any part of the triplet is missing or empty", () => {
    expect(readImdsMockCredentials({})).toBeUndefined();
    expect(readImdsMockCredentials({ ...FED_ENV, AWS_ACCESS_KEY_ID: "" })).toBeUndefined();
    expect(readImdsMockCredentials({ ...FED_ENV, AWS_SESSION_TOKEN: undefined })).toBeUndefined();
  });
});
