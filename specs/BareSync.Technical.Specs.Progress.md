# BareSync — Technical Specs Progress

This file tracks iterative authoring of `Docs/BareSync.Technical.Specs.md`.

## Hard rules (from prompt)

- SPEC ONLY: do not change production code.
- Do not change normative meaning of existing specs; capture ambiguity under **Open questions / Assumptions**.
- UI strings stay English (ASCII where possible).
- Menu screens stay within single-digit input `0..9`; overflow uses pagination/selector screens (per UI specs).
- Separation of concerns:
  - Interactive mode uses a single Interactive Context (single current config).
  - Batch mode uses Batch Library + per-batch context; no dynamic dependency on interactive settings except snapshot copy.
  - Last status is maintained separately for Interactive and Batch modes.

---

## Iteration 1 — Skeleton + glossary + navigation outline (DONE)

### Plan (Iteration 1)

1. Extract glossary + key invariants from `Docs/BareSync.Specs.md`.
2. Extract UI primitives + input/ESC/last-status rules from `Docs/BareSync.UI.Specs.md`.
3. Create `Docs/BareSync.Technical.Specs.md` skeleton with:
   - Derived-vs-decision conventions
   - Glossary
   - Mapping section (spec → technical areas)
   - UI navigation/state machine outline (based on UI navigation matrix + scenarios)
4. Record open questions / assumptions to drive Iteration 2+.

### What I read (Iteration 1)

- `Docs/BareSync.Specs.md` — focused on: 6.1–6.7 (contexts, persistence roots, inheritance), 7.3–7.6 (op catalog, significant params, preflight, secrets), 8–9 (navigation + flows), 12.4–12.5 (crash-safe + ordering), 13 (success criteria).
- `Docs/BareSync.UI.Specs.md` — focused on: 1.1a/1.2/1.5 (last status + input/ESC + pagination), S0.1, S1.1–S1.9, S2.1–S2.17 (esp. S2.12/S2.12a/S2.13/S2.14/S2.15), 6 (navigation matrix + routing rules), 7 (end-to-end scenarios).

### Notes / TODO (Iteration 1)

- Keep tech spec in English; quote UI strings exactly as in UI specs when needed.
- Prefer referencing UI screen IDs (`S0.1`, `S1.*`, `S2.*`) instead of duplicating full mocks.

### Open questions / assumptions (rolling)

- What is the exact runtime location/definition of `AppDataRoot` on each OS? (Specs define `BatchStoreRoot = AppDataRoot/batches`, but not the OS mapping.)
- Persistence format/versioning strategy for batch units is intentionally unspecified in normative docs. (Will be a Technical decision in Iteration 2.)
- How is `Last status` represented internally (in-memory only vs persisted)? UI specs allow reset on restart; technical plan should pick a default.
- What is the concrete identity of the “encrypted index present in EncryptedOutputRoot” used by runtime validation for `RefreshEncryptedFolder` / `RestoreEncryptedFiles`? (Exact filename/pattern is not defined by the normative specs.)
- What is the practical mechanism to supply `EncryptionPassword` to the `SevenZipPath` tool without passing it via command-line arguments (while still running non-interactively)?
- Assumption: declining a `Proceed? (y/n)` confirmation is treated as a user cancellation (`Canceled`) and updates the mode’s `Last status`.
- Is a batch with `0` steps considered executable (no-op success) or non-executable (preflight error such as `NoSteps`)? (Not specified.)

### End of Iteration 1 summary

What I produced/changed:
- Created `Docs/BareSync.Technical.Specs.md` with an Iteration 1 skeleton:
  - Conventions (Derived vs Technical decision)
  - Glossary and key derived constraints
  - UI routing/state machine outline aligned to UI screen IDs + navigation matrix
  - Placeholders for data model, persistence, validation, runner pipeline, artifacts/logging, testing

Next goals (Iteration 2):
- Define the concrete data model (Batch/Step/Context/RunSummary + enums) and explicitly map it to UI needs (list rows, preflight rendering, summary/artifacts).
- Specify persistence unit layout under `AppDataRoot/batches` (filenames, directories, atomic write protocol, compatibility/versioning rules).
- Specify validation layers (static vs runtime) and the error object format that renders cleanly into `S1.5a` and `S2.12a`.
- Decide (Technical decision) whether `Last status` is in-memory only (default) or optionally persisted; keep within UI spec allowance.

Risks / unknowns:
- OS-specific mapping of `AppDataRoot` (needs a clear Technical decision; must remain consistent with existing BareSync config location).
- Serialization format and forward-compat policy (unknown fields, schema upgrades) could impact long-term compatibility.
- Crash-safe write protocol details (atomic rename guarantees differ by filesystem; needs careful spec wording).

