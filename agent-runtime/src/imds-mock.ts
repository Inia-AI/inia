import { randomBytes } from "node:crypto";
import { nonEmpty } from "./config-error.js";

export const IMDS_TOKEN_PATH = "/latest/api/token";
export const IMDS_CREDENTIALS_PATH = "/latest/meta-data/iam/security-credentials/";
export const IMDS_TOKEN_HEADER = "x-aws-ec2-metadata-token";
export const IMDS_TOKEN_TTL_HEADER = "x-aws-ec2-metadata-token-ttl-seconds";
export const IMDS_ROLE_NAME = "agent-runtime-dev-session";

const MAX_TOKEN_TTL_SECONDS = 21_600;
const TOKEN_BYTES = 32;
export const IMDS_MAX_ISSUED_TOKENS = 1024;
const MS_PER_SECOND = 1000;
// short on purpose: the mock can't refresh what it serves, so longer would let the SDK cache past real SSO expiry
const FALLBACK_CREDENTIAL_LIFETIME_MS = 15 * 60 * 1000;

export type ImdsMockCredentials = {
  accessKeyId: string;
  secretAccessKey: string;
  sessionToken: string;
  expiration?: string | undefined;
};

export type ImdsMockRequest = {
  method: string;
  path: string;
  headers: Record<string, string | string[] | undefined>;
};

export type ImdsMockResponse = { status: number; body: string };

export type ImdsMockDeps = {
  credentials?: ImdsMockCredentials | undefined;
  now?: (() => Date) | undefined;
  issueToken?: (() => string) | undefined;
};

export type ImdsMockHandler = (request: ImdsMockRequest) => ImdsMockResponse;

function lowercaseHeaders(headers: Record<string, string | string[] | undefined>): Record<string, string> {
  const normalized: Record<string, string> = {};
  for (const [name, value] of Object.entries(headers)) {
    const single = Array.isArray(value) ? value[0] : value;
    if (single !== undefined) {
      normalized[name.toLowerCase()] = single;
    }
  }
  return normalized;
}

function credentialDocument(credentials: ImdsMockCredentials, at: Date): Record<string, string> {
  return {
    Code: "Success",
    Type: "AWS-HMAC",
    LastUpdated: at.toISOString(),
    AccessKeyId: credentials.accessKeyId,
    SecretAccessKey: credentials.secretAccessKey,
    Token: credentials.sessionToken,
    Expiration:
      credentials.expiration ?? new Date(at.getTime() + FALLBACK_CREDENTIAL_LIFETIME_MS).toISOString(),
  };
}

// Fixed, not configurable — compose.yaml's endpoint URL and healthcheck hardcode this same number.
export const IMDS_MOCK_PORT = 1338;

export function readImdsMockCredentials(
  env: Record<string, string | undefined>,
): ImdsMockCredentials | undefined {
  const accessKeyId = nonEmpty(env["AWS_ACCESS_KEY_ID"]);
  const secretAccessKey = nonEmpty(env["AWS_SECRET_ACCESS_KEY"]);
  const sessionToken = nonEmpty(env["AWS_SESSION_TOKEN"]);
  if (accessKeyId === undefined || secretAccessKey === undefined || sessionToken === undefined) {
    return undefined;
  }
  const expiration = env["AWS_CREDENTIAL_EXPIRATION"];
  const expirationParses = expiration !== undefined && !Number.isNaN(Date.parse(expiration));
  return {
    accessKeyId,
    secretAccessKey,
    sessionToken,
    ...(expirationParses ? { expiration } : {}),
  };
}

export function createImdsMockHandler(deps: ImdsMockDeps): ImdsMockHandler {
  const now = deps.now ?? ((): Date => new Date());
  const issueToken = deps.issueToken ?? ((): string => randomBytes(TOKEN_BYTES).toString("base64url"));
  const tokenExpiries = new Map<string, number>();

  function evictExpiredTokens(atMs: number): void {
    for (const [token, expiresAtMs] of tokenExpiries) {
      if (expiresAtMs <= atMs) {
        tokenExpiries.delete(token);
      }
    }
  }

  function evictOldestToken(): void {
    const oldest = tokenExpiries.keys().next();
    if (!oldest.done) {
      tokenExpiries.delete(oldest.value);
    }
  }

  return (request) => {
    const headers = lowercaseHeaders(request.headers);
    const nowMs = now().getTime();
    evictExpiredTokens(nowMs);
    if (request.path === IMDS_TOKEN_PATH) {
      if (request.method !== "PUT") {
        return { status: 405, body: "token requests are PUT only" };
      }
      const ttlSeconds = Number(headers[IMDS_TOKEN_TTL_HEADER] ?? "");
      if (!Number.isInteger(ttlSeconds) || ttlSeconds < 1 || ttlSeconds > MAX_TOKEN_TTL_SECONDS) {
        return {
          status: 400,
          body: `${IMDS_TOKEN_TTL_HEADER} must be an integer between 1 and ${MAX_TOKEN_TTL_SECONDS}`,
        };
      }
      if (tokenExpiries.size >= IMDS_MAX_ISSUED_TOKENS) {
        evictOldestToken();
      }
      const token = issueToken();
      tokenExpiries.set(token, nowMs + ttlSeconds * MS_PER_SECOND);
      return { status: 200, body: token };
    }
    const token = headers[IMDS_TOKEN_HEADER];
    if (token === undefined || !tokenExpiries.has(token)) {
      return { status: 401, body: "a token from the IMDSv2 token flow is required" };
    }
    const isCredentialsPath =
      request.path === IMDS_CREDENTIALS_PATH || request.path === `${IMDS_CREDENTIALS_PATH}${IMDS_ROLE_NAME}`;
    if (!isCredentialsPath) {
      return { status: 404, body: "not found" };
    }
    if (request.method !== "GET") {
      return { status: 405, body: "metadata reads are GET only" };
    }
    if (deps.credentials === undefined) {
      return {
        status: 503,
        body: "no credentials were fed to the IMDS mock; start the stack through ./scripts/dev.sh",
      };
    }
    if (deps.credentials.expiration !== undefined && Date.parse(deps.credentials.expiration) <= nowMs) {
      // the SDK extends expired creds a few more minutes rather than rejecting, failing deep in Bedrock instead
      return {
        status: 503,
        body: "the credentials fed to the IMDS mock have expired; run aws sso login and restart ./scripts/dev.sh",
      };
    }
    if (request.path === IMDS_CREDENTIALS_PATH) {
      return { status: 200, body: IMDS_ROLE_NAME };
    }
    return { status: 200, body: JSON.stringify(credentialDocument(deps.credentials, new Date(nowMs))) };
  };
}
