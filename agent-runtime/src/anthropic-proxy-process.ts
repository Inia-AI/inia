// The supervisor's half of the Anthropic key holder (ADR 0151): spawned at boot in anthropic mode
// only, as a loopback process (src/loopback-process.ts) handed the key and nothing else.
import {
  type LoopbackProcessHandle,
  loopbackProcessEnv,
  type StartLoopbackProcessDeps,
  startLoopbackProcess,
} from "./loopback-process.js";
import {
  ANTHROPIC_API_KEY_ENV_VAR,
  type AnthropicProxyConfig,
  LOOPBACK_HOST,
  LOOPBACK_PORT,
} from "./model-provider.js";

export const ANTHROPIC_PROXY_ENTRY = "src/anthropic-proxy-main.ts";

export type StartAnthropicProxyDeps = StartLoopbackProcessDeps & {
  proxy: AnthropicProxyConfig;
  supervisorEnv: Record<string, string | undefined>;
  host?: string;
  port?: number;
};

export function anthropicProxyEnv(input: {
  proxy: AnthropicProxyConfig;
  host: string;
  port: number;
  supervisorEnv: Record<string, string | undefined>;
}): Record<string, string> {
  return loopbackProcessEnv({
    supervisorEnv: input.supervisorEnv,
    host: input.host,
    port: input.port,
    settings: { [ANTHROPIC_API_KEY_ENV_VAR]: input.proxy.apiKey },
  });
}

export function startAnthropicProxy(deps: StartAnthropicProxyDeps): Promise<LoopbackProcessHandle> {
  const host = deps.host ?? LOOPBACK_HOST;
  const port = deps.port ?? LOOPBACK_PORT;
  return startLoopbackProcess(
    {
      name: "anthropic_proxy",
      entry: ANTHROPIC_PROXY_ENTRY,
      label: "Anthropic key holder",
      env: anthropicProxyEnv({ proxy: deps.proxy, host, port, supervisorEnv: deps.supervisorEnv }),
      host,
      port,
    },
    deps,
  );
}
