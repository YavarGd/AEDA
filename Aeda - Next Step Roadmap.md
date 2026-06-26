---
created: 2026-06-25
project: PersonalAI (Aeda)
status: active
type: roadmap
tags: [roadmap, aeda, personal-ai, mvp]
---

# Aeda — Next Step Roadmap

> *"An always-available, local, context-aware AI assistant that lives in your tools."*

---

## 📍 Where We Are (June 2025)

### Current State
- **Architecture**: Modular C# .NET 10 solution with 6 projects
- **UI**: WinUI 3 desktop app + VS Code extension (TypeScript)
- **LLM**: Ollama backend, 7 local models available
- **Status**: Early alpha — many features at ~5% completion across 30+ branches
- **Core risk**: Not dogfooding — fear of Aeda breaking its own codebase

### What Works
- ✅ VS Code extension can grab selected code snippets
- ✅ Extension communicates with backend (pipe-based)
- ✅ Ollama integration routes to local models
- ✅ Approval system architecture exists in Core

### What Doesn't (Yet)
- ❌ No end-to-end flow: select code → send to model → get suggestion → apply/reject
- ❌ No read-only / safe mode for dogfooding
- ❌ Branch sprawl: 30+ feature branches, most at 5%
- ❌ No single "happy path" demo
- ❌ Embedding/RAG branches exist but not integrated

---

## 🎯 The Mission

**Build the smallest possible Aeda that one person can use daily without fear.**

Not the full Cowork OS vision. Not every integration. Just:
> *I select code in VS Code → I ask Aeda → Aeda answers (or suggests) → I decide*

---

## 🗺️ Roadmap

### Phase 0: Safety First (Week 1)
> **Goal**: Make it safe to use Aeda on its own codebase

- [ ] Implement **read-only mode** in Aeda
  - Aeda can explain, summarize, search — but NEVER edit
  - Config flag: `read_only = true` (default for dogfooding)
- [ ] Add **Git safety net** to workflow
  - Before any Aeda session: `git stash && git checkout -b aeda-experiment`
  - Or: pre-session hook that creates a backup branch
- [ ] Verify **approval gates** work end-to-end
  - Every edit must go through `ApprovalRequest` → user clicks Apply/Reject
  - No auto-apply mode until Phase 2

**Deliverable**: I can ask Aeda about PersonalAI.Core code without fear.

---

### Phase 1: One Model, One Task (Weeks 2-3)
> **Goal**: Aeda Code MVP — single-file code assistance in VS Code

