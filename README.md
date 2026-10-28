# UNO Game Platform

A cross-platform multiplayer card-game platform, beginning with Classic UNO and designed to support multiple rule variants.

## Product goals
- Responsive web client and Android/iOS clients.
- Private rooms, public matchmaking, human players, and bots.
- Server-authoritative gameplay over real-time connections.
- Configurable rooms supporting 2–10 total participants, subject to each variant's validated rules.
- Start with Classic UNO; add other variants only after the core engine is tested.
- Portable documentation so development can continue across ChatGPT, Claude, Gemini, or other coding tools.

## Planned stack
- Backend: ASP.NET Core Web API, C#, SignalR.
- Web: React + TypeScript + Vite.
- Mobile: React Native + Expo.
- Persistence: PostgreSQL.
- Initial active-game state: in-memory only for local prototype; before production, add recovery/persistence and multi-instance coordination as needed.

## Status
Planning/scaffold stage. No gameplay implementation has been verified yet. See `docs/PROGRESS.md`.

## Start here
1. Read `AGENTS.md`.
2. Read `docs/PROJECT_OVERVIEW.md`, `docs/ARCHITECTURE.md`, and `docs/GAME_RULES.md`.
3. Check `docs/PROGRESS.md` and `docs/AI_HANDOFF.md` before making changes.
4. Keep `docs/PROGRESS.md` accurate and record significant decisions in `docs/DECISIONS.md`.

## Repository layout
- `src/backend/` — ASP.NET Core backend (planned)
- `src/web/` — React web client (planned)
- `src/mobile/` — React Native mobile client (planned)
- `src/shared/` — shared contracts/specification assets (planned; avoid assuming C# and TypeScript can share runtime code directly)
- `tests/` — automated tests
- `docs/` — product, architecture, rules, and handoff documentation

## Security
Never commit secrets, production credentials, tokens, private keys, or real user data. Use environment variables and ignored local configuration files.