---

## Iteration 2 — Core architecture (DATA + PERSISTENCE + VALIDATION)

### Plan (Iteration 2)

1. Specify the core data model (Batch/Step/Context/RunSummary + enums) and map each field to UI screens.
2. Specify persistence layout under `AppDataRoot/batches` (one batch = one unit), including crash-safe write protocol and load-time validity classification (Valid/Invalid/Incompatible).
3. Specify validation layers (static vs runtime) and a concrete `ErrorDescriptor` format that renders cleanly to `S1.5a` and `S2.12a`.
4. Record any new ambiguities as open questions/assumptions.

### What I will read/verify (Iteration 2)

- `Docs/BareSync.Specs.md` — 6.2.1–6.3.2, 7.3–7.6, 12.4–12.5.
- `Docs/BareSync.UI.Specs.md` — S2.2/S2.3/S2.5/S2.6/S2.9, S2.12/S2.12a, plus global input/pagination rules.

### End of Iteration 2 summary

What I produced/changed:
- Expanded `Docs/BareSync.Technical.Specs.md` with Iteration 2 “core architecture” details:
  - Data model: concrete conceptual structures + enums with UI mapping (`BatchDefinition`, `StepDefinition`, `RunSummary`, `ArtifactDescriptor`, `BatchValidity`, `ExecutionStatus`, `OperationType`).
  - Persistence model: `AppDataRoot/batches` layout, one-batch-per-unit, crash-safe write protocol, validity classification and schema versioning/forward-compat rules.
  - Validation model: static vs runtime layers, `ErrorDescriptor` schema, operation-specific checks derived from specs, and rendering rules for `S1.5a` / `S2.12a`.

Key technical decisions (Iteration 2):
- One batch unit is a single file `{BatchId}.json` under `BatchStoreRoot`.
- Crash-safe writes use “same-directory temp file + flush + atomic replace” protocol.
- Schema version is `Metadata.SchemaVersion` (starting at `1`); unknown fields are stored in `Extensions` and round-tripped on save.
- Validation emits stable `ErrorDescriptor` records with deterministic ordering for test stability.

Next goals (Iteration 4+):
- Keep `Docs/BareSync.Technical.Specs.md` focused on implementation design only; testing strategy lives in `Docs/BareSync.Test.Plan.md`.
- Reconcile the remaining open questions (encrypted index identity; sevenzip secret channel; empty-batch executability) and reflect chosen assumptions explicitly.

---

## Iteration 3 — Runner & secrets (DRAFTED)

### Plan (Iteration 3)

1. Specify the runner pipeline stages and their inputs/outputs (preflight → confirm → secrets → execution loop → summary build).
2. Specify the secret slot mechanism (slot key, in-memory cache, prompt rules, lifetime).
3. Specify artifact reporting contracts (descriptor schema, collection rules, UI mapping).
4. Coordinate with `Docs/BareSync.Test.Plan.md` for runner-related scenario coverage (runner stays implementation-only here).

### End of Iteration 3 summary (draft)

What I produced/changed:
- Expanded `Docs/BareSync.Technical.Specs.md` with Iteration 3 design:
  - Runner pipeline stages (preflight → confirm → secrets → execution loop → summary + last status updates)
  - Secret slot mechanism (slot key, in-memory cache, prompt and cancel semantics)
  - Artifact contract + logging/secret-safety guidance (paths only; avoid secret exposure)

Next goals:
- Note: testing strategy content was split out into `Docs/BareSync.Test.Plan.md`; keep this progress file focused on technical spec iterations only.

---

## Iteration 4 — CLI /EXTRACT technical flow (DONE)

### Plan (Iteration 4)

1. Translate normative `/EXTRACT` requirements into implementation-oriented routing/flow constraints.
2. Document source resolution and native archive detection expectations (file vs folder).
3. Document secret-gating and destination prompt ordering (anti-disclosure).
4. Document extraction outcome/exit-code contract (success/failure actionable messaging).

### What I produced/changed (Iteration 4)

- Added `## 11) CLI /EXTRACT technical flow (Iteration 4)` to `Docs/BareSync.Technical.Specs.md`:
  - routing and argument constraints (`/BATCH` vs `/EXTRACT`, duplicate/empty handling),
  - source auto-detection and fail-fast when no native archive is found,
  - secret resolution pipeline and validation behavior,
  - destination prompt ordering rules and anti-disclosure,
  - extraction execution contract (status lines + exit codes).

### Notes / remaining open points

- Password channel hardening toward external tools remains tracked in open questions.
- Any future retry policy refinement must stay aligned with normative stop rules.
