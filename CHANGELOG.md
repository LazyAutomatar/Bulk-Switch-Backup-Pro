# Changelog

Changes for Bulk Switch Backup Pro are tracked independently from the other network applications.

## [Unreleased]

## [3.0.3] - 2026-10-25

### Added
- Local operational and diagnostic file logging for the application and auto-update pipeline written to `%APPDATA%\BulkSwitchBackup\Logs` (`bulkswitchbackup.log` and `updates.log`) with millisecond timestamps and automatic size-based log rotation.
- Real-time download progress tracking with byte counts, percentage milestones, and transfer speeds logged to `updates.log` for troubleshooting network timeouts and firewalls.

### Fixed
- Resolve update download timeouts on slower network connections or high-latency enterprise links by allocating a dedicated 1-hour HTTP client for downloading installer packages.
- Add high-resilience fast-path fallback to CDN-hosted release manifest to prevent rate-limit throttling during frequent update checks.
- Dynamic theme accent contrast for action buttons in About and Credentials views across dark and light desktop themes.

## [3.0.2] - 2026-10-23

### Added
- Publish a silent-install launcher for the existing Windows installer, with installation logging, exit-code forwarding, optional destination directory, and no automatic restart or app launch.
- Document unattended installation commands; the existing v3.0.2 installer binary already supports silent mode and remains unchanged.

### Packaging
- Sign the Windows application before archiving/embedding it, sign the setup installer, verify publisher signatures and timestamps, and generate update hashes after signing.
- Keep Yash Nalawde as author and installer publisher. This owner-authorized public release uses a timestamped self-signed certificate; Windows may still show Unknown publisher or SmartScreen warnings. Future releases require trusted signing by default.

### Fixed
- Keep title-bar zoom controls from initiating window dragging or double-click maximization.
- Fit initial window bounds to the monitor working area at its display density, including smaller high-DPI screens.
- Keep the header unscaled and visible while zoomed content scrolls; adapt navigation and secondary header badges to the available width.


## [3.0.1] - 2026-10-23

### Added
- Command Results shows failed command attempts by device, the command text, rejection or timeout details, device response, filtering and JSON export. Open it automatically after runs with failed attempts.
- Continue working commands after CLI rejections and save clearly marked partial diagnostics when configuration capture fails; preserve previous complete backups.
- Mouse-wheel scrolling over the horizontal results scrollbar and Shift+wheel across the device table.

### Fixed
- Align wide profile dropdowns below the selector with its left edge, instead of letting the popup drift left.
- Keep accepted diagnostics if a later command times out; stop an unsynchronized session rather than mislabel later output.
- Distinguish complete backups from incomplete/failed captures in the run summary.


### Changed
- Show Auto in both execution-mode selectors and use a wider dropdown with a dynamic platform-count summary.
- Replace the Auto command editor with a read-only command reference for all supported platforms, including paging setup and required configuration capture.
- Restrict command editing to Custom Script and preserve its draft when switching execution modes.
- Improve device-table column widths, status wrapping, horizontal scrolling, frozen identity columns and progress/percentage separation.
- Add a local-only portable beta build script and visual beta label; leave release versioning and publication for after testing.

## [3.0.0] - 2026-10-23

### Added
- Auto-detect device OS before backups and select commands from 29 CLI families covering major switch vendors, Cisco access points and wireless controllers.
- Validate command responses, expose rejected diagnostics, and retry supported legacy syntax alternatives.
- View expandable Release Changelog & History in About, including the installed release history offline and incoming release notes before downloading an upgrade.
- Review the target version and release notes before explicitly confirming Download & Install.
- Add regression coverage for platform identity, discovery policy, cyclic topologies, bounded workers and queued cancellation.

### Changed
- Adapt unchanged built-in templates to detected platforms while preserving custom scripts, edited command order and repeated commands.
- Use a fixed discovery worker queue with IP deduplication and concurrency clamped to 1-64 workers.
- Require positive infrastructure evidence for discovered targets, prefer LLDP Enabled Capabilities, and exclude known endpoint advertisements.
- Index inventory merges and desktop row lookup; refresh aggregate metrics on the session timer.
- Restrict optional configuration saves to recognized platforms with supported non-interactive save commands.
- Allow up to 120 seconds for configuration capture while preserving cancellation and complete-prompt requirements.
- Include actual version-specific release notes in the update manifest and public release.

### Fixed
- Separate Catalyst 9800 IOS-XE and AireOS command dialects, including radio syntax and configuration-output fallback.
- Preserve prior backups when required configuration output is empty, denied or rejected.
- Handle vendor prompts, ANSI colors, and paging continuations split across SSH reads.
- Correct LLDP multiline records, management addresses and local-interface selection.
- Exclude invalid discovered addresses, apply subnet scope to APs, promote placeholders to IP targets, and retain distinct IPs sharing a hostname.
- Drain pending desktop progress callbacks before final run summaries.
- Preserve the validated .NET 10 migration, cancellation behavior, offline tests and packaging fixes when reconciling the obsolete rollback branch.

### Compatibility
- Platform templates are covered by simulated SSH tests; live firmware and production performance require device validation. See PLATFORM_SUPPORT.md for the matrix and limits.
- Recursive topology discovery remains Cisco-oriented; provide management IPs directly for mixed-vendor fleets.
- Automatic installer launch is Windows-only; macOS and Linux users download the matching release package.


## [2.5.4] - 2026-10-23

Conservative public testing release from the merged main branch. The platform-detection and migration-revert branches are excluded; live-device and native cross-platform validation remain manual checks.

### Added
- Offline xUnit coverage for backup profiles, captured output, credential fallback, discovery, worker limits, cancellation, paging, and per-device failures.
- Injectable SSH transport and in-memory test sessions; core tests never contact network devices or load stored credentials.
- A compact `scripts/test-core.ps1` runner with optional filtering, TRX/log artifacts, and a failing gate when no tests execute.
- Migration validation CI for Linux, Windows, and macOS, plus a serial roadmap and resumable migration checkpoint.

### Changed
- Target .NET 10 LTS in Core and Desktop; align the SDK selection, packaging path, CI SDK, and runtime labels.
- Make `scripts/test-all.ps1` run the core suite before building the existing Avalonia desktop application.
- Gate release packaging on the offline tests and desktop build; publish Linux archives alongside Windows and macOS packages and use this changelog as release notes.
- Report that backup processing finished without implying every device succeeded.

### Fixed
- Limit recursive discovery connections as well as backup workers to the configured concurrency.
- Atomically claim visited devices and merge discovered targets across concurrent workers.
- Propagate cancellation through port probing, authentication, command reads, discovery, and configuration-save handling.
- Reject incomplete prompt reads after timeout rather than marking partial captures complete.
- Write completed backup files through temporary siblings and preserve earlier backups when capture fails.
- Apply branding-audit directory exclusions on both Unix and Windows paths.
- Pin the patched Tmds.DBus.Protocol 0.21.3 backport instead of Avalonia's vulnerable transitive version.

- Preserve completed backup exports after cancellation and dispose session cancellation sources.
- Align packaging parameters, installer version, release artifact names and update-manifest generation; use native tar on Unix.
