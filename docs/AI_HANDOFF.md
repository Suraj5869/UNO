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
Pull the latest `main` and run the backend build/tests, web production build, and all Playwright suites serially:

```powershell
dotnet build src/backend/Uno.sln
dotnet test src/backend/Uno.sln
cd src/web
npm install
npm run build
npx playwright test --workers=1 --reporter=list
```

The latest browser additions cover UNO call/catch, full match completion and rematch opt-in, reconnect after an active-player timeout, House Rules Draw Two penalty timeout, non-host authorization, creator departure/host promotion, late-join rejection, and invalid room capacity. These additions are committed but have not yet been verified locally or in GitHub Actions. Fix observed failures without weakening assertions. Remaining planned coverage includes hidden-hand privacy verification, draw-pile exhaustion/card conservation, concurrent actions, and desktop/mobile viewport checks. Continue committing directly to `main`; do not create a feature branch.
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
- After a Wild or Wild Draw Four, the top discard card displays the selected color through a matching border and glow; ordinary cards have no added color indicator.
- Completed games show Play Again and Back to Home. `POST /api/rooms/{code}/play-again` resets the room into a new waiting lobby on first opt-in and promotes that player to creator. Each player remains on their own completed-match screen until they independently choose Play Again or Back to Home; lobby broadcasts must not redirect or replace the completed screen for players who have not opted in. Previous players may opt in while the lobby remains open. Starting the new game closes the opt-in window and revokes tokens for non-opted players.
- After a Wild or Wild Draw Four, the selected colour is shown as a coloured outline/glow on the top discard card only. Do not show a separate always-on active-colour callout.
- Verify with two or more browser sessions: wild color selection updates all clients; first Play Again returns caller to lobby and sends non-opted clients home; another player opting in joins the same lobby; new host can change rules/start; old non-opted tokens cannot access the new game.

- GitHub Actions passed for commit `92da248669d3d9b0b64fe0f7b535fd1e3e33175e` (backend restore/build/test and web install/build). Manual multi-browser rematch and wild-color visual verification remains outstanding.


## Responsive UI follow-up
- Notices auto-dismiss after 3.6 seconds; errors after 6.5 seconds.
- Desktop uses compact opponent seats; smaller screens use a horizontally scrollable opponent strip and hand, retaining card tap size. Test 10-player layouts at desktop/mobile viewports.
- Responsive/toast changes are committed; CI and live browser/device verification remain pending.


## Latest E2E work (2026-11-13)

- UNO call/catch test setup now preserves the live match while searching for a player who can move from two cards to one. It never plays a final card during setup and only plays a drawn number card after checking its legality.
- Added `match-rematch.spec.ts` for natural match completion, winner overlay synchronization, independent Play Again opt-in, lobby host promotion, and starting a fresh match.
- Added `realtime-lifecycle.spec.ts` for disconnected-player timeout/reconnect state refresh and automatic resolution of a timed-out House Rules Draw Two penalty.
- Run focused suites with `npm run test:e2e:uno`, `npm run test:e2e:rematch`, and `npm run test:e2e:lifecycle`; run all browser tests with `npx playwright test --workers=1 --reporter=list`.
- These tests have been committed but have not yet been run against the local environment or confirmed green in CI. Keep this status explicit.
- Continue working directly on `main`; do not create a feature branch.


- Added `room-permissions.spec.ts` and `npm run test:e2e:rooms` for host-only rule/start authorization, creator departure and host promotion, post-start join rejection, and invalid room capacity. Run this suite with the other focused tests before treating the E2E rollout as green.

## Review handoff on 2026-11-18

Read [PROJECT_STATUS.md](../PROJECT_STATUS.md) for the current baseline and proposed implementation order. Wait for implementation-plan approval before changing application code. Work from the local checkout and commit each completed task locally and get explicit authorization before pushing. Use isolated Playwright output to preserve the existing tracked failure artifacts.

## Verified test implementation on 2026-11-18

