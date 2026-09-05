<p align="center">
  <img src="https://www.kraugh.it/software/dmt/images/dmt-logo.png" alt="DMT — Dashboard Maintenance Toolkit" width="260">
</p>

# DMT — Dashboard Maintenance Toolkit

**DMT** is an open-source responsive dashboard that turns versioned technical inventory snapshots produced by [Maintenance Toolkit](https://www.kraugh.it/software/maintenance-toolkit/) into a searchable, historical and administrator-friendly asset inventory.

**Project status:** 0.1 development bootstrap.  
Project page: **https://www.kraugh.it/software/dmt/**

## What DMT is for

DMT is deliberately not a full RMM. It is meant to make useful hardware/software information easy to see, compare and search without forcing the administrator to assemble the same view from several tools.

```text
MT observes the endpoint
        │
        ▼
versioned JSON snapshot
        │
        ▼
DMT imports, correlates, historicises and explains what changed
```

The home screen should answer a practical question quickly: **which machines deserve attention now, and why?**

## DMT Free

The official Kraugh open-source build is intended to manage up to **10 active assets**. It is a real usable edition, not a time-limited demo. Source remains open; the 10-asset limit applies to the official build and is not intended as hostile DRM.

## First implementation baseline

- Windows host
- Caddy HTTPS reverse proxy
- SQLite
- WPF graphical installer
- responsive web application for DMT itself
- immutable MT JSON snapshots
- configurable polling + manual import
- multi-tenant-safe data model from day one
- durable asset identity independent of hostname/user/Windows installation

See [`docs/DMT-0.1-BASELINE.md`](docs/DMT-0.1-BASELINE.md).

## Setup experience

The installer is intentionally part of the DMT UX. It starts graphically, uses the same pastel/tactile visual identity as the application, explains operations before performing them and does not request UAC until system modifications are actually required.

The first technical configuration step will inspect local TCP listeners, show occupied ports/processes, propose a free HTTPS port (preferring 8443) and allow the user to choose another one.

The first executable prototype lives in [`src/DMT.Setup`](src/DMT.Setup).

## Internationalisation

**No localisable UI string is hardcoded.** DMT and its installer discover JSON packs from `languages/` at runtime. The initial language follows the operating system/browser; the user can change it; `en-US` is the canonical fallback.

Normal language packs:

- `en-US` — English
- `it-IT` — Italiano
- `fr-FR` — Français
- `de-DE` — Deutsch
- `es-ES` — Español
- `zh-CN` — 简体中文
- `ja-JP` — 日本語
- `sv-SE` — Svenska

A hidden, real `tlh.json` pack exists as a nerd easter egg. It is not shown in the normal selector until unlocked from the Librarian's badge.

Run `python tests/validate-language-packs.py` to enforce key parity with `en-US`.

## Design principles

- **Asset ≠ hostname ≠ Windows installation ≠ user.**
- MT and DMT remain independent.
- Raw JSON evidence is preserved.
- Imports are idempotent and transactional.
- Multi-tenant and authorization boundaries are architectural, not cosmetic.
- Security is never a paid-only feature.
- UI should be warm and welcoming; information should be precise and explainable.
- No motivational-corporate filler: useful tools and clear visibility are the value.

## Repository layout

```text
DMT/
├── assets/branding/
├── docs/
├── languages/
├── src/
│   └── DMT.Setup/
├── tests/
├── DMT.sln
├── LICENSE
├── README.md
└── ROADMAP.md
```

## Licence

DMT is currently released under the **MIT License**. See [`LICENSE`](LICENSE).

## Author

**Luca Miselli — Kraugh**  
https://www.kraugh.it/software/

### 0.1.0-dev.2 — Port & Listener Inspector

The setup's **Network and ports** step now performs a passive local inspection of TCP listeners using the Windows IP Helper API. It does not scan the network and does not generate network traffic. For each listener DMT resolves the local binding, port, owning PID/process, Windows service(s) when available, executable path when accessible, and classifies exposure as loopback-only, all interfaces, or a specific local address. The setup also proposes an available HTTPS port, preferring 8443.
