# DMT — Roadmap

This roadmap records the current direction of **DMT — Dashboard Maintenance Toolkit**. It is intentionally practical and may evolve as real deployments and performance tests provide evidence.

## Recovery checkpoint — 2026-11-17

Tracking: [ingestion #1](https://github.com/Kraugh/DMT/issues/1), [blocked physical installer #2](https://github.com/Kraugh/DMT/issues/2).

See [current implementation and handoff](docs/STATUS.md) and [clean-PC recovery](docs/RECOVERY.md). This section is the current operational status; older phase checklists below also include broader product requirements.

### Implemented and retained in source

- [x] Incremental SQLite imports, UUID/serial identity, snapshot conflicts, raw bytes/hash and collection/import history.
- [x] Latest-snapshot computer/OS/software dashboard, product grouping, reverse search, maintenance review and TXT/CSV exports.
- [x] Verified share archiving, monthly ZIP manifests/catch-up, conflict quarantine and evidence-bound optional pruning.
- [x] WPF service-management/acquisition panel and self-contained installer build.
- [x] Installed-mode local administrator authentication, Caddy HTTPS and owned-child shutdown integration tests.
- [x] Synthetic inventory, setup/runtime, report-text, localization and mocked GPO test harnesses.
- [x] Recovery documentation distinguishing implemented code from real deployment acceptance.

### Open — preserve before any further pilot

- [ ] **BLOCKER: diagnose the physical installer stall** at directory creation (~35%, approximately one hour); preserve partial state, verify package/footer version, inspect IPC/WPF/ACL hypotheses, add durable phase diagnostics, bounded operations and recovery.
- [ ] Complete physical UAC/SCM/LocalService ACL/HTTPS-trust/start/restart/stop/migration/acquisition acceptance; do not label the installer stable before this passes.
- [ ] Implement safe upgrade/uninstall, interrupted-install recovery, code signing and repeatable stable-release packaging.
- [ ] Add configurable scheduled import and service/share identity design; archive catch-up is not scheduled acquisition.
- [ ] Reduce archive verification latency with measured safe caching and visible progress/cancellation.
- [ ] Design bounded source-ZIP intake and diagnosis of same-snapshot payload changes during collection.
- [ ] Deliver as-of-date fleet comparisons, freshness/retirement semantics and software-change timelines; retained history alone is insufficient.
- [ ] Add consistent private backup/restore/rebuild with a verified recovery drill and retention policy.
- [ ] Add trustworthy manufacturer BIOS/GPU applicability checks. Agent enrollment/job transport/update execution remain unimplemented; no remote update is executed from report strings.
- [ ] Add encryption views with explicit unknown/partial evidence; `encrypted` is not a complete BitLocker result.
- [ ] Add multi-user/read-only roles, LAN/certificate support, tenant administration and authorization review.
- [ ] Review new translations beyond Italian/English, accessibility, UI/version presentation and GPO deployment acceptance.

## Phase 0 — Project foundation

- [x] Define DMT scope and separation from Maintenance Toolkit.
- [x] Define initial branding, logo and visual concept.
- [x] Define responsive web application as the primary UI.
- [x] Define native multilingual architecture with JSON language packs.
- [x] Define English fallback and browser/system locale detection.
- [x] Define multi-tenant-ready data model as an architectural requirement.
- [x] Define asset identity as independent from hostname, OS installation and user.
- [x] Define JSON snapshot ingestion contract with MT.
- [x] Define configurable polling plus manual immediate import.
- [x] Select initial Windows setup stack: .NET 10 WPF; Caddy + SQLite deployment baseline.
- [x] Create initial WPF setup skeleton and runtime JSON localisation loader.

## Phase 1 — Working demo / MVP

### Authentication and UI

- [x] Responsive login page.
- [ ] Admin and read-only user roles.
- [ ] Language selector available from login onward.
- [ ] Persist user language preference.
- [ ] Refined pastel/tactile dashboard matching the DMT visual identity.
- [ ] Desktop and smartphone layouts.

### Internationalisation

- [x] Runtime discovery of JSON files in `languages/`.
- [ ] No localisable UI string hardcoded in application source.
- [x] `en-US` canonical fallback.
- [x] Initial `it-IT`, `en-US`, `de-DE`, `fr-FR`, `es-ES`, `ja-JP`, `zh-CN`, `sv-SE` packs.
- [x] Hidden key-complete `tlh` pack architecture for the Klingon easter egg.
- [x] Missing-key fallback to English.
- [ ] Missing-key logging and visible development marker.
- [ ] Locale-aware dates, times, numbers and pluralisation.

### Storage

- [x] SQLite persistence and installer migration code (physical installation acceptance blocked).
- [x] WAL mode and appropriate indexes.
- [x] Schema migrations from the beginning.
- [x] Preserve raw imported snapshots alongside relational data as exact-byte BLOBs.

### Ingestion

- [ ] Configure one or more ingestion sources/folders.
- [ ] Associate ingestion source with tenant and site.
- [ ] Optional default department/status per source.
- [ ] Default automatic polling every 30 minutes.
- [ ] Administrator-configurable polling interval.
- [x] **Import new data now** manual action.
- [x] Ignore `.tmp` files.
- [x] Validate JSON schema version.
- [x] Deduplicate by immutable `snapshotId`.
- [x] Transactional import.
- [x] Keep source read-only; archive successfully imported bytes transactionally in SQLite (supersedes planned moves to `processed/`, ADR 0001).
- [x] Report malformed/unimportable files with reason codes without moving originals (supersedes planned `rejected/` moves).
- [x] Preserve accepted raw JSON rather than deleting it.

### Assets

- [x] Auto-create a minimal asset from reliable inventory data.
- [x] Durable internal Asset ID independent from hostname.
- [x] Recognition using multiple technical identifiers.
- [x] Current inventory view.
- [x] Snapshot history.
- [ ] Detect technical changes between snapshots.
- [ ] Preserve administrative metadata across technical changes.

### Dashboard and search

- [x] Asset list.
- [x] Asset detail page.
- [x] Software inventory.
- [ ] Global search.
- [ ] Filters for OS, software, RAM, firmware and last check-in.
- [ ] Import status and last/next import indicators.
- [x] Import result summary: found/imported/duplicate/rejected/duration.

## Phase 2 — Asset lifecycle

Development evidence for the first importer/dashboard/API slice is recorded in [INVENTORY-DEVELOPMENT.md](docs/INVENTORY-DEVELOPMENT.md). CLI import, localized browser inventory, OS/software/history queries and filtered CSV exist; the broader ingestion issue, multi-user authorization, scheduling and physical deployment acceptance remain incomplete.

- [ ] Sites and departments as independent dimensions.
- [ ] People separate from DMT login users.
- [ ] Temporal asset assignments.
- [ ] Assignment types: personal, shared, pool, spare, kiosk, other.
- [ ] Purpose/use history.
- [ ] Asset statuses: active, spare, storage, repair, retired, disposed, lost, returned.
- [ ] Purchase/acquisition date.
- [ ] Supplier.
- [ ] Ownership / rental / leasing model.
- [ ] Contract end / expected disposal date.
- [ ] Warranty start/end and extensions.
- [ ] Manual notes and service events.
- [ ] Hardware upgrade events derived from snapshot comparison.
- [ ] Retired/disposed assets remain searchable by default.

## Phase 3 — Inventory intelligence

- [ ] Queries for unsupported/old Windows versions.
- [x] Software/version fleet search.
- [ ] Firmware/version fleet search.
- [ ] RAM/storage capacity filters.
- [ ] Last-seen ageing thresholds.
- [ ] Change timeline per asset.
- [ ] Software added/removed history.
- [ ] Hardware added/removed/replaced history.
- [ ] Optional alerts based on rules.
- [ ] Configurable dashboard widgets.

## Phase 4 — Multi-tenant administration

- [ ] Tenant administration UI.
- [ ] Tenant/site/department management.
- [ ] User-to-tenant memberships.
- [ ] Platform admin / tenant admin / tenant user roles.
- [ ] Strict tenant scoping in all queries and history tables.
- [ ] Cross-tenant administration for a future MSP scenario.
- [ ] Tenant-specific ingestion sources and policies.

## Phase 5 — Operational maturity

- [ ] Backup/restore workflow.
- [ ] Database rebuild from preserved raw snapshots.
- [ ] Audit log.
- [ ] Export/report functions.
- [ ] Security hardening review.
- [ ] Performance profiling with realistic fleets.
- [ ] PostgreSQL/MySQL migration path if concurrent load or deployment needs exceed SQLite.
- [ ] Optional PWA capabilities.

## Phase 6 — Optional recommendations and on-premise device monitoring

These are optional future capabilities. They are not requirements for the first DMT release and must not displace core asset, ingestion, security or operational-maturity priorities.

### Procurement provider foundation

- [ ] Define a provider-neutral **Procurement Provider** abstraction, independent from DMT's inventory and recommendation logic.
- [ ] Allow providers such as Amazon, AliExpress, professional IT distributors, a customer's preferred supplier, a custom corporate catalogue/URL or a future internal procurement system.
- [ ] Keep core DMT functionality independent from affiliate programmes and commercial-provider availability.
- [ ] Allow enterprise administrators to disable commercial suggestions completely, select permitted providers or replace public providers with their own supplier.
- [ ] Require an explicit user action before any commercial search, link or provider request; never show automatic advertising, open external sites automatically or initiate purchases.
- [ ] Do not perform commercial tracking or provider calls merely because an asset page is viewed.
- [ ] Limit results to a small number of relevant products or searches rather than exposing large catalogues.
- [ ] Clearly disclose affiliate/partner links when used, subject to the provider's current terms and applicable privacy requirements.
- [ ] Reassess affiliate-programme terms, disclosure duties and privacy implications before implementation.

### Hardware upgrade recommendations

- [ ] Add a discreet, collapsed-by-default **Possible upgrades** section to the asset detail page.
- [ ] Initially evaluate reasonable RAM, SATA SSD and NVMe/M.2 upgrades, including useful capacity increases where applicable.
- [ ] Keep three responsibilities separate: MT hardware inventory, DMT compatibility/upgrade recommendation and the optional Procurement Provider.
- [ ] Never infer compatibility from a generic technology label such as `DDR4` or `NVMe` alone.
- [ ] Recommend an upgrade only when the available technical evidence is sufficient; otherwise state explicitly that compatibility requires verification.
- [ ] Show no products or commercial searches until the user deliberately opens and invokes the feature.

### Network printer and consumable monitoring

- [ ] For on-premise DMT, evaluate read-only discovery and monitoring of network printers via SNMP.
- [ ] Support SNMPv2c where required and prefer/support SNMPv3 where available.
- [ ] Use the standard Printer-MIB as the first source, with an extensible path for vendor-specific OIDs/MIBs when standard data is insufficient.
- [ ] Never use SNMP to modify printer configuration.
- [ ] Collect, when exposed by the device, manufacturer, model, serial number, hostname/IP, device/printer status and errors.
- [ ] Collect consumable type, identifiable product/code, toner or ink level, drum/imaging unit, waste toner and other exposed maintenance/consumable states.
- [ ] Use configurable, reasonably slow polling rather than continuous interrogation.
- [ ] Allow configurable thresholds and alerts such as `Black toner 12%`.
- [ ] When a consumable can be identified, offer an explicit **Find consumable** or **Order new cartridges** action through the shared Procurement Provider abstraction.
- [ ] Do not hardcode printer procurement to Amazon or any other individual provider.
- [ ] Do not show automatic advertising, open commercial sites automatically or initiate purchases.
- [ ] Treat direct private-LAN access as an on-premise capability. Any future DMT cloud equivalent requires a separately designed local agent/relay component and must not be assumed to share the same architecture.

## Explicit non-goals for the initial project

DMT is not currently intended to provide:

- remote shell;
- remote desktop;
- credential vaulting;
- arbitrary remote command execution;
- centralised patch deployment;
- full RMM functionality.

These would materially alter the project's security model and scope and must not be introduced casually.

### 0.1-dev3
- [x] Clickable setup navigation for implemented steps
- [x] Listener selection and detected-facts detail card
- [x] Executable metadata and embedded signing publisher when available
- [x] First local listener knowledge base with explicit unknown state
