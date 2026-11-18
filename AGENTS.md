# Shared AI / coding-agent instructions

## Before changing code
1. Read `README.md` and relevant files in `docs/`.
2. Inspect the actual repository, Git status, recent commits, and relevant tests.
3. Treat code and tests as the evidence of what is implemented; documentation may be stale.
4. Identify the smallest useful task and avoid unrelated rewrites.

## Project invariants
- Target 2–10 total participants per room in the shared room/game infrastructure. Each variant may impose a stricter limit.
- Classic UNO is the first implementation target.
- All game actions must be validated by the authoritative server.
- Never send hidden opponent hands or draw-pile order to clients.
- Humans and bots must submit actions through the same validation path.
- Game variants should plug into a common engine through explicit rule/deck definitions, not scattered variant-name conditionals.
- Do not add CQRS/MediatR or other complexity without a documented need.
- Do not claim tests passed unless they were actually run and passed.
- Do not silently invent rules where official rules or product decisions are needed; record unresolved questions.

## Workflow
- Make focused changes and add tests for rule behavior.
- Update `docs/PROGRESS.md`, `docs/DECISIONS.md`, and `docs/AI_HANDOFF.md` when meaningful status/decision changes occur.
- Before handoff, report changed files, commands/tests actually run, outcomes, known issues, and the next task.
- Never commit secrets. Review `git diff` before committing.


## Architecture and conventions

- `src/backend/Uno.GameEngine` owns cards, decks, rule presets, state transitions and room membership. Keep it independent of ASP.NET, SignalR and the UI.
- `src/backend/Uno.Api` exposes REST under `/api/rooms` and SignalR at `/hubs/uno-room`. Resolve identity from room access tokens and serialize authoritative mutations. Publish filtered DTOs to the intended audience.
- `src/web` uses React, TypeScript and Vite. Treat server snapshots as authoritative. Keep UI feedback, animation and countdowns separate from game-state validation.
- Follow existing C# naming and xUnit `Action_Condition_Outcome` names. Use typed DTOs and descriptive TypeScript names. Add regression tests at the engine or network boundary that owns the behavior.
- Use deterministic setups for rule and timing tests. Use independent browser contexts for multiplayer. Keep run artifacts out of tracked output paths.
- Use the local working tree for approved implementation. Commit each completed task locally with a descriptive message. Push only with explicit user authorization.

## Development and testing

Prerequisites are .NET 10 and Node.js 22 or newer. From the repository root:

```powershell
dotnet build src/backend/Uno.sln
dotnet test src/backend/Uno.sln --no-build
dotnet run --project src/backend/Uno.Api
```

From `src/web`:

```powershell
npm.cmd ci
npm.cmd run build
npm.cmd run dev
```

Stop manually launched dev servers before a clean E2E run. Playwright launches the browser-test API host and Vite on ports 7137 and 5173. Both ports must be free. Only the test host accepts the X-Test-Deal header. From `src/web`:

```powershell
npx.cmd playwright install chromium
npx.cmd playwright test --workers=1 --reporter=list --output "$env:TEMP/uno-playwright-$PID"
```

Use a focused spec under `src/web/e2e` for targeted browser verification. Run the full one-worker suite after shared changes to room lifecycle, SignalR state or core rules. See `PROJECT_STATUS.md` for the latest audit baseline.
