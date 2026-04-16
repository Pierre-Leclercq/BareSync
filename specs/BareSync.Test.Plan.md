# BareSync — Test Plan

Status: test plan derived from:
- `Docs/BareSync.Specs.md`
- `Docs/BareSync.UI.Specs.md`
- `Docs/BareSync.Technical.Specs.md`

SPEC ONLY — no production code.

## Iteration 11 changes (non-normative)

- Added CLI extract test scope for `/EXTRACT:<path>`.
- Added coverage targets for argument exclusivity (`/BATCH` vs `/EXTRACT`) and extract source resolution (file vs folder).
- Added extract-flow test expectations for:
  - secret-gating before destination prompts,
  - actionable failure when no native archive is found,
  - single-archive extraction integrity checks (CRC mismatch handling).

## 0) Conventions

- **Derived from spec:** statements that come directly from the normative specs above. This document uses **MUST/SHOULD/MAY** only for such statements.
- **Technical decision:** testing methodology choices not fixed by the normative specs. These choices must not change normative meaning.
- Determinism is mandatory: ordering, messages, and summaries must be stable so scenario expectations are reproducible.
- Prefer referencing UI screen IDs (`S0.1`, `S1.*`, `S2.*`) instead of duplicating full screen mocks.

## 1) Test strategy overview

BareSync tests focus on verifying:
- Deterministic behavior (ordering, routing, summaries).
- Correct UI routing and input semantics (menus vs prompts, `ESC`, invalid input).
- Safe and robust persistence behavior for the batch library (scan, classification, crash-safe writes).
- Correct runner pipeline behavior (preflight → confirm → secrets → execution → summary).
- Security constraints: no secret leakage in UI/logs/artifacts.

## 2) Test taxonomy (what is tested)

### A) UI navigation + input handling

Derived from `Docs/BareSync.UI.Specs.md`:
- Menu screens accept single-digit input `0..9`; invalid input is ignored and re-prompted.
- Selector/prompt screens accept line input; numeric selectors accept multi-digit integers within an explicit valid range.
- `ESC` behavior:
  - Equivalent to `0` where `0` is offered.
  - Cancels on progress screens.
  - Screen-specific cancel semantics for secret prompts (`S1.7`, `S2.13`).

Test areas:
- Navigation matrix transitions (`S0.1` to `S1.*`/`S2.*`).
- `Last status` rendering placement and per-mode separation.
- Pagination behavior for list/selector screens (page size 9).

### B) Persistence & batch library

Derived from `Docs/BareSync.Specs.md` 6.2.1/6.2.2 and `Docs/BareSync.UI.Specs.md` `S2.2`/`S2.3`:
- Scan units under `AppDataRoot/batches`.
- Classification: `Valid` / `Invalid` / `Incompatible`.
- One bad unit does not break the library; other batches remain usable.
- Listing order is deterministic (name case-insensitive, then id).

### C) Crash-safe write behavior

Derived from `Docs/BareSync.Specs.md` 12.4:
- After interruption, the tool finds either the old version or the new version, not a silently partial write at the final path.

Test areas:
- Temp files are ignored during scanning/listing.
- Replace protocol produces a complete final unit file (no partial visibility at final filename).
- Leftover temp files do not break library operations.

### D) Validation & error reporting

Derived from `Docs/BareSync.Specs.md` 7.5 and `Docs/BareSync.UI.Specs.md` `S1.5a` / `S2.12a`:
- Static vs runtime validation.
- Preflight failure yields actionable errors (step + field + reason).
- Rendering compatibility:
  - Interactive uses `S1.5a`.
  - Batch uses `S2.12a`.

Test areas:
- Error record ordering is deterministic.
- Error routing leads to the appropriate correction screens (`S1.5`, `S2.5`, `S2.6`).

### E) Runner pipeline

Derived from `Docs/BareSync.Specs.md` 9.2.2 and `Docs/BareSync.UI.Specs.md` `S2.12`–`S2.15`:
- Preflight → Confirm (if needed) → Secrets → Execution loop → Summary.
- Stop policy:
  - `Warning` continues.
  - `Fail`/`Canceled` stops; remaining steps are `NotRun`.
