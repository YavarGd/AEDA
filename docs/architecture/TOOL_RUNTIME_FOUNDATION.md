# Tool Runtime Foundation

Created: 2026-06-13

This document describes the first tool-runtime foundation in The Local One. It is a small C# port of the highest-value CoWork OS ideas: task events, typed tools, a registry, and permission brokering. It is not an autonomous agent.

## Scope

Implemented now:

- Core task event bus and event schema in `PersonalAI.Core.Tasks`.
- Core permission abstractions in `PersonalAI.Core.Permissions`.
- Core typed tool contracts and registry in `PersonalAI.Core.Tools`.
- Infrastructure runtime orchestration in `PersonalAI.Infrastructure.Tools`.
- WinUI approval dialog and task timeline hooks.
- One safe production reference tool: `GetCurrentUtcTimeTool`.

Explicitly not implemented:

- Shell tools.
- Browser tools.
- MCP tools.
- File write tools.
- Schedulers or background automations.
- Memory tools.
- Model-issued tool calls.
- Autonomous planning or sub-agent loops.

## Layering

`PersonalAI.Core` owns reusable contracts only. It has no WinUI, WPF, Windows App SDK, persistence, or HTTP dependencies.

`PersonalAI.Infrastructure` owns the first runtime implementation. The runtime depends on:

- `IToolRegistry`
- `ITaskEventBus`
- `IPermissionBroker`

`PersonalAI.Desktop.WinUI` owns human approval UX and task-status presentation. The WPF fallback does not participate in the runtime yet and remains isolated.

## Event Bus

`TaskEventBus` is an in-memory broadcast bus. Subscribers receive future events only. Each subscriber has a bounded buffer with a drop-oldest policy, so slow UI consumers do not block tool execution.

Events are safe for UI display and diagnostics. They include task id, event kind, summary, optional state, optional tool id, progress metadata, and safe error fields. Stack traces and sensitive payloads must not be published as task events.

## Tool Contracts

Each tool exposes a `ToolDescriptor` with:

- Stable `ToolId`.
- Human-facing name and description.
- Input and output .NET types.
- Required permission categories.
- Risk metadata.
- Approval requirement.
- Timeout guidance.
- State/network/sensitivity flags.

Concrete tools should normally derive from `TypedToolBase<TInput,TOutput>` to get input type validation at the contract boundary.

## Runtime Behavior

`TypedToolRuntime` performs the following sequence:

1. Publish `ToolRequested`.
2. Resolve the tool from `IToolRegistry`.
3. Validate typed input.
4. Request permission when the descriptor requires approval.
5. Cache `AllowForTask` responses for the same task and tool id.
6. Publish `ToolStarted`.
7. Execute with cancellation and timeout handling.
8. Publish `ToolCompleted`, `ToolFailed`, or `TaskCancelled`.
9. Return a `ToolResult` with safe error fields.

The runtime fails closed if the permission broker throws or the approval surface is unavailable.

## WinUI Hooks

WinUI has:

- `WinUiPermissionBroker` for `ContentDialog` approval.
- `PermissionRequestViewModel` for dialog display data.
- `TaskTimelineViewModel` and `TaskEventItemViewModel` for status display.
- A small `UTC` development command that invokes only `GetCurrentUtcTimeTool`.

These hooks verify the plumbing without granting the model any tool-calling capability.

## Adding Future Tools

Future tools must:

1. Define explicit input and output records.
2. Declare all required permissions in `ToolDescriptor`.
3. Keep outputs and task events safe for display.
4. Respect cancellation tokens.
5. Provide focused runtime and permission tests.
6. Be manually registered until a reviewed discovery mechanism exists.

Tools that change files, run processes, control browsers, call MCP servers, or leave the machine must require approval and should add path, domain, command, or server-specific scope metadata before shipping.
