import { describe, expect, it } from "bun:test";
import { startScriptedAnthropicStub } from "./anthropic-stub.js";

const NO_REUSE_HEADERS: Record<string, string> = { connection: "close" };

function toolsNamed(...names: string[]): { name: string; input_schema: Record<string, unknown> }[] {
  return names.map((name) => ({ name, input_schema: { type: "object" } }));
}

async function postMessages(baseUrl: string, body: Record<string, unknown>): Promise<void> {
  const response = await fetch(`${baseUrl}/v1/messages`, {
    method: "POST",
    headers: { "content-type": "application/json", ...NO_REUSE_HEADERS },
    body: JSON.stringify({ model: "claude-sonnet-5-5", messages: [], ...body }),
  });
  await response.text();
}

describe("startScriptedAnthropicStub", () => {
  it("records every tool name any request offered, in first-seen order", async () => {
    const stub = await startScriptedAnthropicStub({ toolCommand: "true", finalText: "done" });
    try {
      await postMessages(stub.baseUrl, { tools: toolsNamed("Bash", "Read") });
      await postMessages(stub.baseUrl, { tools: toolsNamed("Read", "Bash", "DeferredToolPlaceholder") });
      expect(stub.observations.toolNamesOffered).toEqual(["Bash", "Read", "DeferredToolPlaceholder"]);
    } finally {
      await stub.close();
    }
  });

  it("records each request's output_config effort, null where a request sent none", async () => {
    const stub = await startScriptedAnthropicStub({ toolCommand: "true", finalText: "done" });
    try {
      await postMessages(stub.baseUrl, { output_config: { effort: "high" } });
      await postMessages(stub.baseUrl, {});
      expect(stub.observations.requestEfforts).toEqual(["high", null]);
    } finally {
      await stub.close();
    }
  });
});
