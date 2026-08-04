import { readFileSync } from "node:fs";
import * as path from "node:path";
import { test } from "node:test";
import assert from "node:assert/strict";

const root = path.resolve(__dirname, "..", "..");
const extension = readFileSync(path.join(root, "src", "extension.ts"), "utf8");
const readme = readFileSync(path.join(root, "README.md"), "utf8");

test("editor activity never publishes context passively", () => {
  assert.doesNotMatch(extension, /onDidChangeTextEditorSelection/);
  assert.doesNotMatch(extension, /onDidChangeActiveTextEditor/);
  assert.doesNotMatch(extension, /publishSelectionContext/);
  assert.doesNotMatch(extension, /command:\s*"updateSelectionContext"/);
});

test("explicit commands collect bounded context and keep voice unavailable", () => {
  assert.match(extension, /personalai\.askAboutSelection/);
  assert.match(extension, /personalai\.explainSelection/);
  assert.match(extension, /personalai\.findProblemsInSelection/);
  assert.match(extension, /collectEditorContext\(maxSelectedTextCharacters\)/);
  assert.match(readme, /Changing the selection or active editor sends nothing/);
  assert.match(readme, /voice capabilities are not advertised and remain unavailable/i);
});
