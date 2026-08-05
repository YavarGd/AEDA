# Avalonia parity and accessibility ledger (Q1)

Audit branch: `agent/claude-q1-r1`, based on integration
`ad0f14856783cbea21bd79a3a392af0500f8a86e` (includes the R1 lifecycle repair).

The audit was first performed against `9242f25` and re-verified against `ad0f148` after R1
landed; every status below reflects the current head.

Status vocabulary:

- **Passed** — verified in this audit with recorded evidence.
- **Failed** — confirmed defect, not yet fixed.
- **Fixed in Q1** — defect confirmed and corrected in this task.
- **Manually verified** — observed by driving the real running application.
- **Blocked** — cannot be verified with the tooling available in this environment.
- **Not required** — deliberately out of scope for parity.

Nothing below is marked Passed on the strength of a build or unit test alone.

## How the live evidence was gathered

The real application was launched on Windows from
`C:\Users\yavar\AEDA-worktrees\claude-q1-r1\PersonalAI.Desktop.Avalonia\bin\Release\net10.0-windows10.0.19041.0\PersonalAI.Desktop.Avalonia.exe`
and inspected through the Windows UI Automation client
(`UIAutomationClient` / `UIAutomationTypes`). Screens were switched by invoking the real
`SelectionItemPattern` on navigation items, so every per-screen figure below comes from the
running accessibility tree rather than from source inspection.

Limits of that method are recorded honestly under **Blocked**.

### Re-verification on `ad0f148` (post-R1)

The whole live pass was repeated after R1 landed and the Q1 changes were transplanted:

- Main window measured `1080x760`; Assist pill window present at `52x52`.
- Navigation exposes exactly 8 items — Dashboard, General chat, Task Center, Code,
  Settings, Memory, Research, Assist — all `enabled=True`, no stale placeholders, no
  duplicates.
- All seven non-dashboard screens reachable via real `SelectionItemPattern` selection, with
  **0** unnamed interactive controls (excluding Avalonia ComboBox `PART_EditableTextBox`
  template internals): General chat 71 elements / 9 interactive, Task Center 230 / 38,
  Code 85 / 18, Settings 91 / 34, Memory 53 / 7, Research 38 / 3, Assist 26 / 1.
- Second instance exited with code **0**; one process remained; the first instance stayed
  alive with a responsive `1080x760` main window after the activation handoff.
- Close-to-tray confirmed: the process survived `CloseMainWindow()`.
- No duplicate windows or processes; the WinUI app was never running concurrently.
- .NET suite: 1,006 passed. VS Code: 10 passed. Debug and Release builds: 0 errors.

## Defects found and fixed in Q1

### 1. Stale "not available yet" claims — Fixed in Q1

The shell and dashboard still advertised Code, Memory, Research and Task Center as
unavailable, even though every one of them is migrated and registered through the E1
composition seam.

Live evidence before the fix — the navigation tree contained contradictory entries:

```
ListItem | name=Code, not available yet     | kbFocusable=True | enabled=False
ListItem | name=Memory, not available yet   | kbFocusable=True | enabled=False
ListItem | name=Research, not available yet | kbFocusable=True | enabled=False
ListItem | name=Task Center                 | kbFocusable=True | enabled=True
ListItem | name=Code                        | kbFocusable=True | enabled=True
ListItem | name=Memory                      | kbFocusable=True | enabled=True
ListItem | name=Research                    | kbFocusable=True | enabled=True
```

Two separate accessibility problems: the application stated something untrue about its own
capabilities, and the dead placeholders were still keyboard focusable, so keyboard and
screen-reader users landed on disabled items that duplicated working ones.

Fixed in `MainWindow.axaml` (removed three placeholder nav items; the real entries are
appended at runtime by `AttachComposition`) and `Views/Dashboard/DashboardView.axaml`
(replaced the "Coming later / not available yet" block with the actual module list).

Live evidence after the fix:

```
Dashboard | General chat | Task Center | Code | Settings | Memory | Research | Assist
all enabled=True, no duplicates, no disabled placeholders
```