- `ESC` cancel semantics on progress screens (`S1.8`, `S2.14`).

Test areas:
- Confirmation gating only when at least one step requires confirmation.
- Cancel/decline behavior produces deterministic outcomes and correct routing.
- Summary contents and status propagation rules are consistent.

### F) Artifacts & secret safety

Derived from `Docs/BareSync.Specs.md` 6.5.3 and 7.6:
- Artifacts are reported as paths only.
- Secrets are never echoed, persisted, logged, or included in artifacts produced by the orchestration layer.
- Avoid passing secrets via exposed channels (e.g., CLI args to external tools).

Test areas:
- Artifact lists are deterministic and contain no secret material.
- Logs/UI messages never include secret values.

### G) CLI extract mode (`/EXTRACT`)

Derived from `Docs/BareSync.Specs.md` 9.3 and `Docs/BareSync.UI.Specs.md` section 8:
- `/EXTRACT` routing and argument validation are deterministic.
- `/EXTRACT` and `/BATCH` cannot be combined.
- Source is auto-detected (single native file vs folder recursive search).
- Secret validation occurs before destination menu/prompts are shown.
- Failures are actionable (invalid source, wrong secret, CRC mismatch, missing index mapping).

Test areas:
- CLI argument parsing/routing (`Program.TryParseCliArguments`, `CliHelp`).
- `ExtractCommandLineRunner` behavior for file/folder sources.
- `EncryptedFolderService` extraction primitives:
  - native archive detection,
  - password validation,
  - index entry resolution,
  - single-archive extraction + CRC verification.

## 3) Test harness contracts (conceptual)

Tests represent scenarios using:

### 3.1) Preconditions

- Filesystem setup:
  - `AppDataRoot` contents (including `BatchStoreRoot = AppDataRoot/batches`).
  - Existing batch units (valid/invalid/incompatible).
  - External tool availability stubs (e.g., `SevenZipPath` invocability).
- Mode state:
  - Interactive settings snapshot (for Interactive mode tests).
  - Batch selection (for Batch mode tests).
  - Initial `Last status` values for both modes (if relevant).

### 3.2) User inputs

- Sequence of inputs as the user would type:
  - Menu digits (`0..9`) for menu screens.
  - Line inputs for prompt/selector screens.
  - `ESC` events (including progress screen cancellation).

### 3.3) Expected screen sequence

- Deterministic ordered list of visited screen IDs, for example:
  - `S0.1 -> S2.1 -> S2.2 -> S2.3 -> S2.12 -> S2.13 -> S2.14 -> S2.15`
- When a prompt is expected (e.g., `Proceed? (y/n):`), represent it as an input gate associated with the screen transition.

### 3.4) Expected outputs (deterministic)

Compare outputs in deterministic order:
- Validation errors as ordered `ErrorDescriptor[]` (or their rendered lines).
- Run outcomes as `RunSummary` / `StepRunResult[]` (status + user messages).
- Artifact lists as ordered paths per step.

Non-deterministic fields (e.g., timestamps, elapsed time) must be excluded from strict comparisons or normalized.

## 4) Canonical scenario suite (Iteration 1: list only)

Only names + short descriptions in Iteration 1; detailed test cases are added later.

