# Share archive simulation

Implemented scope: **dry-run only**. This command never creates `archiviati`, moves or deletes JSON, creates or changes ZIPs, imports snapshots, or installs a scheduled task. See [ADR 0003](decisions/0003-share-archive-simulation.md).

## Run

From the repository root, with the existing private database:

```powershell
$inventoryDb = Join-Path $env:LOCALAPPDATA 'DMT\Development\dmt.sqlite'
dotnet run --project .\src\DMT.Web -- --Dmt:ArchivePlan=true --Dmt:Source=M:\ --Dmt:Database="$inventoryDb" --Dmt:Tenant=local
```

The plan is technical JSON on stdout; progress on stderr reports inspected files and actual elapsed milliseconds. To keep a plan, redirect stdout to a private local file outside the repository. Plans contain company filenames, snapshot identifiers and hashes. Never redirect the output onto the source share when performing a read-only simulation.

The database must already exist and use schema version 1. It is opened read-only, before the regular application's database constructor is reached. `Dmt:Import=true` is rejected when combined with simulation. Cancellation through Ctrl+C stops the read-only scan. Exit code 0 means no blockers; code 2 means file/month blockers or directory-scan issues were found. Operational failures such as an inaccessible source/database fail the command; they are not a successful empty plan.

## How to read the plan

Each file includes its source/destination, original byte count, `snapshotId`, SHA-256 and collection month in Europe/Rome:

| Action | Meaning in a future apply implementation |
| --- | --- |
| `would_move_after_copy_verification` | Acquired evidence matches; copy to `archiviati`, verify it, only then remove the root original |
| `would_remove_source_after_staged_copy_verification` | Identical acquired evidence already exists as a loose staged file; reverify it before removing the root duplicate |
| `already_staged` | Acquired loose JSON already exists inside `archiviati`; include it in month planning |
| `blocked` | Leave this report in place and resolve the listed reason |

A maintenance error does not prevent archiving a successfully acquired inventory. Eligibility requires matching tenant/snapshot ID, the source hash, the committed hash, the database raw evidence hash and collection timestamp. Unimported/changed/invalid/future-dated reports stay blocked. Same-name destination conflicts are never overwritten. Temporary files, recycle bins and nested directories are not traversed. Unsafe/reparse files are blocked. Unreadable or unsafe archive-directory scans fail closed.

Month actions:

| Action | Meaning |
| --- | --- |
| `scheduled` | Not yet due; keep loose verified JSON until the due instant |
| `would_compress_and_verify` | Due or overdue; proposed ZIP creation and verification, without executing it |
| `blocked` | Existing ZIP/destination or incomplete inspection requires review |

For example, October reports target `M:\archiviati\202610-MTREPORT.zip`, due on October 31 at 23:59 Europe/Rome. September is already due on an October run. Late arrivals retain their collection month. ZIP members include verified root reports that would first be transferred, plus verified loose files already staged, with identical same-name staged copies counted once. Sizes are uncompressed source bytes, not estimates of compressed ZIP size.

An existing monthly ZIP is reported as `archive_exists_review_required`. The planner does not inspect its payloads or manifest, deduplicate against ZIP members, or propose automatic replacement. This is a deliberate limitation until ZIP update, recovery, reconciliation and manifest verification are implemented and tested. No source deletion may depend solely on a ZIP name or an old saved simulation.

## Verification

```powershell
dotnet build .\DMT.sln
dotnet run --project .\tests\DMT.Inventory.Tests
```

Synthetic checks cover read-only behavior, acquired/unimported/tenant isolation, raw evidence corruption, payload/destination conflicts, matching staged copies, staged-only reconciliation, existing ZIP preservation, ignored temporary files, unsafe destinations, missing database, Rome month boundaries/DST, exact month-end due time and missed-month catch-up.

The subsequent [live archive workflow](ARCHIVE-LIVE.md) implements explicit approved transfers, verified ZIPs and opt-in first-use catch-up. None is enabled by the simulation command; simulation remains strictly read-only. An existing ZIP remains a conservative blocker in dry-run output, while the live compressor can update a verified DMT-manifest ZIP safely. UI integration remains pending.

## Pilot evidence — 2026-10-16

The read-only simulation inspected 339 real JSON reports in about 231 seconds. It proposed 338 transfers (81.01 MB of source bytes), with 41 reports for the overdue September archive (13.41 MB) and 297 for the October archive (67.59 MB), due October 31 at 23:59 Europe/Rome. One report was blocked because the same acquired snapshotId now had different source bytes. That report was not imported, repaired, overwritten or moved.

Before/after checks found all 339 source names, lengths and modification times unchanged. The main database SHA-256 was identical, and the archive directory remained absent. Build passed without warnings/errors, and 57 synthetic assertions passed. Private JSON/Markdown plans were saved outside the repository; no company filenames or identifiers are included here. Exit code 2 correctly signaled the one blocked report, not a failed scan. These are pilot results, not guarantees for all network/share failure scenarios.
