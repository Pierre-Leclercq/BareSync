# BareSync — Technical Specs (Implementation Design)

Status: technical specification derived from the normative documents:
- `Docs/BareSync.Specs.md` (functional/menu spec)
- `Docs/BareSync.UI.Specs.md` (console UI spec)

This document is **SPEC ONLY** (no production code changes).

## Iteration 11 changes (non-normative)

- Added technical coverage for CLI extraction mode `/EXTRACT:<path>`.
- Clarified command-line exclusivity rule between `/BATCH` and `/EXTRACT` at routing level.
- Clarified extract flow constraints aligned with specs/UI:
  - source auto-detection (file `.bse` vs folder of `.bse`),
  - secret resolution before destination prompts,
  - actionable failure when no native archive is found.

## 0) Conventions (Derived vs Technical decision)

- **Derived from spec:** a statement that comes directly from the normative specs above. When needed, this document may use **MUST/SHOULD/MAY** only for such statements.
- **Technical decision:** an implementer choice not fixed by the normative specs. These decisions must not change the normative meaning; when in doubt, capture the ambiguity under **Open questions / Assumptions**.

## 1) Scope

This technical spec translates the functional + UI specs into implementable design guidance for:

- Data model (Batch, Step, Context, RunSummary, status enums)
- Persistence under `AppDataRoot/batches` (crash-safe writes, compatibility/versioning)
- UI routing/state machine and screen contracts
- Input handling rules (single-digit menus vs line prompts, ESC behavior)
- Runner pipeline (preflight -> confirm -> secrets -> execution -> summary/artifacts)
- Validation (static vs runtime) + error reporting compatible with `S1.5a` / `S2.12a`
- Artifact reporting (paths only; no secrets)
- Logging safety (never log secrets; avoid passing secrets via CLI args)
- Testing guidance (deterministic navigation, persistence, runner outcomes)

## 2) Glossary (Derived from spec)

### 2.1) Core domain

- **Interactive Context**: the single current user configuration used by Interactive mode (`Docs/BareSync.Specs.md` 6.1).
- **Batch Library**: the persistent set of batch definitions stored under `BatchStoreRoot` (`Docs/BareSync.Specs.md` 6.2).
- **Batch**: a persistent, executable definition made of ordered steps, with batch-level defaults and metadata (`Docs/BareSync.Specs.md` 6.3).
- **Step**: one operation occurrence in a batch, with operation parameters and optional context overrides (`Docs/BareSync.Specs.md` 6.4).
- **Batch Run**: a dated execution instance of a batch, separate from the batch definition (`Docs/BareSync.Specs.md` 6.5.2).
- **Artifact**: a materialized output (often a file) produced by an operation; UI can show artifact paths (`Docs/BareSync.Specs.md` 6.5.3).
- **Secret**: sensitive runtime input (e.g., encryption password) that is never persisted in a batch (`Docs/BareSync.Specs.md` 7.6).
- **Secret slot**: grouping key used to reuse a secret within one run; defined as (`EncryptionPassword`, effective `EncryptedOutputRoot`) (`Docs/BareSync.Specs.md` 7.6).

### 2.2) Status vocabulary

Derived from `Docs/BareSync.Specs.md` 6.5.1 and `Docs/BareSync.UI.Specs.md` 1.3:
- `Success`, `Warning`, `Fail`, `Canceled`, `NotRun`

### 2.3) Persistence roots

Derived from `Docs/BareSync.Specs.md` 6.2.1:
- **AppDataRoot**: directory containing BareSync persistent configuration (`appsettings.json`).
- **BatchStoreRoot**: persistent batch directory located at `AppDataRoot/batches` (directory name is literally `batches`), not user-configurable.

### 2.4) UI terminology

Derived from `Docs/BareSync.UI.Specs.md` 1.2:
- **Menu screen**: single-digit input only (`0..9`).
- **Prompt/selector screen**: line input; numeric selections accept multi-digit integers within an explicit valid range.
- **Progress screen**: no menu; `ESC` cancels the running operation.

## 3) Key derived constraints (non-exhaustive)

- Derived from `Docs/BareSync.Specs.md` 6.6 and 6.7: execution uses **effective values** resolved by step override -> batch default -> preflight error if missing.
- Derived from `Docs/BareSync.Specs.md` 9.2.1: copying Interactive Context into a batch is a **snapshot** (no dynamic link); overwriting existing values requires explicit confirmation.
- Derived from `Docs/BareSync.UI.Specs.md` 1.1a: `Last status` is maintained separately for Interactive mode and Batch mode.
- Derived from `Docs/BareSync.UI.Specs.md` 1.2 and 1.5: menu screens stay within `0..9`; lists that may exceed 9 items use pagination/selectors.
- Derived from `Docs/BareSync.Specs.md` 7.6 and `Docs/BareSync.UI.Specs.md` S1.7/S2.13: secrets are never echoed, persisted, or logged; canceling secret entry cancels the operation/run (screen-specific routing).

## 4) UI routing / state machine (Iteration 1 focus)

### 4.1) Input handling rules (Derived from UI specs)

Derived from `Docs/BareSync.UI.Specs.md` 1.2:
- Invalid input is ignored; the UI re-prompts without crashing or leaving partial state.
- `ESC` behavior:
  - If a screen offers `0` (`Back`/`Cancel`), `ESC` is equivalent to `0`.
  - On progress screens, `ESC` cancels the operation even if no `0` is shown.
  - Otherwise, `ESC` has no effect unless the screen defines it.

Derived from `Docs/BareSync.UI.Specs.md` 1.2 and screen-specific rules:
- `S1.7` (Interactive secret): empty input or `ESC` cancels the operation (result `Canceled`) and returns to the calling screen.
- `S2.13` (Batch secret): empty input or `ESC` cancels run start and returns to `S2.12` (no steps executed).
- `S2.14` (Batch run progress): `ESC` cancels the current step; the run ends with global `Canceled`, remaining steps `NotRun`, then shows `S2.15`.

### 4.2) Last status model (Derived from UI specs)

Derived from `Docs/BareSync.UI.Specs.md` 1.1a and 6.2:
- Maintain `Last status` independently for Interactive mode and Batch mode.
- Update it after each completed operation/run in the relevant mode (including `Canceled`).
- Switching modes MUST NOT modify the other mode's `Last status`.
- After restart, last status MAY be reset.
- Display it on all screens of the mode (except progress screens) immediately before `** Menu **`.

### 4.3) Screen IDs as states (Derived from UI specs)

Derived from `Docs/BareSync.UI.Specs.md` 6.1 (navigation matrix) and 7 (scenarios):

- Main:
  - `S0.1` Main Menu

