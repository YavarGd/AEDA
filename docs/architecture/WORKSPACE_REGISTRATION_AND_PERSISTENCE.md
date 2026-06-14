# Workspace Registration And Persistence

Created: 2026-06-14

This document describes the production workspace registration milestone for The Local One. It replaces the temporary Debug-only registration path with explicit, reviewed, persistent workspace management. The workspace tools remain read-only and are still not exposed to model-issued tool calls.

## Persistence Model

Workspace registrations are stored in SQLite in the existing PersonalAI database. The persisted table stores:

- stable workspace id;
- safe display name;
- canonical root path;
- source;
- date added;
- last successful validation time;
- current validation status;
- optional safe status code;
- read-only policy;
- removal timestamp.

The database does not store file contents, search results, tool outputs, permission grants, credentials, raw exception messages, or stack traces.

## Startup Validation

Persisted workspace records are not trusted just because they were saved previously. On startup the registration service:

1. reads persisted active records;
2. validates every root through the same hardened `WorkspaceRegistry` root checks used by first registration;
3. canonicalizes every root again;
4. rejects missing roots, files, access failures, and reparse-point roots or parents;
5. compares the newly canonicalized path with the stored canonical path;
6. registers only currently valid workspaces into the in-memory runtime registry;
7. marks invalid records with safe structured status;
8. continues loading other workspaces if one record is broken.

Invalid workspaces remain visible for review but are unavailable to workspace tools.

## Trust Boundary

The persistent repository is separate from the in-memory `WorkspaceRegistry`. The registry remains the runtime authority for tool access. A persisted record must be revalidated before it enters the registry.

Runtime tool inputs still use `WorkspaceId`, not raw root paths. The path resolver continues to enforce the approved root boundary for every invocation.

## Folder Picker Flow

Production registration starts from an explicit WinUI folder picker. The picker is initialized with the active window handle. Cancelling the picker does nothing. Picker failures produce a safe `folder_picker_failed` error.

Selecting a folder does not register it immediately. The selection enters a review state first.

## Review Flow

The Settings workspace section shows the selected folder, proposed display name, and read-only policy. The user must explicitly choose Add workspace before registration occurs.

The review text states that workspace tools may request permission to read files under the workspace, that current tools cannot change files, and that symlinks and junctions are rejected.

## Runtime Registry Relationship

Persisted workspace IDs are stable across restarts. `WorkspaceRegistry` supports registration with a caller-supplied `WorkspaceId`, while still running the same validation checks. Duplicate canonical roots deduplicate to the active persisted record rather than creating multiple active workspaces.

Removal unregisters the runtime workspace and marks the persisted record removed.

## Invalid Workspace Behavior

Statuses shown to users include:

- Available;
- Missing;
- Access denied;
- Unsafe link/reparse point;
- Needs review;
- Validation failed.

Only Available active workspaces are registered for tool use. Invalid records can be revalidated or removed.

## Removal Behavior

Removing a workspace requires confirmation. Removal:

- marks the persisted record removed;
- unregisters the runtime workspace;
- invalidates runtime permission grants scoped to that workspace;
- does not delete folders or files;
- does not delete conversation history.

## Permission Invalidation

`TypedToolRuntime` exposes a narrow workspace invalidation API. It removes cached per-task grants whose normalized resource scope starts with the exact normalized `workspace:{WorkspaceId}:` prefix. Other workspace permissions remain untouched.

There are still no permanent approvals.

## Migration Strategy

Workspace persistence uses idempotent SQLite schema creation:

- `CREATE TABLE IF NOT EXISTS persisted_workspaces`;
- unique active-root index on the canonical root key;
- status index for workspace management display.

The active-root key follows OS path comparison expectations: case-insensitive on Windows and ordinal on Unix-like systems. SQL values are parameterized.

## Privacy Behavior

Workspace management UI may display the user-selected root path back to the user. Task events and tool errors must not include file contents, raw exception messages, stack traces, database connection strings, or unrestricted tool payloads.

## Known Limitations

- Reauthorization is conservative: invalid workspaces remain unavailable until the user revalidates or removes them.
- Filesystem identity tracking is limited to canonical path and safety checks in this milestone.
- The UI is implemented inside the existing Settings overlay rather than a separate navigation page.
- Manual picker/restart verification is required on a real WinUI desktop session.

## Deferred Features

Model-issued tool calls remain disabled. This milestone does not add autonomous agents, shell execution, file writes, browser automation, MCP, Office integration, scheduling, long-term memory, automatic VS Code workspace registration, or permanent permissions.
