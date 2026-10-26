# Assessment snapshots and sharing exports

Choose **Assessment** under Collection profile in Run Monitor or Command Reference.
The existing platform selector remains available; Assessment identifies the device
and uses a read-only platform plan. **Configuration Backup** preserves the existing
backup/custom-script workflow. Assessment never sends `write memory`, even if the
save option was previously enabled.

## Commands

The Catalyst assessment list is pinned to Network Health Reporter's Commands screen
(`RequiredCommandsCatalog.cs`) at commit
`5c87329a2d896271b7cb4efee87296f8a396f3fc`, with inventory, CDP summary and IP-interface
summary included for portable offline analysis. It includes running configuration,
version, inventory, CPU/memory, power, temperature, STP summary, VTP, interface
status/error counters, CDP detail/summary and the optional FED diagnostic.

Stack detail/ring commands require a recognized StackWise-capable model: Catalyst
9200/9300 or supported 2960-X/S, 3650, 3750 and 3850 families. They are omitted for
unknown models, C1000, C9500, Small Business/CBS and other OS families. Runtime
unsupported/unauthorized responses remain visible; model detection does not certify
every firmware variant. The preview lists potential commands and explains this gate.

Catalyst 9800 and AireOS retain separate plans. AP plans explicitly include
`show version`, inventory, running configuration, wired-interface output, CAPWAP RCB,
CAPWAP client configuration and radio-controller output. Failure of one CAPWAP
command does not prevent the other AP evidence from being collected. `show version`
is exported as an ordinary command block as well as identity evidence.

## Existing manifest, additive fields

`capture-manifest.json` remains schema version 1. Existing application, timestamps,
requested targets, selected profile, device IP/hostname/platform/type/profile,
identity evidence, status/reason, configuration/save flags, output filename,
commands, stages and attempts are preserved. Existing `success`, `unsupported`,
`failure`, `notAttempted` and device-level statuses remain compatible.

Additional fields:

| Level | Fields and meaning |
| --- | --- |
| Run | `snapshotId`, `collectorVersion`, `commandProfileVersion`, `collectionPurpose`, `counterRateSampling`, `exportKind` |
| Device | `snapshotId`, `deviceId`, `identityBasis`, `family`, `serialNumber`, `chassisId`, `model`, `role`, `members`, `uplinkDevice`, `uplinkPort` |
| Device collection | `collectionStartedAtUtc`, `collectionCompletedAtUtc`, monotonic `durationMilliseconds`, `completeness`, `snapshotFile`, `outputSha256` |
| Command | Existing `command` plus `requestedCommand`, `canonicalCommand`, `startedAtUtc`, `completedAtUtc`, `durationMilliseconds`, `outcome`, `completeness`, `completionCheck`, optional `sampleNumber`/`sampleGroup` |
| Attempt | Existing actual `command`, `capturedAtUtc`, `status`, `reason`, plus UTC start/end, monotonic duration, `outcome`, `completeness`, `promptReceived`, and exact `rawOutput` |

Desktop collection supplies the application assembly version; direct core callers can
supply `CaptureOptions.CollectorVersion`, otherwise the version is explicitly labelled
`core/<version>`.

`family` is `switch`, `router`, `wlc` or `ap` when identified. Hardware fields and
active/standby/stack roles are extracted only from local device evidence; unknowns
remain null. Management IP identifies a collection endpoint independently of its
filename or hostname. Serial/chassis fields permit hardware correlation, while two
controllers sharing a hostname remain distinct targets. Do not merge by hostname.

Detailed outcomes are `accepted`, `unsupported`, `unauthorized`, `timedOut`,
`truncated`, `empty`, `failed`, `cancelled` and `notAttempted`. Accepted syntax alone
does not mean complete output: a prompt-delimited, non-empty response without
rejection/truncation evidence is required. IOS/IOS-XE and Catalyst 9800 running
configuration additionally require the trailing `end` marker. `completionCheck`
records this criterion. These are CLI framing checks, not a guarantee that a device
reported every internal object. A 32 MiB per-command limit is reported as truncated.

Attempt output is kept unchanged, including command echo, prompt and terminal
control characters. Text blocks add framing around that output. Requested/canonical
commands remain separate from the actual fallback attempts; output is never
relabeled as another command. Command durations include a reconnect when required
before an attempt; command-group duration is the sum of its attempt durations.

## Snapshot isolation

Each run receives a new ID. Current-run text files are copied into an immutable
`.capture/snapshots/<snapshotId>/` directory, with a manifest and SHA-256 hashes.
The latest `.capture/capture-manifest.json` points to that run. ZIP export reads
only its referenced snapshot files and verifies hashes. It refuses mixed IDs,
unfinalized or changed files; it never substitutes an older configuration for a
failed current capture. Concurrent runs cannot share a destination directory.
Failed, unreachable, skipped and discovered-but-unattempted targets remain in the
manifest even when no text file exists. Legacy directories without a manifest keep
the existing explicitly unverified export behavior.

## Optional counter samples

Enable **Sample errors/discards twice**, with an interval of 1–300 seconds (default
30). IOS/IOS-XE and NX-OS collect their interface error/discard table twice. Other
platforms explicitly record that counter sampling is unavailable. Both attempts,
original table outputs and UTC collection windows remain in the same snapshot.

`counterRates` contains paired numeric port/counter entries, the elapsed sample
interval, delta and per-second rate. Rates are emitted only for complete paired
outputs and nondecreasing counters. A decrease yields `resetOrWrap` and no rate;
unmatched/unparsed values produce no estimate. Intervals may be longer than the
requested delay because other diagnostics and reconnects take time.

Lifetime totals show historical drops. Sample deltas describe only the observed
interval. They are neither packet-loss percentages nor proof of AP disconnections.

## Private and sharing exports

**Private ZIP** contains raw configuration and evidence. No export is uploaded.
New session/snapshot directories and raw files/ZIPs use owner-only Unix permissions
or Windows ACLs. Save to Folder creates a separate private directory per export.
Windows ACL behavior and all live firmware behavior still require native validation.

**Sharing ZIP** creates a separate copy, redacting password/secret/community/key,
SNMP authentication/privacy, wireless PSK and PEM private-key material from both
text files and manifest strings. Repeated secrets receive consistent opaque tokens
within that export; the random token key is not exported. Device names, addresses,
serials, ports, VLANs, STP settings and link relationships are intentionally retained.
The raw snapshot remains unchanged, and exported hashes describe the exported text.

This is secret redaction, not topology anonymization. Review custom/vendor-specific
secret formats and free-text descriptions before external sharing. The exporter
does not claim to identify arbitrary secrets in prose or unknown command dialects.