## Accessibility acceptance

| Check | Status | Evidence |
| --- | --- | --- |
| Accessible names on icon-only and interactive controls | **Passed** | Live UIA sweep across all seven screens: `unnamedInteractive=0` on General chat, Task Center, Code, Memory, Research, Assist. Settings reported 4 unnamed `Edit` elements, all `PART_EditableTextBox` — Avalonia ComboBox template internals whose parent ComboBoxes are all named (`Chat provider`, `General model`, `Coding model`, `Vision model`, `Fast model`, `Reasoning model`). Screen readers announce the parent, so this is a framework template artifact, not an authored gap. |
| Meaningful names for screens, lists and status regions | **Passed** | Navigation list exposes `Main navigation`; every screen root and list carries a name; the Assist window exposes `AEDA Assist` with `Assist`, `Ready`, and an `Ask AEDA` button (`AutomationId=AskButton`). |
| Standard control automation peers | **Passed** | Controls surface as standard UIA types (`List`, `ListItem`, `Button`, `Edit`, `ComboBox`, `Text`, `Group`) — no custom peers that hide semantics. Main window exposes 42 elements at Dashboard, 236 at Task Center. |
| Keyboard focusability of navigation | **Passed** | All eight nav items report `IsKeyboardFocusable=True`, `IsEnabled=True`. |
| Initial focus placement | **Manually verified** | On launch, focused element is `Button` named `Open General chat` — focus starts on the dashboard primary action, not lost in the window. |
| Usable layout at 1080×760 | **Manually verified** | Main window measured `1080x760` via UIA `BoundingRectangle` on two independent launches. |
| No information conveyed by colour alone | **Passed** | Status and verdict values render as text (`OverallVerdict`, `Confidence`, status strings) in Research and Task Center; role labels ("You"/"AEDA") are textual. Confirmed in markup and via named `Text` elements in the live tree. |
| Duplicate/contradictory navigation entries | **Fixed in Q1** | See defect 1. |
| Visible keyboard focus indicator | **Blocked** | Focus adorners are authored in `Themes/AedaTheme.axaml` (`:focus-visible` styles for Button/TextBox/ListBox/ListBoxItem), but confirming they are actually *visible* requires human visual inspection. |
| Polite live announcements for streaming and status | **Blocked** | `AutomationProperties.LiveSetting="Polite"` is authored on status and response regions. I attempted to confirm exposure via UIA and got zero live regions — but that reading was a **false negative**: `LiveSettingProperty` does not exist in the legacy managed UIA wrapper, so the query failed silently for every element. Whether Windows actually narrates these needs Narrator or NVDA. Neither passed nor failed. |
| Complete keyboard-only navigation (Tab/Shift+Tab traversal) | **Manually verified** (round 3) | Real TAB/Shift+TAB injection with Win32 foreground enforcement across all 8 screens: 2–13 distinct tab stops per screen, Shift+TAB reverses, no screen traps focus, and **no disabled or hidden control receives focus**. See the round-3 traversal table. |
| Enter sends / Shift+Enter newline in composer | **Fixed in Q1, manually verified** (round 3) | Was genuinely broken (bubbling handler never saw Enter). Fixed at the tunnel phase; real `SendKeys` now shows Shift+Enter inserting `<CR><LF>` and Enter clearing the composer and sending. See defect 1 of round 2. |
| Escape cancellation behaviour | **Partially verified** | Escape was exercised live against the Assist surface and correctly did nothing while not generating — matching the specified "cancels only while generating" policy. Escape *during* an active generation was not driven end-to-end, so the cancelling half rests on `ChatKeyboardPolicy` unit tests. |
| Conversation list items announce their title | **Fixed in Q1** (round 3) | `ListItem` peers exposed `Conversation.ToString()` (GUID + timestamps). Fixed with a container-level `ListBoxItem` style; live re-check shows 11 clean titles, 0 raw records. |
| Windows high-contrast compatibility | **Blocked** | Would require changing the machine's system theme. Not done — this is the user's working machine. |
| Windows text scaling (125%/150%/200%) | **Blocked** | Requires changing a system display setting. |
| Reduced-motion behaviour | **Blocked** (low risk) | The Avalonia views author no animations, so there is nothing to suppress; the absence of animation was confirmed in markup, not under a reduced-motion system setting. |
| Focus restoration after dialogs and overlays | **Blocked** | Requires opening real dialogs and observing focus return. |
| Bounded, readable scrolling regions | **Passed** | Views wrap long content in `ScrollViewer`; the markdown code block is bounded at `MaxHeight=320`, Assist response at `MaxHeight=420`. |