- Interactive mode:
  - `S1.1` Home
  - `S1.2` Index Menu
  - `S1.3` Sync Menu
  - `S1.4` Encrypted Menu
  - `S1.5` Settings Menu
  - `S1.5a` Validation Errors (routes user to `S1.5`)
  - `S1.6` Confirmation prompt (risk actions)
  - `S1.7` Secret prompt (non-echo)
  - `S1.8` Operation Progress (cancel with `ESC`)
  - `S1.9` Last status display rule (rendered on parent menus; not a standalone menu screen)

- Batch mode:
  - `S2.1` Home
  - `S2.1a` Create (name prompt)
  - `S2.2` List (paged)
  - `S2.2a` List selector (pick a batch number)
  - `S2.3` Batch Details (hub)
  - `S2.4` Identity editor
  - `S2.5` Context editor (defaults)
  - `S2.5a` Copy snapshot confirm
  - `S2.6` Steps editor (paged)
  - `S2.6a` Steps selector (pick a step)
  - `S2.7` Add step (pick operation type)
  - `S2.8` Step details (hub)
  - `S2.8a` Step details (help)
  - `S2.9` Step context overrides editor
  - `S2.10` Step reorder
  - `S2.11` Step remove confirm
  - `S2.12` Preflight (plan summary)
  - `S2.12a` Preflight errors (batch not executable)
  - `S2.13` Secret prompt(s)
  - `S2.14` Run progress (per step)
  - `S2.15` Run summary
  - `S2.15a` Artifacts (per-step index)
  - `S2.15a1` Artifacts selector (pick step number)
  - `S2.15b` Artifacts viewer (paths)
  - `S2.16` Validity details (invalid/incompatible batch unit)
  - `S2.17` Unsaved changes confirm

### 4.4) Navigation outline (Derived from UI specs)

Derived from `Docs/BareSync.UI.Specs.md` 6.1 and 7:

- Main:
  - `S0.1` -> `S1.1` (Interactive) | `S2.1` (Batch) | Exit

- Interactive (happy path):
  - `S1.2` option `1/2` -> `S1.8` -> return to `S1.2` (last status visible)
  - `S1.3` option `2` (sync apply) -> `S1.6` -> `S1.8` -> return to `S1.3`
  - `S1.4` options `1/2/3` -> `S1.6` -> `S1.7` -> `S1.8` -> return to `S1.4`

- Interactive (validation fail routing):
  - Any operation that requires missing/invalid settings routes to `S1.5a`, which offers `Edit settings` (`S1.5`) or `Back` (returns to caller).

- Batch (edit + run):
  - `S2.1` -> `S2.2` -> `S2.3` (select batch) -> editors (`S2.4`/`S2.5`/`S2.6`) -> back to `S2.3`
  - Preflight/run: `S2.3` -> `S2.12`
    - If preflight fails: `S2.12a` (errors + route to `S2.5`/`S2.6`)
    - If preflight ok: run start from `S2.12` option `1`:
      - If any step requires confirmation: global `Proceed? (y/n)` gate (decline returns to `S2.12`)
      - Then secrets (`S2.13`) if required, then progress (`S2.14`), then summary (`S2.15`)

### 4.5) Technical routing model (placeholder)

Technical decision (Iteration 2): choose a concrete navigation implementation strategy (e.g., explicit state machine with a stack of screen states; each screen produces a navigation action and a per-mode context update).

## 5) Functional mapping (spec → technical areas) (Iteration 1 outline)

- Derived from `Docs/BareSync.Specs.md` 6.2.1/12.4: batch persistence lives under `BatchStoreRoot = AppDataRoot/batches`, is versioned, tolerant to corruption/incompatibility per batch unit, and uses crash-safe writes.
- Derived from `Docs/BareSync.Specs.md` 7.3/7.4/7.5: operation types define required context fields, whether confirmation/secret is required, which significant parameters must be displayed, and preflight validation requirements.
- Derived from `Docs/BareSync.Specs.md` 9.2.2: runner stages are preflight -> global confirmation (only if needed) -> secrets -> sequential execution -> summary/artifacts.
- Derived from `Docs/BareSync.Specs.md` 7.6 and `Docs/BareSync.UI.Specs.md` S2.13: secret slots are computed from effective values; prompt once per slot; never persist/log/echo secrets.

## 6) Data model (Iteration 2)

This section defines conceptual (non-code) structures used by the UI, persistence, validation and runner.

Technical decision:
- The canonical persisted shape is “JSON-like” objects.
- Every persisted object supports an `Extensions` bag for unknown fields (forward compatibility).

### 6.1) Enums

#### 6.1.1) `BatchValidity` (Derived from spec)

Values (derived from `Docs/BareSync.Specs.md` 6.3.2):
- `Valid`: parseable and conforms to a supported schema.
- `Invalid`: corrupted or not interpretable.
- `Incompatible`: schema is recognized as future/unsupported.

UI mapping:
- `S2.2` displays `status={Valid|Invalid|Incompatible}`.
- `S2.3` displays `Status: {Valid|Invalid|Incompatible}`.
- `S2.16` displays details/reason for invalid/incompatible units.

#### 6.1.2) `ExecutionStatus` (Derived from spec)

Values (derived from `Docs/BareSync.Specs.md` 6.5.1 and `Docs/BareSync.UI.Specs.md` 1.3):
- `Success`, `Warning`, `Fail`, `Canceled`, `NotRun`

UI mapping:
- Interactive mode: `Last status: {Status} — {StatusLine}` (rendered on menu screens, see `Docs/BareSync.UI.Specs.md` 1.1a / `S1.9`).
- Batch mode: `S2.15` global `Status: {Success|Warning|Fail|Canceled}` and per-step statuses.

#### 6.1.3) `OperationType` (Derived from spec)

Values (derived from `Docs/BareSync.Specs.md` 7.3 and `Docs/BareSync.UI.Specs.md` `S2.7`):
- `RefreshIndexesFull` (UI label: `Refresh indexes (full)`)
- `RefreshIndexesSmart` (UI label: `Refresh indexes (smart)`)
- `OneWaySyncDryRun` (UI label: `One-way sync (dry run)`)
- `OneWaySyncApply` (UI label: `One-way sync (apply)`)
- `CreateEncryptedFolder` (UI label: `Create encrypted folder`)
- `RefreshEncryptedFolder` (UI label: `Refresh encrypted folder`)
- `RestoreEncryptedFiles` (UI label: `Restore encrypted files`)

Derived per-type flags (from `Docs/BareSync.Specs.md` 7.3):
- `RequiresConfirmation` is true for:
  - `OneWaySyncApply`, `CreateEncryptedFolder`, `RefreshEncryptedFolder`, `RestoreEncryptedFiles`
- `RequiresSecret` is true for:
  - `CreateEncryptedFolder`, `RefreshEncryptedFolder`, `RestoreEncryptedFiles`

### 6.2) `BatchDefinition`

Purpose:
- Persisted definition of an executable scenario (batch-level defaults + ordered steps).

