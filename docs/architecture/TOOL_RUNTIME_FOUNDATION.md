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

`TaskEventBus` is an in-memory broadcast bus. Subscribers receive future events only. Each subscriber has a bounded buffer with a drop-oldest policy, so slow UI consumers do not block tool execution. Publishing uses non-blocking `TryWrite`; if a subscriber falls behind, its oldest queued events may be dropped.

Subscriptions are explicit disposable enumerators. Cancelling enumeration, disposing enumeration, or channel completion removes the subscriber, completes the channel writer, and releases the task filter. Consumers must dispose enumerators; `await foreach` does this automatically when the loop exits. One subscriber leaving does not affect other subscribers. Publication fan-out is serialized, so concurrent publishers produce one well-defined global publication order. Events retained by each subscriber are observed in that same order, and task-specific subscribers preserve that order for their task.

Events are safe for UI display and diagnostics. They include task id, event kind, summary, optional state, optional tool id, progress metadata, and safe error fields. Stack traces and sensitive payloads must not be published as task events.

`TaskEventMetadata` is the public metadata gate:

- metadata must be explicitly supplied;
- keys must be short and non-secret;
- values are bounded and secret-like values are redacted;
- summaries are bounded and deliberately authored;
- tool input objects are never serialized automatically.

## Tool Contracts

Each tool exposes a `ToolDescriptor` with:

- Stable `ToolId`.
- Human-facing name and description.
- Input and output .NET types.
- Required permission categories.
- Permission resource scope and access mode when a tool acts on a resource.
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
5. Cache `AllowForTask` responses only for the same task, tool, permission type, access mode, and normalized resource scope.
6. Publish `ToolStarted`.
7. Execute with cancellation and timeout handling.
8. Publish `ToolCompleted`, `ToolFailed`, `ToolTimedOut`, `ToolCancelled`, or `TaskCancelled` as appropriate.
9. Return a `ToolResult` with safe error fields.

The runtime fails closed if the permission broker throws or the approval surface is unavailable.

## Permission Grants

The per-task grant cache key is:

```csharp
PermissionGrantKey(
    TaskId,
    ToolId,
    ToolPermission,
    PermissionAccessMode,
    NormalizedResourceScope)
```

Resource scopes are normalized by trimming, converting backslashes to slashes, removing trailing slashes, and applying invariant uppercase comparison. Empty or missing scopes are not cacheable. This prevents an unscoped approval from becoming a wildcard approval.

Rules:

- `AllowOnce` is never cached.
- `AllowForTask` is cached only for cacheable scoped grants.
- denials are never cached as approvals.
- multiple required permissions produce independent grant keys.
- grants never cross task ids.
- grants never cross resource scopes or permission types.
- the cache is thread-safe and is cleared when the runtime handles task-cancellation terminal paths.

There is no permanent approval.

## Cancellation And Timeout

Caller/task cancellation and timeout have separate status and event paths.

- Caller cancellation before or during execution returns `ToolExecutionStatus.Cancelled` and publishes `ToolCancelled`.
- A permission decision of `CancelTask` returns `ToolExecutionStatus.Cancelled`, publishes `ToolCancelled`, and also publishes `TaskCancelled`.
- Permission broker cancellation is treated as task cancellation and does not execute the tool.
- Configured timeout returns `ToolExecutionStatus.TimedOut` and publishes `ToolTimedOut` with safe error code `tool_timeout`.
- Timeout is not classified as ordinary cancellation.

Unexpected exceptions are logged through `IToolRuntimeLogger` with task id and tool id. Public `ToolResult` and task events contain only safe error codes and user-facing messages.

## WinUI Hooks

WinUI has:

- `WinUiPermissionBroker` for `ContentDialog` approval.
- `PermissionRequestViewModel` for dialog display data.
- `TaskTimelineViewModel` and `TaskEventItemViewModel` for status display.
- A DEBUG-only `Dev UTC` diagnostic command that invokes only `GetCurrentUtcTimeTool`.

These hooks verify the plumbing without granting the model any tool-calling capability. The diagnostic button is hidden from release builds.

`WinUiPermissionBroker` serializes permission requests with a `SemaphoreSlim`, so only one dialog is presented at a time. Waiting requests remain cancellation-aware. This is one-at-a-time presentation, not a strict FIFO queue.

Dialog outcomes fail closed:

- explicit allow maps to `AllowOnce`;
- explicit allow-for-task maps to `AllowForTask`;
- explicit cancel-task maps to `CancelTask`;
- close, Escape, unavailable owner window, dispatcher failure, broker disposal, dialog exceptions, cancellation, and unknown outcomes map to deny or cancel-task depending on whether the cancellation explicitly represents task cancellation.

The outcome-to-decision mapper is in Core so this behavior can be unit tested without launching WinUI.

## Adding Future Tools

Future tools must:

1. Define explicit input and output records.
2. Declare all required permissions in `ToolDescriptor`.
3. Keep outputs and task events safe for display.
4. Respect cancellation tokens.
5. Provide focused runtime and permission tests.
6. Be manually registered until a reviewed discovery mechanism exists.

Tools that change files, run processes, control browsers, call MCP servers, or leave the machine must require approval and should add path, domain, command, or server-specific scope metadata before shipping.
