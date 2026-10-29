# Progress Tracker

Last updated: 2026-10-29

## Current phase

Phase 1 — Core Classic gameplay engine.

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
- Rule documentation updated to reflect current decisions and remaining work.

## Not yet verified

- The new gameplay tests have been committed to the feature branch, but they have not been executed in this environment because the .NET SDK is unavailable here.
- Local test execution is required before merging.
- Wild Draw Four challenge edge cases, draw-pile exhaustion, and card-conservation behavior need more automated coverage.
- Official initial discard behavior is still temporarily simplified to a number card.
- UNO call and missed-call handling are not implemented.
- Room configuration and API integration are not implemented.
- Multiplayer transport, state recovery, and persistence are not implemented.

## Next task

Run dotnet test for the solution locally on feature/gameplay-engine. Fix any compile/test failures before merging. Then add more tests for successful/failed Wild Draw Four challenges and draw-pile recycling, implement UNO call handling, and introduce the room's immutable Official/House Rules configuration.
