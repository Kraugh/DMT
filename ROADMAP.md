# DMT — Roadmap

This roadmap records the current direction of **DMT — Dashboard Maintenance Toolkit**. It is intentionally practical and may evolve as real deployments and performance tests provide evidence.

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
- [ ] Select implementation stack for the first working demo.
- [ ] Create initial application skeleton and development environment.

## Phase 1 — Working demo / MVP

### Authentication and UI

- [ ] Responsive login page.
- [ ] Admin and read-only user roles.
- [ ] Language selector available from login onward.
- [ ] Persist user language preference.
- [ ] Dark technical dashboard matching the DMT visual identity.
- [ ] Desktop and smartphone layouts.

### Internationalisation

- [ ] Runtime discovery of JSON files in `languages/`.
- [ ] No localisable UI string hardcoded in application source.
- [ ] `en-US` canonical fallback.
- [ ] Initial `it-IT`, `en-US`, `de-DE`, `fr-FR`, `ja-JP`, `zh-CN` packs.
- [ ] Missing-key fallback to English.
- [ ] Missing-key logging and visible development marker.
- [ ] Locale-aware dates, times, numbers and pluralisation.

### Storage

- [ ] SQLite database for the initial deployment.
- [ ] WAL mode and appropriate indexes.
- [ ] Schema migrations from the beginning.
- [ ] Preserve raw imported snapshots outside/alongside relational data.

### Ingestion

- [ ] Configure one or more ingestion sources/folders.
- [ ] Associate ingestion source with tenant and site.
- [ ] Optional default department/status per source.
- [ ] Default automatic polling every 30 minutes.
- [ ] Administrator-configurable polling interval.
- [ ] **Import new data now** manual action.
- [ ] Ignore `.tmp` files.
- [ ] Validate JSON schema version.
- [ ] Deduplicate by immutable `snapshotId`.
- [ ] Transactional import.
- [ ] Move successfully committed files to `processed/`.
- [ ] Move malformed/unimportable files to `rejected/` with reason logging.
- [ ] Preserve processed raw JSON rather than deleting it.

### Assets

- [ ] Auto-create a minimal asset from reliable inventory data.
- [ ] Durable internal Asset ID independent from hostname.
- [ ] Recognition using multiple technical identifiers.
- [ ] Current inventory view.
- [ ] Snapshot history.
- [ ] Detect technical changes between snapshots.
- [ ] Preserve administrative metadata across technical changes.

### Dashboard and search

- [ ] Asset list.
- [ ] Asset detail page.
- [ ] Software inventory.
- [ ] Global search.
- [ ] Filters for OS, software, RAM, firmware and last check-in.
- [ ] Import status and last/next import indicators.
- [ ] Import result summary: found/imported/duplicate/rejected/duration.

## Phase 2 — Asset lifecycle

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
- [ ] Software/version fleet search.
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

## Explicit non-goals for the initial project

DMT is not currently intended to provide:

- remote shell;
- remote desktop;
- credential vaulting;
- arbitrary remote command execution;
- centralised patch deployment;
- full RMM functionality.

These would materially alter the project's security model and scope and must not be introduced casually.
