# PersonalAI VS Code Extension

Phase 1 sends explicit editor context from VS Code to the local PersonalAI desktop app over a local Windows named pipe.

## Local Development

1. Start the PersonalAI desktop app.
2. From this folder, run `npm install`.
3. Run `npm run compile`.
4. Open this folder in VS Code.
5. Press `F5` to launch the Extension Development Host.
6. In the Extension Development Host, select text in an editor.
7. Run `PersonalAI: Ask About Selection` from the Command Palette or editor context menu.

## Privacy

- The extension does not monitor editor changes.
- The extension does not index the workspace.
- The extension sends only explicit selected text, current line, or metadata when the user invokes a PersonalAI command.
- It does not read terminals, environment variables, credentials, authentication sessions, or workspace files.
- It does not modify source files.

## Protocol

- Pipe: `\\.\pipe\PersonalAI.EditorContext.v1`
- Direction: VS Code extension client to PersonalAI desktop server.
- Framing: newline-delimited JSON.
- Encoding: UTF-8.
- Maximum message size: 2 MB.
- Protocol version: `1`.
