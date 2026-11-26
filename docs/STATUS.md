# Development checkpoint — 2026-11-17

This document is a recovery handoff, not a production-readiness claim. The repository contains the implementation and synthetic verification; private inventories and machine configuration are excluded.

## Retry candidate — 2026-11-26

`0.2.0-local.2` removes the synchronous wait on asynchronous named-pipe progress in the WPF worker: deployment callbacks are awaited end-to-end, and the transport does not capture a dispatcher. A deterministic regression test uses a non-pumping STA context with delayed writes and checks completion, protocol fields and cancellation (21 setup/runtime assertions total). This confirms a code-level deadlock risk was removed; it does not establish that this was the only cause of the physical-PC stall.

Logs now start before pipe connection under `%LOCALAPPDATA%\DMT\SetupLogs`, are flushed to disk at every phase and record directory/ACL and migration boundaries without serializing the installation plan or password. Progress writes are bounded to 30 seconds and `sc.exe` waits to 45 seconds; payload copying reports every 25 files. There is not yet a universal timeout/recovery mechanism for synchronous filesystem/SQLite calls. The footer uses the executable's actual informational version.

Before retrying on a previously interrupted PC, run `Get-DmtInstallationDiagnostics.ps1` from the extracted package. It inspects service/process/directory state and candidate version/hash without reading database/configuration secrets, changing permissions, stopping services or deleting anything. Review partial state before choosing recovery; the candidate still refuses an existing installation. Do not remove the previous installation automatically.

The software dashboard now distinguishes matching software-entry count from distinct asset count, across all result pages. A broad product search includes subcomponents and multiple reported versions; it is not an installed-computer count.

## Available implementation

- .NET 10 WPF setup, existing branding/port inspection and external language packs, including the hidden Klingon pack.
- SQLite incremental imports, transactional UUID/serial identity, snapshotId deduplication/conflict detection, original-byte BLOBs and SHA-256, collection/import timestamps and per-file outcomes.
- ASP.NET Core loopback dashboard: current computer/OS/software searches, product grouping, computer details/history, maintenance review, filtered full CSV and hostname TXT/CSV exports. Current results use only each asset's newest observation; old observations remain in history.
- Explicit acquisition and verified archiving to `archiviati`, optional conflict quarantine/pruning with newer verified evidence, monthly ZIPs with manifests and closed-month catch-up. Standalone import remains read-only. Original report content is never rewritten.
- Local WPF service manager with start/restart/stop, dashboard link, acquisition/archive and administrator-password reset.
- Fresh-install code: self-contained runtimes, hashed payload, optional SQLite Backup migration, automatic LocalService Windows service, owned Caddy process, dedicated localhost HTTPS CA and instance health check. Installed mode has local-admin cookie authentication, PBKDF2, CSRF, login rate limiting and password-reset session revocation. Development mode remains unauthenticated and loopback-only.
- Separately invoked, opt-in PDF24/Cisco VPN GPO helpers with synthetic tests. These are not a DMT remote execution channel.

## Blocking installer incident — open ([issue #2](https://github.com/Kraugh/DMT/issues/2))

The first physical-PC pilot of `0.2.0-local.1` stalled for approximately one hour on **"Creazione delle directory programmi e dati DMT"**, around 35%, and the user stopped it. The cause has not been diagnosed. The screenshot footer still displayed `0.1.0-dev`, so version presentation/actual package identification also need review. No successful physical-PC installation has been demonstrated.

Next investigation:

1. Identify the exact executable version/hash and retain the interrupted state before any retry or cleanup.
2. Inspect both setup processes and private `%TEMP%\DMT-Setup-*.log` files if present. Current logging writes a file on caught exceptions only, so a hung operation may leave **no log**.
3. Inspect existing `%ProgramFiles%\DMT`, `%ProgramData%\DMT` and the `DMT` service without overwriting or deleting resources. Record ACLs and ownership privately.
4. Reproduce with synthetic data. Check named-pipe progress, WPF synchronization context, synchronous progress callbacks, ACL/directory creation and child-process termination; these are hypotheses, not established causes.
5. Add phase-level durable diagnostics, explicit elapsed-time/status reporting, bounded or cancellable operations and a safe interrupted-install recovery procedure.
6. Only then repeat UAC, SCM, LocalService ACL, trusted HTTPS, automatic-start, restart/stop, migration and acquisition acceptance on the physical pilot.

The installer currently refuses an existing installation; there is no complete upgrade/uninstaller. Do not recommend forcing a reinstall over partial state. Checkpoint installer packages are diagnostic previews, not a stable release.

## Unfinished work to retain

| Work | State / next acceptance |
| --- | --- |
| Acquisition scheduling | Manual acquisition works. Configurable periodic import is not implemented; archive catch-up is separate and does not import. Services must use explicit UNC/identity design, not assume an interactive mapped drive. |
| Archive performance / visibility | Planning rescans staged evidence and can take minutes on a share. Add phase progress/cancellation and safe incremental evidence caching without reducing verification. |
| Changes during collection | A report may change between import and archive verification. Conflict retention works; improve diagnosis and stable-write detection without altering MT or replacing accepted bytes. |
| Source ZIP intake | Automatic import scans loose root JSON only. Arbitrary source ZIPs are not automatically imported; design explicit, bounded, safe recovery/import and conflict reporting. |
| Historical fleet comparisons | All snapshots exist, but no end-to-end as-of-date fleet query/comparison UI yet. Add latest-observation-at-or-before-date semantics and freshness rules; do not infer retirement from silence. Index software installation/filesystem dates only with their distinct meanings. |
| BIOS/GPU actions | Candidate review exists from acquired reports. Fresh manufacturer applicability checks, enrolled agent, authenticated job transport, package verification and update execution are absent. No remote updates/reboots are performed. See ADR 0005. |
| Encryption | Raw evidence retained. Dedicated UI distinguishing unknown/available/partial encryption state and full BitLocker evidence is pending. `encrypted` alone is insufficient. |
| Authentication / LAN | Installed local-admin flow exists. Read-only/multi-user roles, tenant administration, LAN access and enterprise certificates remain pending. Tenant-scoped data is not a complete authorization system. |
| Localization | Nine key-complete packs. New operational strings are complete in Italian/English; other packs contain English drafts. Review translations, error text, version display and accessibility. |
| Data durability | Manual SQLite Backup migration exists. Automated consistent backup/restore, retention, rebuild from originals/ZIP manifests and verified disaster recovery are not complete. GitHub is a code backup, not an inventory backup. |
| GPO deployment helpers | Synthetic tests exist. Production deployment/results must be verified independently; no claim that every endpoint was updated or removed. |
| Distribution | Windows x64 self-contained build exists. Code signing, supported upgrade/uninstall, successful physical pilot and stable release remain open. |

## Verification boundary

Run the commands in [RECOVERY.md](RECOVERY.md) and [tests/README.md](../tests/README.md). The Windows workflow repeats build, inventory tests, language parity, report-text tests, mocked GPO tests, payload verification and synthetic HTTPS/authentication/process-ownership integration. It never registers a service, changes machine certificate trust, contacts company endpoints or imports company reports.

The known local test counts are 133 inventory assertions, 21 setup/runtime assertions, 19 report-text assertions, 25 PDF24 assertions and 16 Cisco assertions, plus nine-pack language parity. Passing these checks does not close the installer incident above. Open work is also tracked in [ROADMAP.md](../ROADMAP.md), [ingestion issue #1](https://github.com/Kraugh/DMT/issues/1) and [installer blocker #2](https://github.com/Kraugh/DMT/issues/2).
