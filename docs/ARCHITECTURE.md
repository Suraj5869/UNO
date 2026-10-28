# Architecture

## Proposed stack
- Backend: ASP.NET Core Web API (C#) with SignalR for real-time events.
- Web: React + TypeScript + Vite.
- Mobile: React Native + Expo.
- Database: PostgreSQL.
- Hosting: choose after prototype and load/cost requirements are understood.

## High-level components
1. API/authentication: account and non-real-time endpoints.
2. SignalR gateway: authenticated real-time connections and room events.
3. Room manager: room creation, membership, seat reservations, capacity checks.
4. Game coordinator: serializes actions for each active match.
5. Shared game engine: turn flow, common state transitions, action pipeline.
6. Variant rules: deck definitions, legal actions, card effects, win conditions.
7. Bot decision provider: chooses from legal actions; never bypasses validation.
8. Persistence: profiles, room metadata as appropriate, match results, and eventually recoverable game snapshots/event history.

## Authoritative state
The server owns the actual game state. Clients send intentions/actions; the server validates and applies them, then broadcasts each participant's filtered view. Never send opponents' card identities or draw-pile order.

## Player capacity
The shared room infrastructure targets 2–10 total participants, humans plus bots. Enforce capacity on the server. Each variant supplies its supported player range and validates its deck and rules at the selected count. Do not assume every variant can support every count without testing.

## Rule architecture
Use a common game-engine pipeline with a variant rules interface/contract. A variant defines its deck, initial setup, legal actions, action effects, turn/penalty behavior, and terminal conditions. Keep variant-specific state explicit. Avoid a single giant service full of `if variant == ...` branches.

## Concurrency
Actions for a given match must be processed sequentially or with equivalent concurrency control. Reject stale turn/action identifiers and duplicate requests where applicable. Never rely on client-side validation.

## State storage
For the first local prototype, in-memory active state is acceptable. Before production or multi-instance hosting, implement snapshots/event persistence or another recovery mechanism, and design for single-writer ownership/distributed coordination. PostgreSQL alone is not automatically a real-time coordination solution.

## Testing
Test rules independently of HTTP/SignalR. Add integration tests for room membership, broadcasts, reconnects, concurrent actions, and player-specific views. Load-test the actual hosting plan before setting a concurrent-user target.