Both builds passed, all 77 backend cases passed and all 28 Playwright scenarios passed serially with zero retries in 3.7 minutes. See [PROJECT_STATUS.md](../PROJECT_STATUS.md) and [E2E_TEST_PLAN.md](E2E_TEST_PLAN.md) for the test host, commands, coverage and remaining gameplay defects. The subsequent number-only finish rule addresses the final-card Draw Two issue.

## Number-only finish

Both Classic presets reject a last-card action before applying its effects. The client disables that card and explains the rule. Remaining gameplay follow-up includes reverse-direction departures and turn-deadline enforcement.

## Turn handling on departure

When the active player leaves, the turn moves to the next remaining player in the current direction and starts a new deadline. An inactive player leaving preserves the active player's deadline and their play-or-pass decision after a draw.


## P0 gameplay stabilization — implemented, verification pending

Direction-aware departures return hands to the shuffled draw pile, preserve the existing active turn when another seat leaves, and transfer outstanding penalties to the next seat when the active player leaves. Challenges are disabled if the offender leaves or becomes the penalty recipient. A remaining single player completes the match. Turn actions are rejected at or after the server deadline; timeout after drawing passes without another draw. The next timeout deadline is anchored to the supplied timeout tick. Added 17 engine regression cases. Backend and browser suites must be run before declaring this verified. Realtime snapshot ordering remains P1.


### P1 multiplayer reliability (2026-11-18)
Opening discard remains a number card by product decision; first-turn action cards follow normal matching and effects. Snapshot capture sequences reject older room/game snapshots in the web client; action sequences are assigned under the room lock. Draw animations use actual drawn counts, including exhausted piles and timeout after drawing. Added 12 engine regression cases. Local compile and full regression verification remain pending, including concurrent snapshot delivery and rematch browser coverage.


### Playful Premium UI — initial dashboard and quick match
Approved Canva reference: https://canva.link/yxiflvfeair7wz5. Dashboard puts Play Match first, Play with Friends second (private create/join forms nested), bots third (not implemented). Initial quick match uses two players and Classic Official rules, process-local room registry and automatic start. Existing card art retained. Added dashboard-matchmaking.spec.ts; no builds or tests executed here. Existing browser setup helpers need navigation through Play with Friends before selecting room controls. Remaining: full screen redesign, auth, bots, public queue lifecycle hardening (cancellation/disconnect, idempotency, expiry), regression verification.


### PostgreSQL foundation — 2026-11-18
Supabase PostgreSQL dedicated UNO project; Dapper/Npgsql, optional configured singleton data source, GET /api/health/database (200 connected, 503 not_configured/unavailable). Migration database/migrations/001_initial_schema.sql creates 15 backend-only tables with RLS and revoked client grants. Apply once via Supabase SQL Editor. Never commit connection secrets. UserSecretsId is now uno-api-local-development; if previously initialized locally, re-set ConnectionStrings:DefaultConnection after pulling to use this stable ID. Existing in-memory gameplay unchanged. No database migration, restore/build or tests executed by agent. Next: verify connection and schema, then transactional registration/profile/progress initialization and authentication. Persistence of live rooms and matches is not implemented yet.


### Account authentication — 2026-11-18
Implemented register/login/refresh/logout/me in AuthController, JWT HS256 with 15 minute access token and rotating 30 day hashed refresh token, PasswordHasher, transactional profile/progress/statistics/default inventory setup, and IP fixed-window rate limit. Apply database/migrations/003_default_cosmetics.sql before registration. Set Jwt:SigningKey via user secrets (random >=32 bytes); preserved effective existing UserSecretsId 87775d58-e96c-4242-9d71-37ef9d4d7204. Without JWT configuration existing room gameplay remains available; auth issuance unavailable. Existing room access tokens remain unchanged; account-bound rooms, guest accounts, UI, email verification, reset, refresh-family reuse detection, integration tests and production hardening pending. Logout revokes submitted refresh token only; access token remains valid until expiry. Build and database tests not executed by agent. Verify register, duplicate registration, login, me, refresh, replay rejection, logout, and regression tests locally.
