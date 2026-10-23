# Device recognition and command validation

Select **Auto-detect (multi-vendor switches / WLC)** for a mixed inventory. It is
now the default. Every backup session runs read-only identity probes first, then
selects an OS-specific command template and paging command. The output records
the detected OS, device category, identity evidence and each command's result.
An unchanged built-in template also adapts to the detected OS. Edited templates
and Custom scripts retain their command order after identification and paging.
Auto mode ignores the editor: select Custom to supply commands.

## OS families

| OEM / family | Recognized CLI | Configuration capture |
| --- | --- | --- |
| Cisco | IOS / IOS-XE | `show running-config`, fallback `show run` |
| Cisco Nexus | NX-OS | `show running-config` |
| Cisco Catalyst 9800 | IOS-XE wireless controller | `show running-config` |
| Cisco legacy WLC | AireOS | `show run-config commands`, fallback `show run-config` |
| Cisco AP | IOS autonomous AP | `show run` |
| Cisco Small Business | SG/SF/CBS managed CLI | `show running-config` |
| Arista | EOS | `show running-config` |
| Juniper | Junos operational CLI | `show configuration \| no-more` |
| Aruba | AOS-CX | `show running-config` |
| Aruba / HP | ProVision / AOS-S | `show running-config` |
| HPE / H3C | Comware | `display current-configuration` |
| Huawei | VRP | `display current-configuration` |
| Dell | OS10 | `show running-configuration` |
| Dell | OS9 / Force10 | `show running-config` |
| Dell | OS6 / N-Series / PowerConnect | `show running-config` |
| Extreme | EXOS / Switch Engine | `show configuration` |
| Extreme | VOSS / Fabric Engine | `show running-config` |
| Ruckus / Brocade | FastIron ICX | `show running-config` |
| Alcatel-Lucent Enterprise | OmniSwitch AOS | `show configuration snapshot` |
| Fortinet | FortiSwitchOS | `show full-configuration` |
| MikroTik | RouterOS CRS | `/export terse` |
| NETGEAR | Managed FASTPATH CLI | `show running-config` |
| D-Link | DGS managed CLI | `show config current_config` |
| TP-Link | JetStream / Omada managed CLI | `show running-config` |
| Ubiquiti | EdgeSwitch | `show running-config` |
| Allied Telesis | AlliedWare Plus | `show running-config` |
| Ruijie | RGOS | `show running-config` |
| ZTE | ZXR10 | `show running-config` |
| SONiC vendors | Management CLI with `show running-configuration` | `show running-configuration` |

These are **CLI-family templates with simulated regression coverage**, not a
claim that every model or firmware release has been hardware-certified. Vendor
names alone cannot establish CLI compatibility. Unknown devices fail Auto mode
without sending backup or save commands. An explicit Custom script can still run
on an unidentified device; that fact is recorded in its output.

## Verification behavior

- Commands are checked for CLI rejection, authorization errors and empty output.
  Known syntax alternatives are attempted only for their matching OS family.
- Optional diagnostics rejected by a model/release are retained with a rejected
  result and warnings. They do not count as accepted commands.
- Auto templates must capture their configuration command successfully. An empty,
  denied, or rejected configuration fails the backup and preserves the previous
  file. A timeout or cancellation never publishes a partial replacement.
- Custom scripts requesting configuration capture also fail if that capture fails.
  Explicit diagnostic-only scripts remain supported.
- Multi-vendor prompts and pagers are handled by the interactive shell reader.
  Unsupported prompt formats or long-running sessions can still time out.
- Configuration saves remain opt-in. The existing non-interactive `write memory`
  path is restricted to recognized families with that command. Other families
  emit a skip warning rather than guessing an interactive save/commit procedure.
- Configuration-mode and common destructive commands are suppressed in scripts.
  Session paging commands remain allowed. This is a backup tool, not a general
  configuration-push engine.

## Cisco and WLC corrections

The default IOS/NX-OS templates now use a smaller portable baseline rather than
sending stack-ring, VSS, platform-specific licensing, IP SLA and redundant command
variants to every switch. These can still be supplied in a reviewed Custom script.
Older IOS MAC-table syntax has a targeted fallback.

Catalyst 9800 uses `show ap dot11 24ghz summary`, IOS-XE wireless summaries and
running/startup configuration. AireOS uses `config paging disable`,
`show ap image all`, `show ap cdp neighbors all`, `show route summary` and its own
configuration export. AireOS never inherits IOS-XE terminal or wireless commands
in Auto mode. Optional commands can differ across releases and are checked at run
time; rejection is visible rather than silently reported as success.

## Boundaries and lab acceptance

Existing recursive CDP/LLDP discovery is preserved and remains Cisco-oriented.
For a mixed-vendor inventory, supply management IPs directly; this change does not
claim universal topology discovery. SSH availability and account privileges remain
prerequisites. Web-only/cloud-only switches, Meraki Dashboard backups, UniFi OS
shells, MikroTik SwOS, and a SONiC Linux shell are outside these SSH CLI templates.
Configuration text exports are not full binary restore images and may omit secrets,
certificates or controller-managed state (for example RouterOS sensitive values
and FortiLink-managed configuration).

Before production rollout, run one representative device per deployed firmware,
review detected OS and rejected commands, and compare configuration output against
a manual capture. Use the relevant OEM command reference for that exact release.
No live switches or WLCs were contacted during this implementation. The automated
suite uses synthetic identities and simulated SSH replies, including wrong-profile
selection, missing configuration, rejection/fallback and vendor prompt handling.