1. **Interactive missing settings routes to `S1.5a`** — Choose an operation requiring fields that are `<not set>`; expect `S1.5a` with missing/invalid fields and option to `Edit settings`.
2. **Interactive sync apply confirmation declined** — From `S1.3`, decline `S1.6 Proceed?`; return to `S1.3` with `Last status` reflecting cancellation.
3. **Interactive secret prompt canceled** — For an encrypted/restore operation, cancel `S1.7`; expect operation canceled with no execution.
4. **Interactive progress canceled by `ESC`** — During `S1.8`, press `ESC`; expect `Canceled` result and correct `Last status` update.
5. **Batch list empty library** — `S2.2` shows `(no batches)`; `Open batch` disabled/absent; paging disabled/absent.
6. **Batch library tolerates invalid unit** — One valid batch + one corrupted unit; list shows both with correct validity, and valid batch remains editable/runnable.
7. **Batch incompatible schema shows `S2.16`** — Batch unit loads as `Incompatible`; `S2.3` offers `Show validity details` → `S2.16` with reason.
8. **Batch preflight fails routes to `S2.12a`** — Missing required fields for at least one step; `Run (preflight)` → `S2.12a` with actionable errors and routing to `S2.5`/`S2.6`.
9. **Batch preflight ok shows `S2.12` plan** — Valid executable batch; `S2.12` shows significant params and requiresConfirmation/requiresSecret flags.
10. **Batch confirmation required: `S2.12` label + proceed gate** — At least one risky step; `S2.12` option 1 label is `Confirm & run`; `Proceed? (y/n)` gate appears.
11. **Batch confirmation declined returns to `S2.12`** — Decline proceed; no steps executed; mode `Last status` reflects cancellation (per technical decision/assumption).
12. **Batch secret prompt canceled returns to `S2.12`** — Cancel `S2.13`; no steps executed; ensure no secret is stored/logged.
13. **Batch run canceled by `ESC` during `S2.14`** — Press `ESC`; expect global `Canceled`, remaining steps `NotRun`, and `S2.15` summary reflects this.
14. **Secret slot reuse** — Two encrypted steps with same effective `EncryptedOutputRoot` prompt once; different roots prompt separately; reuse within one run only.
15. **Crash-safe persistence scan ignores temp files** — Leave `{BatchId}.json.tmp.*` files; scan/list ignores them; no partial final `{BatchId}.json` is observed.
16. **CLI extract arg exclusivity** — Mixed `/BATCH` + `/EXTRACT` is rejected with non-zero exit and actionable message.
17. **CLI extract folder without native archive fails** — Folder source with no native `.bse` returns failure and clear message.
18. **CLI extract secret-gating** — Destination options are not shown before password validation succeeds.
19. **CLI single-file extract destination options** — Default `Extract to` + alternatives (current/sub-folder/custom/cancel) behave deterministically.
20. **Single archive extraction CRC mismatch** — Extraction fails with no destination write when expected CRC does not match.

## 5) Iteration 2 — Formal test cases (scenarios 1–5)

### 5.1) Formal test case template (tool-friendly)

Each test case uses this deterministic structure:

- **Test ID**: `TP-...` (stable)
- **Scenario name**: short, human-readable name
- **Derived requirements**: bullet list referencing the specs (screen IDs and/or sections)
- **Assumptions** (optional): only when the specs are ambiguous
- **Preconditions**:
  - Filesystem under `AppDataRoot` (and `BatchStoreRoot` when applicable)
  - Mode state (Interactive Context / Batch selection)
  - Initial `Last status` (Interactive + Batch)
- **User input sequence**: ordered `(ScreenId, Input)` pairs
  - Menu digits are `0..9`
  - Prompt inputs are full lines (e.g., `y`, `n`, `12`)
  - `ESC` is recorded as `ESC`
- **Expected screen trace**: single `Sx.y -> ...` chain
  - If input is ignored or an option is disabled, repeat the same screen ID (e.g., `S2.2 -> S2.2`).
- **Expected outcomes**:
  - Validation errors (if applicable): `ErrorDescriptor[]` in deterministic order OR rendered lines on `S1.5a` / `S2.12a`
  - Run summary (if applicable): `OverallStatus` and ordered step statuses; `NotRun` when applicable
  - Artifacts (if applicable): paths only; deterministic ordering; no secret leakage
- **Non-determinism handling**: what to ignore/normalize (timestamps, elapsed time, progress counters)
- **Notes**: edge cases / risks

### 5.2) Canonical representations (used by all test cases)

**Screen trace format**
- A screen trace is a single line with ordered screen IDs:
  - `S0.1 -> S1.1 -> ...`
- If input is ignored or an option is disabled, the trace repeats the same screen ID:
  - `S2.2 -> S2.2` (stays on `S2.2`).

**Input format**
- Inputs are recorded as an ordered list of `(ScreenId, Input)` pairs.
- Menu input is a single digit `0..9` (e.g., `1`).
- Prompt input is a full line (e.g., `n`, `y`, or a selector number like `12`).
- `ESC` is recorded as `ESC`.

