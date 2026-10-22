# Changelog

Changes for Bulk Switch Backup Pro are tracked independently from the other network applications.

## [Unreleased]

### Added
- Offline xUnit coverage for backup profiles, captured output, credential fallback, discovery, worker limits, cancellation, paging, and per-device failures.
- Injectable SSH transport and in-memory test sessions; core tests never contact network devices or load stored credentials.
- A compact `scripts/test-core.ps1` runner with optional filtering, TRX/log artifacts, and a failing gate when no tests execute.
- Migration validation CI for Linux, Windows, and macOS, plus a serial roadmap and resumable migration checkpoint.

### Changed
- Target .NET 10 LTS in Core and Desktop; align the SDK selection, packaging path, CI SDK, and runtime labels.
- Make `scripts/test-all.ps1` run the core suite before building the existing Avalonia desktop application.
- Report that backup processing finished without implying every device succeeded.

### Fixed
- Limit recursive discovery connections as well as backup workers to the configured concurrency.
- Atomically claim visited devices and merge discovered targets across concurrent workers.
- Propagate cancellation through port probing, authentication, command reads, discovery, and configuration-save handling.
- Reject incomplete prompt reads after timeout rather than marking partial captures complete.
- Write completed backup files through temporary siblings and preserve earlier backups when capture fails.
- Apply branding-audit directory exclusions on both Unix and Windows paths.
- Pin the patched Tmds.DBus.Protocol 0.21.3 backport instead of Avalonia's vulnerable transitive version.
