# UNO project status

Reviewed 2026-11-18 on local `main` at `c9f1cb6`. The branch was three commits ahead of its local `origin/main` reference. Existing modified Playwright artifacts were preserved.

## Multiplayer departures

When the active player leaves, the turn moves to the next remaining player in the current direction and starts a new deadline. An inactive player leaving preserves the active player's deadline and their play-or-pass decision after a draw. The next gameplay task is enforcing the deadline for actions received before the timeout poll runs.

## Number-only finish

The server and web client now require a number card to go out in either Classic preset. A final Draw Two cannot create or extend a penalty. Verification: 90 backend tests and 29 Chromium scenarios passed, with zero Playwright retries in 4.2 minutes. The web production build passed.

## Test implementation follow-up

Local verification on 2026-11-18: `dotnet build src/backend/Uno.sln --no-restore`, the browser-host build and `npm.cmd run build` passed. `dotnet test src/backend/Uno.sln --no-restore` passed all 77 cases. From src/web, `npx.cmd --no-install playwright test --workers=1 --retries=0 --reporter=list` with isolated temporary output passed all 28 Chromium scenarios in 3.7 minutes.

- The two previously failing browser flows now use controlled opening deals through a dedicated test executable. The real web client, REST endpoints, SignalR groups, server deadlines and engine process the moves.
- Engine coverage includes discard recycling at every table size from 2 through 10, a complete ten-player match with card conservation checked each turn, and duplicate/stale actions after timeout.
- Browser coverage exercises 2, 4, 5, 6 and 10 seats at 390 by 844 and 1280 by 720 pixels. Direct network tests cover personal snapshots, public draws, room-token boundaries, contested seats and simultaneous plays.
- Actions received after the deadline but before the timeout poll remain a pending gameplay fix. Production load, physical devices and additional browser engines remain unverified.

The sections below retain the original audit baseline.

## Implemented

- **Classic gameplay.** The C# engine deals seven cards from a 108-card deck and validates plays, draws, passes, Skip, Reverse, Draw Two, Wild color choice and Wild Draw Four accept/challenge. The Official preset disables stacking. The House preset enables same-type stacking. The engine implements a three-second UNO call grace period, a two-card missed-call penalty, a 30-second turn timer and discard recycling.
- **Private multiplayer.** Shareable codes, 2–10 human participants, host-only rules and start, room tokens, live lobby and game updates, same-session reconnect, explicit leave, host promotion, match completion and independent rematch opt-in.
- **Web client.** React and TypeScript implement lobby and game screens, player-specific hands, contextual controls, countdowns, game animations and responsive seating.
- **Automation.** xUnit covers deck, factory, rules and room lifecycle. Chromium Playwright covers common gameplay, room authorization, capacity, special cards, UNO calls, timeout/reconnect and rematch. GitHub Actions defines build, unit test and E2E jobs.

## Architecture

| Component | Responsibility |
| --- | --- |
| `src/backend/Uno.GameEngine` | Deck, state, rule presets, turn logic and room lifecycle. |
| `src/backend/Uno.Api` | REST lobby operations, SignalR actions and broadcasts, process-local room registry and a background timeout service. |
| `src/web` | React UI over REST and SignalR. The active seat's countdown derives from a server deadline. |
| `src/backend/Uno.GameEngine.Tests`, `src/web/e2e` | Engine/room unit tests and browser scenarios through the real local API. |
| `src/mobile`, `src/shared`, `tests` | Placeholder directories. |

The backend serializes mutations through a shared room-store lock. REST exposes room summaries. The realtime publisher sends game snapshots to per-player groups, with the viewer's hand and opponents' card counts. Dedicated integration tests should verify privacy and authorization at the network boundary.

## Partial and pending

- **Rules completeness.** The opening discard is intentionally restricted to a number card. Confirm the intended opening-action rules and document the prototype's timing conventions. Additional variants still need explicit rule and deck definitions.
- **Multiplayer readiness.** The 2–10 range is enforced by room capacity and game setup. The suite includes two-player gameplay, three-player rule flows and a ten-context capacity/departure scenario that checks an eleventh join is rejected and remaining clients update. Broader coverage is pending for intermediate seat counts, full ten-player matches, multiple simultaneous rooms, reconnect/leave races and mobile or short desktop layouts.
- **Account and operations work.** Account authentication, player profiles, public matchmaking, bots, durable match results, restart recovery, room expiry, rate limiting, multi-instance coordination, production deployment and monitoring remain pending. Disconnect recovery currently depends on the existing room token and API process. Native Expo clients remain pending.
- **Coverage gaps.** Add network-level privacy and authorization tests, deterministic fixtures for special cards and UNO/rematch, tests for draw exhaustion and card conservation, simultaneous actions and timeout races, and desktop/mobile viewport coverage.