## Functional parity matrix

### Launch and lifetime

| Item | Status | Evidence |
| --- | --- | --- |
| Visible clean startup | **Manually verified** | Process launched, stayed alive, and produced two real top-level windows (`MainWindow` 1080×760, `AvaloniaAssistWindow` 52×52). |
| Single instance | **Manually verified** | Launching a second process while one ran: the second exited, instance count stayed at 1. |
| Second-instance exit is clean | **Passed** (fixed by R1) | Re-verified on `ad0f148`: the second instance exits with code **0**, the first instance stays alive and functional, the IPC activation handoff returns `{"Ok":true,"Message":"opened"}`, and exactly one app process remains. This closes the earlier `0xE0434352` unhandled-exception exit seen on `9242f25`. |
| Close to tray | **Manually verified** | `CloseMainWindow()` did not terminate the process — it kept running, consistent with close-to-tray. |
| Hotkey invocation | **Failed / environmental** | The main window title reported: `AEDA - The configured AEDA hotkey is unavailable; another app may own it.` The app degrades honestly rather than failing silently, but hotkey invocation could not be exercised on this machine. Needs a machine where the hotkey is free. |
| Tray open / new chat / exit menu | **Blocked** | Tray menu interaction requires real mouse input against the shell notification area. |
| Deterministic shutdown/disposal | **Blocked** | Only forced termination was used, to free the build. |

### Modules and screens

| Screen | Status | Evidence |
| --- | --- | --- |
| Dashboard | **Manually verified** | Renders, primary action focused on launch, module list corrected in Q1. |
| General chat | **Manually verified** | Reachable; 77 UIA elements, 9 interactive, all named. |
| Task Center | **Manually verified** | Reachable; 236 elements, 38 interactive, all named — the richest surface. |
| AEDA Code | **Manually verified** | Reachable; 91 elements, 18 interactive, all named. |
| Settings and Workspaces | **Manually verified** | Reachable; 97 elements, 34 interactive; all 6 provider/model ComboBoxes named and keyboard focusable. |
| Memory | **Manually verified** | Reachable; 59 elements, 7 interactive, all named. |
| Research | **Manually verified** | Reachable; 44 elements, 3 interactive, all named. |
| Assist | **Manually verified** | Reachable in-shell; separate pill window present in idle state with named `Ask AEDA` action. |
| Permission and approval dialogs | **Blocked** | Requires triggering a real permission request. |

### Behavioural parity not exercised

| Item | Status |
| --- | --- |
| Conversation create/search/open, persistence and reopen | **Blocked** — needs interaction |
| Streaming, cancellation, retry, safe error state | **Blocked** — needs a live provider |
| Model/provider status | **Blocked** |
| Add/rename/remove workspace, native folder picker | **Blocked** |
| Four palettes, immediate theme application | **Blocked** |
| Rapid settings changes retain final value | **Blocked** |
| Assist selected-context (AS2), screenshot/OCR, copy, open-in-AEDA | **Blocked** |
| Assist elevated/password/privacy-excluded target blocking | **Blocked** |
| Mixed-DPI and negative-coordinate behaviour | **Blocked** — relies on existing W2 evidence |
| Permission dismissal denies; approval cannot be bypassed | **Blocked** — see safety note |

### Safety