**Expected output comparison format**
- Validation errors:
  - Prefer `ErrorDescriptor[]` for deterministic comparison (see `Docs/BareSync.Technical.Specs.md` validation model).
  - When an error screen is the output surface (`S1.5a`, `S2.12a`), tests may also assert the rendered lines are present and ordered consistently.
- Run outcomes:
  - When a run/operation is canceled before execution starts, a run summary may be absent; when a summary exists, compare:
    - `RunSummary.OverallStatus`
    - Ordered `StepResults[]` (status + non-empty English message)
    - `NotRun` where applicable (batch stop policy)
- Artifacts:
  - Assert only what is normative:
    - Paths only (no contents), deterministic ordering, and no secret leakage.
  - Do not assert the existence of specific files unless mandated by a spec; when needed, use test doubles/stubs to make artifact presence deterministic.

**Non-determinism handling**
- Do not assert:
  - timestamps / elapsed time / progress counters
  - filesystem enumeration order (unless the spec mandates ordering)
- Normalize paths when comparing (platform-appropriate normalization), but do not alter displayed UI strings.

---

### TP-UI-001

**Scenario name**: Interactive missing settings routes to `S1.5a`

**Derived requirements**
- Derived from UI specs `S1.2` + navigation matrix: selecting an operation routes to `S1.8` if settings are OK, otherwise to `S1.5a`.
- Derived from UI specs `S1.5a`: missing/invalid settings are displayed and user is guided to `Edit settings`.
- Derived from functional specs (Interactive mode): validation occurs before operation execution and errors are actionable.

**Assumptions**
- Assumption: a validation failure does not update `Last status` (Interactive and Batch remain unchanged).

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - Interactive configuration exists but required fields are unset.
  - `BatchStoreRoot` content is irrelevant for this test.
- Interactive Context:
  - `SourceRoot=<not set>`
  - `MirrorRoot=<not set>`
  - `SourceIndexCsvPath=<not set>`
  - `DestIndexCsvPath=<not set>`
- Initial `Last status`:
  - Interactive: none (not displayed / not available)
  - Batch: none

**Inputs**
1. (`S0.1`, `1`)
2. (`S1.1`, `1`)
3. (`S1.2`, `1`)
4. (`S1.5a`, `0`)
5. (`S1.2`, `0`)
6. (`S1.1`, `0`)

**Expected screen trace**
- `S0.1 -> S1.1 -> S1.2 -> S1.5a -> S1.2 -> S1.1 -> S0.1`

**Expected outcomes**
- Validation errors (as `ErrorDescriptor[]`, deterministic order):
  - `{ StepIndex: <absent>, FieldName: DestIndexCsvPath, ErrorCode: MissingRequiredField, UserMessage: <non-empty> }`
  - `{ StepIndex: <absent>, FieldName: MirrorRoot, ErrorCode: MissingRequiredField, UserMessage: <non-empty> }`
  - `{ StepIndex: <absent>, FieldName: SourceIndexCsvPath, ErrorCode: MissingRequiredField, UserMessage: <non-empty> }`
  - `{ StepIndex: <absent>, FieldName: SourceRoot, ErrorCode: MissingRequiredField, UserMessage: <non-empty> }`
- RunSummary: none (validation failure before execution).
- Step results: none.
- Artifacts: none.
- `Last status`:
  - Interactive unchanged (remains none).
  - Batch unchanged.

**Non-determinism handling**
- Do not assert the exact `UserMessage` text beyond “non-empty English”.

**Notes**
- Open question: should validation failure update `Last status`? (Tracked in progress.)

---

### TP-UI-002

**Scenario name**: Interactive sync apply confirmation declined

**Derived requirements**
- Derived from UI specs `S1.3` + navigation matrix: `One-way sync (apply)` routes through `S1.6` before execution.
- Derived from UI specs `S1.6`: confirmation prompt `Proceed? (y/n):`.
- Derived from functional specs: risky operations require explicit confirmation in both modes.