Fields:
- Required:
  - `BatchId` (string) — stable opaque identifier (derived constraints in `Docs/BareSync.Specs.md` 6.3.1)
  - `Name` (string) — non-empty after trim (derived constraints in `Docs/BareSync.Specs.md` 6.3.1)
  - `Metadata` (`BatchMetadata`)
  - `ContextDefaults` (`BatchContextDefaults`)
  - `Steps` (ordered list of `StepDefinition`)
- Optional:
  - `Extensions` (object/map)

UI mapping:
- `S2.2` uses: `Name`, derived `IdShort`, `Steps.Count`, and library-level `BatchValidity`.
- `S2.3` uses: `Name`, `BatchId` (full), `Metadata.Description`, `Steps.Count`, `BatchValidity`.
- `S2.4` edits: `Name` and `Metadata.Description`.
- `S2.5` edits: `ContextDefaults`.
- `S2.6` edits: `Steps`.

Technical decision:
- `IdShort` is not persisted; it is derived deterministically for list disambiguation (e.g., first 8 chars of `BatchId`).

### 6.3) `BatchMetadata`

Purpose:
- Schema/versioning + descriptive information that does not affect execution semantics.

Fields:
- Required:
  - `SchemaVersion` (integer)
- Optional:
  - `Description` (string)
  - `CreatedUtc` (string/instant; informational)
  - `ModifiedUtc` (string/instant; informational)
  - `Extensions` (object/map)

UI mapping:
- `S2.3` / `S2.4` displays/edits `Description`.

### 6.4) `BatchContextDefaults`

Purpose (derived from `Docs/BareSync.Specs.md` 6.6 and 6.7):
- Provide batch-level default context values; steps inherit unless overridden.

Fields (all optional strings; requiredness is enforced by preflight based on `OperationType`):
- `SourceRoot`
- `MirrorRoot`
- `SourceIndexCsvPath`
- `DestIndexCsvPath`
- `EncryptedOutputRoot`
- `RestoreRoot`
- `SevenZipPath`
- Optional: `Extensions` (object/map)

UI mapping:
- `S2.5` displays each field as `{Value|<not set>}` and edits them.

### 6.5) `StepDefinition`

Purpose:
- One ordered operation occurrence in a batch.

Fields:
- Required:
  - `OperationType` (`OperationType`)
  - `OperationParams` (`StepOperationParams`)
  - `ContextOverrides` (`StepContextOverrides`)
- Optional:
  - `StepId` (string) — stable identity for editor operations (reorder/remove) independent of list index
  - `Extensions` (object/map)

UI mapping:
- `S2.6` shows the current 1-based ordinal and `OperationType`.
- `S2.8` shows `OperationType`, `OperationParams`, and `ContextOverrides`.
- `S2.9` edits `ContextOverrides`.

Technical decision:
- UI ordinals are always 1-based and derived from current ordering in `BatchDefinition.Steps`.

### 6.6) `StepOperationParams`

Purpose:
- Persist operation-specific parameters when an operation type exposes any configurable parameters.

Derived behavior (from `Docs/BareSync.UI.Specs.md` `S2.8a`):
- For operation types where all parameters are fixed by the type, the UI displays `(none)` and returns without changes.

Fields:
- Required:
  - `Values` (object/map) — operation-specific key/value pairs (empty for fixed-parameter types)
- Optional:
  - `Extensions` (object/map)

Technical decision:
- For the current 7 `OperationType` values, `Values` is empty because parameters are fixed by the type (as presented in `S2.7`).
- Significant parameters displayed in preflight are computed from `OperationType` and effective context (see Runner / Preflight).

### 6.7) `StepContextOverrides`

Purpose:
- Step-local context overrides (replace batch defaults for this step only).

Fields (all optional strings; absence means “inherit from batch defaults”):
- `SourceRoot`
- `MirrorRoot`
- `SourceIndexCsvPath`
- `DestIndexCsvPath`
- `EncryptedOutputRoot`
- `RestoreRoot`
- `SevenZipPath`
- Optional: `Extensions` (object/map)

UI mapping:
- `S2.9` shows each field as either `'{OverrideValue}'` or `<inherit>`.
- `S2.9` “Clear one override” removes a single property from this object.
- `S2.9` “Clear all overrides” replaces this object with `{}`.

### 6.8) `RunSummary`

Purpose (derived from `Docs/BareSync.Specs.md` 6.5.2):
- Represent the current execution outcome without mutating the batch definition.

Fields:
- Required:
  - `RunId` (string; unique per process execution)
  - `OverallStatus` (`ExecutionStatus`)
  - `StepResults` (ordered list of `StepRunResult`)
- Optional:
  - `BatchId` (string) — present for batch runs, absent for interactive operations
  - `StartedUtc` / `FinishedUtc` (string/instant; informational only)
  - `UserSummaryLine` (string) — short summary used by `Last status` rendering
  - `Extensions` (object/map)

UI mapping:
- Batch: `S2.15` uses `OverallStatus` and `StepResults`.
- Interactive: `S1.9` uses `OverallStatus` + `UserSummaryLine`.

Technical decision:
- Persisted run history is out of scope; `RunSummary` is kept in memory as “current/last run” data for the mode (consistent with UI spec allowing reset after restart).

### 6.9) `StepRunResult`

Purpose:
- Outcome of executing one step within a run.

Fields:
- Required:
  - `StepIndex` (integer; 1-based UI ordinal)
  - `OperationType` (`OperationType`)
  - `Status` (`ExecutionStatus`)
  - `UserMessage` (string; user-facing English line)
  - `Artifacts` (list of `ArtifactDescriptor`)
- Optional:
  - `StartedUtc` / `FinishedUtc` (string/instant; informational)
  - `Extensions` (object/map)

UI mapping:
- `S2.15` shows `{OpTypeK} — {Status} — {UserMessage}`.
- `S2.15a` / `S2.15b` shows artifacts grouped by step.

### 6.10) `ArtifactDescriptor`

Purpose:
- Describe one artifact produced by a step as a safe UI-displayable record (paths only).

Fields (Iteration 3 defines contract details; see Artifacts contract):
- Required:
  - `StepIndex` (integer; 1-based UI ordinal)
  - `Path` (string)
  - `Type` (string enum-like: `log|report|index|archive|other`)
- Optional:
  - `DisplayName` (string)
  - `Extensions` (object/map)

## 7) Persistence model (Iteration 2)

### 7.1) Roots and unitization (Derived from spec)

Derived from `Docs/BareSync.Specs.md` 6.2.1 / 6.2.2 / 12.4:
- `BatchStoreRoot` is `AppDataRoot/batches` (fixed location, not user-configurable).
- Each batch is persisted as an independent unit to isolate corruption/incompatibility.
- The library is the aggregate of units; any derived index/cache is optional and reconstructible from units.
- A single invalid/incompatible unit does not prevent listing/editing/executing other batches.
- Persistence operations are crash-safe: after interruption, the tool finds either the old version or the new version (not a silently partial write).

### 7.2) On-disk layout under `BatchStoreRoot` (Technical decision)

