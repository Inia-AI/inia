import { join } from "node:path";
import { DOCUMENTS_AREA, type StagedInputSummary } from "./run-documents.js";
import { WORKSPACE_DOCUMENTS_DIR } from "./workspace-layout.js";

const STATEMENT_HEADER = "Staged input documents for this run:";
const STATEMENT_PROMPT_SEPARATOR = "\n\n";

type StatedDocument = { path: string; documentClass: string | undefined };

function statedDocumentsOf(
  workspacePath: string,
  stagedInputs: readonly StagedInputSummary[],
): StatedDocument[] {
  return stagedInputs
    .filter((input) => input.area === DOCUMENTS_AREA)
    .map((input) => ({
      path: join(workspacePath, WORKSPACE_DOCUMENTS_DIR, input.name),
      documentClass: input.documentClass,
    }));
}

export function statedDocumentPathsOf(
  workspacePath: string,
  stagedInputs: readonly StagedInputSummary[],
): string[] {
  return statedDocumentsOf(workspacePath, stagedInputs).map((document) => document.path);
}

function documentClassLabelOf(documentClass: string | undefined): string {
  const singleLine = documentClass?.replace(/\s+/g, " ").trim() ?? "";
  return singleLine === "" ? "" : ` (documentClass: ${singleLine})`;
}

export function composeDocumentsStatement(
  workspacePath: string,
  stagedInputs: readonly StagedInputSummary[],
): string | undefined {
  const documents = statedDocumentsOf(workspacePath, stagedInputs);
  if (documents.length === 0) {
    return undefined;
  }
  const lines = documents.map(
    (document) => `- ${document.path}${documentClassLabelOf(document.documentClass)}`,
  );
  return [STATEMENT_HEADER, ...lines].join("\n");
}

export function composeUserTurn(
  prompt: string,
  workspacePath: string,
  stagedInputs: readonly StagedInputSummary[],
): string {
  const statement = composeDocumentsStatement(workspacePath, stagedInputs);
  if (statement === undefined) {
    return prompt;
  }
  return `${statement}${STATEMENT_PROMPT_SEPARATOR}${prompt}`;
}