**Assumptions**
- Assumption: declining confirmation is treated as cancellation and sets Interactive `Last status` to `Canceled`.

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - `SourceRoot` directory exists.
  - `MirrorRoot` directory exists.
  - `SourceIndexCsvPath` exists and is a file.
  - `DestIndexCsvPath` exists and is a file (choose this deterministically; do not vary across runs).
- Interactive Context:
  - All required fields for `OneWaySyncApply` are present and pass validation.
- Initial `Last status`:
  - Interactive: none
  - Batch: none

**Inputs**
1. (`S0.1`, `1`)
2. (`S1.1`, `2`)
3. (`S1.3`, `2`)
4. (`S1.6`, `n`)
5. (`S1.3`, `0`)
6. (`S1.1`, `0`)

**Expected screen trace**
- `S0.1 -> S1.1 -> S1.3 -> S1.6 -> S1.3 -> S1.1 -> S0.1`

**Expected outcomes**
- Validation errors: none.
- RunSummary: none (no execution started).
- Step results: none.
- Artifacts: none.
- `Last status` (Interactive): `Canceled`.

**Non-determinism handling**
- Do not assert the exact last-status message text.

**Notes**
- Open question: if confirmation is declined, should `Last status` change or remain unchanged? (Tracked in progress.)

---

### TP-UI-003

**Scenario name**: Interactive secret prompt canceled

**Derived requirements**
- Derived from UI specs `S1.7`: empty input or `ESC` cancels the operation (`Canceled`) and returns to the calling screen.
- Derived from functional specs: secrets are requested at runtime and are never persisted.

**Assumptions**
- Assumption: canceling the secret prompt is treated as an operation cancellation and updates Interactive `Last status` to `Canceled`.

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - `EncryptedOutputRoot` exists (directory).
  - `RestoreRoot` is creatable (directory).
  - `SevenZipPath` is invocable.
- Interactive Context:
  - `EncryptedOutputRoot`, `RestoreRoot`, `SevenZipPath` are set to values that pass validation for `RestoreEncryptedFiles`.
- Initial `Last status`:
  - Interactive: none
  - Batch: none

**Inputs**
1. (`S0.1`, `1`)
2. (`S1.1`, `3`)
3. (`S1.4`, `3`)
4. (`S1.6`, `y`)
5. (`S1.7`, `ESC`)
6. (`S1.4`, `0`)
7. (`S1.1`, `0`)

**Expected screen trace**
- `S0.1 -> S1.1 -> S1.4 -> S1.6 -> S1.7 -> S1.4 -> S1.1 -> S0.1`

**Expected outcomes**
- Validation errors: none.
- RunSummary.OverallStatus: `Canceled`.
- Step results (conceptual single operation):
  - Step 1 status: `Canceled`
  - Message: non-empty English
- Artifacts: none.
- `Last status` (Interactive): updated to `Canceled`.

**Non-determinism handling**
- Do not assert any timing/progress output.

**Notes**
- Do not assert how the secret would be passed to external tools (stdin/args/etc); treat as out of scope.

---

### TP-UI-004

**Scenario name**: Interactive progress canceled by `ESC`

**Derived requirements**
- Derived from UI specs `S1.8`: `ESC` cancels the running operation and produces a `Canceled` result.
- Derived from UI specs input rules: progress screens accept `ESC` cancellation even without `0` options.

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - `SourceRoot` and `MirrorRoot` exist (directories).
  - Parent directories for `SourceIndexCsvPath` and `DestIndexCsvPath` are creatable.
- Interactive Context:
  - Fields required for `RefreshIndexesFull` pass validation.
- Initial `Last status`:
  - Interactive: none
  - Batch: none
- Test harness capability:
  - The operation remains in `S1.8` long enough to accept an `ESC` cancellation deterministically.

**Inputs**
1. (`S0.1`, `1`)
2. (`S1.1`, `1`)
3. (`S1.2`, `1`)
4. (`S1.8`, `ESC`)
5. (`S1.2`, `0`)
6. (`S1.1`, `0`)

**Expected screen trace**
- `S0.1 -> S1.1 -> S1.2 -> S1.8 -> S1.2 -> S1.1 -> S0.1`

