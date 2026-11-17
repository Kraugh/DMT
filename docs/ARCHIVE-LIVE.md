# Verified live archiving and month-end catch-up

See [ADR 0004](decisions/0004-verified-share-archive.md). These are explicit write operations, separate from [dry-run planning](ARCHIVE-SIMULATION.md). Run them only within an authorized archive scope and with the account that owns the mapped drive/share access. The development web host remains loopback-only and is not an installed Windows service.

## Transfer an approved allowlist

```powershell
$inventoryDb = Join-Path $env:LOCALAPPDATA 'DMT\Development\dmt.sqlite'
dotnet run --project .\src\DMT.Web -- --Dmt:ArchiveMove=true --Dmt:ArchivePlanFile="C:\private\approved-simulation.json" --Dmt:ArchiveJournal="C:\private\DMT-move-unique.jsonl" --Dmt:Database="$inventoryDb" --Dmt:Tenant=local
```

The approved JSON must be a simulation with no global scan issues. Only eligible source entries are considered; blocked entries and later arrivals never enter the allowlist. Per-file failures remain at source. The CLI requires matching tenant and rechecks the current source and database evidence. Existing matching staged copies are verified, never overwritten. Already-absent originals are counted as archived only after destination/evidence verification.

Transfers use 4 workers by default (`Dmt:ArchiveConcurrency` accepts 1–8). Native Windows source handles exclude writes/path replacement until explicit delete-by-handle. A destination is read-locked until source retirement. An append-only local journal is flushed before deletion. Original creation/modification dates are preserved. A unique temporary copy is recorded as `<destination>.dmt-<guid>.tmp`, cleaned after handled errors. A process crash may leave such a recorded temporary file for manual inspection; future runs never blindly delete another run's files. `archiviati/.dmt-archive.lock` is opened exclusively with delete-on-close: an abandoned filename can be reopened, but an active owner still blocks access. Journal/summary filenames must be unique; old evidence is not overwritten.

## Compress closed months once

```powershell
dotnet run --project .\src\DMT.Web -- --Dmt:ArchiveCompress=true --Dmt:Source=M:\ --Dmt:ArchiveJournal="C:\private\DMT-compress-unique.jsonl" --Dmt:Database="$inventoryDb" --Dmt:Tenant=local
```

The collection month in Europe/Rome chooses `archiviati/yyyyMM-MTREPORT.zip`. Files from the current month are excluded, regardless of filesystem creation dates. September is eligible during October; October becomes eligible after October closes. The database catalog identifies months without rereading current-month JSON. Unknown/ambiguous staged filenames remain untouched for reconciliation.

Each ZIP contains exact JSON bytes and `DMT-MANIFEST.json` with tenant/month, snapshot IDs, hashes, original byte sizes, collection times and original file timestamps. The ZIP is closed, reopened and verified against database evidence, then published and verified again. Only after that are loose originals retired, while the ZIP and source handles remain locked against changes.

Late arrivals update an existing DMT ZIP through a verified temporary ZIP; the old ZIP remains at `<zip>.dmt-previous-<guid>.zip`. The journal records its exact path. These previous ZIPs are deliberately retained; no automatic retention cleanup is implemented. Non-DMT/corrupt ZIPs and content conflicts are blocked, never overwritten. SMB must support atomic replacement for updates; failures retain source evidence. A remaining loose duplicate already present in a verified ZIP can be retired without rewriting that ZIP.

## Automatic first-use catch-up

```powershell
dotnet run --project .\src\DMT.Web -- --Dmt:Database="$inventoryDb" --Dmt:Tenant=local --Dmt:Source=M:\ --Dmt:ArchiveCatchUp=true --urls=http://127.0.0.1:5081
```

With this explicit flag, a background worker checks at startup and every 15 minutes while DMT is running. It processes all closed months, including missed runs, and retries temporary failures later. It does not require this VM at 23:59. No scheduled task, firewall rule or remote service is installed. If DMT is off, the work waits until a future start **with the catch-up flag**. Default startup leaves the worker disabled. `Dmt:ArchiveJournals` selects a private local journal directory; development default is `%LOCALAPPDATA%/DMT/Development/ArchivePlans`.

Catch-up compresses staged files only. It does not automatically import or transfer new root reports. Later transfers still require their own verified simulation/authorization. The first enabled run may immediately compress old closed months; do not enable the flag merely to preview a plan.

## Historical interpretation

`collected_at` is observation time; `imported_at` is DMT acquisition time. Software `installDate` is what the report declares and may be missing or change on reinstall. Filesystem creation time is separate, preserved by archiving but not yet indexed in SQLite. ZIP storage does not change historical query semantics: indexed snapshots and raw database evidence stay available.

A historical OS count must choose each asset's last observation at/before the cutoff (day-end Europe/Rome), show the observed population and distinguish assets newly entering coverage. Missing reports are not proof of decommissioning. Comparing before/after software observations establishes a change interval, not its deployment mechanism; a user-reported GPO deployment is separate evidence. Multiple concurrently installed versions must be compared as sets per snapshot.

## Pilot evidence — 2026-10-16

The explicitly approved 338 reports were transferred successfully. A controlled interruption after ten transfers was resumed with the same allowlist: 328 further transfers, ten verified already-archived entries and zero failures. The excluded conflicting report remained byte-identical at source. One new report arriving after approval was not added to the transfer scope.

The first closed-month catch-up compressed 41 September reports (13,413,812 original bytes) into `202609-MTREPORT.zip` (563,207 bytes). An independent Python ZIP/hash check verified every member against the approved hashes. All 41 loose September copies were retired only after ZIP verification. The 297 October JSONs remain loose. No approved original remains in the root; the conflicting report and new arrival remain there. No temporary file or archive lock was left behind.

The database SHA-256 remained identical across live archiving; integrity returned `ok`, no foreign-key violations were found, and the development dashboard still returned HTTP 200 with 96 assets. The localhost 5081 instance was restarted with opt-in catch-up enabled. A private local launcher retains the same configuration for later starts; this is not an installed Windows service or an OS startup task. Full paths, company filenames and operation journals remain outside the public repository.

The full solution built with zero warnings/errors, 91 synthetic assertions passed, and all nine packs retained 212 keys. These are pilot results, not a guarantee of power-loss durability on all SMB servers. Existing-archive replacement, restart and corruption cases were exercised synthetically; the pilot created its first monthly ZIP and did not replace an existing real archive.
