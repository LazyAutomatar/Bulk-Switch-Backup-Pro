# Serial network application migration

## Locked decisions

- One active application at a time; preserve independent repositories and existing workflows.
- Native C# host/core logic and .NET 10 LTS. Keep Cisco Guest Shell Python only where its on-device `cli` module requires it.
- Use the Network Health Reporter Avalonia/Fluent design language; keep theme resources, scaling, titles, and app attribution consistent.
- Adapt useful topology improvements with commit provenance, retaining each destination's stronger existing behavior. No new shared package feed.
- Preserve existing updater behavior, application identity, release version, and app-specific distribution endpoints during each migration.
- Firmware CLI testing is explicitly skipped; compilation/static review do not count as runtime validation.

## Git and checkpoint contract

Start each app from a clean current `main` and record its baseline SHA. Create
`migration/avalonia/<lowercase-repository-name>` only when its serial turn starts.
Use separate commits for core, UI, packaging, and documentation work. Maintain
that repository's `CHANGELOG.md` with `[Unreleased]` notes and a small
`MIGRATION_STATE.md`. Open a draft PR per app and leave `main` unchanged until
review/merge. Do not synchronize unrelated migration branches, force-push, tag,
or publish at these checkpoints.

Every app follows: baseline inventory → headless core gate → Avalonia integration
→ validation → documented handoff. An app's PR may remain open, but its unfinished
technical gates must not be silently bypassed to begin the next application.

## Execution queue

| Order | Repository | Core gate before UI |
| --- | --- | --- |
| 1 | Bulk-Switch-Backup | Existing Avalonia consolidation; profiles, backup content, fallback credentials, bounded discovery/backup workers, cancellation and per-device failures. |
| 2 | CiscoTechSupportAnalyzer | Python-to-C# parser; section extraction, hardware/stack/redundancy, counters, incomplete evidence, and retained output formats. |
| 3 | Switch-Health-Parser | Extract headless C# ingestion/parsing/rules/reporting from WPF, verify findings and evidence, then replace WPF services/views. |
| 4 | firewall-rule-review | Port Python parsing, Palo Alto/Fortinet analysis and workbook export; verify disabled/zero-hit/broad/shadowed-rule cases. |
| 5 | Network-IP-to-Port-Tracer | Port tracing; IP/MAC normalization, ARP/CAM, port channels, neighbor traversal, loops, limits and unreachable devices. |
| 6 | PortShuter | Port scan/planning/execution/rollback; preserve protected-port exclusions and separation between scan and device changes. |
| 7 | Visio-Creator | Preserve Core/Export and regressions; apply selected topology fixes, validate graph/layout/OpenXML before replacing WPF. |
| 8 | switch-handover-generator | Preserve parsers, ZIP and Excel; adopt validated topology, add preview and Visio export alongside inventory output. |
| 9 | Firmware-Upgrade-Orchestrator | Native host orchestration and PnP reachability prerequisite; build/static review only, CLI tests skipped by request. |

## Topology handoff for applications 7 and 8

Compare Reporter commit `8ee7280b373653fc1eb94c5ead03f4bd2d7e1d99` from
`fix/assessment-integrity-ui-review` with Visio-Creator. Reporter already shares
Visio concepts; do not overwrite working destination behavior wholesale or import
the Reporter desktop project. Its main branch has fabricated address/port
fallbacks, and the review branch still documents inferred-link limitations.

Adopt improvements to normalization, graph construction, physical-link identity,
routing, dragged bounds, filtering, export snapshots, and OpenXML only after
destination regression coverage. Preserve Visio's classification, reconciliation,
stencil mapping, and documented geometry/text/stylesheet invariants. Keep OpenXML
cross-platform and COM export Windows-only.

In the handover app, adapt inventory and captured neighbor evidence into the
topology subset validated in Visio-Creator. Record both source commits. Keep the
flow headless: inventory/evidence → graph → layout → export. Preview and Visio
export use the same layout snapshot; existing Excel sheets retain their meaning.
Missing evidence remains unresolved, without invented addresses, ports, or links.

Regression cases: ambiguous/duplicate names, missing addresses, reversed neighbor
observations, physical parallel links, port channels, disconnected devices,
deterministic layout, and Visio package relationships/geometry.

## Validation and context budget

Use sanitized fixtures and simulated transports for the first eight apps. Capture
Python output baselines before porting, documenting intentional corrections.
Test normalized behavior and export structures instead of binary file equality.
Do not contact live devices from cloud tests.

Give each app one `scripts/test-core.ps1` entry point. Restore/build once per
checkpoint, filter while editing, and run its full core suite before UI changes.
Store verbose logs/TRX outside tracked source. Summarize counts/time/failure names
only; zero discovered/executed tests fail the gate. Then validate desktop builds,
bindings, progress, cancellation, themes, scaling and preserved imports/exports.
Mark unavailable platform/runtime checks unverified rather than passed.

Use a fresh task context at stage boundaries. Read the active handoff plus
relevant files, not all nine repos or complete old logs. The handoff records
branch, baseline/current commit, completed gate, validation commands/results,
limitations, source provenance, and the next bounded task.

## Firmware PnP requirement

PnP can remove local laptop reachability after reboot. Send
`pnpa service discovery stop` immediately after obtaining privileged EXEC access
on every initial/retry/post-reboot connection, before ordinary preflight or
transfer operations. Stop progression if that command fails unless a configured
management-route alternative succeeds. Route destination, interface/next hop and
VRF must be explicit; never guess a gateway or modify an unrelated default route.

Apply the reachability prerequisite in the on-device ZTP path before
reboot-sensitive operations. A laptop-side reconnect cannot repair an already
unreachable switch. Retain the Guest Shell Python template only for on-device
execution while porting the desktop host logic to C#.

Run no firmware CLI harness or core tests in this migration. Record
`CLI validation: SKIPPED — requested exception`; device/reboot validation is a
separate pre-release checkpoint and cannot be reported as passed.
