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
