# BareSync

A deterministic console tool to index, verify and synchronize file trees using CRC64.

## Quick commands

- Build
  - `dotnet build BareSync.sln -v minimal`
- Test
  - `dotnet test BareSync.sln -v minimal`

## Examples

See `examples/README.md` for runnable examples (including redirected-input smoke run and migration guard tests).

## User manual

See `userManual.md` for practical, step-by-step usage notes (including secret setup and CLI batch execution with `/BATCH:"..."`).

## Prevent suspend during long runs (Linux)

Long sync/index refresh operations can be interrupted if desktop idle suspend triggers. On Linux systems with systemd, you can run BareSync under an inhibitor:

`systemd-inhibit --what=sleep --why="BareSync running" baresync <args>`

If Windows resume ever leads to a black screen requiring reset, check Event Viewer around the incident in: Kernel-Power, ACPI, Display, Disk, and Ntfs.