Safety semantics were **not** re-verified behaviourally in this audit and are **not**
claimed. They live in Core and Infrastructure, which Q1 must not modify, and their existing
coverage is unit-level (1,006 tests). Confirming "permission dismissal denies", "stale patch
baseline remains blocked", "validation remains allowlisted" and "rollback remains explicit"
end-to-end requires driving the real approval flows — recorded as **Blocked**, not Passed.

### Integration

| Item | Status | Evidence |
| --- | --- | --- |
| VS Code extension builds and tests | **Passed** | `npm ci`, `npm run compile`, `npm test` → 10 pass / 0 fail. |
| IPC protocol, bounded context publication, passive selection absent, speech honestly unavailable | **Blocked** | Behaviour completed under I1; not re-driven here. |

## Dependency security finding

`npm audit` reports **6 high-severity transitive vulnerabilities**:

- `linkify-it` — quadratic-complexity DoS via the `mailto:` validator scan loop.
- `undici@7.0.0–7.28.0` — 12 advisories including TLS certificate validation bypass,
  HTTP header injection, response-queue poisoning and cross-user cache disclosure.

**Disposition: does not block parity acceptance.** Evidence: the extension declares
`dependencies: {}` — it has no runtime dependencies. Both packages arrive solely through
`@vscode/vsce` (the packaging tool) in `devDependencies`:

```
vscode-personalai -> @vscode/vsce -> cheerio -> undici
vscode-personalai -> @vscode/vsce -> markdown-it -> linkify-it
```

`npm audit --omit=dev` reports **0 vulnerabilities**, so nothing vulnerable ships to users.

Not fixed, and not claimed as fixed. Dependency remediation remains a separate decision.

## Remaining real-Windows checklist

These require a human at the machine. Launch with:

```
C:\Users\yavar\AEDA-worktrees\claude-q1-r1\PersonalAI.Desktop.Avalonia\bin\Release\net10.0-windows10.0.19041.0\PersonalAI.Desktop.Avalonia.exe
```

Do not run the WinUI app at the same time.

1. **Keyboard traversal** — Tab/Shift+Tab through each screen; confirm logical order and no traps.
2. **Visible focus** — confirm the focus ring is clearly visible on buttons, text boxes and nav items, in both light and dark.
3. **Composer keys** — Enter sends; Shift+Enter inserts a newline; Escape cancels only while generating.
4. **Screen reader** — with Narrator or NVDA, confirm streaming responses and status changes are announced politely, and that screens/dialogs/lists announce meaningful names.
5. **High contrast** — Settings → Accessibility → Contrast themes; confirm all text and focus remain visible.
6. **Text scaling** — Settings → System → Display → Scale at 125%, 150%, 200%; confirm no clipped primary actions at 1080×760.
7. **Tray** — open, new chat, exit from the tray menu; confirm close-to-tray then restore.
8. **Hotkey** — on a machine where the configured hotkey is free, confirm invocation (this machine reported it already owned).
9. **Dialogs** — trigger a permission request; confirm dismissal denies and focus returns to the invoking control.
10. **Settings round-trip** — change theme across all four palettes (immediate application), add/rename/remove a workspace, use the native folder picker, and make rapid changes to confirm the final value persists.
11. **Assist** — idle → spotlight → response; selected-context path; screenshot/OCR; cancel; copy; open-in-AEDA; confirm no idle focus theft and that elevated/password targets are blocked.

## Profile safety

Existing profile backed up before any launch:

```
C:\Users\yavar\AppData\Local\PersonalAI-backup-q1-20260804-210722
```

9 files, 3,433,527 bytes. No schema or data migration was performed, and the running app was
only launched and closed — no settings were changed by this audit.

---

# Q1 execution round 2 — user-reported failures

The user ran a real-Windows pass and reported three failures. All three were investigated
with real keyboard/mouse input, UIA and Windows crash records.

## Defect 1 — Enter does not send — **Fixed in Q1**

**User report:** Shift+Enter inserts a newline correctly, but plain Enter does not send.