## Findings from code and existing artifacts

| Finding | Evidence |
| --- | --- |
| Active-player departure can select the wrong next seat in reverse play | `UnoGameEngine.RemovePlayer` uses the removed index modulo remaining count. Normal turn progression uses `TurnDirection`. |
| A mid-game departure reduces the circulating deck | `RemovePlayer` removes the seat and its hand from game state. Define the intended destination for these cards, then add conservation tests. |
| A bystander leaving can reset another player's draw restriction and clock | `RemovePlayer` unconditionally clears `JustDrawnCard` and resets the deadline when at least two players remain. |
| Turn deadline enforcement has a polling gap | Engine turn actions check seat and UNO grace, while the timeout service checks the deadline on a 500 ms polling loop. An action arriving after the deadline can win the race against that loop. |
| A final-card House Rules Draw Two can leave a zero-card player in an active game | Playing the last card with a pending draw amount defers completion. Draw Two acceptance clears the penalty and advances the turn without checking for a winner. The timeout path only handles the deferred Wild Draw Four winner. |
| Draw animations can overstate an exhausted penalty | The engine draws only available cards. Several hub and timeout events report the nominal penalty even if fewer cards were actually drawn. |
| Realtime ordering needs race coverage | Publication occurs outside the mutation lock, and the UI applies game snapshots as they arrive. Concurrent actions or timeout/leave broadcasts can deliver outdated state unless snapshot ordering is enforced. |
| Saved UNO browser failures occurred in shuffled match setup | One saved failure exhausted the 180-second test timeout. The other reported a closed page during a locator count. These artifacts alone do not establish a UNO rule defect. |
| Build hygiene | Backend reports xUnit2031 in `UnoDeckTests.cs`. Vite reports two removable annotation warnings from the SignalR dependency. Playwright results and TypeScript build metadata are tracked. |

## Initial audit verification

| Check | Result |
| --- | --- |
| `dotnet build src/backend/Uno.sln` | Passed, one analyzer warning. |
| `dotnet test src/backend/Uno.sln --no-build` | 66 passed, zero failed or skipped. |
| `npm.cmd run build` from `src/web` | Passed, two SignalR dependency annotation warnings. |
| `npx.cmd --no-install playwright test --workers=1 --reporter=list --output "$env:TEMP/uno-audit-20261118-$PID"` from `src/web`, with `CI=true` | 18 passed, 2 failed after one retry (exit 1, 20.4 minutes). |

The ten-player capacity/departure scenario passed, as did room permissions, timeout/reconnect, all six special-card scenarios and the missed-UNO catch scenario, including late-call rejection.

The rematch and successful UNO-call scenarios both hit their 180-second test timeout on both attempts, before reaching their feature assertions. The first rematch attempt waited on a disabled draw button. The retry timed out while waiting for draw resolution. The UNO attempts timed out during active-turn detection and draw resolution. Both fixtures search shuffled matches by playing number cards only. This makes setup dependent on the deal and can consume the entire test budget. These failures leave the browser success paths for UNO call and rematch unverified.

Fresh artifacts are in `%TEMP%\uno-audit-20261118-15824`. The run used the existing local dependencies, .NET SDK 10.0.401, Node.js 24.11.0 and Chromium. Mobile devices and other browsers were outside this run.

## Proposed implementation order

1. Triage current browser failures and make UNO/rematch fixtures reproducible while retaining real API, SignalR and browser assertions.
2. Fix direction-aware departures and turn deadline enforcement. Preserve the active player's draw restriction and deadline when another seat leaves. Agree how departed cards and unresolved penalties should be handled, then record that decision and test it.
3. Add API/SignalR privacy and authorization tests, simultaneous-action and timeout coverage, ordered snapshots and responsive multiplayer checks.
4. Complete opening-action rules and prioritize bots, public matchmaking, accounts, persistence, native clients and additional variants against product goals.

The user approved the test implementation after this initial review. Gameplay defect fixes and product decisions remain separate follow-up work.


## P0 gameplay stabilization — implemented, verification pending

Direction-aware departures return hands to the shuffled draw pile, preserve the existing active turn when another seat leaves, and transfer outstanding penalties to the next seat when the active player leaves. Challenges are disabled if the offender leaves or becomes the penalty recipient. A remaining single player completes the match. Turn actions are rejected at or after the server deadline; timeout after drawing passes without another draw. The next timeout deadline is anchored to the supplied timeout tick. Added 17 engine regression cases. Backend and browser suites must be run before declaring this verified. Realtime snapshot ordering remains P1.