Technical decision:
- One batch = one file (one unit) stored directly under `BatchStoreRoot`.
- Batch unit filename format: `{BatchId}.json`.
  - `BatchId` must be a filesystem-safe opaque identifier (recommended: lowercase GUID without braces).
- Temporary write filename format: `{BatchId}.json.tmp.{WriteId}`.
  - `{WriteId}` is a random/unique suffix to avoid collisions.
- Only files matching `*.json` and the batch schema are considered batch units for listing.
- No persistent “library index” is required; listing can be rebuilt by scanning units (derived from spec).

UI mapping:
- `S2.2` listing is built from scan results and sorted per `Docs/BareSync.Specs.md` 12.5 (name case-insensitive, then id).

### 7.3) Schema versioning + forward compatibility (Derived + Technical decision)

Derived from `Docs/BareSync.Specs.md` 6.3 and 12.4:
- Each batch unit carries a schema version.
- “Incompatible” means schema is recognized as unsupported/future; it is not executable until made compatible.

Technical decision:
- Schema version field name: `Metadata.SchemaVersion` (integer).
- Supported schema versions: start with `1`.
- Reader behavior:
  - If JSON parses but `SchemaVersion` is greater than supported → classify as `Incompatible`.
  - If JSON parses but required schema fields are missing/invalid (e.g., missing `BatchId`/`Name`) → classify as `Invalid`.
  - Unknown fields do not cause load failure:
    - Unknown fields at any object level are captured into `Extensions` and ignored by the core logic.
    - When a batch is saved, `Extensions` are written back (“round-trip”) so that opening/saving with an older BareSync does not drop newer fields.

### 7.4) Crash-safe write protocol (Derived requirement, specified as Technical decision)

Derived requirement (`Docs/BareSync.Specs.md` 12.4):
- Save must be crash-safe (old version or new version; no silent partial writes).

Technical decision: atomic replace protocol (same-directory temp file)

For “create or update batch unit”:
1. Serialize the batch unit to bytes (UTF-8 JSON).
2. Write to a new temp file `{BatchId}.json.tmp.{WriteId}` in the same directory as the final file.
3. Flush file buffers so bytes reach the filesystem (best-effort: flush file stream).
4. Atomically replace the final `{BatchId}.json` with the temp file:
   - If final exists: atomic replace (platform primitive such as “replace”/“rename over”).
   - If final does not exist: atomic rename temp → final.
5. Best-effort cleanup:
   - If an atomic replace primitive produces a backup file, keep it out of the listing scan (or delete it if safe).
   - Remove any leftover temp file if the atomic step failed.

Notes:
- “Atomic” here means: readers will observe either the previous complete file or the new complete file, never a partially-written file at the final path.
- The temp file is intentionally in the same directory to avoid cross-volume rename behavior.

### 7.5) Detecting partially written units / temp leftovers (Technical decision)

At library scan time (startup and/or when entering `S2.2`):
- Ignore temp files matching `*.tmp.*` for listing.
- Optional recovery path:
  - If `{BatchId}.json` does not exist but a temp file exists, the loader may attempt to parse the temp file:
    - If parse+schema validation succeeds, rename it into place as `{BatchId}.json`.
    - Otherwise, leave it as temp (or delete it) and treat the batch as not present.

### 7.6) Load-time classification (Valid/Invalid/Incompatible) (Technical decision aligned to spec)

Classification is per unit and does not require filesystem-dependent validation:
- `Invalid`:
  - JSON parse error, unreadable file, or structurally invalid schema (missing/invalid required schema fields).
- `Incompatible`:
  - JSON parses and schema is recognized, but `SchemaVersion` is higher than supported.
- `Valid`:
  - JSON parses, schema version supported, required schema fields valid.

Important separation (Derived from `Docs/BareSync.Specs.md` 6.3.2 vs 7.5):
- A `Valid` batch unit may still be “non executable” at runtime if required context fields are missing for its steps; that is reported by **preflight** and rendered via `S2.12a`, not by setting `BatchValidity=Invalid`.

### 7.7) Batch Storage Contract v0 (Technical decision; required for deterministic batch list/tests)

This section **freezes** the minimal storage contract used by Batch library scanning, listing, and
preflight classification. It is a **spec-only** contract (no production code in this document).

#### 7.7.1) Purpose

- Provide a stable and deterministic on-disk representation for batch definitions.
- Enable “load/list + status” minimal implementation and deterministic UI routing tests.
- Ensure cross-platform compatibility (Windows + Linux) and avoid any dependency on cursor APIs
  for batch routing tests.

#### 7.7.2) Scope

In-scope:
- Physical layout of batch unit files under `BatchStoreRoot`.
- Discovery rules and stable ordering for listing.
- JSON schema v0 (minimal but complete) including required/optional fields and formats.
- Loader classification rules (Valid / Invalid / Incompatible / NonExecutable).
- Deterministic stdout markers for batch list + details screens used in routing tests.
- Minimal preflight contract (structural checks only; no filesystem runtime checks).

Out-of-scope:
- Full runner execution and artifacts persistence.
- Full validation of filesystem paths or tool availability (runtime checks). 
- Migration tooling and interactive editing UI.

#### 7.7.3) Constraints

- **Cross-platform:** stored data must be valid on Windows and Linux.
- **Deterministic:** discovery and sorting must be stable across runs.
- **No cursor APIs requirement** for routing tests: batch list/preflight screens must remain
  readable under stdout redirection.

#### 7.7.4) Storage layout and naming

- Root directory: `BatchStoreRoot = {AppDataRoot}/batches` (derived; literal folder name `batches`).
- Batch unit file: `{BatchId}.json` stored directly under `BatchStoreRoot`.
- Auxiliary files (ignored for listing):
  - Temp writes: `{BatchId}.json.tmp.{WriteId}`
  - Lock files: `{BatchId}.json.lock` or `*.lock`
  - Backup files: `{BatchId}.json.bak` or `*.bak`
  - Any other file not matching `*.json` or not parseable to the v0 schema.

Ignore rules:
- Only files matching `*.json` are candidates for batch units.
- Files with suffixes `.tmp.*`, `.lock`, `.bak` are always ignored.
- A batch is **one file**; there is no directory-based batch unit in v0.

#### 7.7.5) Discovery and deterministic ordering

Discovery:
- Scan `BatchStoreRoot` for `*.json` files.
- For each file, attempt JSON parse; classify (see §7.7.8).
- Treat each file independently; a failure does not block others.

Ordering (for list screens and deterministic tests):
1. Primary: `Name` (case-insensitive using `StringComparer.OrdinalIgnoreCase`)
2. Secondary: `Id` (case-insensitive using `StringComparer.OrdinalIgnoreCase`)
3. Tertiary: filename (case-insensitive) for ties or invalid entries without `Id`/`Name`

Paging convention:
- Page size = **9** items (matches UI spec list paging).
- Stable page boundaries are computed after sorting.

