# CoWork OS Reuse Audit

Audit date: 2026-06-11  
External repo: `CoWork-OS/CoWork-OS` at `d4beb0c71fef8bd1c923ed870534920d70064273`  
Local repo: The Local One / PersonalAI, C# WinUI 3 native Windows app

## 1. Executive Summary

CoWork OS is a very large MIT-licensed TypeScript/Electron project with real implementations in several areas that matter to The Local One: agent task execution, event timelines, tool contracts, provider abstractions, Ollama/cloud providers, MCP client/host plumbing, artifact generation, browser automation, skills, memory experiments, and a Node-only daemon/control-plane path.

The highest-value reuse is not the Electron shell. The Local One should preserve its WinUI shell, C# provider contracts, SQLite conversations, context capture, VS Code integration, tray/hotkey behavior, and native approval UX. CoWork OS should be mined for algorithms, schemas, tool contracts, prompt/skill conventions, and artifact/browser design ideas.

Recommended posture:

- Copy or port small, self-contained TypeScript ideas with MIT attribution.
- Port the agent/tool/event abstractions to C# instead of embedding the whole Electron runtime.
- Use CoWork OS as a short-lived reference sidecar only for experiments, never as a privileged production agent until The Local One owns permission brokering.
- Reject or defer remote channels, device management, payments/crypto, broad cloud infrastructure, signed-in browser control, and self-modifying/background autonomy.

Bottom line: CoWork OS can materially shorten our roadmap for task timelines, typed tools, artifact generation, skills, MCP, and browser workbench design. It does not safely replace our native app architecture, provider system, persistence, or permission model.

## 2. Verified Architecture

### Composition

- TypeScript/React/Electron desktop app: `src/electron/main.ts`, `src/electron/preload.ts`, `src/renderer/App.tsx`.
- Node-only daemon: `src/daemon/main.ts`, exposed via bins `coworkd`, `coworkd-node`.
- CLI: `src/cli/main.ts`, `src/cli/direct-run.ts`, bins `cowork`, `cowork-os`, `coworkctl`.
- Shared contracts: `src/shared/types.ts`, `src/shared/*`.
- SQLite persistence: `src/electron/database/schema.ts`, `src/electron/database/repositories.ts`.
- Agent runtime: `src/electron/agent/daemon.ts`, `src/electron/agent/executor.ts`, `src/electron/agent/runtime/*`, `src/electron/agent/tools/registry.ts`.
- Providers: `src/electron/agent/llm/*`, including `ollama-provider.ts` and `provider-factory.ts`.
- MCP: `src/electron/mcp/client/*`, `src/electron/mcp/host/*`, `src/electron/mcp/registry/*`, `connectors/*-mcp`.
- Browser: `src/electron/browser/browser-session-manager.ts`, `src/electron/browser/browser-workbench-service.ts`, `src/electron/agent/browser/browser-service.ts`, `src/electron/agent/tools/browser-tools.ts`.
- Office/artifacts: `src/electron/agent/tools/document-tools.ts`, `src/electron/documents/*`, `src/electron/spreadsheet/*`, `src/electron/utils/document-generators/*`.
- Automations: `src/electron/cron`, `src/electron/routines`, `src/electron/automation`, `src/electron/agents/HeartbeatService.ts`.
- Security/policy: `src/electron/security/*`, `src/electron/guardrails/*`, `src/electron/agent/tool-policy-engine.ts`, `src/electron/agent/runtime/PermissionEngine.ts`.

### Architecture Diagram

```text
React renderer
  -> preload bridge (large contextBridge API)
  -> Electron main process
     -> IPC handlers
     -> AgentDaemon / TaskExecutor / TurnKernel
     -> ToolRegistry / ToolScheduler / PermissionEngine
     -> LLM providers, search providers, MCP clients, browser services
     -> SQLite repositories
     -> cron/routines/heartbeat/background services
     -> channel gateway / control plane / remote clients

Node-only daemon
  -> DatabaseManager + SecureSettingsRepository
  -> AgentDaemon
  -> ChannelGateway
  -> CronService
  -> MCPClientManager
  -> optional ControlPlaneServer over WebSocket
```

The daemon is real, but it is not narrow. `src/daemon/main.ts` initializes memory, channels, cron, MCP, x-mentions, strategic planner, and optional control plane. A sidecar would need configuration hardening and/or extraction.

## 3. Feature Reality Matrix

