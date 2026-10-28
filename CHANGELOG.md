# Changelog

Changes for Bulk Switch Backup Pro are tracked independently from the other network applications.

## [Unreleased]

## [3.2.0] - 2026-10-28

### Added
- **Comprehensive Cisco Switch Enterprise Audit Template**: Expanded the built-in Cisco Switch (IOS / IOS-XE) template with extensive operational, hardware, security, and protocol inspection commands:
  - Hardware & Stacking: `show switch stack-ports summary`, `show switch virtual`, `show stack-power`, `show stackwise-virtual`, `show stackwise-virtual link`, `show stackwise-virtual dual-active-detection`, `show post`, `show redundancy`.
  - Power & Environment: `show power inline`, `show env power`, `show env fan`, `show environment temperature`.
  - Interfaces & L2/L3 Diagnostics: `show interfaces transceiver detail`, `show udld`, `show storm-control`, `show errdisable recovery`, `show errdisable detect`, `show spanning-tree inconsistentports`, `show spanning-tree blockedports`, `show spanning-tree active`, `show spanning-tree root`, `show vlan`.
  - First-Hop Redundancy & IP Protocols: `show standby brief`, `show standby all`, `show vrrp brief`, `show vrrp all`, `show ip protocols`, `show ip route summary`, `show ip sla summary`, `show ip sla statistics`.
  - Network Security & Snooping: `show port-security`, `show port-security address`, `show ip dhcp snooping`, `show ip dhcp binding`, `show ip dhcp conflict`, `show ip source binding`, `show ip arp inspection`, `show ip verify source`, `show device-tracking database`, `show authentication sessions`, `show aaa servers`, `show access-lists`, `show crypto pki certificates`.
  - System Auditing: `show license usage`, `show log | include Interface`, `show archive config differences nvram:startup-config system:running-config`.
- **Comprehensive Cisco Catalyst 9800 WLC Template**: Integrated enterprise wireless controller operational state and telemetry:
  - High Availability & Chassis: `show chassis`, `show chassis rmi`, `show redundancy summary`.
  - Wireless & SSIDs: `show wireless summary`, `show wlan all`, `show wireless profile policy detailed`, `show wireless profile flex summary`, `show wireless profile flex detailed`, `show wireless country configured`.
  - AP Fleet & Radios: `show ap config general`, `show ap image`, `show ap rf-profile summary`, `show ap dot11 5ghz channel`, `show ap dot11 24ghz channel`, `show ap dot11 5ghz txpower`, `show ap dot11 24ghz txpower`.
  - Clients & Infrastructure: `show wireless stats client delete reasons`, `show interfaces transceiver detail`, `show aaa servers`, `show crypto pki certificates`, `show archive config differences nvram:startup-config system:running-config`.
- **Default Customizable Template Enhancement**: Updated the custom script default template to include sorted CPU, sorted memory, temperature, and CPU policer hardware diagnostics out-of-the-box.

### Validation
- 230 offline and headless UI tests passed.
- Solo branding audit passed with 100% clean attribution to Yash Nalawde.

## [3.1.0] - 2026-10-27

### Packaging
- This owner-authorized release supplies **unsigned Windows application and installer packages**. Windows may show Unknown publisher or SmartScreen prompts. SHA-256 hashes remain available in the update manifest.
- Add an explicit unsigned-release workflow option; signed packaging remains the default for other releases.

### Added
- Offer a read-only Assessment collection profile alongside Configuration Backup, aligned with Network Health Reporter's Catalyst Commands screen; gate stack commands by supported model.
- Export explicit AP version, inventory, controller and wired-interface commands without requiring one CAPWAP dialect to succeed.
- Extend the existing capture manifest with snapshot ID, collector/profile versions, independent device family/hardware/HA/stack metadata, collection windows and per-attempt UTC timing/duration.
- Preserve exact requested, canonical and executed commands separately, with unmodified attempt output and detailed accepted/unsupported/unauthorized/timeout/truncated/empty outcomes.
- Optional timestamped interface-error/discard samples and reset-aware interval rates for IOS/IOS-XE and NX-OS, without interpreting lifetime totals as current loss or AP disconnect evidence.
- Separate Private ZIP and redacted Sharing ZIP exports; redact secrets in both text and manifest while retaining diagnostic configuration and topology identifiers.

