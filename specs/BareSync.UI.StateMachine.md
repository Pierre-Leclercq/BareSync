# BareSync — UI State Machine (Spec Helper)

Status: SPEC support document. No production code.

This document summarizes the UI state machine from `Docs/BareSync.UI.Specs.md` for test planning.

## Stable matchers (cross-cutting)

Use these as stable anchors when parsing stdout:

- Header: `** BareSync **`
- Menu block: `** Menu **` followed by numbered options and `Select an option:`
- Confirmation prompts: `Proceed? (y/n):`
- Secret prompts: `Enter password (will not be echoed):`
- Validation errors: `Missing or invalid settings:`
- Pagination: `Page {Current}/{Total}`
- Batch validity screen: `** Batch / Validity details **`
- Preflight errors: `** Batch / Preflight (FAILED) **`

## Not safe under stdout redirection (progress screens)

Progress screens are not safe under stdout redirection on Windows because they often rely on
cursor/buffer APIs (e.g., `Console.CursorTop`, `Console.SetCursorPosition`, `Console.BufferWidth`).

Do not target these screens in redirect-based UI routing tests:

- `S1.8` — Operation progress (interactive)
- `S2.14` — Batch run progress

TP_UI_002 remains **SKIP** until console redirection is safe for progress rendering.

## State machine table (S0.*, S1.*, S2.*)

Legend:
- Input: menu digit (`0..9`), line input, y/n, selector int, or ESC.
- Transition: expected next screen ID (or Exit).
- Matchers: stable lines to detect the screen.

### Main / Interactive

| State | Input type | Inputs | Transition | Stable matchers |
|---|---|---|---|---|
| **S0.1 Main Menu** | menu digit | `1` | `S1.1` | `** Menu **`, `1. Interactive mode`, `2. Batch mode`, `0. Exit`, `Select an option:` |
|  |  | `2` | `S2.1` | same |
|  |  | `0` | Exit | same |
| **S1.1 Interactive Home** | menu digit | `1` | `S1.2` | `** Interactive mode **`, `Source =`, `Mirror =`, `1. Index`, `2. Sync`, `3. Encrypted`, `4. Settings`, `0. Back` |
|  |  | `2` | `S1.3` | same |
|  |  | `3` | `S1.4` | same |
|  |  | `4` | `S1.5` | same |
|  |  | `0` | `S0.1` | same |
| **S1.2 Interactive / Index** | menu digit | `1` | `S1.8` or `S1.5a` | `** Interactive / Index **`, `SourceIndexCsvPath =`, `DestIndexCsvPath =` |
|  |  | `2` | `S1.8` or `S1.5a` | same |
|  |  | `0` | `S1.1` | same |
| **S1.3 Interactive / Sync** | menu digit | `1` | `S1.8` or `S1.5a` | `** Interactive / Sync **`, `1. One-way sync (dry run)`, `2. One-way sync (apply)` |
|  |  | `2` | `S1.6` or `S1.5a` | same |
|  |  | `0` | `S1.1` | same |
| **S1.4 Interactive / Encrypted** | menu digit | `1/2/3` | `S1.6` → `S1.7` → `S1.8` or `S1.5a` | `** Interactive / Encrypted **`, `EncryptedOutputRoot =`, `RestoreRoot =`, `SevenZipPath =` |
|  |  | `0` | `S1.1` | same |
| **S1.5 Settings Menu** | menu digit | `1..7` | prompt (`P1/P3/P4`) → return `S1.5` | `** Current config **`, `1. Edit Source Root`, `7. Edit SevenZip Path` |
|  |  | `0` | `S1.1` | same |
| **S1.5a Validation Errors** | menu digit | `1` | `S1.5` | `Missing or invalid settings:` |
|  |  | `0` | return to caller | same |
| **S1.6 Confirmation** | line input | `y` | proceed to operation flow | `Proceed? (y/n):` |
|  |  | `n` | return to caller | same |
| **S1.7 Secret Prompt** | secret line / ESC | `ESC` or empty | cancel → return | `Enter password (will not be echoed):` |
| **S1.8 Progress** | ESC | `ESC` | cancel → return | `Operation:`, `Progress:`, `Elapsed:` |

### Batch

