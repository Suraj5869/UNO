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
- `UnoGameRoom` and `UnoRoomPlayer` implement in-memory private-room lifecycle: creator, room code, 2–10 capacity, join, creator-only rule selection and start, selected rule passed to game factory, room-to-engine player ID mapping, and settings/membership locked after start. New room tests have been added but not yet run.
- User confirmed the existing tests passed locally before the latest UNO-call changes. The newly added UNO-call tests still need a local run.

## Last relevant commit
`52fc52553ea2c154e598b9f4ab5ab5200c197213` — add tests for UNO-call behavior.

## Branch policy
Work directly on `main`. Do not create feature branches or pull requests unless the user changes this instruction.

## Exact next task
Run `dotnet test src/backend/Uno.sln` on `main` to verify room lifecycle and all existing tests. If green, add API endpoints for creating and joining rooms and selecting rules, then add SignalR for live room/game updates.

## Constraints
- Inspect existing code before editing.
- Keep game rules server-authoritative.
- Never expose opponents' hidden cards.
- Bots must use the same legal-action validation path as humans.
- Keep docs and progress truthful.
- Never commit secrets.