**Expected outcomes**
- Validation errors: none.
- RunSummary.OverallStatus: `Canceled`.
- Step results (conceptual single operation):
  - Step 1 status: `Canceled`
  - Message: non-empty English
- Artifacts:
  - If any are reported, they are paths only and ordered deterministically.
- `Last status` (Interactive): updated to `Canceled`.

**Non-determinism handling**
- Ignore progress counters, current item names, and timestamps.

**Notes**
- Avoid asserting presence/absence of artifacts unless mandated by a spec; use stubs if the operation layer is non-deterministic.

---

### TP-BATCH-001

**Scenario name**: Batch list empty library

**Derived requirements**
- Derived from UI specs `S2.2`: when the list is empty, display `(no batches)` and disable/omit `Open batch` and paging actions.
- Derived from functional specs: batch store is under `BatchStoreRoot = AppDataRoot/batches`.

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - `BatchStoreRoot` exists and contains no batch unit files.
- Batch library state:
  - Empty.
- Initial selection:
  - No batch selected.
- Initial `Last status`:
  - Interactive: none
  - Batch: none

**Inputs**
1. (`S0.1`, `2`)
2. (`S2.1`, `1`)
3. (`S2.2`, `1`)
4. (`S2.2`, `0`)
5. (`S2.1`, `0`)

**Stdin script (menu digits)**
- `2` (Batch mode)
- `1` (List batches)
- `1` (Open batch — expected ignored/disabled)
- `0` (Back)
- `0` (Back)

**Expected screen trace**
- `S0.1 -> S2.1 -> S2.2 -> S2.2 -> S2.1 -> S0.1`

**Expected outcomes**
- Stdout matchers (minimal):
  - `(no batches)`
  - `** Batch / List **`
- No validation errors.
- No run summary.
- Expected artifacts: none.
- Exit code: `0`.
- `Last status` unchanged for both modes.

**Non-determinism handling**
- None.

**Notes**
- The repeated `S2.2` in the trace represents “Open batch disabled/ignored” behavior.
- Console risk: none (no progress screens).

---

### TP-BATCH-002

**Scenario name**: Batch library tolerates invalid unit

**Derived requirements**
- Derived from UI specs `S2.2`: list shows entries with `status={Valid|Invalid|Incompatible}`.
- Derived from functional specs: one bad unit does not break listing.

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - `BatchStoreRoot` contains:
    - One batch that loads as `Valid`.
    - One batch that is unreadable/invalid and must appear as `Invalid`.
- Initial `Last status`: none for both modes.

**Inputs**
1. (`S0.1`, `2`)
2. (`S2.1`, `1`)
3. (`S2.2`, `0`)
4. (`S2.1`, `0`)

**Stdin script (menu digits)**
- `2` (Batch mode)
- `1` (List batches)
- `0` (Back)
- `0` (Back)

**Expected screen trace**
- `S0.1 -> S2.1 -> S2.2 -> S2.1 -> S0.1`

**Expected outcomes**
- Stdout matchers (minimal):
  - `** Batch / List **`
  - One line containing `status={Valid}`
  - One line containing `status={Invalid}`
- Expected artifacts: none.
- Exit code: `0`.

**Non-determinism handling**
- Do not assert ordering beyond the deterministic list rules already mandated by spec.

**Notes**
- Console risk: none (no progress screens).

---

### TP-BATCH-003

**Scenario name**: Batch incompatible schema shows `S2.16`

**Derived requirements**
- Derived from UI specs `S2.3` + `S2.16`.
- Derived from functional specs: incompatible batches are listed and show validity details.

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - `BatchStoreRoot` contains one batch that loads as `Incompatible`.

**Inputs**
1. (`S0.1`, `2`)
2. (`S2.1`, `1`)
3. (`S2.2`, `1`)
4. (`S2.2a`, `1`)
5. (`S2.3`, `5`)
6. (`S2.16`, `0`)
7. (`S2.3`, `0`)
8. (`S2.2`, `0`)
9. (`S2.1`, `0`)