#### 7.7.6) JSON schema v0 (minimal but complete)

Schema versioning:
- `schemaVersion` integer at the root level.
- v0 value = `0`.
- Loader supports only `0` for now. Higher values are `Incompatible`.

Root object (BatchDefinition v0):
- Required fields:
  - `schemaVersion` (integer; must be `0`)
  - `id` (string; filesystem-safe, opaque; recommended lowercase GUID)
  - `name` (string; trimmed, non-empty)
  - `createdUtc` (string; RFC3339 UTC, informational)
  - `updatedUtc` (string; RFC3339 UTC, informational)
  - `steps` (array; can be empty)
  - `contextSnapshot` (object; batch defaults)
- Optional fields:
  - `description` (string)
  - `tags` (array of string)
  - `extensions` (object/map) — forward compatibility container

`contextSnapshot` (all fields optional; empty means “unset”):
- `sourceRoot`, `mirrorRoot`, `sourceIndexCsvPath`, `destIndexCsvPath`,
  `encryptedOutputRoot`, `restoreRoot`, `sevenZipPath`
- `extensions` (object/map, optional)

`steps` items (StepDefinition v0):
- Required fields:
  - `operationType` (string enum; see §6.1.3)
  - `operationParams` (object; required, even if empty)
  - `contextOverrides` (object; required, even if empty)
- Optional fields:
  - `stepId` (string; stable step identity)
  - `extensions` (object/map)

`operationParams`:
- Required field: `values` (object; empty allowed)
- Optional: `extensions`

`contextOverrides`:
- Same fields as `contextSnapshot`; all optional. Absent means “inherit”.

Types/format constraints:
- `id`, `stepId`: non-empty string; recommended lowercase GUID or ULID. (No strict regex in v0.)
- `createdUtc`, `updatedUtc`: RFC3339 UTC (`YYYY-MM-DDTHH:MM:SSZ`).
- Strings must be valid UTF-8.
- Unknown fields are preserved under `extensions` when round-tripping (forward compatibility).

Compatibility rule:
- `schemaVersion` > supported (0) ⇒ `Incompatible` (parse OK).
- Missing or invalid required fields/types ⇒ `Invalid`.

#### 7.7.7) Loader classification (used by list + details)

Definitions (per unit file):
- **Valid**: JSON parse OK, `schemaVersion` supported, required fields present and of valid types.
- **Invalid**: JSON unreadable OR missing/invalid required fields/types.
- **Incompatible**: JSON parse OK, required fields present, but `schemaVersion` unsupported.
- **NonExecutable**: Valid batch where minimal preflight (see §7.7.10) fails.

Mapping to UI:
- `S2.2` displays `status={Valid|Invalid|Incompatible|NonExecutable}`.
- `S2.3` displays `Status: {Valid|Invalid|Incompatible|NonExecutable}`.
- `S2.16` displays details for Invalid/Incompatible/NonExecutable.

#### 7.7.8) Classification table (stable message contract)

| Condition (per file) | Status | Stable message (for UI/tests) |
|---|---|---|
| JSON parse error OR unreadable file | Invalid | `Invalid: unreadable or malformed JSON` |
| Missing required root fields (id/name/createdUtc/updatedUtc/steps/contextSnapshot/schemaVersion) | Invalid | `Invalid: missing required field` |
| Required field type mismatch | Invalid | `Invalid: invalid field type` |
| `schemaVersion` > 0 but JSON parse OK | Incompatible | `Incompatible: unsupported schemaVersion={n}` |
| Valid schema, preflight fails (see §7.7.10) | NonExecutable | `NonExecutable: preflight failed` |
| Valid schema, preflight ok | Valid | `Valid` |

Notes:
- The “Stable message” column is designed for deterministic UI tests; it may be
  included verbatim in `S2.16` and/or appended in `S2.2` detail lines.

#### 7.7.9) Deterministic stdout contract for routing tests

Stable lines (must remain unchanged):
- List screen (`S2.2`):
  - `** Batch / List **`
  - `Page {Current}/{Total}`
  - Each entry line contains: `status={Valid|Invalid|Incompatible|NonExecutable}` and `Name:` and `Id:`
- Details screen (`S2.3`):
  - `** Batch / Details **`
  - `Name: {Name}`
  - `Id: {Id}`
  - `Steps: {Count}`
  - `Status: {Status}`
- Validity details (`S2.16`):
  - `** Batch / Validity details **`
  - `Status: {Status}`
  - `Reason: {StableMessage}`

Placeholders allowed (tests should not assert exact values):
- Paths (absolute/relative), timestamps, batch ids (unless explicitly fixed in test data).

#### 7.7.10) Minimal preflight contract (for NonExecutable)

Preflight in v0 is **structural only** (no filesystem access). It checks:
- `steps` is non-empty.
- For each step:
  - `operationType` is one of the known values (§6.1.3).
  - Required context fields for that `operationType` exist in the **effective context**
    (step overrides → batch defaults).

Triggers:
- `requiresConfirmation=true` if **any** step `operationType` is risky
  (`OneWaySyncApply`, `CreateEncryptedFolder`, `RefreshEncryptedFolder`, `RestoreEncryptedFiles`).
- `requiresSecret=true` if **any** step requires secrets
  (`CreateEncryptedFolder`, `RefreshEncryptedFolder`, `RestoreEncryptedFiles`).

Stable preflight stdout (S2.12 / S2.12a):
- `** Batch / Preflight **`
- `requiresConfirmation={true|false}`
- `requiresSecret={true|false}`
- `** Batch / Preflight (FAILED) **` for error display

#### 7.7.11) JSON examples (v0)

Valid example:
```json
{
  "schemaVersion": 0,
  "id": "c0f5d7a0-8d3c-4f52-9f2d-1a2b3c4d5e6f",
  "name": "Nightly sync",
  "description": "Daily run",
  "tags": ["daily", "prod"],
  "createdUtc": "2026-01-20T10:00:00Z",
  "updatedUtc": "2026-01-20T10:00:00Z",
  "contextSnapshot": {
    "sourceRoot": "D:/Data/Source",
    "mirrorRoot": "D:/Data/Mirror",
    "sourceIndexCsvPath": "D:/Data/Index/source.csv",
    "destIndexCsvPath": "D:/Data/Index/dest.csv"
  },
  "steps": [
    {
      "stepId": "s1",
      "operationType": "OneWaySyncDryRun",
      "operationParams": { "values": {} },
      "contextOverrides": {}
    }
  ]
}
```

Incompatible example:
```json
{
  "schemaVersion": 1,
  "id": "8f6c6d30-9e2a-44f1-80e4-9ad3e4de8b77",
  "name": "Future batch",
  "createdUtc": "2026-01-20T10:00:00Z",
  "updatedUtc": "2026-01-20T10:00:00Z",
  "contextSnapshot": {},
  "steps": []
}
```

