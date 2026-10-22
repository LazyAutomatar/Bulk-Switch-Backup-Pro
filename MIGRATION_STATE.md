# Bulk Switch Backup Pro migration checkpoint

## Resume here

- Repository: `Majordean0303/Bulk-Switch-Backup` (application 1 of 9).
- Branch: `migration/avalonia/bulk-switch-backup`.
- Baseline: `6c155f3b9593badf6ec32d08451934e1abeef61c` from `main`.
- Last completed code checkpoint: `59f7ec46b1cfdd1f9209cf5206021b10d72daa58`.
- Current stage: **.NET 10 and offline core gate complete; UI/runtime and packaging validation remain**.
- The existing desktop already uses Avalonia. No other application has been modified.
- Source topology imports: none at this stage. Their pinned sources are in `MIGRATION_ROADMAP.md`.

## Completed

- Baseline Release build passed on .NET 8 with no tests present; the previous test script only built the solution.
- Core/Desktop now target .NET 10, with matching SDK selection, runtime labels, packaging output path and release SDK setup.
- An injectable transport preserves the production SSH.NET path and makes complete backup/discovery runs testable offline.
- Added 43 xUnit cases, two synthetic CDP/LLDP fixtures, and compact validation scripts with persistent logs/TRX.
- Fixed bounded recursive discovery, atomic discovery claims/merges, cancellation propagation, incomplete prompt timeouts, and atomic backup file replacement.
- Pinned the patched D-Bus 0.21.3 backport. Final local desktop build has zero warnings/errors.
- Added Linux/Windows/macOS validation CI. Remote CI outcomes must be checked separately; local success is not a claim of remote success.
- Added the app changelog and serial roadmap. Existing update feed, app version, packaging identity and production releases are unchanged.

## Reproduce validation

Requires .NET 10 SDK and PowerShell 7 on PATH:

```powershell
./scripts/test-core.ps1
./scripts/test-core.ps1 -Filter 'FullyQualifiedName~Cancellation' -NoRestore
./scripts/test-all.ps1
./scripts/audit-branding.ps1
```

The final local `test-all.ps1` run passed **43 tests, zero failed/skipped**, then
built the Avalonia desktop project with **zero warnings/errors**. The branding
audit and `git diff --check` passed. An intentionally unmatched filter, using a
separate results directory, exited 1 as required instead of reporting success.

Artifacts: `artifacts/core-tests/core.trx`, `build.log`, `restore.log`, `test.log`,
and `artifacts/desktop-build.log` (ignored). The environment used .NET SDK 10.0.402
and PowerShell 7.6.8 on Linux. No real device, stored credential, firmware image,
installer, update download or release endpoint was exercised by tests.

## Remaining gates and concrete follow-ups

1. Finish UI/runtime consolidation: exercise navigation, bindings, theme switching,
   progress/cancellation, saved partial results, scaling and folder/ZIP export.
   Local compilation does not certify visual behavior or Windows/macOS rendering.
2. Resolve the existing Windows-only DPAPI credential-vault behavior deliberately
   before claiming saved-credential parity on macOS/Linux. Session credentials
   remain available; tests do not load the user's credential vault.
3. Review packaging independently. The existing release workflow passes `-Version`
   and `-Repo` to `publish-all.ps1`, although that script only declares `-Target`
   and `-Configuration`. Its expected release asset names differ from generated
   names, and the script does not generate a new update manifest. The Linux tar
   branch invokes `tar.exe` even when the located command is Unix `tar`. These are
   existing issues; this checkpoint changes the runtime path without claiming a
   successful package/release. Do not run release-preparation/publishing to test it.
4. Inspect the migration PR's validation matrix and record actual remote results.
5. Close application 1's technical gates and hand off before creating application
   2's branch (`migration/avalonia/ciscotechsupportanalyzer`). Preserve the original
   Python parser behavior/fixtures before its C# port.

## Other applications

Applications 2–9 remain queued in `MIGRATION_ROADMAP.md`. Visio-Creator and the
handover app use selective, provenance-tracked topology upgrades, not a shared
package rewrite. Firmware must apply `pnpa service discovery stop` immediately
on privileged connection/reconnection (or an explicitly configured management
route); its CLI tests remain **SKIPPED — requested exception**.

## Continued execution (22 October 2026)

- Morning review is owned by the user; continue serial implementation of the remaining apps without waiting for a merge.
- Core checkpoint CI passed on Linux, Windows and macOS (PR #1).
- Fixed partial-result export availability after cancellation and packaging/workflow contracts identified above.
- Native Windows/macOS rendering, real-device runs, installer execution, and non-Windows persistent credential-vault parity remain manual review items. Existing Windows DPAPI storage is preserved; use direct session credentials on other platforms.
- Continue with CiscoTechSupportAnalyzer after the updated local core/build check.

## Nine-app continuation complete

The user's later instruction authorized continuing through all nine applications before morning review. See `MIGRATION_OVERVIEW.md` for all draft PRs, per-app test counts and hardware/native UI gates. The first eight have 330 core and 14 headless UI checks; firmware retains the explicit build/static-only exception. No main branches or releases were advanced.
