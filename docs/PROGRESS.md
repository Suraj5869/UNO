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
- Official Rules do not permit draw stacking. House Rules permit same-type Draw Two and Wild Draw Four stacking only; mixed stacking is rejected.
- Portable documentation is required so work can continue across chats and AI tools.

## Implemented in the repository

- Standard 108-card Classic UNO deck creation and shuffle.
- Game factory for 2–10 players, seven cards per player, and card-conservation checks.
- Game state and player models.
- Gameplay engine methods for playing cards, drawing, passing after a playable draw, Skip, Reverse, Draw Two, Wild color selection, and Wild Draw Four accept/challenge flows.
- Tests covering core card play, invalid moves, current-player enforcement, two-player Reverse, Draw Two, and Wild Draw Four challenge setup/acceptance.
- UNO call and missed-call engine actions: a player who is reduced to one card can call UNO; another participant can catch a missed call during the response window, causing a two-card penalty.
- Explicit `UnoRuleSet.Official` and `UnoRuleSet.House` presets; new games default to Official and persist the selected preset in a read-only game-state property.
- House Rules same-type draw stacking: Draw Two stacks accumulate by two; Wild Draw Four stacks accumulate by four and retain challenge handling for the latest Wild Draw Four. Players can accept a pending Draw Two stack to draw the accumulated amount and lose their turn.
- Tests added for UNO calls, missed-call penalties, and expiry of the call window when the next player begins an action.
- Rule documentation updated to reflect current decisions and remaining work.

## Not yet verified

- The user confirmed the gameplay and UNO-call tests passed locally before the latest rule-preset changes. Rule-preset tests have been added but have not yet been run in this environment.
- Wild Draw Four challenge edge cases, draw-pile exhaustion, and card-conservation behavior need more automated coverage.
- Official initial discard behavior is still temporarily simplified to a number card.
- UNO-call actions are implemented in the engine; real-time transport must enforce the call window across connected clients.
- Room lifecycle, creator-only rule selection, and API integration are not implemented.
- House-rule stacking has been implemented for same-type Draw Two and Wild Draw Four chains; new automated coverage still needs to be run locally.
- Multiplayer transport, state recovery, and persistence are not implemented.

## Next task

Run `dotnet test src/backend/Uno.sln` on `main` to verify House Rules stacking. If green, implement private-room lifecycle (creator-only rule selection before start, 2–10 player validation, and immutable settings after start) before API/SignalR integration.