Invalid example (missing required fields / wrong type):
```json
{
  "schemaVersion": 0,
  "id": "",
  "name": 42,
  "createdUtc": "2026-01-20T10:00:00Z",
  "updatedUtc": "2026-01-20T10:00:00Z",
  "contextSnapshot": {},
  "steps": "not-an-array"
}
```

NonExecutable example (valid schema, preflight fails):
```json
{
  "schemaVersion": 0,
  "id": "a2f4ef1d-0b2d-4f8d-8e0e-0c78d0e33f6b",
  "name": "Missing context",
  "createdUtc": "2026-01-20T10:00:00Z",
  "updatedUtc": "2026-01-20T10:00:00Z",
  "contextSnapshot": {
    "sourceRoot": "D:/Data/Source"
  },
  "steps": [
    {
      "operationType": "OneWaySyncApply",
      "operationParams": { "values": {} },
      "contextOverrides": {}
    }
  ]
}
```

## 8) Validation model and error reporting (Iteration 2)

### 8.1) Two-layer validation (Derived from spec)

Derived from `Docs/BareSync.Specs.md` 7.5:
- Preflight combines:
  - **Static validation**: schema/required fields/syntax checks independent of filesystem state.
  - **Runtime validation**: filesystem/tool availability checks at the moment of launching.

Routing (derived from `Docs/BareSync.UI.Specs.md`):
- Interactive: validation errors are rendered via `S1.5a` and the user is guided to `S1.5` (settings).
- Batch: preflight errors are rendered via `S2.12a` and the user is guided to `S2.5` (context) and/or `S2.6` (steps).

### 8.2) `ErrorDescriptor` format (Technical decision, compatible with UI specs)

Purpose:
- A single, stable error record that can be rendered verbatim into `S1.5a` and `S2.12a`, and used for deterministic behavior and programmatic handling.

Fields:
- `StepIndex` (optional integer; 1-based):
  - Present for batch errors tied to a specific step (rendered as `Step {k}: ...` in `S2.12a`).
  - Absent for interactive settings validation (rendered as `- {Field}: {Reason}` in `S1.5a`).
  - Absent for batch-level errors not tied to a step (e.g., “no steps”).
- `FieldName` (string):
  - Canonical key name, typically one of the context fields:
    - `SourceRoot`, `MirrorRoot`, `SourceIndexCsvPath`, `DestIndexCsvPath`,
      `EncryptedOutputRoot`, `RestoreRoot`, `SevenZipPath`
  - May also represent a non-context field (e.g., `BatchName`) when needed.
- `ErrorCode` (string):
  - Stable machine-readable code for programmatic handling (see recommended codes below).
- `UserMessage` (string; English, ASCII where possible):
  - Human-readable explanation suitable for direct display.
- Optional: `Extensions` (object/map)

Recommended `ErrorCode` set (Technical decision):
- `MissingRequiredField`
- `InvalidValue`
- `InvalidPathSyntax`
- `UnsafePath`
- `InvalidFileExtension`
- `DirectoryNotFound`
- `FileNotFound`
- `NotAFile`
- `NotADirectory`
- `ParentNotCreatable`
- `ToolNotInvocable`
- `EncryptedIndexMissing`
- `NoSteps`

### 8.3) Field display names (Technical decision)

To keep UI strings consistent with the UI spec, map `FieldName` to display labels when rendering:
- `SourceRoot` → `Source Root`
- `MirrorRoot` → `Mirror Root`
- `SourceIndexCsvPath` → `Source Index Csv Path`
- `DestIndexCsvPath` → `Dest Index Csv Path`
- `EncryptedOutputRoot` → `Encrypted Output Root`
- `RestoreRoot` → `Restore Root`
- `SevenZipPath` → `SevenZip Path`

### 8.4) Path safety and syntax validation (Derived requirement + Technical decision)

Derived from `Docs/BareSync.Specs.md` 7.5:
- Path fields must respect BareSync path safety rules (canonicalizable and safe vs injection/traversal).

Technical decision:
- Static validation delegates to the existing BareSync “path safety” routine (same semantics as current settings validation).
- When validation fails, emit `ErrorDescriptor` with:
  - `ErrorCode=UnsafePath` (or `InvalidPathSyntax` as appropriate)
  - `UserMessage` that explains the issue without echoing any secret (paths are not secrets).

### 8.5) Operation-specific validation rules (Derived from spec)

Derived from `Docs/BareSync.Specs.md` 7.3 (required fields per operation) and 7.5 (minimal validation table).

Notation:
- For batch preflight, validation runs against the **effective context** for each step (step override → batch default).
- For interactive operations, validation runs against the Interactive Context (current settings).

#### 8.5.1) Required context fields by `OperationType` (Derived from spec)

- `RefreshIndexesFull` / `RefreshIndexesSmart`:
  - `SourceRoot`, `MirrorRoot`, `SourceIndexCsvPath`, `DestIndexCsvPath`
- `OneWaySyncDryRun` / `OneWaySyncApply`:
  - `SourceRoot`, `MirrorRoot`, `SourceIndexCsvPath`, `DestIndexCsvPath`
- `CreateEncryptedFolder`:
  - `SourceRoot`, `SourceIndexCsvPath`, `EncryptedOutputRoot`, `SevenZipPath`
- `RefreshEncryptedFolder`:
  - `EncryptedOutputRoot`, `SevenZipPath`
- `RestoreEncryptedFiles`:
  - `EncryptedOutputRoot`, `RestoreRoot`, `SevenZipPath`

#### 8.5.2) Static validation rules (Derived from spec)

For required fields:
- Missing or empty value → `ErrorCode=MissingRequiredField` or `InvalidValue`.

For index CSV paths (derived from `Docs/BareSync.Specs.md` 7.5):
- Enforce “.csv” expectation where applicable:
  - `SourceIndexCsvPath`, `DestIndexCsvPath` must be valid `.csv` paths.

For all path-like fields (derived from `Docs/BareSync.Specs.md` 7.5):
- Apply path safety checks (canonicalizable + safe).

#### 8.5.3) Runtime validation rules (Derived from spec)

Derived from `Docs/BareSync.Specs.md` 7.5 minimal table:

- Refresh indexes (full/smart):
  - `SourceRoot` and `MirrorRoot` directories exist.
  - Parent directories for `SourceIndexCsvPath` and `DestIndexCsvPath` are creatable (and not a file).

- One-way sync (dry run):
  - `SourceRoot` and `MirrorRoot` directories exist.
  - `SourceIndexCsvPath` exists and is a file.
  - `DestIndexCsvPath` may be absent (treated as empty index); if present, it must be a file.

- One-way sync (apply):
  - Same as dry run.
  - Parent directory of `DestIndexCsvPath` is creatable (and not a file).

- Create encrypted folder:
  - `SourceRoot` exists.
  - `SourceIndexCsvPath` exists and is a file.
  - `EncryptedOutputRoot` directory is creatable.
  - `SevenZipPath` is invocable.

