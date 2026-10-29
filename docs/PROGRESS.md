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
- Private room lifecycle model: generated room ID and shareable six-character code, creator membership, 2–10 capacity validation, waiting/in-progress status, join validation, creator-only rule selection and game start, and mapping room participants to engine player IDs.
- Automated room tests cover room creation, capacity, joining, creator permissions, rule selection, game start, participant mapping, and settings lock after start.
- REST API endpoints added for room creation, lookup by code, joining, creator-only rule selection, and starting a game.
- API returns room summaries rather than the full engine state, so opponents' hands are not exposed through these room endpoints.
- Room-specific random access tokens are issued to the creator and each joining participant; creator-only actions resolve the player identity from the token rather than trusting a client-supplied player ID.
- SignalR hub added at `/hubs/uno-room` with token-validated room joining, lobby broadcasts, game actions routed through `UnoGameEngine`, and per-player state updates.
- Game state responses reveal the viewer's own hand only; other players' cards are represented by counts. The server serializes room/game mutations through the in-memory room registry lock.
- `RoomStore` is registered as a singleton. Rooms and access tokens remain process-local and are lost on restart; multi-instance deployment is not supported yet.

## Not yet verified

- The user confirmed the gameplay and UNO-call tests passed locally before the latest rule-preset changes. Rule-preset tests have been added but have not yet been run in this environment.
- Wild Draw Four challenge edge cases, draw-pile exhaustion, and card-conservation behavior need more automated coverage.
- Official initial discard behavior is still temporarily simplified to a number card.
- UNO-call actions are implemented in the engine; real-time transport must enforce the call window across connected clients.
- API room endpoints are implemented but have not yet been built or smoke-tested in this environment. The room registry and tokens are process-local; account authentication, durable storage, room expiry, collision retry, rate limiting, and SignalR are not implemented.
- The user confirmed the full test suite passes after room lifecycle implementation. API-specific integration tests and build/smoke tests remain to be added/run.
- SignalR transport is implemented in the API layer, but client integration, reconnect/state recovery, and persistence are not implemented. SignalR/API behavior still needs a successful build and smoke test.

## Next task

Build and smoke-test the API and SignalR hub (`dotnet build src/backend/Uno.sln`; test room join, per-player state filtering, turn validation, card play/draw, UNO call, and challenge actions). Add API/hub integration tests. Then implement the React client and reconnect/state recovery.
