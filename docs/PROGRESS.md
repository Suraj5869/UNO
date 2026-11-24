# Progress Tracker

Last updated: 2026-11-10

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
- UNO-call actions are implemented in the engine and exposed through the SignalR hub; multi-client Playwright scenarios have now been added for successful calls, late-call rejection, grace-period gating, missed-call catch, and the two-card penalty. A server-side deadline check and engine regression test now reject calls after the three-second window. The new browser suite still needs a local run.
- API room endpoints and SignalR hub now compile in CI and the engine test suite passes. Manual two-client SignalR smoke tests are still outstanding. The room registry and tokens are process-local; account authentication, durable storage, room expiry, collision retry, and rate limiting are not implemented.
- The full solution test suite passes in GitHub Actions. Dedicated API/SignalR integration tests and manual multi-client smoke tests remain to be added/run.
- First React + TypeScript + Vite web client added under `src/web/`: room creation/join, host rule selection and start, SignalR lobby updates/reconnect, playable card hand, wild-color selection, clickable draw pile, contextual accept/challenge/stack/pass choices (no permanent Your Move action bar), UNO call and catch, room-wide UNO-call celebration overlay, and a full-screen winner modal. The latest contextual-action changes are committed; CI and browser testing are still required.

## Next task

Pull the latest `main`; run the .NET build and unit tests, web production build, and the complete Playwright suite serially. Resolve actual failures without relaxing gameplay assertions. After the new match/rematch, reconnect, and timeout scenarios are verified, continue with the remaining coverage gaps: host permissions and departure, direct API authorization and hidden-hand privacy, draw-pile exhaustion/card conservation, concurrent actions, and desktop/mobile viewport checks. Persistence and production CORS origins remain required before deployment.

## Latest UI/realtime improvement

- Replaced the top-positioned turn clock with a circular countdown ring around the active player's avatar, visible to all players including the active player. The warning state begins in the final 10 seconds; server timeout enforcement is unchanged.
- Added sequenced room-wide `GameAction` events for public card plays, normal draws, and automatic timeout draws. Played-card details are public; draw events contain only player ID and draw count, never hidden card identities.
- The web client ignores duplicate/older action sequence numbers and renders transient card-flight animations independently of authoritative game-state updates.
- GitHub Actions CI passed for commit `144b00ad8288839540ef136815509882af2686cc` (backend restore/build/test and web dependency install/build). Manual two-browser visual verification remains pending.

- Extended shared draw animations to accepted draw stacks, accepted Wild Draw Four penalties, Wild Draw Four challenge outcomes (targeting the actual recipient), missed-UNO catch penalties, and Official Rules Draw Two's automatic two-card penalty. Draw-card identities remain private.


## Wild color clarity and rematch flow

- Completed games offer independent Play Again and Back to Home choices. The first player to opt in creates a fresh lobby and becomes its host; other players stay on their completed-match screen until they choose for themselves. Previous players can opt in while the lobby is open. The new lobby can be configured and started normally.
- Added REST `POST /api/rooms/{code}/play-again`; ordinary room updates keep non-opted clients on their completed-match screen. Stale non-opted player tokens are revoked when the new game starts.
- After a Wild or Wild Draw Four, the chosen colour is communicated through a matching coloured outline and glow on the top discard card. No active-colour callout is shown for normal cards.
- Added engine room tests for rematch reset, host promotion, returning participants, and invalid rematch timing. GitHub Actions passed for commit `92da248669d3d9b0b64fe0f7b535fd1e3e33175e` (backend restore/build/test and web install/build). Manual multi-browser rematch verification remains pending.


## Responsive layout and notification follow-up

- Responsive game-table and toast changes are committed; CI and browser/device verification are pending.
- Verify 10-player desktop and mobile layouts, readable opponent identity/card counts, hand scrolling/tap targets, and automatic toast dismissal.


## Explicit player leave handling

- The web Leave room / Back to Home action invokes the SignalR `LeaveRoom` method before disconnecting.
- The server removes the participant from room membership, revokes their room token, and broadcasts the updated room and remaining player game states.
- If a player leaves during an active game, their engine seat is removed. If they were the active player, the turn advances to the next remaining player. UNO-call state is cleared when the departing player owns it. If fewer than two players remain, the game is marked completed.
- Added room tests for current-player departure and creator promotion. CI status and live multiplayer verification remain pending.


## Desktop circular seating

- Desktop (1000px and wider) opponent seats are positioned around the felt perimeter rather than in a grid. Draw/discard remain centered and the local player's hand remains anchored along the bottom.
- Mobile/tablet horizontal opponent strip is unchanged. Validate with 2, 4, 6, and 10 players at wide and short desktop viewports.


## Wild color picker dismissal

