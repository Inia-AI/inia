// A scripted Anthropic Messages API stub: enough of the streaming wire for the real CLI to run
// one Bash tool call, one Read of a staged file, and finish, at zero model cost. It is
// deliberately dumb — the script is "call each tool once, then stop" — because its job is to
// exercise the SDK/CLI machinery (frame parsing, sandbox spawn, tool round-trips, the CLI's own
// image handling), not to imitate a model.
import { createServer, type Server } from "node:http";
import { sseEvent } from "../loopback-http.js";
import { isRecord } from "../narrow.js";
import { imageDimensionsOf, type ToolResultImage } from "./probe-image.js";

export type ScriptedStubObservations = {
  messageRequests: number;
  toolResultTexts: string[];
  toolResultImages: ToolResultImage[];
  toolNamesOffered: string[];
  requestEfforts: (string | null)[];
  requestPaths: string[];
  requestApiKeys: string[];
  systemTexts: string[];
};

export type ScriptedStub = {
  baseUrl: string;
  observations: ScriptedStubObservations;
  close(): Promise<void>;
};

// The script is a fixed sequence keyed on how many tool results the request carries: the Bash
// call first, then (when a path is scripted) one Read of it, then the final text.
export type ScriptedStubScript = {
  toolCommand: string;
  readFilePath?: string;
  finalText: string;
};

const STUB_INPUT_TOKENS = 25;
const STUB_OUTPUT_TOKENS = 42;
const BASH_TOOL_NAME = "Bash";
const READ_TOOL_NAME = "Read";

type ScriptedToolUse = { name: string; input: Record<string, unknown> };

function scriptedToolUseFor(
  script: ScriptedStubScript,
  toolResultsSeen: number,
): ScriptedToolUse | undefined {
  if (toolResultsSeen === 0) {
    return { name: BASH_TOOL_NAME, input: { command: script.toolCommand } };
  }
  if (toolResultsSeen === 1 && script.readFilePath !== undefined) {
    return { name: READ_TOOL_NAME, input: { file_path: script.readFilePath } };
  }
  return undefined;
}

function collectText(value: unknown, sink: string[]): void {
  if (typeof value === "string") {
    sink.push(value);
    return;
  }
  if (Array.isArray(value)) {
    for (const entry of value) {
      collectText(entry, sink);
    }
    return;
  }
  if (isRecord(value) && typeof value["text"] === "string") {
    sink.push(value["text"]);
  }
}

function collectImages(value: unknown, sink: ToolResultImage[]): void {
  if (Array.isArray(value)) {
    for (const entry of value) {
      collectImages(entry, sink);
    }
    return;
  }
  if (!isRecord(value) || value["type"] !== "image" || !isRecord(value["source"])) {
    return;
  }
  const source = value["source"];
  const data = typeof source["data"] === "string" ? Buffer.from(source["data"], "base64") : Buffer.alloc(0);
  sink.push({
    mediaType: typeof source["media_type"] === "string" ? source["media_type"] : "",
    byteLength: data.byteLength,
    dimensions: imageDimensionsOf(data),
  });
}

type ObservedToolResults = { texts: string[]; images: ToolResultImage[]; count: number };

function toolResultsOf(messages: unknown): ObservedToolResults {
  const observed: ObservedToolResults = { texts: [], images: [], count: 0 };
  if (!Array.isArray(messages)) {
    return observed;
  }
  for (const message of messages) {
    if (!isRecord(message) || !Array.isArray(message["content"])) {
      continue;
    }
    for (const block of message["content"]) {
      if (isRecord(block) && block["type"] === "tool_result") {
        observed.count += 1;
        collectText(block["content"], observed.texts);
        collectImages(block["content"], observed.images);
      }
    }
  }
  return observed;
}

export function toolResultTextsOf(messages: unknown): string[] {
  return toolResultsOf(messages).texts;
}

export function systemTextOf(system: unknown): string {
  const texts: string[] = [];
  collectText(system, texts);
  return texts.join("\n");
}

export function toolNamesOf(tools: unknown): string[] {
  if (!Array.isArray(tools)) {
    return [];
  }
  return tools
    .map((tool) => (isRecord(tool) && typeof tool["name"] === "string" ? tool["name"] : undefined))
    .filter((name): name is string => name !== undefined);
}

function effortOf(requestBody: Record<string, unknown>): string | null {
  const outputConfig = requestBody["output_config"];
  return isRecord(outputConfig) && typeof outputConfig["effort"] === "string" ? outputConfig["effort"] : null;
}

// The same script as a complete, non-streaming Messages response — what a gateway that never
// streams hands back, for the shim to re-emit as the event stream above.
export function scriptedMessage(input: {
  requestBody: Record<string, unknown>;
  script: ScriptedStubScript;
  messageId: string;
  sawToolResult: boolean;
}): Record<string, unknown> {
  const model = typeof input.requestBody["model"] === "string" ? input.requestBody["model"] : "scripted-stub";
  const useTool = !input.sawToolResult && toolNamesOf(input.requestBody["tools"]).includes("Bash");
  return {
    id: input.messageId,
    type: "message",
    role: "assistant",
    model,
    content: [
      useTool
        ? {
            type: "tool_use",
            id: `${input.messageId}_tool`,
            name: "Bash",
            input: { command: input.script.toolCommand },
          }
        : { type: "text", text: input.script.finalText },
    ],
    stop_reason: useTool ? "tool_use" : "end_turn",
    stop_sequence: null,
    usage: {
      input_tokens: STUB_INPUT_TOKENS,
      output_tokens: STUB_OUTPUT_TOKENS,
      cache_creation_input_tokens: 0,
      cache_read_input_tokens: 0,
    },
  };
}

