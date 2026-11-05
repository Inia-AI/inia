// Entry point of the Anthropic key holder (ADR 0151) — like the other entry points it reads
// process.env and hands every value to the seam's parsers.
import { startAnthropicProxyServer } from "./anthropic-proxy.js";
import { runLoopbackEntry } from "./loopback-entry.js";
import {
  ANTHROPIC_API_URL,
  assertNoProxyVariables,
  parseAnthropicProxyKey,
  parseLoopbackListenAddress,
} from "./model-provider.js";

await runLoopbackEntry(process.env, (env, log) => {
  assertNoProxyVariables(env, "Anthropic key holder");
  const apiKey = parseAnthropicProxyKey(env);
  const { host, port } = parseLoopbackListenAddress(env);
  return startAnthropicProxyServer({ apiKey, upstreamUrl: ANTHROPIC_API_URL, host, port, logger: log });
});
