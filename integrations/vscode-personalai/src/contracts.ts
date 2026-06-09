import { randomUUID } from "node:crypto";

export const protocolVersion = 1;
export const defaultPipeName = "\\\\.\\pipe\\PersonalAI.EditorContext.v1";
export const maxMessageBytes = 2 * 1024 * 1024;

export type ContextSource = "vscode";

export type CommandType =
  | "askAboutSelection"
  | "explainSelection"
  | "findProblemsInSelection"
  | "openPersonalAi";

export interface TextRange {
  startLine: number;
  startCharacter: number;
  endLine: number;
  endCharacter: number;
}

export interface EditorDiagnostic {
  message: string;
  severity: string;
  range?: TextRange;
  source?: string;
  code?: string;
}

export interface EditorContext {
  selectedText?: string;
  fullActiveFilePath?: string;
  relativeWorkspacePath?: string;
  fileName?: string;
  languageId?: string;
  selection?: TextRange;
  workspaceFolderName?: string;
  workspaceFolderPath?: string;
  documentVersion?: number;
  isDirty: boolean;
  diagnostics: EditorDiagnostic[];
  timestampUtc: string;
}

export interface EditorContextEnvelope {
  protocolVersion: number;
  requestId: string;
  source: ContextSource;
  command: CommandType;
  userPrompt?: string;
  context?: EditorContext;
}

export function serializeEnvelope(envelope: EditorContextEnvelope): string {
  const line = JSON.stringify(envelope);
  const byteLength = Buffer.byteLength(line, "utf8");

  if (byteLength > maxMessageBytes) {
    throw new Error("PersonalAI message exceeds the 2 MB protocol limit.");
  }

  return `${line}\n`;
}

export function createRequestId(): string {
  return randomUUID();
}
