# Bulk Switch Backup Pro - Project State

## Status Overview
- **Current Phase:** .NET 10 offline core gate complete; UI/runtime and packaging validation pending (see MIGRATION_STATE.md)
- **Active Sprint:** Serial application 1 migration on `migration/avalonia/bulk-switch-backup`; see `MIGRATION_ROADMAP.md` and `CHANGELOG.md`

---

## Phase 0: Repo Plumbing & Infrastructure Alignment — DONE
- [x] Fixed public repo distribution URL in `UpdateService.cs` -> `LazyAutomatar/Bulk-Switch-Backup-Pro`.
- [x] Added automated GitHub Actions release workflow `.github/workflows/release.yml`.
- [x] Scaffolded `scripts/` automation suite (`audit-branding.ps1`, `run-dev.ps1`, `test-all.ps1`, `prepare-release.ps1`, `publish-release.ps1`).
- [x] Created `AI_AGENT_GUIDE.md` developer guide.
- [x] Initialized `STATE.md` and `BUGS_AND_FEATURES.md`.

---

## Phase 1: MainWindow Cross-Platform Title Bar Parity — DONE
- [x] Implemented dedicated `UpdateTitleBarMetrics()` with macOS 78px traffic light clearance and Windows 142px caption button clearance.
- [x] Verified seamless theme switching, window dragging, and LayoutTransformControl DPI scaling.

---

## Phase 2: RunMonitorView — Session Summary Dashboard + Console Uplift — NEXT
- [ ] Add session summary banner card displaying elapsed runtime, progress percentage, completed count, and error count.
- [ ] Uplift "Run Backup" button with high-contrast accent theme styling and interactive status text ("Run Backup" / "Stop").
- [ ] Ensure execution console maintains crisp dark-mode terminal contrast across all application themes.