- [ ] **Hardcode one model**: `qwen2.5-coder:7b`
  - Remove multi-model routing for now
  - One model, one prompt template, one flow
  - Can re-add routing later (it's architecture, not MVP)
- [ ] **Define the happy path**:
  1. User selects code in VS Code
  2. Extension sends snippet + prompt to backend
  3. Backend sends to `qwen2.5-coder:7b` via Ollama
  4. Response streams back to VS Code panel
  5. User reads the suggestion (read-only mode)
- [ ] **Polish the VS Code panel**
  - Markdown rendering for responses
  - Code block syntax highlighting
  - "Copy to clipboard" button
- [ ] **Dogfood 30 min/day**
  - Start with: "Explain this function"
  - Then: "What does this class do?"
  - Then: "Find the bug in this snippet"

**Deliverable**: I use Aeda daily in read-only mode to understand my codebase.

---

### Phase 2: Suggest + Apply (Weeks 4-5)
> **Goal**: Aeda can suggest edits, I approve or reject

- [ ] **Implement suggest-apply flow**:
  1. Aeda returns a diff/patch suggestion
  2. VS Code shows diff preview (green/red)
  3. User clicks ✅ Apply or ❌ Reject
  4. If applied: Git commit with "Aeda: [description]"
- [ ] **Approval gate enforcement**
  - Every code change requires user approval
  - Log all approvals in `ApprovalCheckpoint`
  - "Undo last Aeda change" button
- [ ] **Rollback mechanism**
  - Before any apply: `git stash` or checkpoint
  - One-click rollback to last checkpoint
- [ ] **Dogfood: start with safe edits**
  - "Add XML doc comments to this class"
  - "Rename this variable consistently"
  - "Write a unit test for this method"

**Deliverable**: Aeda can make code changes, I stay in control.

---

### Phase 3: Context Awareness (Weeks 6-8)
> **Goal**: Aeda understands more than just the selected snippet

- [ ] **Active file context**
  - Send entire open file + cursor position
  - Include imports/usings for language awareness
- [ ] **Project context**
  - Send `Directory.Build.props`, `.csproj` metadata
  - Include project structure (file tree)
- [ ] **Conversation memory (in-session)**
  - Aeda remembers what we discussed this session
  - "Now apply the same pattern to this other file"
- [ ] **Embedding-based RAG (local)**
  - Use `qwen3-embedding:0.6b` for local embeddings
  - Index PersonalAI.Core codebase
  - Aeda can search: "Where is the approval logic?"

**Deliverable**: Aeda gives contextually relevant answers, not just snippet-level responses.

---

### Phase 4: Multi-Model Routing (Weeks 9-10)
> **Goal**: Right model for the right task

- [ ] **Re-enable model routing** (simplified)
  - Coding tasks → `qwen2.5-coder:7b`
  - General chat → `qwen3:8b` or `gemma4:12b`
  - Vision/screenshots → `qwen3-vl:8b`
  - Fast/simple → `mistral:7b`
- [ ] **Routing logic**
  - Task type detection (simple heuristics first)
  - Configurable model preferences
  - Fallback chain if model unavailable

**Deliverable**: Aeda automatically picks the best model for each request.

---

### Phase 5: Agent Mode (Weeks 11-14)
> **Goal**: Aeda can plan and execute multi-step tasks

- [ ] **Task planning**
  - "Refactor the approval system" → Aeda creates a plan
  - User reviews/approves the plan
  - Aeda executes step-by-step with approval gates
- [ ] **Multi-file operations**
  - Read/write across project files
  - Coordinated changes (e.g., interface + implementation)
- [ ] **Validation pipeline**
  - After changes: run `dotnet build`
  - After changes: run `dotnet test`
  - If tests fail: auto-revert or suggest fix
- [ ] **Background execution**
  - Worker process for long tasks
  - Progress reporting in VS Code

**Deliverable**: "Aeda, refactor this module" → plan → execute → validate → done.

---

### Phase 6: Beyond VS Code (Months 4-6)
> **Goal**: Aeda lives everywhere

- [ ] **WinUI 3 desktop app**
  - System tray / always-available
  - Global hotkey to summon Aeda
  - Context from active window (not just VS Code)
- [ ] **Other integrations**
  - Obsidian plugin (research/notes)
  - PowerPoint (presentation assistance)
  - Terminal (command suggestions)
- [ ] **Cross-application context**
  - Aeda knows what I'm working on across apps
  - "I just updated the API in VS Code, tell PowerPoint to update the demo slide"

**Deliverable**: Aeda is always there, contextually aware, across tools.

---

## 🧰 Hardware Allocation (Your 4070 Ti)

| Task | Model | VRAM | Strategy |
|------|-------|------|----------|
| **Primary coding** | qwen2.5-coder:7b (Q8) | ~6 GB | Keep loaded always |
| **General chat** | qwen3:8b (Q4) | ~5 GB | Load on demand |
| **Embeddings** | qwen3-embedding:0.6b | ~0.5 GB | Keep loaded always (tiny!) |
| **Vision** | qwen3-vl:8b | ~6 GB | Load on demand (swap with chat) |
| **Fast responses** | mistral:7b (Q8) | ~7 GB | Load on demand |

**Strategy**: Keep coder + embedding resident (~6.5 GB). Swap other models in/out as needed. 4070 Ti has 12 GB VRAM — you can run coder + embedding + one other model simultaneously.

---

## 🌿 Branch Cleanup Plan

### Keep (merged or actively developing)
- `master` / `main`
- `feature/aeda-code-module`
- `feature/rich-chat-rendering`
- `feature/local-embedding-rag-integration`

### Archive (ideas explored, may revisit)
- `feature/aeda-memory-module` → merge into RAG integration
- `feature/aeda-research-module` → Phase 5+
- `feature/aeda-shell-module-dashboard` → Phase 5+
- `feature/aeda-task-center` → Phase 5
- `feature/voice-worker-integration` → Phase 6
- `feature/winui-visual-redesign` → Phase 6

### Evaluate (could be useful now)
- `feature/provider-routing-integration` → Phase 4
- `feature/openai-compatible-routing` → Phase 4
- `feature/tool-runtime-foundation` → Phase 5
- `feature/approved-patch-apply-foundation` → Phase 2
- `feature/controlled-validation-runner` → Phase 5

---

## 📏 Success Metrics

| Phase | "I know I succeeded when..." |
|-------|------------------------------|
| **0** | I can ask Aeda about PersonalAI code without fear |
| **1** | I use Aeda daily to understand code (read-only) |
| **2** | Aeda suggests edits, I approve, code gets better |
| **3** | Aeda's answers are contextually relevant (not just snippet-level) |
| **4** | Right model for right task, automatically |
| **5** | "Aeda, refactor this" → she does it, tests pass |
| **6** | I summon Aeda from anywhere, she knows what I'm doing |

---

## ⚠️ Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| **Scope creep** | Strict phase gates — don't move on until current phase works |
| **Code quality (prompt-coding)** | Always review before applying; tests first |
| **Aeda breaks its own code** | Read-only first; approval gates; Git safety net |
| **C# productivity** | Use Aeda to help write C# once she's safe |
| **Model quality limitations** | qwen2.5-coder:7b is best-in-class for code at this size |
| **Burnout / motivation** | Ship small wins often; dogfood daily |

---

## 💡 Guiding Principles

1. **Safety over capability** — Aeda that can't break things > Aeda that can do everything
2. **One thing working > five things half-working** — Depth over breadth
3. **Dogfood or die** — If I'm not using it, it's not real
4. **Local first** — No cloud dependency. My data, my machine, my AI
5. **Context is king** — The more Aeda knows, the better she helps
6. **Named with love** — Aeda is personal. She carries Aida's name

---

## 🔗 Related

- [[PersonalAI Architecture]] (create from docs/architecture/)
- [[Cowork OS Research]] (create from docs/research/)
- [[Aeda Model Strategy]]
- [[Aeda Dogfood Log]] (create daily entries)

---

*Last updated: 2026-06-25 by Yavar + Hermes*
