import * as vscode from "vscode";
import {
  CommandType,
  createRequestId,
  defaultPipeName,
  EditorContextEnvelope,
  protocolVersion
} from "./contracts";
import { collectEditorContext } from "./contextCollector";
import {
  PipeTimeoutOptions,
  sendEnvelope
} from "./pipeClient";
import {
  getEditorChatTimeoutOptions as getEditorChatTimeoutOptionsCore,
  getQuickTimeoutOptions as getQuickTimeoutOptionsCore,
  isEditorAiCommand
} from "./timeoutOptions";

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

  await sendToPersonalAi(
    pipeName,
    envelope,
    getEditorChatTimeoutOptions(configuration));
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

  await sendToPersonalAi(
    pipeName,
    envelope,
    getQuickTimeoutOptions(configuration));
}

async function sendToPersonalAi(
  pipeName: string,
  envelope: EditorContextEnvelope,
  timeoutOptions: PipeTimeoutOptions
): Promise<void> {
  try {
    const response = isEditorAiCommand(envelope.command)
      ? await vscode.window.withProgress(
        {
          location: vscode.ProgressLocation.Notification,
          title: "AEDA is analyzing the selected code...",
          cancellable: false
        },
        () => sendEnvelope(pipeName, envelope, timeoutOptions))
      : await sendEnvelope(pipeName, envelope, timeoutOptions);

    if (response.Ok) {
      vscode.window.showInformationMessage(
        response.Message ?? "No message received"
      );
      return;
    }

    vscode.window.showWarningMessage(
      `PersonalAI rejected the request: ${response.Message ?? JSON.stringify(response)}`
    );
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    vscode.window.showErrorMessage(
      `Could not send context to PersonalAI: ${message}`
    );
  }
}

function getQuickTimeoutOptions(
  configuration: vscode.WorkspaceConfiguration
): PipeTimeoutOptions {
  return getQuickTimeoutOptionsCore(
    (key, defaultValue) => configuration.get(key, defaultValue));
}

function getEditorChatTimeoutOptions(
  configuration: vscode.WorkspaceConfiguration
): PipeTimeoutOptions {
  return getEditorChatTimeoutOptionsCore(
    (key, defaultValue) => configuration.get(key, defaultValue));
}
