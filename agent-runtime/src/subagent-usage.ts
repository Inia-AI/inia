import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { isRecord } from "./narrow.js";
import { isSafePathSegment } from "./safe-path.js";
import type { ApiMessageUsage, SubagentApiMessagesReader } from "./usage-cost.js";

export const PROJECTS_DIR_NAME = "projects";
export const SUBAGENTS_DIR_NAME = "subagents";
export const SUBAGENT_FILE_PATTERN = /^agent-(.+)\.jsonl$/;
const ASSISTANT_LINE_TYPE = "assistant";

function subagentTranscriptFileName(agentId: string): string {
  return `agent-${agentId}.jsonl`;
}

export function subagentsDirsOf(configDir: string, sessionId: string): string[] {
  const projectsDir = join(configDir, PROJECTS_DIR_NAME);
  if (!isSafePathSegment(sessionId) || !existsSync(projectsDir)) {
    return [];
  }
  const dirs: string[] = [];
  for (const project of readdirSync(projectsDir, { withFileTypes: true })) {
    if (!project.isDirectory()) {
      continue;
    }
    const candidate = join(projectsDir, project.name, sessionId, SUBAGENTS_DIR_NAME);
    if (existsSync(candidate)) {
      dirs.push(candidate);
    }
  }
  return dirs;
}

// The CLI appends one transcript line per content block of an API message, and the earlier
// lines of a message carry the message_start placeholder output count (measured on CLI
// 2.1.251: a thinking-block line at 8 output tokens, then the tool_use line at 128 for the
// same message id) — so the last line for a message id is the settled usage.
export function subagentApiMessagesOf(transcriptText: string): ApiMessageUsage[] {
  const byMessageId = new Map<string, ApiMessageUsage>();
  for (const line of transcriptText.split("\n")) {
    if (line === "") {
      continue;
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(line);
    } catch {
      continue;
    }
    if (!isRecord(parsed) || parsed["type"] !== ASSISTANT_LINE_TYPE) {
      continue;
    }
    const message = parsed["message"];
    if (!isRecord(message)) {
      continue;
    }
    const messageId = message["id"];
    const rawUsage = message["usage"];
    if (typeof messageId !== "string" || messageId === "" || !isRecord(rawUsage)) {
      continue;
    }
    const model = message["model"];
    byMessageId.set(messageId, {
      messageId,
      model: typeof model === "string" ? model : "",
      rawUsage: { ...rawUsage },
      settled: true,
    });
  }
  return [...byMessageId.values()];
}

export function createSubagentTranscriptReader(configDir: string): SubagentApiMessagesReader {
  return (sessionId, agentId) => {
    if (!isSafePathSegment(agentId)) {
      return undefined;
    }
    for (const dir of subagentsDirsOf(configDir, sessionId)) {
      const transcriptPath = join(dir, subagentTranscriptFileName(agentId));
      if (!existsSync(transcriptPath)) {
        continue;
      }
      try {
        return subagentApiMessagesOf(readFileSync(transcriptPath, "utf8"));
      } catch {
        return undefined;
      }
    }
    return undefined;
  };
}