| Feature | Verified status | Evidence |
|---|---|---|
| Local chat | Functional but immature | `AgentDaemon.createTask/sendMessage`, `TaskExecutor`, renderer task UI |
| Ollama | Production-like provider integration | `src/electron/agent/llm/ollama-provider.ts`, provider factory |
| Cloud providers | Production-like breadth, uneven maturity | Anthropic/OpenAI/Gemini/OpenRouter/Bedrock/etc. in `src/electron/agent/llm` |
| Provider fallback | Functional but complex | provider factory, retry and transient error handling in `daemon.ts`/`executor.ts` |
| Model routing | Functional | `modelKey` routing in `daemon.ts`, `ModelCapabilityRegistry.ts` |
| Multi-model mode | Partial/functional | multi-LLM role IDs in `daemon.ts`; UI/docs broader than core evidence |
| Task planning | Functional but coupled | `executor.ts`, `plan-utils.ts`, `turn-kernel.ts` |
| Agent loop | Production-like but oversized | `executor.ts` is extremely large; runtime helpers split some concerns |
| Retries | Functional | transient retry logic in `daemon.ts` |
| Cancellation | Functional | `cancelTask`, child cascade, executor cancellation in `daemon.ts` |
| Parallel tasks | Functional | `queue-manager.ts`, scheduler tests |
| Sub-agents | Functional but immature | `SubAgentOrchestrator.ts`, `spawn-agent.test.ts`; README status is inconsistent |
| Permissions | Functional but must not be trusted wholesale | `PermissionEngine.ts`, `tool-policy-engine.ts`, security tests |
| Approval dialogs | Functional in Electron | `ApprovalRepository`, IPC approval channels, renderer dialogs |
| Blocked commands | Functional rules, not a sandbox by itself | `shell-tools.ts`, `agent-policy.ts`, `admin/policies.ts` |
| Budget controls | Functional | `LoopBudgetPolicy.ts`, guardrail settings |
| Shell execution | Implemented, high risk | `shell-tools.ts`, sandbox fallback logic |
| Filesystem tools | Functional | `file-tools.ts`, path resolution and tests |
| Code editing | Functional | `edit-tools.ts`, registry entries |
| Test execution | Functional through shell | `run_command`, approval/sandbox requirements |
| MCP | Production-like client/host/registry | `src/electron/mcp`, connector packages |
| Memory | Broad functional implementation | `src/electron/memory`, `MemoryRepository`, `DurableContextService` |
| Knowledge graph | Functional but likely immature | `src/electron/knowledge-graph`, KG tables in schema |
| Reusable skills | Production-like content and routing | `resources/skills`, `custom-skill-loader.ts`; 150 skills checked |
| Workflow capture | Functional but product-specific | playbook, heartbeat, routines, reports |
| Scheduled automations | Functional | `cron`, `routines`, `automation` |
| Background execution | Functional and risky | daemon, cron, heartbeat, subconscious services |
| Browser automation | Production-like, high-risk edge cases | Browser V2 docs and `browser-tools.ts`/session manager |
| Visible browser workbench | Implemented | renderer `BrowserWorkbenchView`, workbench service |
| Signed-in browser control | Partial, consent-gated in code | `browser-tools.ts` real-browser consent checks |
| Word generation | Functional new `.docx` creation/preview | `DocumentBuilder`, `document-tools.ts`, docx block parser |
| Excel generation | Functional new `.xlsx` creation/edit preview | `SpreadsheetWorkbookSessionService.ts`, ExcelJS |
| PowerPoint generation | Functional, fallback quality | `pptx-generator.ts`, smoke tests; artifact-tool runtime absent in test |
| PDF generation/editing | Functional new PDFs and region edits | `pdfkit`, `pdf-lib`, `pdf-region-editor.ts` |
| Artifact preview | Functional | file viewer, shared preview types, workbench docs |
| Email integration | Functional but irrelevant/high risk now | mailbox services and channel gateway |
| Remote channels | Functional but excessive | gateway/channel docs and services |
| Device management | Partial/functional but irrelevant | control plane/devices UI |
| Sandboxing | Partial, platform dependent | macOS/Docker/none; Windows falls back unless Docker available |
| VM isolation | Documentation/planned | README says planned; no production VM sandbox found |
| Encrypted storage | Partial | secure settings encrypted, main SQLite not whole-file encrypted |
| Secrets management | Functional local secure settings | `SecureSettingsRepository.ts` |
| Audit history | Functional events/audit logs | task events, audit repositories |
| Rollback/undo | Partial | checkpoints/worktrees/document versions; no universal undo |

