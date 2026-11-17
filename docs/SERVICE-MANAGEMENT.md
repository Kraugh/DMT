# Local service management and explicit acquisition

**Physical installation is blocked:** the first pilot stalled during directory creation. Code described here is not successful installation acceptance. Preserve partial state and follow [STATUS.md](STATUS.md).

The existing WPF setup remains the installation entry point. **Manage DMT** launches its separate `--manage` window through Windows UAC. This local window is intentionally separate from the dashboard: it remains available when the web host is stopped. No privileged control endpoint is exposed through the unauthenticated web API.

## Implemented

- Start, restart, stop and refresh status for the fixed Windows service name `DMT`, with bounded waits and localized service states. Control requires a DMT-managed `service` record in the installation manifest. Missing services are shown as not installed, not confused with stopped services or a running development `dotnet` process.
- Acquisition can run independently of the web host. The operator selects an existing SQLite inventory database and the source folder. Use a UNC path for shares: mapped drives such as `M:` can be absent from elevated sessions or services. Access uses the current Windows account; no network credentials are collected or stored by this panel.
- One click imports reports, generates and saves the archive plan, transfers only entries verified against acquired evidence, and catches up ZIP compression for closed months. Conflicts stay at source and result in a partial outcome. Current-month originals remain loose under `archiviati`. The existing file locks, byte verification, copy-before-retirement and append-only archive journals are retained.
- A database-side exclusive acquisition lock prevents concurrent management acquisitions. The UI disables service changes and a second acquisition while a job runs, and prevents closing its own process midway through an archive operation. A crash releases the OS lock; retry remains incremental.
- Evidence includes `plan.json`, `move.jsonl`, `move-summary.json`, `compress.jsonl` and `result.json` under a unique private run directory. No company reports or runtime settings are copied into the repository.
- The web host now supports Windows Service lifetime through `Microsoft.Extensions.Hosting.WindowsServices` 10.0.12. This is hosting support, not service registration.

For CLI use (these options perform real source archiving):

```powershell
dotnet .\DMT.Web.dll --Dmt:Acquire=true --Dmt:Database="C:\private\dmt.sqlite" --Dmt:Source="\\server\share" --Dmt:Tenant=local --Dmt:ArchiveJournals="C:\private\DMT-acquisitions"
```

Exit 2 indicates partial acquisition/archive outcomes. The CLI must not be combined with the separate import/archive modes. Running the management window does not automatically acquire or archive reports.

## Deployment boundary

The elevated installer now supports a **fresh local x64 installation**: verified self-contained payload, optional SQLite Backup migration, automatic `DMT` service under LocalService, Caddy-managed localhost HTTPS, dedicated local CA trust and HTTPS instance health verification. A Start-menu shortcut opens the manager. Network-share operations remain explicit in the elevated operator panel, not in the LocalService web host. See [physical-PC installation](INSTALL-LOCAL.it.md).

LAN/existing-certificate choices are disabled in this local pilot. The loopback guard remains enforced. Installed web access uses cookie authentication with a PBKDF2-derived local administrator credential, secure/HttpOnly/SameSite cookies, CSRF validation, login rate limiting and session revocation after a password reset. No network credentials are persisted. Caddy is assigned to a kill-on-close Windows job; the service configures recovery actions and a Caddy failure exits the service with a failure status. Actual SCM/UAC/ACL and machine-trust installation need the physical-PC pilot; HTTPS/authentication/process ownership are integration-tested without trusting a root in the VM.

## Conflict reconciliation

The explicit acquisition CLI accepts `--Dmt:ReconcileConflicts=true`. After verified moves, rejected root JSONs are copied byte-for-byte to `conflitti`, verified while read-locked, journaled, then retired from the root. Existing quarantined JSONs are also considered. Unfinished root `*.json.tmp` files older than 24 hours are quarantined without being imported or deleted. Active/recent temporary files and files locked by a writer remain in place. Directory links and unsafe paths are rejected.

`--Dmt:PruneSupersededConflicts=true` additionally permits deletion of obsolete conflicting JSON variants. Identity must match unambiguously through UUID/serial (never hostname alone), the current acquired report must have a strictly later JSON collection time, its database raw bytes/hash must verify, and its original must exist byte-exact in `archiviati`, held read-locked through retirement. The journal is flushed with both snapshot references/hashes before deletion. A conflicting newest snapshot is retained in quarantine, never silently substituted under its reused snapshotId. Original historical database evidence is preserved. A latest report stored only in a monthly ZIP does not satisfy this pruning condition; the conflict remains quarantined.

If a root report cannot be read stably but the SMB writer permits rename, reconciliation may move that same file atomically into `conflitti` without overwriting any destination. It is logged as `quarantined_unreadable`, not acquired/archived, and remains unavailable for inventory until a later successful read/import. No writer is closed or interrupted. If rename is also denied, the source remains in place and the failure is reported. Two additional synthetic assertions verify this path and exact retained bytes (133 total assertions).

The WPF acquire/archive action enables both reconciliation and superseded-conflict pruning, as described in its help text. No pruning occurs in the standalone read-only importer or by simply opening the management window. The CLI switches remain opt-in for compatibility. Twelve synthetic assertions cover replacement-first ordering, retained history, exact quarantine, no-newer-evidence protection, explicit pruning and abandoned/active archive locks; the executable harness now has 131 assertions.

## Verification

The inventory harness contains 133 assertions; the separate setup/runtime harness adds 18 HTTPS/authentication/process-ownership assertions without service registration or machine trust changes. WPF/UAC, real SCM transitions and trusted installed-host HTTPS remain blocked on the physical pilot. Language packs have matching keys; new management text is Italian/English, with English drafts in the other packs. See [verification commands](RECOVERY.md).
