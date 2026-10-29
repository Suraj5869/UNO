# Progress Tracker

Last updated: 2026-10-29

## Current phase

Phase 1 — Core Classic gameplay engine and action validation.

## Confirmed product decisions

- Web and mobile clients are planned.
- Classic UNO first; variants follow after the base engine is reliable.
- Shared architecture should support multiple variants.
- Target capacity is 2–10 total participants (humans plus bots); this is a design target, not a load-test result.
- Bots and online multiplayer are planned.
- Server-authoritative rules and filtered per-player state are required.
- Private rooms will let the creator choose Official Rules or House Rules before the game starts. Rules are locked once play begins.
- Official Rules do not permit draw stacking. House Rules can enable stacking, with exact combinations still to be specified.
- Portable documentation is required so work can continue across chats and AI tools.

## Implemented in the repository

- Standard 108-card Classic UNO deck creation and shuffle.
- Game factory for 2–10 players, seven cards per player, and card-conservation checks.
- Game state and player models.
- Gameplay engine methods for playing cards, drawing, passing after a playable draw, Skip, Reverse, Draw Two, Wild color selection, and Wild Draw Four accept/challenge flows.
- Tests covering core card play, invalid moves, current-player enforcement, two-player Reverse, Draw Two, and Wild Draw Four challenge setup/acceptance.
- UNO call and missed-call engine actions: a player who is reduced to one card can call UNO; another participant can catch a missed call during the response window, causing a two-card penalty.
- Tests added for UNO calls, missed-call penalties, and expiry of the call window when the next player begins an action.
- Rule documentation updated to reflect current decisions and remaining work.

## Not yet verified

- The user confirmed the existing gameplay tests passed locally before the UNO-call changes. The newly added UNO-call tests have not yet been run in this environment.
- Wild Draw Four challenge edge cases, draw-pile exhaustion, and card-conservation behavior need more automated coverage.
- Official initial discard behavior is still temporarily simplified to a number card.
- UNO-call actions are implemented in the engine; real-time transport must enforce the call window across connected clients.
- Room configuration and API integration are not implemented.
- Multiplayer transport, state recovery, and persistence are not implemented.

## Next task

Run `dotnet test src/backend/Uno.sln` on `main` to verify the new UNO-call tests. Then strengthen draw-pile recycling/card-conservation coverage and introduce the room lifecycle with immutable Official/House Rules configuration before wiring gameplay into the API.