## 4. Security Findings

### High: Shell execution and sandbox claims are platform dependent

Evidence: `shell-tools.ts` runs commands through sandbox factory; `sandbox-factory.ts` supports macOS sandbox, Docker, and `NoSandbox`; admin policy defaults disallow unsandboxed fallback, but fallback exists behind env/policy. Windows has no native OS sandbox equivalent in this code path.

Attack scenario: prompt injection convinces the agent to run a command that reads or mutates files outside intended scope if sandbox/approval policy is misconfigured.

Affects us: yes, if reused as sidecar or copied directly.

Mitigation: The Local One must own a C# permission broker and typed tool runtime. Shell should be disabled by default, with per-command approval, workspace path verification, process tree kill, output caps, and no inherited secrets.

### High: Huge renderer preload API creates broad IPC attack surface

Evidence: `src/electron/preload.ts` exposes a very large `electronAPI` with file, task, terminal, browser, mailbox, automation, MCP, voice, worktree, and settings methods. `main.ts` sets `contextIsolation: true`, `nodeIntegration: false`, but `sandbox: false` for main preload because it needs Node built-ins.

Attack scenario: XSS or compromised renderer path invokes privileged IPC methods to open files, create tasks, run terminal actions, or access integrations.

Affects us: no if we reject Electron shell; yes if we embed it.

Mitigation: do not reuse Electron shell. If using sidecar, expose only a small authenticated localhost API designed by The Local One.

### High: Control plane and remote channels are powerful

Evidence: `src/daemon/main.ts` starts optional `ControlPlaneServer`; settings include token/allowed origins; deployment posture blocks unsafe public bind in some cases. Channel gateway initializes in daemon.

Attack scenario: exposed WebSocket/control plane token or misconfigured bind allows remote task creation or tool invocation.

Affects us: yes for sidecar.

Mitigation: sidecar must bind loopback only, require per-install high-entropy token, reject remote channels, and route all permission events to The Local One.

### High: Browser automation can reach signed-in sessions

Evidence: `browser-tools.ts` supports `profile: "user"`, external CDP attach, Browser Use Cloud, upload/download, screenshots; code has explicit consent checks for real profile control.

Attack scenario: malicious page prompt-injects agent to exfiltrate account data from a signed-in browser or submit destructive forms.

Affects us: yes for browser roadmap.

Mitigation: default to isolated workspace browser profile; signed-in profile control must require explicit target/domain/action approval and never be available to background automations.

### Medium: MCP and skill supply chain

Evidence: MCP registry and connectors can launch stdio servers; external skills and plugin packs are supported; declarative connector loader notes VM contexts are not a full security sandbox.

Attack scenario: malicious MCP server/skill gets broad tool access or reads environment variables.

Affects us: yes if adopting MCP/skills.

Mitigation: The Local One should implement signed/allowlisted skill packs, static capability manifests, disabled-by-default external MCP, and per-tool permission prompts.

### Medium: Dependencies report serious vulnerabilities

Evidence: `npm ci --ignore-scripts` reported 58 vulnerabilities: 25 moderate, 26 high, 7 critical. `SECURITY.md` acknowledges `tar` build dependency issues.

Attack scenario: dependency exploit in build/runtime path, especially messaging/browser/crypto packages.

Affects us: mostly if shipping Node sidecar or copied dependencies.

Mitigation: do not vendor dependency tree. Port concepts to C# when possible; if sidecar remains, run npm audit review and dependency pruning.

## 5. Licensing Findings

Legal facts:

- Top-level license is MIT in `LICENSE`.
- `package.json` declares `"license": "MIT"`.
- MIT requires preserving copyright and license notice in copies/substantial portions.
- Dependencies carry their own licenses; the dependency license set was not fully enumerated in this audit.
- Bundled assets, screenshots, icons, fonts, generated templates, skills, and plugin packs appear under the repository but should receive legal review before direct asset reuse.
- README includes a branding note: `"Cowork" is an Anthropic product name. CoWork OS is independent...`. Trademark/naming should not be copied.

Recommendations:

- Direct source copying is legally plausible under MIT with attribution, but do not copy branding, screenshots, or icons without review.
- Prefer conceptual porting for architecture and algorithms to reduce dependency-license and trademark exposure.
- Run formal third-party license inventory before any sidecar distribution.

## 6. Code Quality Assessment

Strongest parts:

