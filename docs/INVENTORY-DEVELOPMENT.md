# Incremental inventory development slice

See [ADR 0001](decisions/0001-incremental-inventory.md), [ADR 0002](decisions/0002-inventory-browser-preview.md) and issue #1 for scope. This guide describes unauthenticated loopback development mode. Installed mode adds local HTTPS/authentication; the physical-PC installer pilot is blocked. See [current status](STATUS.md).

## Build and verify

```powershell
dotnet build .\DMT.sln
dotnet run --project .\tests\DMT.Inventory.Tests
python .\tests\validate-language-packs.py
```

The inventory uses Microsoft.Data.Sqlite 10.0.12; Windows service hosting/control packages are also referenced by the web host and WPF setup. The executable test harness adds no testing packages. Python remains a development-only language-pack validator, not a product dependency.

## Explicit import

```powershell
dotnet run --project .\src\DMT.Web -- --Dmt:Import=true --Dmt:Source="\\server.example\MT\" --Dmt:Database="C:\private-dmt-data\dmt.sqlite" --Dmt:Tenant=local --Dmt:Site=default
```

Use a private writable database folder. The directory is created if absent. The source must be provided explicitly using an accessible UNC or mapped drive. Configuration can also use .NET environment keys such as `Dmt__Source` and `Dmt__Database`. Sources are enumerated at one directory level, only `.json`; subfolders, `.tmp` and recycle bins are not traversed. Read permission suffices. DMT neither renames nor writes any source file.

Progress emits technical JSON with processed count and real elapsed milliseconds; no estimated percentage. The final summary includes found/imported/duplicates/rejected/duration. A completed scan with rejections exits with code 2. Source enumeration/database failures stop the command rather than being mislabeled as invalid reports. Per-file read/schema/identity failures are recorded and later files continue. Failed files remain available at source for a later retry.

SQLite schema migration v1 enables WAL and foreign keys. Accepted snapshots preserve exact bytes, including BOM/whitespace, and SHA-256. Deduplication is scoped to tenant and snapshotId; a changed payload with that ID is reported as a conflict. Unknown schema versions, duplicated JSON properties, unreliable identity, invalid dates without timezone, and malformed modeled sections are rejected. Maximum input size is 32 MiB.

Identity uses UUID and manufacturer-scoped BIOS serial, never hostname. Conflicting UUID/serial mappings require review rather than automatic merging. A new UUID matching an existing serial is deliberately rejected for review. Missing manufacturer disables serial-only matching. Missing/placeholder identifiers cannot create anonymous hostname-based assets. The current view selects newest UTC collection time, then snapshotId for ties. Software results reflect that snapshot; an unavailable latest section does not silently reuse older software. Inspect systems' section statuses and history to distinguish unavailable from empty inventory. Partial inventory remains indexed independently of maintenance failures.

## Read-only development API

```powershell
dotnet run --project .\src\DMT.Web -- --Dmt:Database="C:\private-dmt-data\dmt.sqlite" --Dmt:Tenant=local --urls=http://127.0.0.1:5080
```

The host accepts only loopback URLs and rejects custom Kestrel endpoint configuration. This development mode has no authentication: do not connect this slice to a public reverse proxy. Tenant is fixed by host configuration, never selected through request parameters.

| Endpoint | Parameters / result |
| --- | --- |
| `/` | Inventory dashboard with computer/OS search, software filters, computer detail/history and CSV |
| `/api/languages` | Runtime-discovered ordinary language metadata; hidden packs excluded |
| `/api/languages/{code}` | Selected pack merged with English fallback; unknown codes fall back to English |
| `/api/systems` | Optional `site`, OS `name`, internal `asset`; current OS and inventory/maintenance statuses |
| `/api/software` | Optional `site`, `name`, `publisher`, `version`, internal `asset`; program-to-computers and computer-to-programs |
| `/api/products` | Optional `site`; groups by name/publisher with distinct computer count across versions |
| `/api/software.csv` | Same filters as software; quoted UTF-8 CSV with spreadsheet formula protection |
| `/api/software-computers.txt` | Same filters as software; unique computer names sorted ignoring case, one per line |
| `/api/software-computers.csv` | Same filters as software; distinct hostname/version pairs, UTF-8 BOM, quoted CSV with formula protection |
| `/api/assets/{asset}/history` | All snapshot observations and original section data/status/errors |
| `/api/maintenance` | Latest observations requiring review, including reported BIOS/GPU candidates; tenant scoped |
| `/api/assets/{asset}/maintenance` | Latest maintenance findings for one asset, or 404 in the configured tenant |
| `/api/import-attempts` | Latest 200 attempts for the configured tenant |

