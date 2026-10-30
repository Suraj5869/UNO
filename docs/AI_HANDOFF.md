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
- Engine supports card play, drawing/passing, Skip, Reverse, Draw Two, Wild color selection, and Wild Draw Four accept/challenge in both Official and House Rules. Turns have a 30-second server-enforced deadline; timeout draws one card and skips, or accepts an unresolved penalty and skips.
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


## Latest multiplayer table interaction work
- The top-positioned countdown was replaced with a circular progress ring around the active player's avatar, including the local player's avatar. Every client derives it from the same server-issued deadline; the client timer is visual only.
- The hub emits sequenced room-wide `GameAction` events for card plays and draws. Played-card details are public; draw events deliberately omit card identity. Timeout-driven draws also emit a draw event.
- The web client animates card plays from a player's seat toward the discard pile and card backs from the pile toward the active seat. It ignores duplicate/older action sequence numbers and refreshes authoritative state on reconnect rather than replaying old animations.
- Run backend build/tests and web production build; then smoke-test two browser sessions for synchronized rings, card-play animation, normal draw animation, timeout draw animation, and hidden opponent-hand privacy.

- Shared draw animations also cover accepted draw stacks, Wild Draw Four accept/challenge outcomes, missed-UNO catch penalties, automatic timeout draws, and Official Rules Draw Two's immediate penalty. Verify each recipient is correct and hidden card identities remain private.

- GitHub Actions passed for commit `144b00ad8288839540ef136815509882af2686cc`: backend restore/build/test and web install/build succeeded. The two-browser visual smoke test still needs to be run locally.


## Wild color and rematch behavior
- The game table displays a prominent `ACTIVE COLOR` callout, labelled `WILD COLOR SELECTED` when the top discard is Wild/Wild Draw Four. It is based on server-filtered `GameStateResponse.ActiveColor` and visible to every player.
- Completed games show Play Again and Back to Home. `POST /api/rooms/{code}/play-again` resets the room into a new waiting lobby on first opt-in and promotes that player to creator. Each player remains on their own completed-match screen until they independently choose Play Again or Back to Home; lobby broadcasts must not redirect or replace the completed screen for players who have not opted in. Previous players may opt in while the lobby remains open. Starting the new game closes the opt-in window and revokes tokens for non-opted players.
- After a Wild or Wild Draw Four, the selected colour is shown as a coloured outline/glow on the top discard card only. Do not show a separate always-on active-colour callout.
- Verify with two or more browser sessions: wild color selection updates all clients; first Play Again returns caller to lobby and sends non-opted clients home; another player opting in joins the same lobby; new host can change rules/start; old non-opted tokens cannot access the new game.

- GitHub Actions passed for commit `92da248669d3d9b0b64fe0f7b535fd1e3e33175e` (backend restore/build/test and web install/build). Manual multi-browser rematch and wild-color visual verification remains outstanding.
