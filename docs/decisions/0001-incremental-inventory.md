# ADR 0001 — Incremental inventory slice (2026-10-14)

Accepted for the slice requested in this session; follows issue #1.

Add a .NET 10 inventory library, an ASP.NET Core localhost development host and a dependency-free executable test harness. Keep WPF setup and its installation contract unchanged. Microsoft.Data.Sqlite and transitive dependencies were explicitly authorized by the user.

The observed schema is 1.0: section envelopes carry status/errors/data; software contains win32 and appx lists. This supersedes the preliminary flat JSON example only for this importer. Unsupported versions and malformed sections are reported, never repaired.

Sources are read-only. Store exact input bytes as a SQLite BLOB in the same transaction as indexed inventory, rather than moving files to processed/rejected on the share. This supersedes the original moving-file flow. SHA-256 distinguishes a true duplicate from conflicting bytes under the same snapshotId. Keys are tenant-scoped. SQLite uses foreign keys, WAL and versioned migrations.

Resolve durable internal assets through normalized SMBIOS UUID and manufacturer-scoped serial. Conflicting identifiers reject the snapshot; hostnames never establish identity. Missing reliable identity rejects the snapshot. All observations retain their own identity and inventory. Current queries choose the newest collected UTC instant with snapshotId as deterministic tie-breaker; older late arrivals cannot overwrite it. Missing/failed sections remain distinguishable from empty successful sections.

The first host has no authentication and must stay on loopback for local development. It is not integrated into Caddy or Windows services and is not a production deployment. Import is an explicit CLI operation; HTTP exposes read queries only. No network execution, BIOS action, scheduled import or MT changes are part of this slice.

Local development data defaults to ignored data/; the installed ProgramData layout remains unchanged. Real test databases stay outside the public repository. Technical API keys/status codes are language independent; this slice adds no UI strings.
