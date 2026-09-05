# DMT architecture — current decisions

## Product boundary

DMT consumes versioned technical snapshots. Maintenance Toolkit remains an independent maintenance tool and snapshot producer. DMT never relies on MT internal implementation details.

## Durable identity

An asset is not equivalent to its hostname, operating-system installation or currently assigned user. DMT assigns an internal durable identity and correlates new observations using multiple technical identifiers supplied by the collector.

## Snapshot semantics

Every MT inventory execution produces an immutable point-in-time observation with a unique `snapshotId`. DMT imports snapshots idempotently and preserves the raw JSON as evidence/source material.

## Ingestion sources

An ingestion source maps a physical/logical folder to organisational context, initially at least tenant and site. MT itself does not need to know those concepts.

Conceptual fields:

```text
ingestion_sources
- id
- tenant_id
- site_id
- department_id nullable
- path
- enabled
- default_status
```

## Processing flow

```text
MT writes .tmp
     ↓
MT finalises .json
     ↓
DMT scans incoming/
     ↓
validate schema + snapshotId
     ↓
identify/create asset
     ↓
DB transaction
     ↓
COMMIT
     ↓
move raw JSON to processed/
```

Invalid inputs go to `rejected/` with a recorded reason. A file must never be marked processed before the database commit succeeds.

## Polling

The initial design deliberately avoids a permanent filesystem watcher. DMT uses configurable polling with a default interval of 30 minutes plus a manual immediate-import command. This keeps the first implementation simple, predictable and low-overhead.

## Data model foundations

Tenant is the customer/organisation, not a site. Sites and departments are independent organisational dimensions. People receiving assets are separate from accounts that can log in to DMT.

Almost every tenant-owned business entity must be scoped by `tenant_id` directly or through safe foreign-key relationships.

## Internationalisation

No localisable UI string may be hardcoded. JSON packs under `languages/` are discovered at runtime. `en-US` is the canonical fallback. Adding a language must not require application source changes.

Technical source data from MT is preserved as observed; DMT translates its own interface, not arbitrary manufacturer/product data.

## Initial persistence

SQLite is the planned first database because the initial workload is read-heavy and small. The data-access layer and migrations must avoid assumptions that prevent later migration to PostgreSQL/MySQL.


## DMT Free / Home deployment baseline

The first executable target is Windows with Caddy terminating HTTPS and SQLite as local persistence. DMT backend traffic remains on localhost; the user-facing Caddy port is selected during setup. The setup inspects local listeners before proposing a default port, initially preferring 8443. Caddy Internal CA is the default Home/Free certificate model.

## Visual and setup baseline

The setup is a WPF graphical application and uses the same refined pastel/tactile language as DMT. It explains actions before changing the machine and delays UAC until privileged operations are actually required. The Librarian mascot is good-natured and technically competent; visual references remain original rather than reproducing protected designs.

## Language baseline

Normal runtime-discovered packs are en-US, it-IT, fr-FR, de-DE, es-ES, zh-CN, ja-JP and sv-SE. `tlh.json` is a hidden but otherwise normal pack unlocked as an easter egg. Language packs are validated against en-US key parity.
