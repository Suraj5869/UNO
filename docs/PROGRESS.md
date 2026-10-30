# Progress Tracker

Last updated: 2026-10-30

## Current phase

Phase 4 — Real-time multiplayer backend foundation.

## Confirmed product decisions

- Web and mobile clients are planned.
- Classic UNO first; variants follow after the base engine is reliable.
- Shared architecture should support multiple variants.
- Target capacity is 2–10 total participants (humans plus bots); this is a design target, not a load-test result.
- Bots and online multiplayer are planned.
- Server-authoritative rules and filtered per-player state are required.
- Private rooms will let the creator choose Official Rules or House Rules before the game starts. Rules are locked once play begins.
- Wild Draw Four can be challenged in both Official and House Rules. Official Rules do not permit draw stacking; House Rules permit same-type Draw Two and Wild Draw Four stacking only; mixed stacking is rejected.
- Portable documentation is required so work can continue across chats and AI tools.

## Implemented in the repository

- Standard 108-card Classic UNO deck creation and shuffle.
- Game factory for 2–10 players, seven cards per player, and card-conservation checks.
- Game state and player models.
- Gameplay engine methods for playing cards, drawing, passing after a playable draw, Skip, Reverse, Draw Two, Wild color selection, and Wild Draw Four accept/challenge flows.
- Tests covering core card play, invalid moves, current-player enforcement, two-player Reverse, Draw Two, and Wild Draw Four challenge setup/acceptance.
- UNO call and missed-call engine actions: a player reduced to one card gets a server-enforced three-second grace period to call UNO; other players cannot act or catch them during this period. If they do not call, a participant can catch the missed call for a two-card penalty.
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

- GitHub Actions CI passed for commit `249658b016dc4c66bc81d89b63697e68b91771b7`: backend restore/build/test and web dependency install/build all succeeded. Manual browser-based multiplayer smoke tests have not been performed.
- Wild Draw Four challenge success/failure is now covered in both rule sets; draw-pile exhaustion and card-conservation behavior still need more automated coverage.
- Server-enforced 30-second turn timeout is implemented with a live countdown. Normal timeout draws one card and skips; unresolved penalty timeout accepts the penalty and skips.
- Official initial discard behavior is still temporarily simplified to a number card.
- UNO-call actions are implemented in the engine and exposed through the SignalR hub; end-to-end multi-client call-window behavior still needs testing.
- API room endpoints and SignalR hub now compile in CI and the engine test suite passes. Manual two-client SignalR smoke tests are still outstanding. The room registry and tokens are process-local; account authentication, durable storage, room expiry, collision retry, and rate limiting are not implemented.
- The full solution test suite passes in GitHub Actions. Dedicated API/SignalR integration tests and manual multi-client smoke tests remain to be added/run.
- First React + TypeScript + Vite web client added under `src/web/`: room creation/join, host rule selection and start, SignalR lobby updates/reconnect, playable card hand, wild-color selection, clickable draw pile, contextual accept/challenge/stack/pass choices (no permanent Your Move action bar), UNO call and catch, room-wide UNO-call celebration overlay, and a full-screen winner modal. The latest contextual-action changes are committed; CI and browser testing are still required.

## Next task

Pull `main`; run `dotnet build src/backend/Uno.sln`, `dotnet test src/backend/Uno.sln`, then `cd src/web && npm install && npm run build && npm run dev`. Test two browsers joining one room, privacy of each hand, all action flows, and reconnect behavior. Fix compile/runtime issues found locally before adding automated API/SignalR integration tests. Persistence and production CORS origins remain required before deployment.
