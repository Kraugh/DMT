# ADR 0002 — Local inventory browser preview (2026-10-14)

The user requested a usable interface after testing the JSON APIs. Add a small native HTML/CSS/JavaScript frontend to the existing ASP.NET Core host. No frontend framework, external CDN, package or replacement installer is needed.

The root route serves the inventory page, superseding its temporary redirect to `/api/systems`. Keep all inventory APIs and read-only operations. Static assets and JSON language packs are copied beside the development/published backend. A catalog discovers language packs at startup, excludes hidden packs from the ordinary selector and merges selected strings with English fallback. The mascot badge unlocks the hidden Klingon pack; newly added Klingon strings remain English drafts pending review.

Use the existing WPF cream/paper/orange palette and branding. Provide computer/OS search, name/publisher/version software search, reverse lookup through computer detail, collection history and filtered CSV. User data is inserted as DOM text, never interpreted as HTML. Render at most 100 inventory rows per page; CSV exports every matching row. Loading shows actual elapsed seconds rather than invented progress.

This preview still lacks authentication and remains restricted to loopback. Refresh reloads already imported data; it must not suggest that it runs collection, maintenance or a new import. Statuses of inventory and maintenance stay separate. No new remote action, Caddy installation or setup behavior is introduced.
