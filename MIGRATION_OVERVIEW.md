# Morning migration review dashboard

All nine apps have independent migration branches, changelogs, handoffs and draft PRs. No main branch was merged, no release/tag was published, and no live network equipment was contacted. Native host projects target .NET 10 LTS and use Avalonia/Fluent controls. Original implementations remain references.

| Order | App / draft PR | Automated validation | Morning focus |
|---|---|---|---|
| 1 | [Bulk-Switch-Backup #1](https://github.com/Majordean0303/Bulk-Switch-Backup/pull/1) | 43 core checks; desktop build | Backup/discovery/cancellation, native appearance, local packaging and non-Windows credential-vault parity |
| 2 | [CiscoTechSupportAnalyzer #1](https://github.com/Majordean0303/CiscoTechSupportAnalyzer/pull/1) | 41 core + 2 UI checks; all 31 legacy diagnostic families covered | Real sanitized show-tech files, section/evidence browsing and HTML/JSON reports |
| 3 | [Switch-Health-Parser #1](https://github.com/Majordean0303/Switch-Health-Parser/pull/1) | 33 core + 2 UI checks; portable PDF | Capture/ZIP coverage, rule findings, PDF appearance and explicit SSH collection |
| 4 | [firewall-rule-review #2](https://github.com/Majordean0303/firewall-rule-review/pull/2) | 20 core + 2 UI checks; workbook smoke | Palo/Fortinet workbooks and reports; address overlap candidates require review and are not full shadow proofs |
| 5 | [Network-IP-to-Port-Tracer #1](https://github.com/Majordean0303/Network-IP-to-Port-Tracer/pull/1) | 25 core + 2 UI checks; recorded replay | SSH/optional Telnet, port channels, candidate edge ports and partial traces |
| 6 | [PortShuter #1](https://github.com/Majordean0303/PortShuter/pull/1) | 30 core + 2 UI checks; recorded audit | TDR support/timing, protected/unassessed ports, explicit selection, durable shutdown/rollback journals on lab equipment |
| 7 | [Visio-Creator #1](https://github.com/Majordean0303/Visio-Creator/pull/1) | 93 core + 2 UI checks; JSON→VSDX; 10 private-data/Visio checks explicitly skipped | Import/reconciliation, stencils, drag positions and actual Visio opening/rendering |
| 8 | [switch-handover-generator #1](https://github.com/Majordean0303/switch-handover-generator/pull/1) | 45 core + 2 UI checks; capture→XLSX/VSDX/ZIP | Detailed CDP reports, SFP/stack data, bounded live capture and topology next to existing Excel |
| 9 | [Firmware-Upgrade-Orchestrator #1](https://github.com/Majordean0303/Firmware-Upgrade-Orchestrator/pull/1) | **Build/static review only. CLI/core/harness/runtime validation SKIPPED by request.** | First-command PnP guard, DHCP NIC/pool, HTTP/TFTP, Guest Shell prompts and local laptop reachability through reboot; follow its `docs/STATIC_REVIEW.md` |

The first eight apps have **330 passing core checks and 14 passing headless UI checks**. These counts do not certify native OS rendering, installer behavior, Visio rendering or real device compatibility. The firmware app has no runtime-test claim.

## Review workflow

Each repository uses `migration/avalonia/<lowercase-repository-name>`. Start from its `MIGRATION_STATE.md`, then review the PR and `CHANGELOG.md`. Source and test artifacts remain separate; verbose logs/TRX are under ignored `artifacts/`.

For apps 1–8:

```powershell
pwsh scripts/test-all.ps1
```

For firmware only:

```powershell
pwsh scripts/build.ps1
```

Each new desktop repo has a local `scripts/package.ps1 -Runtime <RID>` command; Bulk retains its own platform packaging scripts. Native package signing/installers were not released or certified. Use the project's README for its desktop project name and fixture CLI examples. Firmware has no CLI/test/harness project; do not add that work to its current validation gate.

## Topology lineage

Visio-Creator adopts selected graph/layout improvements from Network Health Reporter commit `8ee7280b373653fc1eb94c5ead03f4bd2d7e1d99`, retaining its stronger import/reconciliation/export logic. Handover embeds a local subset pinned to Visio commit `8bab17b8d593f8037079f887b59d71bf8dcad51e`. Both have `docs/TOPOLOGY_PROVENANCE.md`. No shared package feed or sibling checkout dependency was introduced. Missing evidence stays unresolved; reciprocal links deduplicate while physical parallel members remain.

## Firmware reachability gate

`pnpa service discovery stop` precedes ordinary commands immediately after privileged access on initial, retry, reboot and lease-monitor connections. A configured management route can be preferred or used after an explicit PnP command rejection. Its nondefault network must cover the laptop; next hop/interface and VRF are explicit. Ambiguous transport timeouts close/retry the guarded session. The on-device Guest Shell script applies the same prerequisite before its first ordinary command and before install. A reconnect cannot repair an already unreachable switch.

No firmware server, transfer or device command was executed during migration. Native DHCP/TFTP and on-device installation remain lab gates before release.