| State | Input type | Inputs | Transition | Stable matchers |
|---|---|---|---|---|
| **S2.1 Batch Home** | menu digit | `1` | `S2.2` | `** Batch mode **`, `1. List batches`, `2. Create new batch`, `0. Back` |
|  |  | `2` | `S2.1a` | same |
|  |  | `0` | `S0.1` | same |
| **S2.1a Create batch** | line input | name | `S2.3` | `Enter batch name (empty to cancel):` |
|  |  | empty | `S2.1` | same |
| **S2.2 Batch List** | menu digit | `1` | `S2.2a` (if non-empty) or stay `S2.2` | `** Batch / List **`, `Page`, `(no batches)` |
|  |  | `2/3` | page change | same |
|  |  | `0` | `S2.1` | same |
| **S2.2a Select batch** | line int | `0` | `S2.2` | `Select batch number (1..{PageCount}, 0 to cancel):` |
|  |  | `1..PageCount` | `S2.3` | same |
| **S2.3 Batch Details** | menu digit | `1` | `S2.4` | `** Batch / Details **`, `Name:`, `Id:`, `Steps:`, `Status:` |
|  |  | `2` | `S2.5` | same |
|  |  | `3` | `S2.6` | same |
|  |  | `4` | `S2.12` | same |
|  |  | `5` | `S2.16` (if invalid/incompatible) | same |
|  |  | `0` | `S2.2` | same |
| **S2.4 Batch Identity Editor** | menu digit | `1/2` | prompt → return `S2.4` | `** Batch / Identity **` |
|  |  | `3` | save → `S2.3` | same |
|  |  | `0` | `S2.17` or `S2.3` | same |
| **S2.5 Batch Context Editor** | menu digit | `1..7` | prompt → return `S2.5` | `** Batch / Context (defaults) **` |
|  |  | `8` | `S2.5a` | same |
|  |  | `9` | save → `S2.3` | same |
|  |  | `0` | `S2.17` or `S2.3` | same |
| **S2.5a Snapshot confirm** | line input | `y/n` | `S2.5` | `Copy interactive settings into batch context (snapshot).` + `Proceed? (y/n):` |
| **S2.6 Batch Steps Editor** | menu digit | `1` | `S2.7` | `** Batch / Steps **` |
|  |  | `2/3/4` | `S2.6a` | same |
|  |  | `5` | `S2.6b` | same |
|  |  | `6/7` | next/prev page → `S2.6` | same |
|  |  | `0` | `S2.17` or `S2.3` | same |
| **S2.6a Step Number Prompt** | line int | `1..N` | `S2.8`/`S2.11`/`S2.10` | `Enter step number (1..{N}, 0 to cancel):` |
|  |  | `0` | `S2.6` | same |
| **S2.6b Append Batch Selector** | line int | `1..Count` | append → `S2.6` | `** Select a batch **`, `Choice:` |
|  |  | `0` | `S2.6` | same |
| **S2.7 Step Type Picker** | menu digit | `1..7` | `S2.8` | `** Step / Select operation **` |
|  |  | `0` | `S2.6` | same |
| **S2.8 Step Editor** | menu digit | `1` | `S2.8a` | `** Step / Edit **` |
|  |  | `2` | `S2.9` | same |
|  |  | `3` | save → `S2.6` | same |
|  |  | `0` | `S2.17` or `S2.6` | same |
| **S2.8a Operation Params** | menu digit | `0` | `S2.8` | `** Step / Operation parameters **` |
| **S2.9 Step Overrides Editor** | menu digit | `1..7` | prompt → return `S2.9` | `** Step / Overrides **` |
|  |  | `8/9` | clear → return `S2.9` | same |
|  |  | `0` | `S2.8` | same |
| **S2.10 Step Reorder** | menu digit | `1/2` | move → return `S2.10` | `** Steps / Reorder **` |
|  |  | `3` | save → `S2.6` | same |
|  |  | `0` | `S2.6` | same |
| **S2.11 Step Remove Confirm** | line input | `y` | remove → `S2.6` | `Remove step #{k} — {OpTypeK} ?` + `Proceed? (y/n):` |
|  |  | `n` | `S2.6` | same |
| **S2.12 Preflight plan** | menu digit | `1` | confirm gate → `S2.13`/`S2.14` | `** Batch / Preflight **`, `requiresConfirmation=`, `requiresSecret=` |
|  |  | `2/0` | `S2.3` | same |
|  |  | `3/4` | next/prev page → `S2.12` | same |
| **S2.12a Preflight errors** | menu digit | `1/0` | `S2.3` | `** Batch / Preflight (FAILED) **`, `Step {k}: Missing field:` |
|  |  | `2` | `S2.5` | same |
|  |  | `3` | `S2.6` | same |
|  |  | `4/5` | next/prev page → `S2.12a` | same |
| **S2.13 Batch Secret Prompt** | secret/ESC | `password` | `S2.14` | `Secret required:`, `Enter password (will not be echoed):` |
|  |  | `empty`/`ESC` | cancel → `S2.12` | same |
| **S2.14 Run Progress** | ESC | `done` | `S2.15` | `** Batch / Running **`, `Step:  {i}/{N}` |
|  |  | `ESC` | abort → `S2.15` | same |
| **S2.15 Run Summary** | menu digit | `1` | `S2.15a` | `** Batch / Summary **`, `Status:` |
|  |  | `2/0` | `S2.3` | same |
|  |  | `3/4` | next/prev page → `S2.15` | same |
| **S2.15a Batch Artifacts** | menu digit | `1` | `S2.15a1` | `** Batch / Artifacts **` |
|  |  | `2/3` | next/prev page → `S2.15a` | same |
|  |  | `0` | `S2.15` | same |
| **S2.15a1 Artifact Step Prompt**| line int | `1..N` | `S2.15b` | `Enter step number (1..{N}, 0 to cancel):` |
|  |  | `0` | `S2.15a` | same |
| **S2.15b Artifact Details** | menu digit | `1/2` | next/prev page → `S2.15b` | `** Artifacts / Step {k} **` |
|  |  | `0` | `S2.15a` | same |
| **S2.16 Validity details** | menu digit | `0` | `S2.3` | `** Batch / Validity details **` |
| **S2.17 Unsaved confirm** | line input | `y` | discard/return | `You have unsaved changes.`, `Discard changes? (y/n):` |
|  |  | `n` | return editor | same |