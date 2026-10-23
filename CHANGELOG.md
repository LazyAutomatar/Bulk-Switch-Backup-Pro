# Changelog

Changes for Bulk Switch Backup Pro are tracked independently from the other network applications.

## [Unreleased]

Release notes for the next version. Assign the release version and date when preparing
that release; retain this history and start a new `[Unreleased]` section for later changes.

### Added

- Default Auto profile: identify each device OS and category before selecting backup commands, with identity evidence included in the output.
- Templates for 29 CLI families across major switch OEMs, Cisco access points, Catalyst 9800 and AireOS wireless controllers. See [platform support](PLATFORM_SUPPORT.md) for the exact matrix and boundaries.
- Per-command response validation for CLI errors, authorization failures and empty output, with targeted syntax fallbacks and visible accepted/rejected results.
- Discovery regression coverage for endpoint exclusion before TCP probing, enabled-capability precedence, AP scope, duplicate addresses, placeholder promotion and queued cancellation.
- Scale scenarios covering a 100-device cyclic topology with one/four workers and a 128-device single-worker chain.
- Offline xUnit coverage for backup profiles, captured output, credential fallback, discovery, worker limits, cancellation, paging, and per-device failures.
- Injectable SSH transport and in-memory test sessions; core tests never contact network devices or load stored credentials.
- A compact `scripts/test-core.ps1` runner with optional filtering, TRX/log artifacts, and a failing gate when no tests execute.
- Migration validation CI for Linux, Windows, and macOS, plus a serial roadmap and resumable migration checkpoint.

### Changed

- Automatically adapt unchanged built-in templates to the detected OS; preserve explicit Custom scripts and edited command order, including repeated commands.
- Use a smaller portable IOS/NX-OS diagnostic baseline instead of sending hardware- and release-specific commands to every Cisco switch.
- Require positive CDP/LLDP infrastructure evidence before admitting discovered targets. Prefer LLDP Enabled Capabilities, distinguish protocol-specific capability letters, and exclude known endpoint advertisements. Hostnames alone do not establish device type.
- Replace recursive discovery tasks with a fixed asynchronous worker queue, deduplicate IP scheduling, and clamp concurrency to 1–64 workers. Preserve separate discovery and backup phases.
- Index inventory merges and desktop device-row lookup; scan only SSH response tails for prompt/pager handling and refresh aggregate UI metrics on the session timer.
- Restrict opt-in configuration saves to recognized families with a supported non-interactive save path; log skipped saves on other families.
- Allow up to 120 seconds for configuration capture while retaining command cancellation and prompt-completion requirements.
- Target .NET 10 LTS in Core and Desktop; align the SDK selection, packaging path, CI SDK, and runtime labels.
- Make `scripts/test-all.ps1` run the core suite before building the existing Avalonia desktop application.
- Report that backup processing finished without implying every device succeeded.

### Fixed

- Separate Catalyst 9800 IOS-XE and AireOS command dialects. Correct Catalyst `24ghz` radio syntax and AireOS AP image, CDP-neighbor, route and configuration commands; add AireOS configuration-output fallback.
- Retry supported legacy IOS running-configuration, MAC-table and environment syntax alternatives when the primary command is rejected.
- Prevent failed, denied or empty required configuration captures from replacing previous backups; report rejected optional diagnostics as warnings.
- Handle vendor-specific prompts, colored prompts and pager markers split across SSH reads, including Enter and yes/no continuations.
- Correct LLDP multiline descriptions, adjacent records, management address parsing and local-interface selection; do not mistake remote Port Description for the local interface.
- Exclude invalid, unspecified, loopback, link-local and multicast discovered management addresses from probing. Keep recognized neighbors without usable IPv4 addresses visible as skipped layer-2 entries.
- Apply subnet filters to discovered APs as well as switches and routers.
- Schedule placeholders promoted to real IP addresses and avoid collapsing distinct management IPs solely because they share a hostname.
- Drain pending desktop progress callbacks before finalizing success, cancellation or failure so summary counters and row states are consistent.
- Limit recursive discovery connections as well as backup workers to the configured concurrency.
- Atomically claim visited devices and merge discovered targets across concurrent workers.
- Propagate cancellation through port probing, authentication, command reads, discovery, and configuration-save handling.
- Reject incomplete prompt reads after timeout rather than marking partial captures complete.
- Write completed backup files through temporary siblings and preserve earlier backups when capture fails.
- Apply branding-audit directory exclusions on both Unix and Windows paths.
- Pin the patched Tmds.DBus.Protocol 0.21.3 backport instead of Avalonia's vulnerable transitive version.
- Preserve completed backup exports after cancellation and dispose session cancellation sources.
- Align packaging parameters, installer version, release artifact names and update-manifest generation; use native tar on Unix.

### Serial migration handoff

- Add the nine-app review dashboard with independent draft PRs, validation counts, topology provenance and the explicit firmware build-only exception.


### Validation and compatibility notes

- Latest local validation: 151 tests passed, zero failed/skipped; Avalonia desktop build, branding audit and whitespace checks passed.
- Validation uses synthetic device identities and simulated SSH responses. Live firmware compatibility, production throughput and native desktop load behavior remain unverified.
- LLDP is not authenticated device identity: an endpoint advertising only bridging without endpoint-identifying details can still resemble infrastructure. Auto backup independently recognizes the OS before selecting commands.
- Recursive discovery remains Cisco-oriented. Supply management IPs directly for mixed-vendor fleets; the CLI-family matrix does not certify every model or firmware release.
- Preserve credential fallback, cancellation propagation, atomic backup replacement and existing completed-backup exports. Release packaging and version/tag publication are separate from this feature branch.