function scriptedSse(input: {
  requestBody: Record<string, unknown>;
  script: ScriptedStubScript;
  messageId: string;
  toolResultsSeen: number;
}): string {
  const model = typeof input.requestBody["model"] === "string" ? input.requestBody["model"] : "scripted-stub";
  const scriptedToolUse = scriptedToolUseFor(input.script, input.toolResultsSeen);
  const useTool =
    scriptedToolUse !== undefined && toolNamesOf(input.requestBody["tools"]).includes(scriptedToolUse.name);
  const head = sseEvent("message_start", {
    type: "message_start",
    message: {
      id: input.messageId,
      type: "message",
      role: "assistant",
      model,
      content: [],
      stop_reason: null,
      stop_sequence: null,
      usage: { input_tokens: STUB_INPUT_TOKENS, output_tokens: 1 },
    },
  });
  const block = useTool
    ? sseEvent("content_block_start", {
        type: "content_block_start",
        index: 0,
        content_block: {
          type: "tool_use",
          id: `${input.messageId}_tool`,
          name: scriptedToolUse.name,
          input: {},
        },
      }) +
      sseEvent("content_block_delta", {
        type: "content_block_delta",
        index: 0,
        delta: {
          type: "input_json_delta",
          partial_json: JSON.stringify(scriptedToolUse.input),
        },
      }) +
      sseEvent("content_block_stop", { type: "content_block_stop", index: 0 })
    : sseEvent("content_block_start", {
        type: "content_block_start",
        index: 0,
        content_block: { type: "text", text: "" },
      }) +
      sseEvent("content_block_delta", {
        type: "content_block_delta",
        index: 0,
        delta: { type: "text_delta", text: input.script.finalText },
      }) +
      sseEvent("content_block_stop", { type: "content_block_stop", index: 0 });
  const tail =
    sseEvent("message_delta", {
      type: "message_delta",
      delta: { stop_reason: useTool ? "tool_use" : "end_turn", stop_sequence: null },
      usage: { output_tokens: STUB_OUTPUT_TOKENS },
    }) + sseEvent("message_stop", { type: "message_stop" });
  return head + block + tail;
}

export function startScriptedAnthropicStub(script: ScriptedStubScript): Promise<ScriptedStub> {
  const observations: ScriptedStubObservations = {
    messageRequests: 0,
    toolResultTexts: [],
    toolResultImages: [],
    toolNamesOffered: [],
    requestEfforts: [],
    requestPaths: [],
    requestApiKeys: [],
    systemTexts: [],
  };
  let messageCounter = 0;
  const server: Server = createServer((req, res) => {
    const chunks: Buffer[] = [];
    req.on("data", (chunk: Buffer) => {
      chunks.push(chunk);
    });
    req.on("end", () => {
      const url = req.url ?? "";
      observations.requestPaths.push(url);
      const apiKey = req.headers["x-api-key"];
      observations.requestApiKeys.push(typeof apiKey === "string" ? apiKey : "");
      if (url.includes("count_tokens")) {
        res.writeHead(200, { "content-type": "application/json" });
        res.end(JSON.stringify({ input_tokens: STUB_INPUT_TOKENS }));
        return;
      }
      if (req.method === "POST" && url.includes("/messages")) {
        observations.messageRequests += 1;
        messageCounter += 1;
        let requestBody: Record<string, unknown> = {};
        try {
          const parsed: unknown = JSON.parse(Buffer.concat(chunks).toString("utf8"));
          if (isRecord(parsed)) {
            requestBody = parsed;
          }
        } catch {
          // a malformed body still gets a scripted reply; the test asserts on what was observed
        }
        observations.systemTexts.push(systemTextOf(requestBody["system"]));
        const toolResults = toolResultsOf(requestBody["messages"]);
        observations.toolResultTexts.push(...toolResults.texts);
        observations.toolResultImages.push(...toolResults.images);
        // The model-visible tool surface is part of the contract: collect every name any request
        // offered, so a bump that widens the surface on a later request trips the gate test too.
        for (const name of toolNamesOf(requestBody["tools"])) {
          if (!observations.toolNamesOffered.includes(name)) {
            observations.toolNamesOffered.push(name);
          }
        }
        observations.requestEfforts.push(effortOf(requestBody));
        res.writeHead(200, { "content-type": "text/event-stream" });
        res.end(
          scriptedSse({
            requestBody,
            script,
            messageId: `msg_scripted_${messageCounter}`,
            toolResultsSeen: toolResults.count,
          }),
        );
        return;
      }
      res.writeHead(404, { "content-type": "application/json" });
      res.end(JSON.stringify({ error: { type: "not_found_error", message: "not scripted" } }));
    });
  });
  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      if (address === null || typeof address === "string") {
        reject(new Error("scripted stub bound no TCP port"));
        return;
      }
      resolve({
        baseUrl: `http://127.0.0.1:${address.port}`,
        observations,
        close: () =>
          new Promise<void>((resolveClose, rejectClose) => {
            server.close((error) => (error === undefined ? resolveClose() : rejectClose(error)));
          }),
      });
    });
  });
}
