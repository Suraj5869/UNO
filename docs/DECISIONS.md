# Architecture and Product Decisions

## D-001: Start with Classic
Status: Accepted.
Reason: Build and test a complete core loop before adding variant-specific complexity.

## D-002: Shared engine, separate variant rules
Status: Accepted.
Reason: Avoid rewriting room management and turn infrastructure for every variant.

## D-003: Target 2–10 total participants per room
Status: Accepted as a design target.
Qualification: Humans and bots combined; each variant must validate its supported player range and deck/rules. Actual support must be proven by tests.

## D-004: Server-authoritative game state
Status: Accepted.
Reason: Prevent illegal moves, stale turns, and hidden-card leakage.

## D-005: Proposed stack
Status: Proposed; confirm before scaffolding application code.
- ASP.NET Core Web API + SignalR.
- React + TypeScript + Vite for web.
- React Native + Expo for mobile.
- PostgreSQL for persistence.

## D-006: Keep architecture straightforward
Status: Accepted.
Reason: Prefer simple modular boundaries and dependency injection; avoid adding CQRS/MediatR without a demonstrated need.

## D-007: Portable AI project memory
Status: Accepted.
Reason: Keep source code in GitHub and project context in version-controlled docs, including progress and handoff.

## Open decisions from the 2026-11-18 review

- Define how to return a departing player's cards to play and who resolves an outstanding penalty when the active player or Wild Draw Four offender leaves.
- Confirm the Classic opening-action-card behavior and keep the prototype's turn timer and UNO grace interval explicitly documented.
- See [PROJECT_STATUS.md](../PROJECT_STATUS.md) for evidence and the proposed implementation order.

## Browser test host

Playwright starts a separate executable that composes the same REST API, SignalR hub and game engine and injects a per-game shuffle source. Only that executable interprets fixture selection. The production API uses the default shuffle.

## Number-only finish in both Classic presets

A player can go out only by playing a legal number card. With one card in hand, Skip, Reverse, Draw Two, Wild and Wild Draw Four cannot be played, including onto a House Rules stack. The player can draw on a normal turn or resolve an outstanding penalty using the usual choices.

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


## Batch 11 — social features (verification deferred)
Apply database/migrations/005_social.sql after 004. Authenticated /api/social endpoints support username/public ID search, canonical friendship pairs, incoming acceptance, removal, presence and private lobby invitations. SocialPanel polls every 15 seconds, presence expires after 45 seconds, invitations after five minutes. Sender must own an authenticated seat in a waiting private room with capacity; only accepted friends can be invited. Recipient alone can consume invitation; joining uses existing room API. Guests have no social panel. Pending: push notifications, blocking/reporting, social rate limits, atomic invitation acceptance plus room join, concurrent invite deduplication, expired invitation cleanup, presence privacy controls and dedicated tests. No build, migration or tests executed. Earlier batch limitations remain.


## Batch 12 — audio and visual feedback (verification deferred)
Web Audio synthesizes original effects and optional ambient arpeggios; no external media or backend changes. Sound effects default on, music off, independent volume and motion preferences persist per browser. Audio unlocks on user interaction; music plays only during active matches and pauses in hidden tabs. Existing server events trigger card and UNO cues, snapshot transitions trigger deal/turn/result cues, and a five-second warning is deduplicated per deadline. CSS adds staggered hand entry and special-card bursts without changing table geometry; motion toggle and OS reduced-motion preferences suppress enhanced effects. Existing card flights and victory/UNO overlays retained. Added audio-settings.spec.ts, not executed. Pending: actual per-seat flight trajectories, bot UNO event parity, full dealing choreography, cross-browser audio/visual verification, mobile audio lifecycle and prior batch hardening. No build or tests run; no migration.

## Batch 12.5 recovery — 2026-11-26
Added migration 007_recovery.sql, explicit engine and room checkpoints, runtime registry serialization, PostgreSQL periodic recovery service, startup schema probes, and empty room/unused reservation cleanup. Single instance only. Checkpoint interval one second; abrupt failure can lose unsaved actions/results. Checkpoints contain private hands and bearer tokens; restrict database access. Existing ambiguous room history is not automatically relabeled; verified historical cutoff required. Build, migrations and all tests deferred at user request. Before release verify recovery, reward idempotency, reconnect sequence handling, cleanup, and migration compatibility. Do not mark production ready.
