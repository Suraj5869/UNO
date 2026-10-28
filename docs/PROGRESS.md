# Progress Tracker

Last updated: 2026-10-28

## Current phase
Phase 0 — Foundation.

## Confirmed product decisions
- Web and mobile clients.
- Classic game first.
- Shared architecture intended to support multiple variants.
- Shared room infrastructure target: 2–10 total participants (humans plus bots).
- Bots and online multiplayer are both planned.
- Server-authoritative rules and filtered player state.
- Portable documentation is required for continuity across AI tools.

## Implemented and verified
- No application code has been created or verified yet.
- This repository scaffold is documentation-only.

## In progress
- Establish GitHub repository and local working copy.

## Pending
- Create repository and push initial commit.
- Confirm local prerequisites (.NET SDK, Node.js, Git).
- Create solution and project layout.
- Finalize Classic rules decisions.
- Implement game engine and automated tests.

## Known risks/open questions
- Exact Classic rule choices remain unresolved in `GAME_RULES.md`.
- Actual supported capacity must be tested; 10 participants is a design target, not a load-test result.
- Active state recovery is not implemented.
- Public/commercial licensing and branding review is required.

## Next task
Create a private GitHub repository named `uno-game-platform` (unless a different name is preferred), add this scaffold, and clone it locally.
