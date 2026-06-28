import { CommandType } from "./contracts";
import {
  defaultPipeTimeoutOptions,
  PipeTimeoutOptions
} from "./pipeClient";

export const defaultEditorChatResponseTimeoutMs = 120000;

export type ConfigurationGetter = <T>(key: string, defaultValue: T) => T;

export function isEditorAiCommand(command: CommandType): boolean {
  return command === "askAboutSelection" ||
    command === "explainSelection" ||
    command === "findProblemsInSelection";
}

export function getQuickTimeoutOptions(
  getConfigurationValue: ConfigurationGetter
): PipeTimeoutOptions {
  return {
    connectTimeoutMs: getConfigurationValue(
      "pipe.connectTimeoutMs",
      defaultPipeTimeoutOptions.connectTimeoutMs),
    sendTimeoutMs: getConfigurationValue(
      "pipe.sendTimeoutMs",
      defaultPipeTimeoutOptions.sendTimeoutMs),
    responseTimeoutMs: getConfigurationValue(
      "pipe.responseTimeoutMs",
      defaultPipeTimeoutOptions.responseTimeoutMs)
  };
}

export function getEditorChatTimeoutOptions(
  getConfigurationValue: ConfigurationGetter
): PipeTimeoutOptions {
  return {
    ...getQuickTimeoutOptions(getConfigurationValue),
    responseTimeoutMs: getConfigurationValue(
      "editorChat.responseTimeoutMs",
      defaultEditorChatResponseTimeoutMs)
  };
}
