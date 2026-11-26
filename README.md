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
The Classic UNO deck, game factory, gameplay engine, Wild Draw Four challenge flow, and UNO-call actions are implemented. The existing gameplay tests passed locally before the latest UNO-call additions; run the full test suite after pulling the latest `main` commit to verify those new tests. See `docs/PROGRESS.md` for the current status and next milestone.

## Run the web prototype locally

Prerequisites: .NET 10 SDK, Node.js 22 or newer, and npm.

1. Start the API from the repository root:

   ```bash
   dotnet run --project src/backend/Uno.Api
   ```

   The default web client API URL is `https://localhost:7137`. If your launch profile uses another port, copy `src/web/.env.example` to `src/web/.env.local` and set `VITE_API_URL` to the actual API origin. Trust the ASP.NET Core development HTTPS certificate if the browser reports a certificate error.

2. In a second terminal, start the React client:

   ```bash
   cd src/web
   npm install
   npm run build
   npm run dev
   ```

3. Open the Vite URL shown in the terminal (usually `http://localhost:5173`). Create a room in one browser and join its code from a second browser/profile to test multiplayer.

The prototype stores room data and access tokens in API process memory. Refreshing the page keeps the current participant token in that tab's `sessionStorage`, but restarting the API loses rooms. Configure production `Cors__AllowedOrigins__0` and the subsequent indexed values to the exact frontend origins before deployment.

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