- The pending Wild/Wild Draw Four color picker is cleared whenever the local player performs another game action and whenever it is no longer their turn. This prevents a stale color picker from remaining visible after playing another card, drawing, or resolving a turn.

## Automated E2E testing rollout

- Added a Playwright multiplayer capacity scenario using ten independent browser contexts, an eleventh-player full-room rejection, match start, per-player seven-card hand checks, and current-player departure with state verification on all remaining clients.
- Added `npm run test:e2e:multiplayer` for the focused scenario.
- Playwright now retains a named test-results directory and an HTML report in CI; GitHub Actions uploads diagnostics after every E2E run.
- The new scenario has been committed but has not yet been executed locally or confirmed green in GitHub Actions. Do not treat it as passing until CI reports success.
- See [E2E_TEST_PLAN.md](E2E_TEST_PLAN.md) for the coverage matrix and rollout order.

## Special-card browser E2E rollout

- Added `special-card-actions.spec.ts` with browser scenarios for Skip, Reverse, Wild color selection, Wild Draw Four acceptance and challenge outcome, and House Rules Draw Two penalty resolution with same-type stacking when the next player has a +2.
- Added `npm run test:e2e:special` for the focused suite.
- These scenarios use independent browser contexts for three players and exercise the actual REST/SignalR/game-engine path. They have been committed but are not yet run locally or verified in CI.
- The House Rules stacking scenario currently checks stacking when the shuffled hand permits it; deterministic test-only dealing is still required to guarantee stack coverage and both Wild Draw Four challenge branches every run. UNO call/catch, full match completion/rematch, reconnect, and timeout browser scenarios remain next.


## UNO call/catch browser E2E

- Added uno-call-actions.spec.ts with three independent browser contexts.
- The fixture advances a real shuffled match using legal number-card plays and draws until a player can move from two cards to one; it does not seed or inspect opponents' hands.
- Successful-call coverage verifies the caller can call within the three-second grace period and all clients receive the UNO celebration.
- Missed-call coverage verifies the catch action is hidden during the grace period, a different non-current player can catch afterward, the target draws two cards, and the active turn does not change.
- Added npm run test:e2e:uno. The suite is committed but not yet executed; do not mark it passing until the local Playwright run confirms it.


## Remaining browser E2E implementation

- Fixed the UNO call/catch fixture so it never plays a player's final card while searching for the two-to-one-card transition. It also validates that a drawn number card is actually legal before playing it, and fails explicitly if the match completes unexpectedly.
- Added a real-match completion and rematch browser scenario. It verifies both clients see the winner overlay, the first opt-in creates a one-player rematch lobby, non-opted clients stay on the completed screen, the second opt-in joins the same lobby, and the new host can start the rematch.
- Added reconnect and timeout browser scenarios: a disconnected active player is timed out by the server and receives the refreshed state after reconnect; a House Rules Draw Two recipient who times out draws the full penalty and loses the turn.
- Added focused scripts: `npm run test:e2e:rematch` and `npm run test:e2e:lifecycle`.
- None of these new scenarios should be considered passing until the local Playwright suite or GitHub Actions provides a green result. Run the complete suite serially from `src/web` with `npx playwright test --workers=1 --reporter=list`.


## Room authorization and lifecycle E2E additions

- Added `room-permissions.spec.ts`: verifies disabled non-host controls and server-side rejection of non-host rule-change/start requests; verifies creator departure promotes the next participant; verifies a promoted host can start after a new player joins; rejects late joins after match start; and checks the API rejects capacity 11.
- Added `npm run test:e2e:rooms`. This suite is committed but not yet verified by a local run or GitHub Actions.


## Remaining E2E verification and coverage gaps

- The new room-permissions suite also verifies host-only rule/start operations, creator departure and promotion, rejection of late joins, and invalid capacity.
- The match/rematch suite now explicitly asserts that the first player to opt in is shown as HOST to both clients.
- Remaining planned automated/manual coverage after these additions: hidden-hand privacy verification at the realtime/API boundary, draw-pile exhaustion and card-conservation invariants, concurrent action races, and responsive desktop/mobile viewports.
- All recently added browser scenarios remain unverified until the user runs the full suite or GitHub Actions reports a successful E2E job.

## Local review on 2026-11-18

See [PROJECT_STATUS.md](../PROJECT_STATUS.md) for the current local verification results, remaining features and multiplayer findings. Both builds passed and all 66 backend unit tests passed at local revision `c9f1cb6`. The review found departure and deadline edge cases that need regression coverage.

## Verified test implementation on 2026-11-18

Both builds passed, all 77 backend cases passed and all 28 Playwright scenarios passed serially with zero retries in 3.7 minutes. See [PROJECT_STATUS.md](../PROJECT_STATUS.md) and [E2E_TEST_PLAN.md](E2E_TEST_PLAN.md) for the test host, commands, coverage and remaining gameplay defects. The subsequent number-only finish rule addresses the final-card Draw Two issue.

