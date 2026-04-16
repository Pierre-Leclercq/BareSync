# ConsoleMock.Progress.md

## Problem statement

- Windows crash observed: "Descripteur non valide" from System.ConsolePal.GetBufferInfo/GetCursorPosition when stdout is redirected (no TTY).
- Trigger path: UI progress rendering in `UI/OperationRunner.cs` uses `Console.CursorTop` and `Console.SetCursorPosition` even when `Console.IsOutputRedirected` is true.
- Current harness runs the app as a child process with `RedirectStandardOutput = true`, so any progress screen can crash.

## Constraints

- Tests-only changes; no production code edits.
- Must support Windows first (Release), ideally cross-platform.
- Keep harness deterministic and TDD-friendly.
- All console mocking must go through IConsoleAdapter; do not add parallel mocks.

## Chosen direction

- Keep the process-based redirected harness as the baseline.
- Keep TP_UI_002 skipped until a safe strategy exists for cursor APIs under redirection.
- Provide a tests-only console adapter for unit-level tests.

## Minimal console mock (tests-only)

Design rationale:
- Provide a tests-only adapter for deterministic input/output.
- Avoid direct `System.Console` usage in unit tests that need buffering.
- Keep the surface area minimal and intentional.

Minimal surface area (current paths):
- Output: `Write`, `WriteLine`, `Clear` (no-op), `Out`/`Error`.
- Input: `ReadLine` via queued lines; `ReadKey`/`KeyAvailable` queue for key paths; `In` reader.
- Cursor/layout: `CursorTop`, `SetCursorPosition`, `CursorVisible`, `WindowWidth`, `BufferWidth` (safe defaults).

## Minimal mock milestones (status)

- [x] M0: IConsoleAdapter + MockConsoleAdapter
- [x] M1: MockConsoleAdapter unit tests (output, ReadLine, cursor safety)
- [x] M2: Redirected process harness retained; TP_UI_002 skipped
- [ ] M3: Establish a safe progress-rendering strategy without production changes

## Success criteria

- TP_UI_001 remains green in Debug and Release.
- TP_UI_002 remains skipped until cursor access is safe under redirection.
- Zero production code changes.

## Risks and fallback

- Redirected console still crashes on cursor APIs during progress rendering.
- Without production guards, progress-screen routing tests remain blocked.

## Known limitations

- Redirected console can throw on cursor APIs during progress rendering; TP_UI_002 stays skipped.
- `Console.IsOutputRedirected` remains environment-driven; the mock does not override static Console flags.

## Worklog (timestamped)

- 2026-01-25T19:10:00+01:00: Restored tests-only console adapter + mock; kept redirected harness and kept TP_UI_002 skipped (not run by agent).