- Refresh encrypted folder:
  - Encrypted index is present in `EncryptedOutputRoot` (see Open questions: encrypted index identity).
  - `SevenZipPath` is invocable.

- Restore encrypted files:
  - Encrypted index is present in `EncryptedOutputRoot`.
  - `RestoreRoot` directory is creatable.
  - `SevenZipPath` is invocable.

### 8.6) Rendering rules for `S1.5a` and `S2.12a` (Derived from UI specs)

`S1.5a` (interactive validation errors):
- Render a bullet list:
  - `- {FieldDisplayName}: {UserMessage}`
- `Edit settings` routes to `S1.5` (derived from UI spec).

`S2.12a` (batch preflight errors):
- Render a paged list (9 items/page) where each line is:
  - If `StepIndex` is present: `Step {k}: {UserMessage}`
  - If `StepIndex` is absent: `{UserMessage}`

Technical decision:
- Sort errors deterministically for stable rendering and reproducible behavior:
  1. `StepIndex` (missing last)
  2. `FieldName`
  3. `ErrorCode`

## 9) Runner pipeline + secrets (Iteration 3)

This section defines the end-to-end execution pipeline for both modes:
- Interactive mode: run a single operation against the Interactive Context.
- Batch mode: run a sequence of steps against the batch’s effective contexts.

### 9.1) Runner responsibilities (Derived + Technical decision)

Derived responsibilities (from `Docs/BareSync.Specs.md` 9.1 / 9.2.2):
- Apply preflight validation before execution.
- Enforce confirmation for risky operations.
- Collect secrets at runtime (never persisted).
- Execute sequentially and deterministically.
- Produce a run summary and artifacts list for UI display.
- Do not mutate the batch definition when running (derived from `Docs/BareSync.Specs.md` 6.5.2).

Technical decision:
- Use a single conceptual pipeline with mode-specific routing to UI screens (`S1.*` vs `S2.*`).

### 9.2) Stage A — Preflight (plan + validation)

Derived from `Docs/BareSync.Specs.md` 7.4 / 7.5 / 9.2.2 and `Docs/BareSync.UI.Specs.md` `S2.12`:
- Preflight runs before any execution and produces either:
  - A plan summary (rendered by `S2.12`) when successful, or
  - A list of actionable errors (rendered by `S1.5a` or `S2.12a`) when failing.

Preflight inputs:
- Mode (`Interactive` or `Batch`)
- Target:
  - Interactive: a single `OperationType` + Interactive Context
  - Batch: `BatchDefinition` (defaults + ordered steps)

Preflight outputs (conceptual):
- `EffectiveStepPlan[]` (batch only):
  - `StepIndex` (1-based)
  - `OperationType`
  - `EffectiveContext` (resolved values)
  - `RequiresConfirmation` / `RequiresSecret`
  - `SecretSlotHint` (optional; see secrets slot mechanism)
  - `SignificantParamsSummary` (string rendered in `S2.12`)
  - `OverridesSummary` (optional; highlights overridden fields per `Docs/BareSync.Specs.md` 7.4)
- `Errors[]` (`ErrorDescriptor`, see Validation model)

Significant parameters (derived from `Docs/BareSync.Specs.md` 7.4):
- Preflight must compute and display the “significant parameters” per operation type using **effective values**.

Routing:
- Batch:
  - If `Errors` is empty → show `S2.12`.
  - Else → show `S2.12a` with the errors.
- Interactive:
  - If preflight fails → show `S1.5a` with the errors and offer `Edit settings` (`S1.5`).

### 9.3) Stage B — Global confirmation (when required)

Derived from `Docs/BareSync.Specs.md` 7.3 / 9.2.2(B) and `Docs/BareSync.UI.Specs.md` `S2.12`:
- If at least one step requires confirmation:
  - Batch mode prompts exactly one global confirmation after preflight and before any execution.
  - The confirmation summary lists all risky steps clearly.

Batch UI behavior (derived from `Docs/BareSync.UI.Specs.md` `S2.12`):
- `S2.12` option `1` label is `Confirm & run` when confirmation is required.
- Selecting option `1` shows `Proceed? (y/n):`.
  - `n` returns to `S2.12` with no execution.

Interactive behavior (derived from `Docs/BareSync.Specs.md` 9.1):
- If an operation requires confirmation, show `S1.6` (`Proceed? (y/n):`) before execution.

Technical decision (confirm decline semantics):
- Declining confirmation is treated as a user cancellation:
  - Interactive: update Interactive last status to `Canceled` with a short line like `Canceled by user`.
  - Batch: return to `S2.12` with no step executed and set Batch last status to `Canceled`.

### 9.4) Stage C — Secret slot mechanism and collection

Derived from `Docs/BareSync.Specs.md` 7.6 and `Docs/BareSync.UI.Specs.md` `S2.13`:
- Secrets are requested at runtime, never persisted, never echoed, never logged.
- Secrets are collected after confirmation (when confirmation is required) and before executing the first dependent step.
- The runner prompts at most once per secret slot and reuses the secret within the same run.

#### 9.4.1) `SecretRole` and `SecretSlotKey`

Technical decision:
- `SecretRole` is a string enum-like value. Current set:
  - `EncryptionPassword`
- `SecretSlotKey` is the tuple:
  - (`SecretRole`, `EffectiveScope`)

Derived from `Docs/BareSync.Specs.md` 7.6:
- For encryption-related steps, the slot key is:
  - (`EncryptionPassword`, effective `EncryptedOutputRoot`)

Technical decision (scope normalization):
- `EffectiveScope` is computed from the effective path value after normalization (trim, normalize separators, canonical case rules as appropriate for the OS) to avoid double-prompting the same folder under equivalent spellings.

#### 9.4.2) Runtime secret cache (in-memory only)

Technical decision:
- Maintain an in-memory map `{SecretSlotKey -> SecretValue}` for the lifetime of a single run.
- Clear the map immediately after the run completes or is canceled.

#### 9.4.3) Prompt rules and cancel behavior (Derived)

Derived from `Docs/BareSync.UI.Specs.md`:
- Interactive (`S1.7`):
  - Empty input or `ESC` cancels the operation (result `Canceled`) and returns to the calling menu.
- Batch (`S2.13`):
  - Empty input or `ESC` cancels run start and returns to `S2.12` with no step executed.

Technical decision:
- The batch secret collection stage prompts for all required secret slots up-front (in a deterministic order) before step 1 execution begins.
- Order for multiple slots is deterministic (e.g., by normalized scope string).

Derived from `Docs/BareSync.Specs.md` 9.2.2(C):
- If a provided secret is invalid and a step fails, the run stops per failure policy; no automatic secret retry occurs within the same run.

### 9.5) Stage D — Execution loop (sequential)

Derived from `Docs/BareSync.Specs.md` 9.2.2(D):
- Execute steps sequentially in order.
- Stop policy:
  - `Warning`: continue.
  - `Fail` or `Canceled`: stop; remaining steps are `NotRun`.

