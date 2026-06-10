import * as path from "node:path";
import * as vscode from "vscode";
import {
  EditorContext,
  EditorDiagnostic,
  TextRange
} from "./contracts";

export type EmptySelectionChoice = "currentLine" | "metadataOnly" | "cancel";

export interface CollectedContextResult {
  context: EditorContext;
  selectedCharacterCount: number;
}

export async function collectEditorContext(
  maxSelectedTextCharacters: number
): Promise<CollectedContextResult | undefined> {
  const editor = vscode.window.activeTextEditor;

  if (!editor) {
    vscode.window.showWarningMessage("Open an editor before sending context to PersonalAI.");
    return undefined;
  }

  const document = editor.document;
  const workspaceFolder = vscode.workspace.getWorkspaceFolder(document.uri);
  let selection = editor.selection;
  let selectedText = document.getText(selection);

  if (!selectedText) {
    const choice = await chooseEmptySelectionBehavior();

    if (choice === "cancel") {
      return undefined;
    }

    if (choice === "currentLine") {
      const line = document.lineAt(selection.active.line);
      selectedText = line.text;
      selection = new vscode.Selection(line.range.start, line.range.end);
    }
  }

  if (selectedText.length > maxSelectedTextCharacters) {
    const choice = await vscode.window.showWarningMessage(
      `The selected text has ${selectedText.length} characters. PersonalAI is configured to send at most ${maxSelectedTextCharacters}.`,
      "Truncate",
      "Cancel"
    );

    if (choice !== "Truncate") {
      return undefined;
    }

    selectedText = selectedText.slice(0, maxSelectedTextCharacters);
  }

  const diagnostics = vscode.languages
    .getDiagnostics(document.uri)
    .map(toEditorDiagnostic);

  const context: EditorContext = {
    selectedText: selectedText || undefined,
    fullActiveFilePath: document.uri.scheme === "file" ? document.uri.fsPath : undefined,
    relativeWorkspacePath: workspaceFolder
      ? path.relative(workspaceFolder.uri.fsPath, document.uri.fsPath)
      : undefined,
    fileName: path.basename(document.uri.fsPath),
    languageId: document.languageId,
    selection: toTextRange(selection),
    workspaceFolderName: workspaceFolder?.name,
    workspaceFolderPath: workspaceFolder?.uri.fsPath,
    documentVersion: document.version,
    isDirty: document.isDirty,
    diagnostics,
    timestampUtc: new Date().toISOString()
  };

  return {
    context,
    selectedCharacterCount: selectedText.length
  };
}

function toTextRange(range: vscode.Range): TextRange {
  return {
    startLine: range.start.line,
    startCharacter: range.start.character,
    endLine: range.end.line,
    endCharacter: range.end.character
  };
}

function toEditorDiagnostic(diagnostic: vscode.Diagnostic): EditorDiagnostic {
  return {
    message: diagnostic.message,
    severity: vscode.DiagnosticSeverity[diagnostic.severity].toLowerCase(),
    range: toTextRange(diagnostic.range),
    source: diagnostic.source,
    code: diagnostic.code === undefined ? undefined : String(diagnostic.code)
  };
}

async function chooseEmptySelectionBehavior(): Promise<EmptySelectionChoice> {
  const choice = await vscode.window.showQuickPick(
    [
      { label: "Send current line", value: "currentLine" as const },
      { label: "Send active file metadata only", value: "metadataOnly" as const },
      { label: "Cancel", value: "cancel" as const }
    ],
    {
      title: "No text selected",
      placeHolder: "Choose what to send to PersonalAI."
    }
  );

  return choice?.value ?? "cancel";
}
