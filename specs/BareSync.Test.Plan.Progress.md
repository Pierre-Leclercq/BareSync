# BareSync — Test Plan Progress

This file tracks iterative authoring of `Docs/BareSync.Test.Plan.md`.

## Hard rules (from prompt)

- SPEC ONLY: do not change production code.
- Do not change normative meaning of existing specs:
  - `Docs/BareSync.Specs.md`
  - `Docs/BareSync.UI.Specs.md`
- Technical specs = implementation design only; test plan = testing strategy only (no mixing).
- UI strings stay English (ASCII where possible).
- Determinism is mandatory: ordering, messages, and summaries must be stable.

---

## Iteration 1 — Skeleton + taxonomy + scenario list (DONE)

### Plan (Iteration 1)

1. Create `Docs/BareSync.Test.Plan.md` skeleton with conventions + determinism rules.
2. Add a test taxonomy aligned to the normative specs and technical design boundaries.
3. Define conceptual test harness contracts (preconditions, inputs, expected screen sequences, expected outputs).
4. Create an initial canonical scenario suite (names + short descriptions only).

### What I read (Iteration 1)

- `Docs/BareSync.Specs.md` (normative functional constraints: persistence, preflight, runner policy, secrets).
- `Docs/BareSync.UI.Specs.md` (normative UI routing, input rules, `ESC` semantics, error screens).
- `Docs/BareSync.Technical.Specs.md` (implementation design choices that influence test expectations deterministically).

### What I produced/changed (Iteration 1)

- Created `Docs/BareSync.Test.Plan.md` with:
  - Conventions + determinism requirements
  - Taxonomy A–F (UI, persistence, crash-safety, validation, runner, artifacts/secret safety)
  - Conceptual harness contracts (preconditions, inputs, expected traces, expected outputs)
  - Canonical scenario suite (Iteration 1 list of ~15 scenarios, names + short descriptions)

### Open questions / assumptions (testing-specific only)

- What is the expected `Last status` behavior when a batch run is canceled before step execution begins (e.g., decline confirmation, cancel `S2.13`)? (UI specs define routing but do not explicitly define last-status update semantics for “run not started”.)
- How should tests normalize or ignore non-deterministic fields (timestamps/elapsed time) when comparing outputs?
- What concrete artifact set is expected from each operation type (beyond “paths only”), given that underlying business ops may or may not produce logs/index artifacts?

### Next iteration goals (small, achievable)

- Expand each canonical scenario into a detailed test case:
  - Preconditions (files + settings)
  - Input sequence
  - Expected screen trace
  - Expected outputs (errors/run summary/artifacts)
- Add example expected renderings for:
  - `S1.5a` (interactive validation errors)
  - `S2.12a` (batch preflight errors)
- Add an example navigation trace format (how screen sequences are represented in tests).

---

## Iteration 2 — Formal template + detailed cases (scenarios 1–5) (DONE)

### Plan (Iteration 2)

1. Expand a first subset of the canonical scenario suite into detailed test cases.
2. For each expanded scenario, specify:
  - Preconditions (filesystem + mode state)
  - Input sequence (digits, prompt inputs, `ESC`)
  - Expected screen trace (ordered screen IDs)
  - Expected outputs (errors/run summary/step statuses/artifacts)
3. Keep expectations deterministic and avoid adding new implementation rules.

### What I produced/changed (Iteration 2)

- Formalized a reusable, tool-friendly test case template in `Docs/BareSync.Test.Plan.md` section 5:
  - Canonical representations: screen trace, inputs, expected outcomes comparison, non-determinism handling.
- Expanded the first 5 canonical scenarios into fully structured detailed test cases in `Docs/BareSync.Test.Plan.md` section 5:
  - `TP-UI-001` … `TP-UI-004`
  - `TP-BATCH-001`

Patterns that emerged:
- Most assertions should be phrased as deterministic sets/ordering rules (e.g., “non-empty English message”) rather than exact strings, unless the UI spec mandates the literal string.
- Many cancel/decline paths are UI-specified in routing but ambiguous in “Last status” semantics; treat these as explicit per-test assumptions until clarified.
- Artifact assertions should default to “paths only; deterministic ordering; no secrets” unless a spec mandates specific artifact presence.

### New open questions / assumptions (testing-specific)

- When interactive validation fails (routing to `S1.5a`), should Interactive mode `Last status` remain unchanged or be updated to something like `Fail`? (UI specs only define last-status updates “after each operation/run”.)
- When confirmation is declined (Interactive `S1.6` / Batch `Proceed? (y/n)`), should the outcome be recorded as `Canceled` and update `Last status`, or should it be treated as “no run”? (Current scenarios assume `Canceled`.)
- For cancellation on progress screens (`S1.8`, `S2.14`), what artifact expectations are safe to assert without over-constraining underlying business operations? (Current test cases only assert determinism/safety and may use harness stubs to force “no artifacts”.)
- Should tests assert `ErrorCode` values (from the technical design), or should they assert only `(FieldName, UserMessage)` to avoid coupling to early error taxonomy changes?

### Next iteration goals (small, achievable)

- Expand scenarios 6–15 into detailed test cases (invalid/incompatible units, preflight ok, confirmation gating, secret slot reuse, batch cancel, crash-safe leftovers).
- Add example expected renderings (string-level) for:
  - `S1.5a` error list lines
  - `S2.12a` error list lines
- Add an example “navigation trace” representation used by tests (including how repeated screens are recorded when input is ignored).

---

## Iteration 3 — CLI /EXTRACT coverage scope (DONE)

### Plan (Iteration 3)

1. Add `/EXTRACT` to test taxonomy and canonical scenario backlog.
2. Align CLI extract expectations with Specs/UI/Technical docs (routing, anti-disclosure, outcomes).
3. Link service-level extraction primitives already covered by tests.

### What I produced/changed (Iteration 3)

- Updated `Docs/BareSync.Test.Plan.md` with:
  - Iteration 11 non-normative update block for CLI extract,
  - new taxonomy section `G) CLI extract mode (/EXTRACT)`,
  - new canonical scenarios 16..20 for `/EXTRACT` (arg exclusivity, no-native failure, secret-gating, destination choices, CRC mismatch),
  - note linking to dedicated service-level tests (`EncryptedFolderServiceExtractTests`).

### Notes

- Detailed formal cases for scenarios 16..20 can be expanded in a next iteration if needed.