1. Broad test inventory: 615 test files found; type-check passes.
2. Clear package scripts and CI coverage for lint/typecheck/test/build.
3. Real typed providers with model capability/pricing support.
4. SQLite repository pattern across many domains.
5. Rich task event/timeline model.
6. Browser V2 design has concrete session/ref/diagnostic abstractions.
7. MCP client/host/registry is unusually complete for an OSS app.
8. Office generation uses established libraries (`docx`, `exceljs`, `pptxgenjs`, `pdfkit`, `pdf-lib`).
9. Permission and policy systems have many dedicated tests.
10. Daemon/CLI path exists and is not documentation-only.

Weakest parts:

1. `src/electron/agent/executor.ts` is extremely large and hard to reason about.
2. Preload API is huge, increasing privileged surface area.
3. Electron, daemon, channels, memory, automation, and control-plane systems are tightly coupled.
4. Runtime security depends on correct configuration and platform sandbox availability.
5. Lint reports 348 warnings.
6. Dependency audit reports 58 vulnerabilities.
7. Tests include Windows/path expectation failures in focused subset.
8. Product scope is sprawling: crypto, payments, channels, devices, enterprise connectors, background autonomy.
9. Documentation sometimes overstates or contradicts implementation maturity.
10. Main SQLite is not whole-file encrypted despite local-first privacy claims.

## 7. Comparison With The Local One

Already implemented better in The Local One:

- Native WinUI shell, Windows tray/hotkey/single-instance/window-placement behavior.
- C# provider abstractions and Ollama streaming/cancellation.
- SQLite conversation persistence, search, and navigation.
- Active-window, clipboard, screenshot context.
- VS Code named-pipe integration.

Already implemented but CoWork OS may improve:

- Model routing and provider metadata.
- Task progress/timeline events.
- Permission categories and approval records.
- Tool contracts and tool registry concepts.

Missing from The Local One and valuable:

- Typed tool runtime.
- Agent task event bus/timeline.
- Skills/workflow packs.
- MCP client/host.
- Browser workbench and Browser V2 ref model.
- Office artifact generation and preview.
- Memory categories beyond conversation storage.
- Scheduled automations.

Irrelevant or too risky:

- Electron shell.
- Remote channel gateway.
- Device/fleet management.
- Crypto wallet/x402/payments/domain registration.
- Self-improvement/autonomy loops as background actors.
- Signed-in browser automation without a Local One permission broker.

## 8. Reuse Decision Matrix

See `docs/research/COWORK_OS_COMPONENT_MATRIX.csv` for subsystem-level decisions.

Summary:

- `PORT_TO_CSHARP`: task timeline, typed tools, permission broker concepts, provider metadata, model router, memory abstractions, skills loader, scheduler model.
- `COPY_WITH_ATTRIBUTION`: small schemas, prompt snippets, tool metadata, test cases, non-branded templates after review.
- `WRAP_AS_SIDECAR`: only MCP connector host or Office/browser prototype services, behind a narrow authenticated API.
- `STUDY_ONLY`: large executor, control plane, Browser V2 implementation, memory experiments.
- `REJECT`: Electron UI replacement, crypto/payments, remote channels, device management, broad autonomous loops.

## 9. Sidecar Feasibility

Command:

- `coworkd` / `coworkd-node` bins map to `bin/coworkd*.js`.
- Source entry is `src/daemon/main.ts`.
- Build command is `npm run build:daemon`.

Protocol:

- Optional local control plane via `ControlPlaneServer` over WebSocket.
- Token is required when enabled.
- Settings support host/port/origins/trust proxy and deployment posture checks.

Answers:

- Authentication present: yes, token-based for control plane.
- Streaming supported: yes through task/event transport and control plane task lifecycle.
- Cancellation supported: yes (`cancelTask`, child cascade).
- Permission requests as structured events: partially, approval repositories/events exist.
- Can The Local One approve/deny actions: not directly without writing an adapter.
- Can tools be restricted: yes in CoWork policy/settings, but The Local One should enforce independently.
- Can filesystem scope be restricted: partially through workspace permissions/path checks; shell/browser weaken this if misconfigured.
- Can providers be configured externally: yes through settings/env import.
- Can unwanted modules be disabled: partially; daemon initializes many broad services by default.
- Fully local with Ollama: likely yes for chat if Ollama/provider configured.
- Runtime overhead: Node + SQLite + potentially browser/MCP/channel services; much heavier than a C# library.
- Coupled to Electron: less than expected for daemon, but imports many `src/electron` services.
- Clean extraction: possible for selected services, not for whole daemon.
- Fork burden: high. A maintained fork is not advisable unless we narrow it to extracted services.