**Stdin script (menu digits)**
- `2` (Batch mode)
- `1` (List batches)
- `1` (Open batch)
- `1` (Select batch #1)
- `5` (Show validity details)
- `0` (Back)
- `0` (Back)
- `0` (Back)

**Expected screen trace**
- `S0.1 -> S2.1 -> S2.2 -> S2.2a -> S2.3 -> S2.16 -> S2.3 -> S2.2 -> S2.1 -> S0.1`

**Expected outcomes**
- Stdout matchers (minimal):
  - `** Batch / Validity details **`
  - `Status: Incompatible`
- Expected artifacts: none.
- Exit code: `0`.

**Non-determinism handling**
- None.

**Notes**
- Console risk: none (no progress screens).

---

### TP-BATCH-004

**Scenario name**: Batch preflight fails routes to `S2.12a`

**Derived requirements**
- Derived from UI specs `S2.12a`: actionable errors and routing to edit screens.

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - `BatchStoreRoot` contains one batch that is parseable but not executable (missing required field).

**Inputs**
1. (`S0.1`, `2`)
2. (`S2.1`, `1`)
3. (`S2.2`, `1`)
4. (`S2.2a`, `1`)
5. (`S2.3`, `4`)
6. (`S2.12a`, `0`)
7. (`S2.3`, `0`)
8. (`S2.2`, `0`)
9. (`S2.1`, `0`)

**Stdin script (menu digits)**
- `2` (Batch mode)
- `1` (List batches)
- `1` (Open batch)
- `1` (Select batch #1)
- `4` (Run preflight)
- `0` (Back)
- `0` (Back)
- `0` (Back)

**Expected screen trace**
- `S0.1 -> S2.1 -> S2.2 -> S2.2a -> S2.3 -> S2.12a -> S2.3 -> S2.2 -> S2.1 -> S0.1`

**Expected outcomes**
- Stdout matchers (minimal):
  - `** Batch / Preflight (FAILED) **`
  - `Step {k}: Missing field:`
- Expected artifacts: none.
- Exit code: `0`.

**Non-determinism handling**
- Do not assert the exact error ordering beyond deterministic rules from specs.

**Notes**
- Console risk: none (no progress screens).

---

### TP-BATCH-005

**Scenario name**: Batch preflight ok shows `S2.12` plan (no run)

**Derived requirements**
- Derived from UI specs `S2.12`: plan rendering with requiresConfirmation/requiresSecret flags.

**Preconditions**
- Filesystem state under `AppDataRoot`:
  - `BatchStoreRoot` contains one batch that is executable (all required fields present).

**Inputs**
1. (`S0.1`, `2`)
2. (`S2.1`, `1`)
3. (`S2.2`, `1`)
4. (`S2.2a`, `1`)
5. (`S2.3`, `4`)
6. (`S2.12`, `0`)
7. (`S2.3`, `0`)
8. (`S2.2`, `0`)
9. (`S2.1`, `0`)

**Stdin script (menu digits)**
- `2` (Batch mode)
- `1` (List batches)
- `1` (Open batch)
- `1` (Select batch #1)
- `4` (Run preflight)
- `0` (Back)
- `0` (Back)
- `0` (Back)

**Expected screen trace**
- `S0.1 -> S2.1 -> S2.2 -> S2.2a -> S2.3 -> S2.12 -> S2.3 -> S2.2 -> S2.1 -> S0.1`

**Expected outcomes**
- Stdout matchers (minimal):
  - `** Batch / Preflight **`
  - `requiresConfirmation=`
  - `requiresSecret=`
- Expected artifacts: none.
- Exit code: `0`.

**Non-determinism handling**
- Do not assert param summaries beyond stable markers.

**Notes**
- Console risk: none (no progress screens).

---

Remaining canonical scenarios (6–15) are not expanded in Iteration 2.

Additional note (Iteration 11):
- CLI extract service-level primitives are now covered by dedicated tests in `EncryptedFolderServiceExtractTests`.

## 6) Non-functional checks

- No secret leakage: secrets never appear in UI text, persisted batch units, logs, or artifact paths.
- Deterministic ordering: listing order, error ordering, step results ordering, and artifact ordering are stable across runs.
- Reproducible routing: given the same inputs and preconditions, screen sequences are identical.