Execution contract (Technical decision):
- Each executed step produces a `StepRunResult` with:
  - `Status` (`ExecutionStatus`)
  - `UserMessage` (English line suitable for `S2.15`)
  - `Artifacts[]` (paths only; see Artifacts contract)

Cancel behavior (derived from `Docs/BareSync.UI.Specs.md`):
- Interactive progress (`S1.8`): `ESC` cancels the current operation → `Canceled`.
- Batch progress (`S2.14`): `ESC` cancels the current step:
  - Global status becomes `Canceled`.
  - Remaining steps become `NotRun`.
  - The UI proceeds to `S2.15` (summary).

### 9.6) Stage E — Summary build + last status updates

Derived from `Docs/BareSync.Specs.md` 6.5.2 / 6.5.3 / 9.2.2(E) and `Docs/BareSync.UI.Specs.md` 1.1a:
- After completion (including cancellation), build a run summary:
  - Global status + per-step status/message + artifact paths.
- Update `Last status` for the current mode after each completed operation/run (including `Canceled`).

Technical decision: computing `RunSummary.OverallStatus`
- If any executed step is `Fail` → `Fail`.
- Else if the run was canceled → `Canceled`.
- Else if any step is `Warning` → `Warning`.
- Else → `Success`.

Batch UI routing (derived from `Docs/BareSync.UI.Specs.md`):
- After execution (including cancel during `S2.14`), show `S2.15` summary.

## 10) Artifacts contract and logging safety (Iteration 3)

### 10.1) Artifact reporting (Derived + Technical decision)

Derived from `Docs/BareSync.Specs.md` 6.5.3:
- Steps may produce zero or more artifacts.
- When artifacts exist, the UI can display their paths (interactive and batch).
- Secrets must never appear in artifacts produced by the menu/orchestration layer.

Technical decision:
- The runner reports artifacts as `ArtifactDescriptor` records attached to `StepRunResult`.
- Only paths are reported; no artifact content is loaded or displayed by the runner.

### 10.2) `ArtifactDescriptor` contract (Technical decision; aligns to UI specs)

Fields:
- Required:
  - `StepIndex` (integer; 1-based)
  - `Path` (string)
  - `Type` (one of: `log`, `report`, `index`, `archive`, `other`)
- Optional:
  - `DisplayName` (string)

Path rules (Technical decision):
- `Path` is a filesystem path to the artifact as created by the underlying operation.
- Prefer absolute paths for display (so the user can locate the file reliably).
- Artifact paths are never treated as secrets; however, they must never embed secret values (e.g., do not include passwords in filenames).

Ordering and deduplication (Technical decision):
- Preserve the natural production order when available; otherwise sort deterministically by `Type`, then `Path`.
- Deduplicate identical `(Type, Path)` pairs per step to avoid noisy UI.

UI mapping:
- `S2.15` may show a flat `Artifacts:` section listing paths.
- `S2.15a` and `S2.15b` show per-step artifact counts and per-step artifact paths.

### 10.3) Logging and secret-safety rules (Derived)

Derived from `Docs/BareSync.Specs.md` 7.6:
- BareSync does not display, log, or persist secrets in clear text in any output under its control (UI, messages, artifacts, files).
- When using external tools that could log or echo, BareSync avoids passing secrets via exposed channels (e.g., command-line arguments).

Technical decision:
- Runner/logging layers must treat secrets as “sensitive” and redact them from any diagnostic output.
- When writing user-facing `UserMessage` fields, never include secret values; use generic wording (e.g., `Invalid password`).

### 10.4) Passing secrets to external tools (Technical decision; subject to feasibility)

Technical decision intent (aligning to the derived constraint above):
- Do not place secrets on the process command line.
- Prefer supplying secrets via a less-exposed channel such as:
  - Standard input to a child process (when the tool supports reading password from stdin), or
  - An OS-provided secure prompt integrated into BareSync (when the operation is implemented in-process).

Open question:
- For the `SevenZipPath`-based operations, confirm the practical mechanism to supply `EncryptionPassword` without passing it on the command line while keeping the run non-interactive from the external tool’s perspective.

## 11) CLI /EXTRACT technical flow (Iteration 4)

This section translates the normative `/EXTRACT` flow into implementation-oriented guidance.

### 11.1) Routing and argument constraints

Derived from `Docs/BareSync.Specs.md` 9.3:
- CLI routing accepts `/EXTRACT:<path>` as a direct execution entrypoint.
- `/EXTRACT` and `/BATCH` are mutually exclusive in a single command invocation.

Technical decision:
- Argument parsing rejects:
  - empty extract path,
  - duplicate `/EXTRACT` arguments,
  - mixed `/BATCH` + `/EXTRACT` usage.
- Rejections return a non-zero exit code with an actionable message.

### 11.2) Source resolution

Derived from `Docs/BareSync.Specs.md` 9.3 and `Docs/BareSync.UI.Specs.md` section 8:
- Source path is resolved to exactly one of:
  - file mode (single native `.bse` archive),
  - folder mode (recursive search of native `.bse` archives).

Technical decision:
- “Native `.bse`” detection uses container-level validation (magic/version), not extension-only filtering.
- Folder mode fails fast when no native archive is found.

### 11.3) Secret resolution pipeline

Derived from `Docs/BareSync.Specs.md` 9.3 and `Docs/BareSync.UI.Specs.md` 8.2:
- Resolution order:
  1. OS secret store,
  2. empty secret attempt,
  3. masked prompt with optional save.

Technical decision:
- Password validation uses a representative archive in folder mode (index archive when present, else first native data archive).
- If validation fails after prompt, user can retry or cancel with deterministic messaging.

### 11.4) Destination prompts and anti-disclosure

Derived from `Docs/BareSync.Specs.md` 9.3 and `Docs/BareSync.UI.Specs.md` 8:
- Destination options are shown only after secret validation succeeds.

Technical decision:
- Folder mode:
  - display recursive extraction warning,
  - propose default `Extract to` sub-folder,
  - allow custom destination.
- File mode:
  - propose default `Extract to` sub-folder,
  - alternatives: current folder / suggested sub-folder / custom path / cancel.

### 11.5) Extraction execution contract

Derived constraints:
- On success: print a clear status line and return success exit code.
- On failure (invalid source, wrong secret, CRC mismatch, missing mapping, etc.): print actionable status line and return failure exit code.

Technical decision:
- If encrypted index is present, use it to restore original names and (when available) CRC expectations.
- If encrypted index is absent in folder mode, extraction may proceed with obfuscated-derived names, with explicit warning.

## 12) Open questions / Assumptions

See `Docs/BareSync.Technical.Specs.Progress.md` (rolling list). Add new items here only when they affect technical decisions or would risk changing normative meaning.

## Testing

Testing strategy and test scenarios are specified in:
- Docs/BareSync.Test.Plan.md