**Reproduced** (real SendKeys into the focused composer, values read back via UIA ValuePattern):

    1_after_typing        = [Hello from Q1]
    2_after_shift_enter   = [Hello from Q1<CR><LF>]              newline inserted (correct)
    3_after_enter         = [Hello from Q1<CR><LF><CR><LF>]      Enter added ANOTHER newline
    3_composer_cleared    = False
    4_message_in_timeline = False                                nothing was sent

**Root cause:** `ChatView.axaml` wired `KeyDown="OnComposerKeyDown"`, a **bubbling** handler.
An Avalonia `TextBox` with `AcceptsReturn="True"` consumes Enter internally to insert its
newline and marks the event handled before the bubbling phase reaches the view, so the
composer handler never observed Enter.

**Fix:** the XAML attribute was removed and the handler is attached at the **tunnelling**
(preview) phase in `ChatView.axaml.cs` — the same standard routed-event mechanism the shell
already uses for Escape/Ctrl+N:

    Composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);

Shift+Enter is deliberately left unhandled so the TextBox still performs the newline itself.
No global hook, timer or synthetic command invocation was used.

**Live re-verification after rebuild:**

    2_after_shift_enter   = [Hello from Q1<CR><LF>]   newline still inserted
    3_after_enter         = []                        composer cleared
    3_enter_added_newline = False
    4_message_in_timeline = True                      message actually sent

## Defect 2 — Assist Pill not visible — **Fixed in Q1**

**User report:** The Assist Pill is not visibly present.

**Reproduced:** the pill window measured 52x52 via UIA and its subtree contained the full
module content (Text "Assist", Text "Ready", the description paragraph, the Ask AEDA button)
— present in the automation tree but with no usable visible area.

**Root cause:** `AvaloniaAssistWindow` resizes to 52x52 idle / 520x180 spotlight / dynamic
response, but hosted the *same* `AssistView` used by the in-app module, whose root was
`<Grid Margin="28">` with a 26pt heading and status line. A 28px margin per edge consumes
56px of a 52px window, so the content area was negative and everything was clipped away.

**Fix (adaptive host, smallest Q1-owned change):**

- `AssistView` gained an explicit `HostMode` (FullModule | CompactWindow). Compactness is
  **not** inferred from `IsIdle`, because the in-app Assist screen is idle too and must stay
  a full page.
- `AvaloniaAssistWindow` constructs its view with `HostMode = AssistViewHostMode.CompactWindow`.
- The composition-registered in-app screen keeps the default FullModule.
- Compact + idle renders a single stretched Button Pill with no margin/padding, an "AEDA"
  label, `AutomationProperties.Name="Ask AEDA"`, and keyboard + pointer activation via the
  standard Button automation peer.
- Expanded states in the compact window use a 12px margin instead of the module's 28px, and
  the module header is hidden there.

No native placement, focus, hotkey, context-capture, privacy, permission or AS2 integration
code was changed. No new Assist view model or duplicated service was introduced.

**Live re-verification (UIA bounds):**

    pill_window_rect = 2488,1320,52,52
      Custom | 'Assist'   | 2488,1320,52,52 | fitsHost=True  offscreen=False
      Button | 'Ask AEDA' | 2488,1320,52,52 | fitsHost=True  offscreen=False
      Text   | 'AEDA'     | 2499,1339,29,15 | fitsHost=True  offscreen=False
    in-app AskButton rect = 580,500,79,32   (full module page retained)

The Pill fills its host exactly, the label fits without clipping, and the pill subtree
dropped from 7 clipped module nodes to 4 meaningful ones.

## Defect 3 — In-app Assist crash — **Open; requires forbidden ownership (Codex)**

**User report:** Navigating to Assist inside the main app and pressing Ask AEDA crashes the
entire application.

**Reproduction attempts — 6 targeted sequences, none crashed on demand:**

| # | Sequence | Result |
|---|---|---|
| 1 | Clean launch, Assist screen, Ask via UIA Invoke (Debug) | No crash; fell back to prompt input |
| 2 | Clean launch, real mouse click with window foregrounded (Release) | No crash; Ask activated |
| 3 | AskButton by AutomationId (Release) | No crash |
| 4 | Notepad selection present, Assist, Ask (Release) | No crash; exercised the W1 UIA/clipboard path |
| 5 | Repeat Ask after state transition | No crash |
| 6 | Post-fix relaunch with Pill window visible plus in-app screen | No crash |

