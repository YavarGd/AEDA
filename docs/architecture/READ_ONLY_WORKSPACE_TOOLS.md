# Read-Only Workspace Tools

Created: 2026-06-13

This document describes the first production tool family for The Local One:

- `GetWorkspaceInfoTool`
- `ListDirectoryTool`
- `ReadTextFileTool`
- `SearchWorkspaceTextTool`

These tools are read-only, permission-gated, bounded, and scoped to explicitly registered workspace roots. They are not connected to model-issued tool calls.

## Trust Boundary

A workspace must be explicitly registered before any workspace tool can read it. Tool inputs use a `WorkspaceId`; raw root paths are not the identity passed between UI and tools.

Workspace tools do not write files, delete files, move files, create directories, execute commands, use PowerShell, run Git, call MCP, control browsers, schedule work, or access memory.

## Workspace Registration

`WorkspaceRegistry` is an in-memory registry for this milestone. Registration:

- canonicalizes the requested root with `Path.GetFullPath`;
- rejects nonexistent roots;
- rejects files when a directory is required;
- rejects roots that are reparse points;
- deduplicates equivalent canonical roots;
- stores a stable `WorkspaceId`, display name, canonical root path, registration timestamp, read-only policy, and optional source.

Workspaces are not persisted yet.

## Path Canonicalization

All relative paths pass through `WorkspacePathResolver`.

The resolver:

- rejects null bytes and invalid path characters;
- rejects rooted paths and UNC paths;
- on Windows, rejects colon-containing path segments such as drive-relative paths and alternate data stream syntax;
- combines the approved root with the relative path;
- canonicalizes with `Path.GetFullPath`;
- verifies the target remains inside the approved root;
- uses OS-appropriate path comparison;
- prevents prefix confusion such as `C:\Project` versus `C:\ProjectSecrets`;
- rejects traversal that escapes the root, including mixed slash forms;
- requires `WorkspacePathKind.Any` targets to already exist as either a file or directory;
- rejects unknown or unsafe path states fail-closed.

The normalized permission resource scope is:

```text
workspace:{WorkspaceId}:{normalized-relative-path}
```

The runtime normalizes this again for permission grant keys.

## Symlinks And Junctions

The first milestone uses a conservative policy: any traversed path component that is a reparse point, symbolic link, or junction is rejected. Tools do not silently follow links.

This may reject legitimate linked folders, but it prevents link-based workspace escapes while the permission model is still new.

## Permission Scopes

Workspace tools implement invocation-specific permission requirements. After validation and before execution, the runtime asks each tool for the actual resource scope for that invocation.

The grant key remains:

```text
TaskId + ToolId + ToolPermission + PermissionAccessMode + NormalizedResourceScope
```

A grant for one file does not authorize another file. A grant for one directory or search root does not authorize another. A grant for one workspace never authorizes another workspace. `AllowOnce` is not cached, and there are no permanent approvals.

## Limits

Default limits are centralized in `WorkspaceToolOptions`:

- directory entries: default 200, maximum 1000;
- readable file bytes: 5 MB;
- read characters: maximum 500,000;
- search results: default 100, maximum 500;
- searched files: maximum 10,000;
- search depth: maximum 32;
- search query length: maximum 512;
- line preview length: maximum 300.

Limits are not user-configurable in WinUI yet.

## Binary Detection

Text-file reads and search use a bounded sample. Files are rejected as binary when:

- UTF-8/no-BOM samples contain null bytes;
- supported decoding and control-character heuristics indicate non-text content;
- file size exceeds the configured maximum.

The detector is conservative and does not rely only on file extension.

## Encoding

Supported encodings:

- UTF-8;
- UTF-8 with BOM;
- UTF-16 little-endian with BOM;
- UTF-16 big-endian with BOM.

The implementation does not use the system default legacy encoding. Decoding errors are reported in output with `HadDecodingErrors`.

## Search Behavior

Search is literal text search only. Regular expressions are not accepted.

Defaults:

- bounded recursion;
- bounded files scanned;
- bounded results;
- bounded line previews;
- binary and oversized files skipped;
- inaccessible or disappearing files skipped safely.

Excluded directories:

- `.git`
- `bin`
- `obj`
- `node_modules`
- `.vs`
- `.idea`

`FilePattern` supports only a small filename glob shape such as `*.cs`, `*.md`, or `*.json`. Rooted patterns, traversal, directory separators, and malformed patterns are rejected.

## Task-Event Privacy

Task events use safe authored summaries. They do not include:

- file contents;
- search-result lines;
- raw tool input objects;
- unrestricted absolute paths;
- raw exception messages;
- stack traces.

Technical exceptions belong only in the technical logger.

## WinUI Developer Harness

WinUI has a DEBUG-only developer section for manual testing. It requires explicit workspace-root registration and then invokes workspace tools through `TypedToolRuntime`, so permission dialogs still appear normally. The harness is hidden in Release builds.

## Known Limitations

- Workspace registrations are in-memory only.
- Symlinks and junctions are rejected rather than resolved.
- Search is literal only.
- File pattern support is intentionally small.
- There is no workspace removal UI yet.
- WinUI developer tooling is a minimal diagnostic harness.

## Deferred Model Tool Calls

Model-issued tool calls remain deferred. The current milestone proves the safe workspace boundary, typed invocation contracts, permission scoping, and bounded execution before any model is allowed to request these tools.
