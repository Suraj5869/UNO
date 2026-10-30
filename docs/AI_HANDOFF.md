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
- Engine supports card play, drawing/passing, Skip, Reverse, Draw Two, Wild color selection, and Wild Draw Four accept/challenge in both Official and House Rules.
- UNO call and missed-call penalty actions are implemented; after a player is reduced to one card, a server-enforced three-second grace period lets them call UNO before opponents may act or catch them. If they miss it, catching them draws two cards.
- `UnoRuleSet` supports Official and House presets, defaults new games to Official, and stores the selected preset as read-only game state.
- House Rules support same-type stacking only (Draw Two on Draw Two, Wild Draw Four on Wild Draw Four); mixed stacks are rejected. Draw Two stacks can be accepted for the accumulated penalty. Wild Draw Four can be accepted or challenged in both rule sets; House Rules additionally allow stacking another +4, and a challenge applies to the latest +4 offender.
- `UnoGameRoom` and `UnoRoomPlayer` implement in-memory private-room lifecycle: creator, room code, 2–10 capacity, join, creator-only rule selection and start, selected rule passed to game factory, room-to-engine player ID mapping, and settings/membership locked after start. User reports the complete test suite passes.
- REST API endpoints are implemented in `Uno.Api/Controllers/RoomsController.cs`: `POST /api/rooms`, `GET /api/rooms/{code}`, `POST /api/rooms/{code}/join`, `PUT /api/rooms/{code}/rules`, and `POST /api/rooms/{code}/start`.
- API responses use room DTOs rather than serializing the full game state, avoiding accidental exposure of hands. Creation/join responses return a random room access token; creator actions use that token rather than trusting a player ID from the request.
- GitHub Actions CI passed at commit `249658b016dc4c66bc81d89b63697e68b91771b7`: .NET solution restore/build/test and web npm install/build succeeded. Manual browser-based two-client SignalR tests remain outstanding.
- `RoomStore` is a singleton process-local registry. It is not durable and will not share rooms between backend instances; account authentication, token expiry/revocation, room expiry, and rate limiting remain future work.
- SignalR hub is mapped at `/hubs/uno-room`. Clients call `JoinRoom(code, accessToken)` and listen for `RoomUpdated` and `GameStateUpdated`. Available authoritative actions: `PlayCard(code, token, cardIndex, chosenColor)`, `DrawCard`, `PassAfterDraw`, `AcceptDrawPenalty`, `AcceptWildDrawFour`, `ChallengeWildDrawFour`, `CallUno`, and `CatchUno(code, token, targetRoomPlayerId)`. Each game state is sent to a player-specific SignalR group; opponents' hand contents are omitted.
- REST joining, rule changes, and game start publish lobby updates; starting also publishes each participant's filtered game state. Room operations are serialized by `RoomStore.ExecuteLocked` in this first in-memory implementation.
- User confirmed the existing tests passed locally before the latest UNO-call changes. The newly added UNO-call tests still need a local run.

## Last relevant implementation
The initial React + TypeScript + Vite web client is now in `src/web/`. It supports creating/joining rooms, lobby state and rule selection, SignalR live updates, hand rendering, card play/wild color selection, clickable draw pile, contextual Wild Draw Four accept/challenge and House stacking choices, contextual penalty acceptance and pass-after-draw, UNO call/catch, room-wide `UnoCalled` celebration overlay, and a full-screen winner modal. There is no permanent Your Move action bar. Latest UI changes need CI and two-browser verification. See `docs/SIGNALR.md` for the protocol.

## Branch policy
Work directly on `main`. Do not create feature branches or pull requests unless the user changes this instruction.

## Exact next task
Pull `main` and verify the new client locally: `dotnet build src/backend/Uno.sln`, `dotnet test src/backend/Uno.sln`, then `cd src/web && npm install && npm run build && npm run dev`. Test with two browser sessions, especially private hands and all turn/penalty flows. The client includes SignalR automatic reconnect but reconnect/state recovery is an initial prototype and needs real multi-client testing. Add automated API/SignalR integration tests after local verification.

## Constraints
- Inspect existing code before editing.
- Keep game rules server-authoritative.
- Never expose opponents' hidden cards.
- Bots must use the same legal-action validation path as humans.
- Keep docs and progress truthful.
- Never commit secrets.
