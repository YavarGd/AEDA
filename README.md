# AEDA

A local-first modular AI assistant for Windows.

## Early alpha

AEDA is an early alpha. Its UI, local data formats, integrations, and provider support may change without migration support. Review all generated patches and keep backups of important data.

## Current capabilities

- WinUI 3 desktop shell with chat, settings, task timelines, and the Assist Pill.
- AEDA Code prepares reviewable patch proposals, requires approval before applying changes, backs up files, and can run allow-listed validation commands.
- AEDA Memory stores local SQLite-backed memory and can index registered workspaces when enabled.
- AEDA Research creates local claim/evidence records; its evidence providers are still foundational.
- Registered workspace tools read, list, and search files. Access is scoped to registered workspaces and permission-gated.
- Provider routing supports a local Ollama-compatible endpoint by default. Remote-provider use is opt-in and controlled by the local settings UI.

## Experimental or partial

Voice support uses an optional local Python speech worker. It needs a separate Python environment and a downloaded Whisper model. The Visual Studio Code integration is present under `integrations/vscode-personalai` but is not part of the .NET solution build.

## Safety model

Workspace access and tools are permission-gated. AEDA Code presents changes for review and requires approval before applying them; it creates backups for applied file changes. Remote providers and sharing local context are disabled by default.

## Requirements

- Windows 10 version 1809 or later for the WinUI project.
- .NET SDK 10.0 (the projects target `net10.0`; this repository was verified with SDK 10.0.301).
- Optional: Ollama running at `http://localhost:11434` with a compatible local model. The default configured model name is `gemma4`.
- Optional voice worker: Python 3.11 or newer, plus the packages in `workers/speech/requirements.txt`. GPU use is the worker default; configure the documented `PERSONALAI_WHISPER_*` environment variables for another model, device, or compute type.

## Build and test

```powershell
dotnet restore PersonalAI.slnx
dotnet test PersonalAI.Tests/PersonalAI.Tests.csproj
dotnet build PersonalAI.slnx
dotnet build PersonalAI.slnx -c Release
```

Launch the WinUI application after a successful build:

```powershell
dotnet run --project PersonalAI.Desktop.WinUI/PersonalAI.Desktop.WinUI.csproj
```

The application keeps settings and local data under the current Windows user's Local AppData directory; no repository-local settings file is required to compile.

## Structure

- `PersonalAI.Core` — application contracts and policies.
- `PersonalAI.Infrastructure` — SQLite persistence, workspace, coding, context, and worker implementations.
- `PersonalAI.Providers` — Ollama and OpenAI-compatible provider adapters.
- `PersonalAI.Desktop.WinUI` — WinUI 3 desktop application.
- `PersonalAI.Tests` — automated tests.
- `workers/speech` — optional FastAPI/faster-whisper speech worker.
- `integrations/vscode-personalai` — optional VS Code integration.

## Limitations and roadmap

The project is Windows-only, has no packaged installer, and expects local providers or user-configured optional remote providers. Planned work includes maturing the existing Code, Memory, Research, Task Center, and Assist workflows; it is not a promise of released functionality.

## License

Apache-2.0. See [LICENSE](LICENSE).
