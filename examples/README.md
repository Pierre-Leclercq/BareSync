# BareSync examples

This folder contains lightweight runnable examples for local validation.

## 1) Redirected-input smoke run (open app and exit)

Input script:
- `inputs/mainmenu-exit.txt`

Run from repository root (`BareSync/`):

```bat
dotnet run --project src/BareSync/BareSync.csproj < examples\inputs\mainmenu-exit.txt
```

Expected behavior:
- BareSync starts,
- main menu is rendered,
- input `0` exits cleanly.

## 2) Migration guard tests

Run only the migration guard tests:

```bat
dotnet test tests/BareSync.Tests/BareSync.Tests.csproj --filter FullyQualifiedName~ConsoleAbstractionGuardTests -v minimal
```

These tests verify that:
- `src/BareSync` has no direct `Console.` calls,
- `src/BareSync/BareSync.csproj` no longer links legacy `TuiCompat` source files.
