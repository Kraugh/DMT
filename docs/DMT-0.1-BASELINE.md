# DMT 0.1 architecture baseline

This document freezes the first executable slice of DMT after the September 2026 design review.

## Product direction

- DMT Free remains genuinely open source and the official Kraugh build manages up to **10 active assets**.
- The free/open-source build may be signed with the existing Certum **Open Source Developer** code-signing certificate.
- DMT is not an RMM. It focuses on maintenance-derived inventory, visibility, history, search and asset lifecycle.
- The product tone is practical and calm: precise information instead of motivational slogans.

## First deployment target

- Windows host.
- **Caddy** terminates HTTPS.
- DMT backend listens only on localhost.
- **SQLite** is the first persistence engine.
- HTTPS is the default. Caddy's internal CA is the Home/Free baseline; HTTP is reserved for explicit localhost/development use.

## Setup experience

`DMT-Setup.exe` is part of the product, not a throw-away bootstrapper.

- WPF, self-contained Windows build.
- Pastel/tactile visual language shared with DMT: soft surfaces, delicate borders, subtle relief and shadows.
- No console-first setup UX.
- Explain each operation before performing it.
- Do not request elevation on launch. UAC is requested only when system changes are about to be applied.
- First technical setup operation: inspect local TCP listeners, show occupied ports/processes, propose a free HTTPS port (initial preference: 8443), and let the user change it.
- Public Caddy port and localhost DMT backend port are separate.

## Internationalisation — global Kraugh rule

No localisable UI string is hardcoded. Installer and application discover JSON language packs from `languages/`.

Primary language is detected from the OS/browser. The user can override it at any time. Missing keys fall back to `en-US`; missing fallback keys must be visible in development as `[MISSING: key]` and logged.

Normal language set:

- `en-US` — English (canonical fallback)
- `it-IT` — Italiano
- `fr-FR` — Français
- `de-DE` — Deutsch
- `es-ES` — Español
- `zh-CN` — 简体中文
- `ja-JP` — 日本語
- `sv-SE` — Svenska

Adding a normal language must require only adding a valid JSON pack. No enum/source edit is allowed.

### Hidden nerd language

`tlh.json` (`tlhIngan Hol`) is a real, key-complete language pack but is hidden from normal discovery. Clicking the Librarian's deliberately non-infringing retro-space badge unlocks it. Once selected, the complete program must remain functional in Klingon exactly as in any other locale.

The initial Klingon text in the prototype is a development draft and must receive linguistic review before a public release.

## Visual identity

- Warm, refined pastel surfaces.
- Strong colours only for semantic state/attention.
- Slightly raised buttons and cards; pressed controls should feel physically pressed.
- Librarian/orangutan: competent, patient and good-natured.
- Retro space-uniform reference may be suggestive but must remain visually original; no protected insignia or direct replica.
- Easter eggs are discreet rewards for attentive nerds and never interfere with accessibility, security or normal workflow.

Core UI principle: **warm in presentation, rigorous in information**.

## 0.1-dev3 — Listener details

- Welcome and Network/ports steps are directly navigable from the left rail.
- Selecting a TCP listener opens a detail card with detected process, PID, service, executable path, file description/company and embedded code-signing publisher when available.
- Detected Windows facts are visually separated from DMT's local knowledge-base explanation.
- Initial knowledge entries cover RPC, SMB, Intel LMS, RDP, WinRM and Dropbox; unknown listeners are explicitly marked as not yet documented rather than guessed.
- The Kraugh footer caption wraps instead of being clipped by longer translations.
- All language packs remain key-complete, including hidden `tlh`.


## dev4 window chrome

- Custom title bar now exposes minimize, maximize/restore and close.
- Double-clicking the title bar toggles maximize/restore.
- Window remains resizable and keeps its existing minimum size.
- Build warnings from listener detail code were cleaned up.
