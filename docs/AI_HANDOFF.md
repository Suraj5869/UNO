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

## Current verified implementation status
Documentation scaffold only. No game code or tests have been implemented.

## Last verified commit
Not yet created.

## Git status / branch
Not yet inspected in a local clone.

## Tests run
None; no application code exists yet.

## Exact next task
Create the GitHub repository, commit/push this scaffold, clone it locally, then confirm installed .NET SDK and Node.js versions before creating the .NET solution and frontend applications.

## Constraints
- Inspect existing code before editing.
- Keep game rules server-authoritative.
- Never expose opponents' hidden cards.
- Bots must use the same legal-action validation path as humans.
- Keep docs and progress truthful.
- Never commit secrets.