Recommendation: no production sidecar based on full CoWork OS daemon. Consider a temporary prototype sidecar only for MCP connector experimentation or Office artifact generation, with shell/browser/channels disabled.

## 10. C# Porting Feasibility

| Component | Source | Complexity | Notes |
|---|---|---:|---|
| Task event timeline | `timeline-v2.ts`, `timeline-events.ts`, `TaskEventRepository` | Medium | Port event schema and projection tests first. |
| Tool contracts/registry | `runtime-tool-definition.ts`, `tools/registry.ts` | Large | Port as small typed C# interfaces, not one giant registry. |
| Permission broker ideas | `PermissionEngine.ts`, `tool-policy-engine.ts` | Medium | Must be Local One-owned. |
| Provider metadata/router | `llm/*`, `pricing.ts`, `ModelCapabilityRegistry.ts` | Medium | Adapt to current C# provider abstractions. |
| Ollama provider learnings | `ollama-provider.ts` | Small | Local One already has Ollama; borrow edge-case tests only. |
| MCP client | `mcp/client/*` | Large | Use .NET MCP libraries if stable; otherwise minimal stdio client first. |
| Skills loader | `custom-skill-loader.ts`, `resources/skills` | Medium | Port skill manifest + markdown prompt convention. |
| Office generation | `document-tools.ts`, generators | Medium | Use OpenXML SDK/ClosedXML/PdfSharp or keep Node sidecar initially. |
| Browser refs/session model | `browser-tools.ts`, `browser-session-manager.ts` | Large | Conceptual port around Playwright .NET/CDP. |
| Memory services | `memory/*`, database repositories | Very large | Port category model gradually; avoid all background loops. |
| Scheduler/routines | `cron`, `routines` | Medium | Use .NET timers/Quartz-style scheduler with explicit approval. |

Likely order:

1. Task event bus and timeline schema.
2. C# typed tool contracts and permission broker.
3. File/read-only tools and deterministic verification commands.
4. Skills/workflow prompt loader.
5. MCP client.
6. Office artifact generation.
7. Browser workbench.
8. Memory categories and automations.

## 11. Recommended Architecture

```text
The Local One WinUI
  -> The Local One Application/Core
     -> Existing providers, SQLite conversations, context capture, VS Code pipe
     -> Permission Broker and Task Event Bus (new C#)
        -> Typed Tool Runtime (new C#)
           -> Local file/context/Office/browser/MCP tools
           -> Optional narrow Node sidecars
              -> MCP connector subprocesses
              -> Office generator prototype
              -> Browser automation prototype
```

The permission broker remains in C#. All consequential actions produce structured approval requests in WinUI. CoWork-derived code never receives ambient filesystem, shell, browser, or credential access without Local One-issued scoped capabilities.

## 12. Recommended Adoption Phases

Phase 0:

- Add C# task event bus and timeline model.
- Define typed tool contracts and permission categories.
- Add MIT attribution document for copied snippets/test cases.

Phase 1:

- Port small file/search/code-edit tools.
- Port skills manifest/loader concept.
- Add provider metadata and model capability registry.

Phase 2:

- Add MCP stdio client and curated local connector support.
- Prototype Office generation either in C# or a narrow Node sidecar.

Phase 3:

- Build native Browser Workbench with isolated profile and snapshot refs.
- Add scheduled automations with explicit permission profiles.

Phase 4:

- Add memory layers: preferences, project facts, episodic summaries, knowledge, skills.
- Add review-first consolidation, deletion, provenance, and privacy exclusions.

## 13. Components To Reject

- Electron renderer/main/preload shell.
- Full Node daemon as privileged production runtime.
- Remote channel gateway and control plane for initial product.
- Device/fleet management.
- Crypto wallet, x402, payments, domain registration.
- Browser Use Cloud and signed-in browser control by default.
- Background self-improvement/subconscious/autonomy loops.
- Whole dependency tree.
- Branding/assets/screenshots.

## 14. Open Questions

- Which Office path do we want first: pure C# OpenXML/ClosedXML, or narrow Node sidecar?
- Should MCP be a first-class typed tool provider in The Local One core, or an optional integration module?
- What is the minimum useful browser workbench for Windows: WebView2/CDP, Playwright .NET, or both?
- What permission categories should be user-facing in WinUI first?
- How much memory should be opt-in versus automatic?
- Do we need multi-agent/sub-agent work before robust single-agent tools?

