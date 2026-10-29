# AI Handoff

## Project
Cross-platform multiplayer card-game platform, beginning with Classic UNO-style gameplay.

## Read first
1. `AGENTS.md`
2. `README.md`
3. `docs/PROJECT_OVERVIEW.md`
4. `docs/ARCHITECTURE.md`
5. `docs/GAME_RULES.md`
6. `docs/PROGRESS.md`
7. `docs/DECISIONS.md`

## Key requirement
Shared room/game infrastructure targets 2–10 total participants, humans plus bots. Each variant must validate its own supported player count and deck.

## Current implementation status
- Classic 108-card deck, game factory, state model, and gameplay engine are implemented.
- Engine supports card play, drawing/passing, Skip, Reverse, Draw Two, Wild color selection, and Wild Draw Four accept/challenge.
- UNO call and missed-call penalty actions are implemented; a missed call draws two cards if caught before the next player begins an action.
- `UnoRuleSet` supports Official and House presets, defaults new games to Official, and stores the selected preset as read-only game state.
- House Rules support same-type stacking only (Draw Two on Draw Two, Wild Draw Four on Wild Draw Four); mixed stacks are rejected. Draw Two stacks can be accepted for the accumulated penalty. A stacked Wild Draw Four retains challenge handling for the latest card; success penalizes the latest offender with the accumulated amount, failure penalizes the challenger with the accumulated amount plus two.
- `UnoGameRoom` and `UnoRoomPlayer` implement in-memory private-room lifecycle: creator, room code, 2–10 capacity, join, creator-only rule selection and start, selected rule passed to game factory, room-to-engine player ID mapping, and settings/membership locked after start. User reports the complete test suite passes.
- REST API endpoints are implemented in `Uno.Api/Controllers/RoomsController.cs`: `POST /api/rooms`, `GET /api/rooms/{code}`, `POST /api/rooms/{code}/join`, `PUT /api/rooms/{code}/rules`, and `POST /api/rooms/{code}/start`.
- API responses use room DTOs rather than serializing the full game state, avoiding accidental exposure of hands. Creation/join responses return a random room access token; creator actions use that token rather than trusting a player ID from the request.
- `RoomStore` is a singleton process-local registry. It is not durable and will not share rooms between backend instances; account authentication, token expiry/revocation, room expiry, and rate limiting remain future work. API and SignalR changes have not yet been built or smoke-tested in this environment.
- SignalR hub is mapped at `/hubs/uno-room`. Clients call `JoinRoom(code, accessToken)` and listen for `RoomUpdated` and `GameStateUpdated`. Available authoritative actions: `PlayCard(code, token, cardIndex, chosenColor)`, `DrawCard`, `PassAfterDraw`, `AcceptDrawPenalty`, `AcceptWildDrawFour`, `ChallengeWildDrawFour`, `CallUno`, and `CatchUno(code, token, targetRoomPlayerId)`. Each game state is sent to a player-specific SignalR group; opponents' hand contents are omitted.
- REST joining, rule changes, and game start publish lobby updates; starting also publishes each participant's filtered game state. Room operations are serialized by `RoomStore.ExecuteLocked` in this first in-memory implementation.
- User confirmed the existing tests passed locally before the latest UNO-call changes. The newly added UNO-call tests still need a local run.

## Last relevant implementation
Room API endpoints and token-based creator authorization were committed directly to `main` in the latest API milestone.

## Branch policy
Work directly on `main`. Do not create feature branches or pull requests unless the user changes this instruction.

## Exact next task
Run `dotnet build src/backend/Uno.sln` and smoke-test REST and SignalR. Verify waiting-room joins, player-specific hand privacy, turn enforcement, card actions, UNO calls, and draw penalties. Add integration tests, then build the React client and add reconnect/state recovery.

## Constraints
- Inspect existing code before editing.
- Keep game rules server-authoritative.
- Never expose opponents' hidden cards.
- Bots must use the same legal-action validation path as humans.
- Keep docs and progress truthful.
- Never commit secrets.
