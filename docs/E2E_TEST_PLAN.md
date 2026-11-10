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

## Coverage matrix

| Area | Scenarios | Status |
|---|---|---|
| Room creation | Valid room, capacities 2–10, invalid capacity, invalid display name | Engine-level tests exist; browser coverage pending |
| Room joining | Valid code, unknown code, full room, game already started, invalid display name | Unknown-room browser smoke test exists; 10-player capacity flow added |
| Multiplayer capacity | 2, 5 and 10 independent players; eleventh player rejected | 10-player browser scenario added; not yet executed |
| Host permissions | Non-host cannot change rules or start; host departure promotes next host | Engine-level tests exist; browser coverage pending |
| Player departure | Lobby departure, non-current player departure, current player departure, consecutive departures, fewer than two players | Engine coverage exists for current-player departure; 10-player current-player departure browser scenario added; not yet executed |
| Game actions | Number, Skip, Reverse, Draw Two, Wild, Wild Draw Four, draw, pass | Number/draw/pass and special-card browser scenarios added; special-card suite not yet executed |
| Rule sets | Official vs House stacking and Wild Draw Four challenge behavior | Browser scenarios added for House Draw Two penalty/conditional stacking and Wild Draw Four accept/challenge; guaranteed deterministic stack/challenge variants pending |
| UNO | Call window, missed UNO, catch penalty, expired window | Multi-client browser scenarios added for successful call, late-call rejection, catch-button gating, non-current-player catch, and +2 penalty; execution pending |
| Timeout | Normal turn timeout, pending penalty timeout, client countdown synchronization | Engine behavior exists; browser coverage pending |
| Realtime | Multiple clients receive updates, duplicate/stale actions, reconnect and rejoin | Basic lobby/game SignalR path exists; robust browser coverage pending |
| Privacy/security | Token authorization, unauthorized host actions, own-hand-only game state, stale token after leave | Some server-side protections exist; API/SignalR security E2E pending |
| Rematch | Opt-in, host promotion, returning players, revoked stale tokens | Engine/room behavior exists; browser coverage pending |
| Responsive UI | Desktop and mobile layouts at 2, 4, 6 and 10 players | Manual verification pending |

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
