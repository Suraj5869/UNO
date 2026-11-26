# End-to-End Test Plan

## Goal

Verify the complete player journey through the real web client, REST API, SignalR hub, room lifecycle, and server-authoritative game engine. E2E tests must use independent browser contexts for independent players; sharing one browser context would incorrectly share session storage and room credentials.

## Test principles

- Exercise user-visible workflows through the browser wherever possible.
- Use a separate browser context and page for each simulated player.
- Keep tests serial initially because all test instances use one process-local room registry and fixed local ports.
- Assert both positive behavior and rejected actions.
- Verify state from more than one client after every multiplayer mutation.
- Never inspect another player's hidden hand; verify only that a player's own hand is visible and opponent card counts are exposed.
- Keep random card distribution out of assertions unless the test is specifically about randomness. Add a test-only deterministic scenario mechanism only when gameplay E2E coverage requires controlled hands; it must be unavailable in production.

## Current coverage

- Independent players create and join rooms, enforce capacity and host permissions, and leave during matches. Capacity tests exercise 2, 4, 5, 6 and 10 seats with phone and short desktop Chromium viewports.
- A dedicated host in `tests/Uno.BrowserHost` runs the API with reproducible opening deals for UNO, rematch, special cards and penalty timeout. Production starts with the normal shuffle. Players submit every move through the ordinary APIs.
- Special-card tests cover Skip, Reverse, Wild color choice, Wild Draw Four acceptance and both challenge outcomes, and an actual House Draw Two stack followed by payment.
- UNO scenarios cover on-time call, late rejection, grace-period gating, missed-call catch and the penalty. Rematch completes a two-player game and checks that the former guest can become host, wait for the former host and start again.
- Network tests cover token isolation, revoked credentials on reconnect, private broadcasts, two submissions for one turn and competing requests for the last seat across concurrent rooms.
- Engine tests cover discard recycling for each supported seat count, card conservation throughout a ten-player match, and duplicate timeout processing. The browser lifecycle tests exercise real server deadlines.
- Physical devices, engine/browser combinations beyond Chromium, production load, and the known deadline and departure defects in PROJECT_STATUS.md remain follow-up work.

## First browser acceptance scenario

`multiplayer-capacity.spec.ts` exercises one complete multi-client path:

1. Ten separate browser contexts create/join the same room.
2. The host sees all ten players in the lobby.
3. An eleventh independent browser is rejected because the room is full.
4. The host starts the match.
5. Every participant receives their own seven-card hand and sees nine opponents.
6. The active player leaves during the match.
7. Every remaining participant sees eight opponents and the departing player is absent.

## Execution

From `src/web`:

- `npm run test:e2e` — all browser E2E tests.
- `npm run test:e2e:multiplayer` — the ten-player capacity and current-player departure scenario.
- `npm run test:e2e:gameplay` — legal number-card play and draw/pass browser scenarios.
- `npm run test:e2e:uno` — UNO call/catch grace-period and penalty scenarios.
- `npm run test:e2e:rematch` — complete a match and exercise the two-player rematch opt-in flow.
- `npm run test:e2e:lifecycle` — reconnect after a disconnected player's timeout and pending Draw Two timeout.
- `npm run test:e2e:rooms` — non-host authorization, creator departure/host promotion, joining after start, and invalid capacity.

Playwright retains traces, screenshots and video on failure. GitHub Actions uploads the HTML report and test results as a workflow artifact.

## Rollout order

1. Multi-player fixtures and capacity/leave scenarios.
2. Lobby permissions, invalid joins, host departure and room lifecycle.
3. Core gameplay browser flows: legal number-card play, draw, and pass.
4. Special-card scenarios: Skip, Reverse, Draw Two, Wild and Wild Draw Four.
5. UNO call/catch and grace-period scenarios, followed by full match completion and Play Again.
6. Reconnect, timeout, concurrent actions, privacy and authorization.
7. Desktop/mobile viewport coverage and repeated-run stability.

Do not call the E2E suite comprehensive or deployment-ready until the full matrix has been implemented and the complete suite has passed repeatedly in CI.
