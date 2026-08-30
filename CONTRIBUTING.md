# Contributing to DMT

DMT is currently in early design and prototyping.

Before proposing substantial implementation changes, please open an issue describing the problem, expected behaviour and architectural impact.

## Localisation rule

**No user-interface string may be hardcoded in application source.**

All localisable text belongs in JSON files under `languages/`. New UI features must add their keys to the English fallback pack and keep the other shipped packs structurally aligned before release.

## Product boundary

Changes must preserve the separation between Maintenance Toolkit and DMT. DMT is an inventory/history/visibility application, not a general remote-control agent.

## Security

Never add collection, persistence or logging of passwords, recovery keys, tokens, private keys or other credential material.