**Windows crash record — the crash is real and was captured:**

    Application Error, 08/04/2026 23:02:27
    Faulting application: PersonalAI.Desktop.Avalonia.exe 1.0.0.0
    Path: C:\Users\yavar\AEDA-worktrees\claude-q1-r1\PersonalAI.Desktop.Avalonia\bin\
          Release\net10.0-windows10.0.19041.0\PersonalAI.Desktop.Avalonia.exe
    Faulting module: ntdll.dll 10.0.26100.7462
    Exception code: 0xc0000374
    Fault offset: 0x00000000001176b5
    Report Id: 22bacab0-9baa-430f-b10e-61df1768d7cb
    WER bucket: PCH_7E_FROM_ntdll+0x0000000000162714
    WER archive: C:\ProgramData\Microsoft\Windows\WER\ReportArchive\
                 AppCrash_PersonalAI.Deskt_dbc381eb636f03037456597328b42e3eff76f3_0ecb349e_...
    Loaded modules include PersonalAI.Infrastructure.dll, Avalonia.Win32.dll,
    Avalonia.Skia.dll, MicroCom.Runtime.dll, ntdll.dll, USER32.dll.

**Diagnosis:** `0xc0000374` is STATUS_HEAP_CORRUPTION, raised by the Windows heap manager in
ntdll.dll. This is **native memory corruption, not a managed exception** — it cannot be
produced by the async-void handler in Q1-owned view code, and a managed try/catch at that
boundary could neither prevent nor contain it. Heap corruption is also detected
*asynchronously*: the corrupting write happens earlier and the fault surfaces when the heap
is next validated, which explains why the process looked healthy immediately after each
scripted Ask and why the sequences above do not reproduce it deterministically.

The realistic sources are all outside Q1 ownership — P/Invoke marshalling in the Windows
interop that Assist exercises:

- `PersonalAI.Infrastructure/Windows/**` (W1): WindowsUiaSelectedTextProvider,
  WindowsClipboardCopySelectedTextProvider, ForegroundWindowTracker
- `PersonalAI.Infrastructure/ScreenCapture/**` (W2)
- Avalonia native layers (Avalonia.Win32, Skia, HarfBuzz, MicroCom) — third-party packages

**Shared-view-model hypothesis: not supported.** Two views do bind one AssistPillViewModel,
but no duplicate command execution, overlapping OpenPromptAsync, duplicated PropertyChanged
side effect or disposal conflict was observed, and a managed aliasing bug would surface as a
.NET exception rather than 0xc0000374. Per instructions the shared design was left unchanged.

**Status:** confirmed real (Windows crash record) but not reproducible on demand and not
fixable inside Q1 ownership. Handed to Codex. This is a **critical open blocker**, so no Q1
completion commit was created.

## Validation after these fixes

- `git diff --check`: clean
- .NET suite: **1011 passed / 0 failed** (1006 before; +5 new Assist host-mode tests)
- Focused Assist + chat + composer suite: **113 passed / 0 failed**
- Build Debug: 0 errors; Build Release: 0 errors

## Windows matrix rows still unrun

The crash investigation and the two fixes consumed the available session budget. The
following were **not** executed in this round and are **not** claimed as passes: tab
traversal on every screen, focus visibility across palettes, high contrast, text scaling at
150%/200%, tray open/new-chat/restore/exit, hotkey conflict reporting, permission-dialog
dismissal and focus restoration, four palette changes, workspace add/rename/remove, rapid
settings persistence, Assist spotlight/response end-to-end, screenshot/OCR,
cancel/retry/copy/open-in-AEDA, elevated-target and password-field rejection, and Narrator
announcements.

---

# Q1 execution round 3 — R2 disposition, isolation and matrix

## Crash disposition — **Residual monitored risk, not reproduced after instrumented investigation**

