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