## 15. Evidence Appendix

Local One evidence:

- WinUI shell: `PersonalAI.Desktop.WinUI/Views/MainWindow.xaml.cs`, `ViewModels/MainViewModel.cs`.
- Provider contracts: `PersonalAI.Core/Chat/IChatProvider.cs`, `PersonalAI.Providers/Ollama/OllamaChatProvider.cs`.
- SQLite conversations: `PersonalAI.Infrastructure/Persistence/SqliteConversationRepository.cs`.
- Context capture: `PersonalAI.Core/Context/*`, `PersonalAI.Desktop.WinUI/Services/*ContextService.cs`.
- VS Code integration: `PersonalAI.Infrastructure/Ipc/PersonalAiPipeServer.cs`, `integrations/vscode-personalai/src/*`.
- Tray/hotkey/native Windows: `PersonalAI.Desktop.WinUI/Services/WinUiTrayIconService.cs`, `WinUiGlobalHotKeyService.cs`.

CoWork OS evidence:

- Entry points: `package.json`, `src/electron/main.ts`, `src/electron/preload.ts`, `src/daemon/main.ts`, `src/cli/main.ts`.
- License/security/status docs: `LICENSE`, `SECURITY.md`, `SECURITY_GUIDE.md`, `PROJECT_STATUS.md`, `README.md`.
- Daemon/control plane: `src/daemon/main.ts`, `src/electron/control-plane/server.ts`, `src/electron/control-plane/settings.ts`.
- Agent runtime: `src/electron/agent/daemon.ts`, `src/electron/agent/executor.ts`, `src/electron/agent/runtime/turn-kernel.ts`.
- Tools: `src/electron/agent/tools/registry.ts`, `file-tools.ts`, `shell-tools.ts`, `document-tools.ts`, `browser-tools.ts`.
- Permissions: `src/electron/agent/runtime/PermissionEngine.ts`, `src/electron/security/*`, `src/electron/agent/tool-policy-engine.ts`.
- Providers: `src/electron/agent/llm/provider-factory.ts`, `ollama-provider.ts`, `openai-provider.ts`, `anthropic-provider.ts`.
- Persistence: `src/electron/database/schema.ts`, `src/electron/database/repositories.ts`.
- Memory: `src/electron/memory/*`, `src/electron/knowledge-graph/*`.
- MCP: `src/electron/mcp/*`, `connectors/*-mcp`.
- Browser: `src/electron/browser/*`, `src/electron/agent/browser/*`, `src/electron/agent/tools/browser-tools.ts`.
- Office/artifacts: `src/electron/documents/*`, `src/electron/spreadsheet/*`, `src/electron/utils/document-generators/*`.
- Automations: `src/electron/cron/*`, `src/electron/routines/*`, `src/electron/automation/*`.
- Tests: `tests/security/*`, `tests/tools/*`, `src/electron/**/__tests__/*`, `tests/electron/*`.

## 16. Build And Test Results

Commands run in temporary CoWork OS clone:

- `git clone --depth 1 https://github.com/CoWork-OS/CoWork-OS`
  - Success, commit `d4beb0c71fef8bd1c923ed870534920d70064273`.
- `npm.cmd ci --ignore-scripts`
  - Success.
  - Added 1367 packages.
  - Reported 58 vulnerabilities: 25 moderate, 26 high, 7 critical.
  - Lifecycle scripts were disabled.
- `npm.cmd run type-check`
  - Success: `tsc --noEmit`.
- `npm.cmd run lint`
  - Success with warnings: 348 warnings, 0 errors.
- `npm.cmd test`
  - First sandboxed attempt: skills checks passed, Vitest config loading failed due sandbox read denial.
  - Escalated full-suite attempt: timed out after 304 seconds.
- Focused Vitest subset:
  - Ran security, tools, runtime, MCP, database secure settings, spreadsheet/document, PPTX tests.
  - Failed with 9 test failures.
  - Failures were mostly Windows/path-separator assumptions, symlink permission on Windows, MCP mock drift (`syncResourceSubscriptions` missing), and one shell unsandboxed fallback expectation.
  - Many security/runtime tests passed, including policy manager, gateway security, tool groups, permission engine, turn kernel, and tool scheduler.

No CoWork OS app, agent, shell task, browser session, channel gateway, or control plane was launched with user data.