## Number-only finish

Both Classic presets reject a last-card action before applying its effects. The client disables that card and explains the rule. Remaining gameplay follow-up includes reverse-direction departures and turn-deadline enforcement.

## Turn handling on departure

When the active player leaves, the turn moves to the next remaining player in the current direction and starts a new deadline. An inactive player leaving preserves the active player's deadline and their play-or-pass decision after a draw.
Local verification passed all 95 backend tests and all 29 Chromium scenarios. Playwright ran serially with zero retries in 3.9 minutes.


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


### Phase 3 frontend authentication
AuthGate.tsx implements login/register, validation, sessionStorage session, /me restoration, proactive refresh rotation, logout and explicit temporary guest mode. App.tsx gates dashboard and uses account profile display name and stats; styles.css adds responsive indigo theme. No DB change. Guest mode is existing anonymous room gameplay, not persistent guest account. Room endpoints still use display name and room access tokens, not JWT identity; no XP awards yet. Browser session storage is a temporary token-storage choice; production HttpOnly refresh cookie and XSS hardening pending. Build/browser tests not run; existing Playwright tests need guest entry and friends navigation updates. Profile/collections/friends dedicated pages and exact Canva parity not verified. Next verify frontend, update E2E, then bind room membership to account identity and persist rooms/matches.


## Batch 7 — progression implementation (verification deferred)
Account JWT binds seats on create/join/matchmaking; guest seats remain anonymous. Unique match IDs and accepted card-play counts drive natural-finish capture under the room lock. Matches with departures are excluded. A background worker persists completed rooms, matches, participants, statistics and XP atomically using existing schema 001; duplicate match IDs prevent repeat rewards. Initial tuning: 30 participation XP + 50 winner XP, minimum seven total accepted card plays, monthly UTC seasons, lifetime threshold 50 × level × (level − 1). Profile progression/history and victory reward polling use authenticated APIs. No migration beyond existing account schema is required. No build or tests run in this implementation round. Pending: database integration verification, durable outbox/restart recovery, stronger collusion and multi-account farming controls, per-player activity thresholds, and deferred Batch 6.5 regression. Do not treat this as production-ready. Next feature batch after user acceptance: cosmetics and inventory.


## Batch 8 — cosmetic inventory (verification deferred)
Apply database/migrations/004_cosmetic_rewards.sql after 001–003 before running updated API. Adds lifetime and seasonal unlock thresholds, nine palette cosmetics and lifetime backfill. MatchResultWriter grants eligible items atomically with XP; seasonal items remain owned. PUT /api/auth/collection/equip validates active ownership and updates one loadout per category; avatar key also updates profile. Profile exposes equipped card back and table theme. Web collection displays requirements, previews and equip actions; local table/deck/own avatar skins preserve card faces and layout. Opponent-specific avatar synchronization, historical seasonal backfill, new-unlock celebration and dedicated automated tests remain pending. No builds, migrations or tests executed. Batch 7 persistence limitations remain.


## Batch 9 — bot practice (verification deferred)
Server-owned bot seats have no access tokens. POST /api/rooms/practice creates and starts a 2–10 seat practice match; POST /api/rooms/{code}/bots allows the host to add bots before start. BotTurnService uses the room lock and existing engine, paced at roughly 900 ms per action, with random Easy, color-frequency Medium and threat-aware Hard policies. Bots use own hand and public opponent counts, automatically call UNO, accept penalties, and persist across rematches. All bot-containing matches are excluded from persistent progression. Dashboard and lobby have count/difficulty controls. No new migration. No build or tests executed; dedicated bot tests, challenge strategy, bot removal controls, disconnect expiry and animation parity remain pending. Verify bot-only cleanup against leave hub publishing and full room lifecycle before release. Batch 8 migration remains required.


## Batch 10 — public matchmaking (verification deferred)
Public waiting rooms are compatible queues keyed by exact capacity (2–10) and Official/House rules. Existing POST /api/rooms/matchmaking defaults to two Official seats for compatibility. Registry lock serializes admission, full-room automatic start and token-authorized POST /api/rooms/{code}/cancel-search. Public waiting rooms reject direct joins, manual starts, rule changes and bots. Registered accounts cannot occupy multiple waiting queues; anonymous duplicate detection is not implemented. Dashboard selects capacity/rules and displays live seat count via existing SignalR room updates. Cancellation works via HTTP without SignalR. Added public-matchmaking.spec.ts for rule isolation, capacity and cancellation; not executed. No migration. Pending: stale queue expiry/heartbeat, distributed queue persistence, account retry recovery, guest deduplication, public rematch lifecycle verification and full build/regression. No skill-based matching or bot fallback. Existing Batch 7–9 limitations remain.