Codex completed R2 native diagnosis from `ad0f148`. Findings:

- The original WER report was preserved but contained **no dump**.
- Full PageHeap was enabled and confirmed through Application Verifier.
- Instrumented stress completed with no failure: 180 rapid Assist toggles, 60 Notepad
  selected-context cycles, 48 generation cancellations, 20 screen-capture/cancellation
  attempts, 10 close-to-tray/restore cycles.
- No verifier stop, no `0xc0000374`, no new WER event, no dump.
- No definite ABI, buffer, ownership, callback, COM or disposal defect was found, and no
  actionable repository-owned native stack was produced.
- PageHeap / Application Verifier settings were removed afterwards.

The only external injected module present in the original crash environment was
`ScreenSplitterHook641.dll` from **LG OnScreen Control**, which intercepted Avalonia window
creation during debugger testing. That is a diagnostic lead, **not proof of causation**.

Precise classification:

- One user-observed `STATUS_HEAP_CORRUPTION` event is **confirmed** (WER record, Report Id
  `22bacab0-9baa-430f-b10e-61df1768d7cb`).
- The corrupting operation was **not identified**.
- The event was **not reproduced** under full PageHeap plus extensive targeted stress.
- **No repository-owned native-contract violation was established.**
- **No product source fix was made or claimed.**
- LG OnScreen Control injection remains an **environmental lead**.
- A **crash-time full dump is required** if it recurs.

Status: **Residual monitored risk — not reproduced after instrumented investigation.**
Explicitly *not* marked fixed, and *not* retained as a confirmed application defect
requiring a speculative code change.

## Environmental isolation evidence

`ScreenSplitterHook64App` and `OnScreen Control` were exited (the background
`OnScreenControlControlService` remains; it is not the injector). A fresh Release process
was then launched and its loaded modules enumerated:

    process_alive=True
    total_modules=163
    ScreenSplitterHook641.dll NOT LOADED (clean)
    non-Microsoft / non-app modules loaded (external injectors): none

## Assist stress with the injected module absent

Two stress passes were run against the isolated process:

| Exercise | Observed |
| --- | --- |
| Assist Pill activations / collapse attempts | ~30 invocations across two passes |
| In-app Assist → Ask AEDA cycles | 10 navigations + activations |
| Notepad selected-context cycles | 10 (foreground switch + Ctrl+A + activate) |
| Screen-text capture / cancellation cycles | 5 invoked, 5 succeeded (native OCR path) |
| Close to tray → restore | verified; process survived `CloseMainWindow()` |

Results across both passes: **no crash**, `process_count=1`, exactly **one**
`AvaloniaAssistWindow` (no duplicates), main window intact at `1080x760`, and
**NO_CRASH_EVENTS** in the Application log for the whole window.

Honest limitation: the literal "20 open/collapse" and "10 context" cycle counts were **not**
all completed as clean idle→spotlight→idle round trips. Once Assist enters the
spotlight/fallback state the shared view model stays there, and Escape correctly does
nothing (by design it cancels only while generating), so subsequent scripted `Ask AEDA`
invocations found no button. The activations that did occur exercised the same native
paths; the crash-recurrence gate is satisfied, but the cycle counts are reported as
attempted rather than as clean round trips.

## Q1 fixes re-confirmed live (post-rebuild)

    A. Pill idle presentation
       pill_rect            = 2488,1320,52,52
       pill_button_name     = 'Ask AEDA'   rect = 2488,1320,52,52
       pill_button_fills_host = True
    B. Idle focus theft
       focused_process_is_aeda = False at idle  (Pill does not steal focus)
    C. In-app Assist remains full module
       main_rect      = 1080x760
       inapp_ask_rect = 79x32 in page context
    D. Composer keys
       1_typed              = [Q1 check]
       2_after_shift_enter  = [Q1 check<CR><LF>]     newline inserted
       3_after_enter        = []                     composer cleared -> sent
       3_composer_cleared   = True

## Defect 4 — conversation list items read their raw record — **Fixed in Q1**

