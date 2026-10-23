# Branch review - 23 October 2026

Reviewed all local branches and all origin branches after fetch/prune.

| Branch | Finding | Release decision |
| --- | --- | --- |
| feature/p0-infra-plumbing | Ancestor of main | Already included |
| feature/p1-titlebar-metrics | Ancestor of main | Already included |
| feature/p2-runmonitor-dashboard | Ancestor of main | Already included |
| feature/p3-credentials-vault | Ancestor of main | Already included |
| feature/p4-command-profiles | Ancestor of main | Already included |
| feature/p5-about-updates | Ancestor of main | Already included |
| feature/p6-app-logging-polish | Ancestor of main | Already included |
| feature/p7-status-table-polish | Ancestor of main | Already included |
| migration/avalonia/bulk-switch-backup | Merged upstream through PR #1 | Fast-forward local main to 970de06 |
| feature/platform-detection-command-validation | Broad command, OS detection and discovery changes (14 files) | Defer from conservative release; needs separate device validation |
| revert/avalonia-migration-pr1 | Removes .NET 10 migration, tests and reliability fixes (31 files) | Exclude; conflicts with current baseline |
| main | Local main was behind origin/main | Update and prepare v2.5.4 |

No unmerged feature branch qualifies as low risk. No branch is deleted.
The pre-existing modified dist executable is preserved in a named Git stash during release preparation.
Only compiled assets and public documentation are distributed to the public repository.

Validation evidence is stored under ignored artifacts/. Native macOS/Linux UI, installer execution,
and live-device backup remain manual checks; offline test success does not certify these workflows.

Release validation on this laptop: .NET SDK 10.0.402; 43/43 offline tests passed; desktop build passed with zero warnings/errors; Windows portable and Inno installer packaging passed; installer SHA-256 matches the v2.5.4 manifest. Startup created a responding Bulk Switch Backup Pro window, then closed cleanly. Installer execution and real-device backups were not performed.

## v3.0.0 integration

The user subsequently requested all branches be merged for v3.0.0.
- Merged the obsolete rollback branch with the ours strategy: its history is recorded while the validated .NET 10 migration, fixes and tests remain in place.
- Merged the platform-detection-command-validation branch, including 29 CLI family templates, command validation and bounded discovery scheduling.
- All local and fetched remote branch tips are now ancestors of main. Branches remain available; none were deleted.
- Added packaged offline changelog history, incoming release notes and an explicit review step before downloading/installing an upgrade.
- The release manifest and GitHub release body use the 3.0.0 changelog section; README, changelog and platform support matrix are mirrored publicly.

Local v3.0.0 validation: 163 tests passed, zero failed/skipped; desktop build passed with zero warnings/errors. Headless About view tests verify incoming-note bindings and review/cancel behavior. Light/dark previews were inspected. Windows executable, ZIP and Inno installer built successfully, and the generated manifest contains the 3.0.0 notes with the matching installer SHA-256. Real-device backups and installer execution remain manual checks.
