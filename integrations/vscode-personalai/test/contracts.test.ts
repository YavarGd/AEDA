import { test } from "node:test";
import assert from "node:assert/strict";
import {
  EditorContextEnvelope,
  maxMessageBytes,
  protocolVersion,
  serializeEnvelope
} from "../src/contracts";

test("serializeEnvelope adds newline framing", () => {
  const envelope: EditorContextEnvelope = {
    protocolVersion,
    requestId: "request-1",
    source: "vscode",
    command: "openPersonalAi"
  };

  const serialized = serializeEnvelope(envelope);

  assert.ok(serialized.endsWith("\n"));
  assert.equal(JSON.parse(serialized).requestId, "request-1");
});

test("serializeEnvelope rejects oversized messages", () => {
  const envelope: EditorContextEnvelope = {
    protocolVersion,
    requestId: "request-1",
    source: "vscode",
    command: "askAboutSelection",
    context: {
      selectedText: "a".repeat(maxMessageBytes),
      isDirty: false,
      diagnostics: [],
      timestampUtc: new Date(0).toISOString()
    }
  };

  assert.throws(() => serializeEnvelope(envelope), /2 MB/);
});

test("selection context updates use the production pipe contract", () => {
  const serialized = serializeEnvelope({
    protocolVersion,
    requestId: "request-2",
    source: "vscode",
    command: "updateSelectionContext",
    context: {
      selectedText: "selected text",
      isDirty: false,
      diagnostics: [],
      timestampUtc: new Date(0).toISOString()
    }
  });

  assert.equal(JSON.parse(serialized).command, "updateSelectionContext");
  assert.equal(JSON.parse(serialized).context.selectedText, "selected text");
});