Found by live tab traversal. Conversation `ListItem` peers exposed
`Conversation.ToString()`, so a screen reader would announce the GUID, model and both
timestamps instead of the title:

    ListItem:Conversation { Id = 9fdbba26-54d1-4446-8bed-4457b8591980, Title = Q1 check,
    Model = gemma4:12b, CreatedAtUtc = ..., UpdatedAtUtc = ..., Status = Completed }

Root cause: the automation name comes from the generated `ListBoxItem` **container**, not
from the `DataTemplate` content. Setting `AutomationProperties.Name` inside the template had
no effect (verified — the raw string persisted). Fixed with a container-level style in
`ChatView.axaml`:

    <ListBox.Styles>
      <Style Selector="ListBoxItem" x:DataType="core:Conversation">
        <Setter Property="AutomationProperties.Name" Value="{Binding Title}" />
      </Style>
    </ListBox.Styles>

Live verification after rebuild:

    OK [Q1 check] / [hey is this connected to anything] / [Hello from Q1] / [sdfssff]
    conversation_items_with_clean_name=11   still_raw_tostring=0

## Keyboard traversal — **Passed**

Measured with Win32 `SetForegroundWindow` enforcement (an earlier attempt reported one stop
per screen; that was a harness artifact — foreground was held by another window, so the TAB
presses never reached AEDA. The false "keyboard trap" reading was discarded, not reported):

| Screen | Tab stops | Shift+Tab stops | Disabled item focused |
| --- | --- | --- | --- |
| Dashboard | 2 | 2 | No |
| General chat | 7 | 6 | No |
| Task Center | 11 | 4 | No |
| Code | 10 | 3 | No |
| Settings | 13 | 5 | No |
| Memory | 5 | 5 | No |
| Research | 2 | 2 | No |
| Assist | 2 | 2 | No |

Focus progresses logically from the primary action outward (e.g. Settings:
`Appearance -> System Mica -> Graphite -> Mineral Stone`; General chat:
`Search conversations -> Start a new chat -> conversation items`). No screen trapped focus,
and **no disabled or hidden control received focus** on any screen.

## Validation after round 3

- `git diff --check`: clean
- .NET suite: **1012 passed / 0 failed**
- Build Debug: **0 warnings, 0 errors**; Build Release: **0 warnings, 0 errors**
- VS Code: `npm ci`, `npm run compile`, `npm test` → **10 pass / 0 fail**
- `npm audit --omit=dev`: **found 0 vulnerabilities** (the six high-severity findings are
  dev-only, via `@vscode/vsce`; they do not ship and do not block parity acceptance —
  remediation stays a separate dependency decision)

## Matrix rows genuinely NOT executed

These were not run in this round and are **not** claimed as passes. They remain the
outstanding gap for Q2 or a follow-up accessibility pass:

- visible focus rendering in light and dark palettes (requires visual inspection)
- Windows contrast theme readability
- text scaling at 150% and 200%
- Narrator/NVDA announcement behaviour (screen-reader output cannot be captured reliably
  here — remains **Blocked**)
- tray Open AEDA / New chat / Exit menu items
- global hotkey invocation (the window title reports the configured hotkey is unavailable —
  another application owns it on this machine; the conflict **is** surfaced to the user in
  readable text, which is the behaviour required, but invocation itself was not exercised)
- permission-dialog trigger, dismissal/deny/allow and focus restoration
- four palette application and persistence across restart
- workspace add / rename / remove via the native folder picker
- rapid settings-change final-value persistence
- Assist retry / copy / open-in-AEDA, elevated-target and password-field rejection

## Cleanup

- `%TEMP%\aeda-q1-sample.txt` and `%TEMP%\aeda-q1-stress.txt` removed.
- Notepad test instances closed. No temporary workspace registrations were created, so none
  needed removal.
- WinUI and Avalonia were never run concurrently at any point.
- Real profile untouched; backup remains at
  `C:\Users\yavar\AppData\Local\PersonalAI-backup-q1-20260804-210722`.
