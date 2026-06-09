import * as vscode from "vscode";
import {
  CommandType,
  createRequestId,
  defaultPipeName,
  EditorContextEnvelope,
  protocolVersion
} from "./contracts";
import { collectEditorContext } from "./contextCollector";
import { sendEnvelope } from "./pipeClient";

export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.commands.registerCommand(
      "personalai.askAboutSelection",
      () => sendSelectionCommand("askAboutSelection")
    ),
    vscode.commands.registerCommand(
      "personalai.explainSelection",
      () => sendSelectionCommand(
        "explainSelection",
        "Explain the selected code. Include important behavior, edge cases, and assumptions."
      )
    ),
    vscode.commands.registerCommand(
      "personalai.findProblemsInSelection",
      () => sendSelectionCommand(
        "findProblemsInSelection",
        "Find problems in the selected code. Do not change the document."
      )
    ),
    vscode.commands.registerCommand(
      "personalai.openPersonalAI",
      () => sendOpenCommand()
    )
  );
}

export function deactivate(): void {
}

async function sendSelectionCommand(
  command: CommandType,
  predefinedPrompt?: string
): Promise<void> {
  const configuration = vscode.workspace.getConfiguration("personalai");
  const maxSelectedTextCharacters = configuration.get<number>(
    "maxSelectedTextCharacters",
    100000
  );
  const pipeName = configuration.get<string>("pipeName", defaultPipeName);

  const collected = await collectEditorContext(maxSelectedTextCharacters);

  if (!collected) {
    return;
  }

  const userPrompt = predefinedPrompt ??
    await vscode.window.showInputBox({
      title: "Ask PersonalAI",
      prompt: "What would you like to ask about the selected code?",
      ignoreFocusOut: true
    });

  if (!userPrompt) {
    return;
  }

  const envelope: EditorContextEnvelope = {
    protocolVersion,
    requestId: createRequestId(),
    source: "vscode",
    command,
    userPrompt,
    context: collected.context
  };

  await sendToPersonalAi(pipeName, envelope);
}

async function sendOpenCommand(): Promise<void> {
  const configuration = vscode.workspace.getConfiguration("personalai");
  const pipeName = configuration.get<string>("pipeName", defaultPipeName);
  const envelope: EditorContextEnvelope = {
    protocolVersion,
    requestId: createRequestId(),
    source: "vscode",
    command: "openPersonalAi"
  };

  await sendToPersonalAi(pipeName, envelope);
}

async function sendToPersonalAi(
  pipeName: string,
  envelope: EditorContextEnvelope
): Promise<void> {
  try {
    const response = await sendEnvelope(pipeName, envelope);

    if (response.ok) {
      vscode.window.showInformationMessage("Sent context to PersonalAI.");
      return;
    }

    vscode.window.showWarningMessage(`PersonalAI rejected the request: ${response.message}`);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    vscode.window.showErrorMessage(`Could not send context to PersonalAI: ${message}`);
  }
}
