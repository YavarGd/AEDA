import { test } from "node:test";
import assert from "node:assert/strict";
import {
  createPipeTimeoutMessage,
  PipeTimeoutError
} from "../src/pipeClient";
import {
  getEditorChatTimeoutOptions,
  getQuickTimeoutOptions,
  isEditorAiCommand
} from "../src/timeoutOptions";

test("connect timeout error says connect timeout", () => {
  assert.equal(
    createPipeTimeoutMessage("connect"),
    "Timed out connecting to PersonalAI.");
});

test("response timeout error says response timeout", () => {
  assert.equal(
    createPipeTimeoutMessage("response"),
    "AEDA received the code, but timed out while generating the response.");
});

test("timeout after successful write does not say connecting", () => {
  const error = new PipeTimeoutError("response");

  assert.equal(error.phase, "response");
  assert.match(error.message, /timed out while generating/i);
  assert.doesNotMatch(error.message, /connecting/i);
});

test("editor AI commands use longer response timeout", () => {
  const settings = new Map<string, number>([
    ["pipe.connectTimeoutMs", 3000],
    ["pipe.sendTimeoutMs", 5000],
    ["pipe.responseTimeoutMs", 10000],
    ["editorChat.responseTimeoutMs", 120000]
  ]);
  const get = <T>(key: string, fallback: T): T =>
    (settings.get(key) as T | undefined) ?? fallback;

  const quick = getQuickTimeoutOptions(get);
  const editorChat = getEditorChatTimeoutOptions(get);

  assert.equal(quick.responseTimeoutMs, 10000);
  assert.equal(editorChat.responseTimeoutMs, 120000);
  assert.equal(editorChat.connectTimeoutMs, quick.connectTimeoutMs);
  assert.equal(editorChat.sendTimeoutMs, quick.sendTimeoutMs);
});

test("quick commands keep short timeout and editor commands are classified", () => {
  assert.equal(isEditorAiCommand("openPersonalAi"), false);
  assert.equal(isEditorAiCommand("explainSelection"), true);
  assert.equal(isEditorAiCommand("askAboutSelection"), true);
  assert.equal(isEditorAiCommand("findProblemsInSelection"), true);
});