Search uses literal substring matching with parameterized SQL. SQLite built-in case folding is limited to ASCII. AppX publisher is unknown when the report does not supply it. Product grouping does not infer aliases or merge similarly named commercial products. API fields/status codes/CSV headings remain technical contracts; interface strings are external JSON resources. Eight ordinary packs have draft web translations; new Klingon strings are English drafts pending linguistic review. Missing-key markers are logged by the frontend.

Open the base address to use the interface. Select a computer and choose **View installed software** for reverse lookup. The software page filters by product name, publisher and version; its CSV link uses the same filters and selected computer. Results are displayed in pages of 100, while export includes all matching rows. The computer page filters locally by hostname/serial and OS. Language follows browser preference with a saved override; hidden Klingon requires the badge to be unlocked again in each session.

Software results offer **Computers (TXT)**, **Computers and versions (CSV)** and **Full CSV**. Downloads use the last successfully loaded filters, including any selected asset; editing a field without submitting does not change the export. Links are hidden while loading or after a failed load. TXT removes duplicate computer names across versions/sources; the small CSV keeps distinct computer/version pairs. Unknown versions remain empty. Neither format is limited to the visible page. Headers remain stable technical field names (`hostname,version`). These are fresh queries at download time, so concurrent imports may change the result.

Refresh reloads imported data only. Stop and restart the host after changing code/resources. The language catalog discovers packs at startup. Browser preview tests use synthetic reports and do not publish company inventory.

Maintenance messages and historical error text may already contain UTF-8 misread as Windows-1252 in the source report. The dashboard attempts at most two strict, conservative display decoding passes per non-whitespace token, accepting only a reduction of corruption markers. Correct text and undecodable tokens are left unchanged. Adjusted messages carry a localized notice and an expandable original text. This is a display heuristic, not an MT fix: original JSON bytes, database values, history API responses and exports are unchanged.

## Verification evidence

Synthetic tests cover identity, imports, byte preservation, current/history semantics and exports. See [test commands](../tests/README.md). Private pilot reports, fleet counts, device names and deployment settings are excluded from this checkpoint.

## Maintenance review — 2026-10-19

The **Maintenance to review** card opens the affected computers. The review filter selects all findings, reported BIOS candidates or reported GPU candidates. Select a computer to inspect the latest findings, installed/candidate versions, observation time and collection history. A report candidate is historical evidence, not a fresh manufacturer applicability check. Other OEM updates and pending restarts remain visible separately.

The remote update control is disabled: no DMT agent, job transport or update execution is implemented yet. DMT issues no reboot command. The agreed future workflow warns the user that a BIOS flasher may restart autonomously; a manual agreed restart remains preferred. See [the agent direction](decisions/0005-maintenance-review-and-agent-direction.md).

## Remaining scope

The separate [archive simulation](ARCHIVE-SIMULATION.md) can compare source/staged JSON against acquired evidence and show proposed moves/monthly ZIPs. Live archiving and month-end catch-up are documented in [archive operations](ARCHIVE-LIVE.md).

Multi-user roles, richer workflows, polling, successful physical-PC installation, change timelines, backup/restore, BIOS manufacturer verification and encryption views remain future work. Installed local-admin authentication and Caddy code exist but do not establish installation acceptance. Issue #1 is not completed in full by this slice. `encrypted` is preserved in raw evidence without interpretation as complete BitLocker state.
