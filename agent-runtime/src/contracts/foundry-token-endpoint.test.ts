// ADR 0166 rests on one behavior of the pinned CLI: in Foundry mode its Azure credential chain takes
// the token from IDENTITY_ENDPOINT, the App Service source, and sends it to Foundry as a bearer
// token. A CLI bump that drops that source would send runs back to the metadata service, which the
// host closes. Zero model cost: both servers are stubs, and the run stops at its first Foundry
// request, because the CLI would otherwise retry the refusal with backoff for minutes.
import { describe, expect, it } from "bun:test";
import { mkdtempSync, rmSync } from "node:fs";
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { query } from "@anthropic-ai/claude-agent-sdk";
import {
  buildProviderSubprocessEnv,
  FOUNDRY_TOKEN_ENDPOINT_ENV_VAR,
  FOUNDRY_TOKEN_ENDPOINT_HEADER,
} from "../model-provider.js";

const RUN_TIMEOUT_MS = 120000;
const FOUNDRY_SCOPE_RESOURCE = "https://cognitiveservices.azure.com";
const RUNTIME_CLIENT_ID = "11111111-2222-3333-4444-555555555555";
const STUB_TOKEN = "STUB-FOUNDRY-TOKEN";
const TOKEN_LIFETIME_SECONDS = 3600;
const RUNS_UNDER_QEMU = process.env["EMULATED_PLATFORM"] === "true";

type Stub = { url: string; server: Server };

async function listening(
  handler: (request: IncomingMessage, response: ServerResponse) => void,
): Promise<Stub> {
  const server = createServer(handler);
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address() as AddressInfo;
  return { url: `http://127.0.0.1:${port}`, server };
}

function closed(stub: Stub): Promise<void> {
  return new Promise((resolve) => {
    stub.server.close(() => resolve());
    stub.server.closeAllConnections();
  });
}

describe("a Foundry run with a token endpoint", () => {
  it(
    "fetches the Foundry token from the endpoint and sends it to Foundry as a bearer token",
    async () => {
      const tokenRequests: { url: URL; header: string | undefined }[] = [];
      const tokenEndpoint = await listening((request, response) => {
        const header = request.headers["x-identity-header"];
        tokenRequests.push({
          url: new URL(request.url ?? "/", "http://stub"),
          header: typeof header === "string" ? header : undefined,
        });
        response.writeHead(200, { "content-type": "application/json" });
        response.end(
          JSON.stringify({
            access_token: STUB_TOKEN,
            expires_on: String(Math.floor(Date.now() / 1000) + TOKEN_LIFETIME_SECONDS),
            resource: FOUNDRY_SCOPE_RESOURCE,
            token_type: "Bearer",
          }),
        );
      });
      const authorizations: string[] = [];
      const abortController = new AbortController();
      const foundry = await listening((request, response) => {
        authorizations.push(request.headers.authorization ?? "");
        abortController.abort();
        request.resume();
        response.writeHead(401, { "content-type": "application/json" });
        response.end(
          JSON.stringify({ type: "error", error: { type: "authentication_error", message: "stub" } }),
        );
      });
      const home = mkdtempSync(join(tmpdir(), "foundry-token-endpoint-"));
      try {
        const env = buildProviderSubprocessEnv("foundry", {
          PATH: process.env["PATH"],
          HOME: home,
          CLAUDE_CONFIG_DIR: join(home, ".claude"),
          CLAUDE_CODE_USE_FOUNDRY: "1",
          ANTHROPIC_FOUNDRY_BASE_URL: `${foundry.url}/anthropic`,
          ANTHROPIC_MODEL: "claude-sonnet-5",
          ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-sonnet-5",
          AZURE_CLIENT_ID: RUNTIME_CLIENT_ID,
          [FOUNDRY_TOKEN_ENDPOINT_ENV_VAR]: `${tokenEndpoint.url}/msi/token`,
        });
        const run = (async () => {
          for await (const _message of query({
            prompt: "Say hi.",
            options: {
              env,
              abortController,
              maxTurns: 1,
              settingSources: [],
              tools: [],
              permissionMode: "dontAsk",
            },
          })) {
            // Drained only until the abort; Foundry refuses every call.
          }
        })().catch(() => undefined);
        await run;
        if (RUNS_UNDER_QEMU) {
          return;
        }
        expect(tokenRequests.length).toBeGreaterThan(0);
        expect(tokenRequests[0]?.url.searchParams.get("resource")).toBe(FOUNDRY_SCOPE_RESOURCE);
        expect(tokenRequests[0]?.header).toBe(FOUNDRY_TOKEN_ENDPOINT_HEADER);
        expect(authorizations).toContain(`Bearer ${STUB_TOKEN}`);
      } finally {
        await closed(tokenEndpoint);
        await closed(foundry);
        rmSync(home, { recursive: true, force: true });
      }
    },
    RUN_TIMEOUT_MS,
  );
});
