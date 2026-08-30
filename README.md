<p align="center">
  <img src="https://www.kraugh.it/software/dmt/images/dmt-logo.png" alt="DMT — Dashboard Maintenance Toolkit" width="260">
</p>

# DMT — Dashboard Maintenance Toolkit

**DMT** is an open-source responsive web dashboard designed to turn technical inventory snapshots produced by [Maintenance Toolkit](https://www.kraugh.it/software/maintenance-toolkit/) into a searchable, historical and administrator-friendly asset inventory.

**Project status:** early design / prototyping.

Project page: **https://www.kraugh.it/software/dmt/**  
Kraugh software projects: **https://www.kraugh.it/software/**

## Why DMT

Maintenance Toolkit already runs on Windows endpoints to perform maintenance, updates, diagnostics and inventory collection. DMT keeps a deliberately separate responsibility: receive versioned JSON snapshots, correlate them with durable assets, preserve history and make the resulting information easy to search and understand.

The core principle is:

```text
MT observes the endpoint
        │
        ▼
versioned JSON snapshot
        │
        ▼
DMT imports, correlates and historicises
```

DMT is **not intended to become a full RMM**. Its initial focus is maintenance-derived inventory, visibility, history, search and asset lifecycle management.

## Design principles

- **Asset ≠ hostname ≠ Windows installation ≠ user.** A physical asset keeps its identity across renames, reimages, assignments and purpose changes.
- **MT and DMT remain independent.** MT must work without DMT; DMT consumes a versioned inventory schema rather than MT internals.
- **Low-friction onboarding.** A new asset should be created automatically from reliable technical data instead of requiring a large administrative form.
- **Raw evidence is preserved.** Imported JSON snapshots are immutable source material and can be archived/reprocessed.
- **Idempotent imports.** Each snapshot has a unique ID; importing the same snapshot again must not create duplicates.
- **Multi-tenant foundations from day one.** Tenant, site, department, people and asset relationships are separated in the data model even if the first deployment uses a single tenant.
- **Responsive web UI.** Desktop and mobile are first-class targets.
- **Lightweight operation.** Initial storage is expected to use SQLite; automatic folder polling is configurable, with manual immediate import available.
- **No hardcoded UI strings.** Every localisable string comes from JSON language packs in `languages/`.

## Native multilingual architecture

DMT must detect the user's browser/system locale on first access and fall back to English when no supported locale matches. A user's explicit language choice overrides automatic detection.

Initial language packs:

- Italian — `it-IT`
- English — `en-US` (canonical fallback)
- German — `de-DE`
- French — `fr-FR`
- Japanese — `ja-JP`
- Simplified Chinese — `zh-CN`

Adding another language must require only adding a new JSON file to `languages/`; application code must not need modification.

Language files also contain their own metadata so the selector can discover and display them dynamically.

## Planned ingestion model

A typical managed deployment will use Active Directory / Group Policy to launch MT with an optional remote inventory destination. MT continues saving locally and additionally writes immutable JSON snapshots to a configured network folder.

DMT associates each ingestion folder with contextual metadata such as tenant and site, then periodically scans for new `.json` files. The administrator can configure the polling interval; the initial default is planned at **30 minutes**, plus an **Import new data now** action.

Files are expected to flow through folders such as:

```text
incoming/
processed/
rejected/
```

DMT only moves a snapshot to `processed` after a successful database commit.

## What DMT should eventually answer

Examples of practical questions:

- Which PCs still run Windows 10?
- Where is Office 2016 installed?
- Which machines have AnyDesk?
- Which devices have less than 16 GB RAM?
- Which devices have not reported in N days?
- Which machines still have Java 8?
- Which systems have an outdated firmware or driver version?
- What changed on this asset since the previous snapshot?
- Who was this asset assigned to six months ago?
- Which retired assets had a specific hardware configuration?

## Repository layout

```text
DMT/
├── assets/
│   └── branding/        # Logo, icons and design concept
├── docs/                # Architecture and MT integration notes
├── languages/           # JSON UI language packs — zero hardcoded UI strings
├── src/                 # Application source (to be implemented)
├── tests/               # Tests (to be implemented)
├── LICENSE
├── README.md
└── ROADMAP.md
```

## Documentation

- [`ROADMAP.md`](ROADMAP.md) — project roadmap and milestones
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — current architectural decisions
- [`docs/MT5-INVENTORY-REQUESTS.md`](docs/MT5-INVENTORY-REQUESTS.md) — inventory/export requirements requested from Maintenance Toolkit 5.0
- [`docs/MT-INTEGRATION-HANDOFF.md`](docs/MT-INTEGRATION-HANDOFF.md) — initial integration handoff

## Branding

The orangutan mascot represents a librarian/control-room operator: a subtle nod to the idea of organising technical knowledge without making the reference explicit. The visual direction is dark navy/black with blue technical accents.

The canonical public logo is hosted on the DMT project page at kraugh.it so GitHub and the website share the same visual source.

## Licence

DMT is released under the **MIT License**. See [`LICENSE`](LICENSE).

## Author

**Luca Miselli — Kraugh**  
https://www.kraugh.it/software/