### Fixed
- Require prompt framing and complete output evidence, including IOS/9800 configuration end markers, before calling a capture complete or replacing a valid configuration.
- Freeze and hash per-run files; export one snapshot without mixing older configuration or omitting unreachable/unattempted targets.
- Protect new files/directories with owner-only permissions/ACLs, prevent concurrent runs in one destination, and isolate Save to Folder exports.

### Validation
- 230 offline/headless tests passed; the Avalonia desktop build and branding audit passed. Live firmware and Windows ACL behavior remain unverified.

See [Assessment exports](ASSESSMENT_EXPORTS.md) for schema compatibility, command provenance, counter interpretation and redaction boundaries.

## [3.0.7] - 2026-10-26

### Added
- Catalyst 9800 WLC evidence collection for platform resources, environment, AP uptime/history, rogue AP summary and 5 GHz CleanAir configuration, keeping each actual command and evidence type distinct.
- Schema-versioned `capture-manifest.json` in ZIP exports with UTC timestamps, requested targets, device identity/platform/profile, configuration state, per-command outcomes and failure reasons. Current-run output selection excludes stale files; legacy exports are explicitly unverified.

### Fixed
- Continue supported diagnostics after command rejection or timeout; reconnect and initialize a fresh session after incomplete responses rather than reusing an unsynchronized shell.
- Preserve partial captures when required configuration is unavailable, and record unreachable/skipped/unattempted targets without successful backup claims.
- Default the explicit save-configuration option to off. Run `write memory` only when selected and after required configuration capture succeeds.

## [3.0.6] - 2026-10-26

### Changed
- Use one default Cisco switch diagnostic list for IOS/IOS-XE, C1000 and Small Business/CBS switches. Keep platform identification, paging and output parsing appropriate to each device.
- Include all 16 Network Health Reporter switching commands in default backups: sorted CPU and memory, power, temperature, stack details, stack-ring speed, VTP, CPU-policer statistics and both CDP views. Preserve rejected commands and continue capturing supported diagnostics and configuration.
- Fall back to unsorted CPU/memory and Small Business VLAN syntax when necessary without relabeling the actual captured command.

### Fixed
- Parse SG350 detailed CDP `Device-ID`, `Primary Management Address: IP` and `Addresses: IP` fields so advertised AP and C1000 neighbor addresses populate the inventory and backup queue.
- Recognize the Small Business `TransBridge` capability label so AP neighbors remain APs even without a summary table.

## [3.0.5] - 2026-10-26

### Fixed
- Synchronize the theme toggle with the actual application theme at startup and after switching between light and dark.
- Repair public release workflow conditions and the versioned installer signature-check path.
- Parse SG350/CBS CDP tables with split headings and wrapped device names, models and remote ports; traverse C1000 and Small Business switch neighbors using their advertised management addresses.
- Use the Small Business `show cdp entry * protocol` address lookup, fall back to `show cdp entry *` when detailed neighbors are absent, and preserve multiple CDP records without dashed separators.
- Identify discovery platforms from full `show inventory` output when version/system output is insufficient. Log parsed neighbor counts and explain when a switch cannot be traversed because no usable management IPv4 address was advertised.

### Added
- Small Business automatic backups include hardware inventory, CDP/LLDP topology and startup configuration alongside the running configuration and existing diagnostics.

## [3.0.4] - 2026-10-26

### Added
- Portable master-password credential vault for Windows, Linux and macOS using AES-256-GCM and PBKDF2-SHA256. Create, unlock and lock the vault from Saved Credentials; explicitly import existing Windows DPAPI profiles on their original Windows account.
- SSH-first connection fallback to Telnet port 23 for connection/negotiation failures, with the actual protocol recorded in progress and backup files. SSH credential rejection continues to the next credential without downgrading to Telnet.
- Bounded terminal login handling for legacy username/password prompts, including repeated prompts after SSH authentication and fragmented Telnet responses.

### Fixed
- Saved Credentials table crash when populated rows resolve Apply/Delete button bindings; hide enable-secret values and clear session credential fields when locking the vault.
- Classify Catalyst C1000 as an IOS switch throughout discovery and backup command selection; reserve wired-interface AP commands for detected access points in automatic profiles.
- Use the Small Business/CBS command profile and `terminal datadump` paging for SG350 devices, including discovery sessions.
- Preserve login timeout details in device status and propagate cancellation when discovery workers close their queue.

### Validation
- Added offline vault encryption, portability, tamper, concurrent-write and legacy-import tests; populated credential-page interaction coverage; login/Telnet tests; and C1000/SG350 pipeline regressions. Physical device and native Linux/macOS validation remain separate from Windows offline checks.

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
