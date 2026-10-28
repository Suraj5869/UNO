# Development Roadmap

## Phase 0 — Foundation
- [ ] Create GitHub repository and initial commit.
- [ ] Add portable project instructions and docs.
- [ ] Decide local development prerequisites and solution layout.
- [ ] Record decisions and initial milestone.

## Phase 1 — Rules engine
- [ ] Create .NET solution/projects.
- [ ] Define cards, deck, players, game state, and actions.
- [ ] Agree exact Classic rules.
- [ ] Implement deterministic shuffle/deal with injectable random source for tests.
- [ ] Implement legal action validation and state transitions.
- [ ] Implement turn progression and winner detection.
- [ ] Add unit tests including 2-player and 10-player scenarios.

## Phase 2 — Playable local prototype
- [ ] Add basic web UI.
- [ ] Add bot decision-making through legal-action API.
- [ ] Complete a full game against bots.

## Phase 3 — Real-time multiplayer
- [ ] Add room lifecycle and room codes.
- [ ] Add SignalR.
- [ ] Add player-specific state views.
- [ ] Add reconnect/disconnect handling.
- [ ] Add concurrency and integration tests.

## Phase 4 — Accounts and persistence
- [ ] Add profiles and authentication.
- [ ] Persist match results.
- [ ] Add recoverable active state before production.

## Phase 5 — Mobile
- [ ] Build Expo client.
- [ ] Join same rooms as web users.
- [ ] Test touch UX and reconnect behavior.

## Phase 6 — Production readiness
- [ ] Load test.
- [ ] Security review.
- [ ] Monitoring and error reporting.
- [ ] Deployment and rollback plan.
- [ ] Privacy, branding, and licensing review.

## Phase 7 — Variants
- [ ] UNO Flip ruleset and tests.
- [ ] No Mercy ruleset and tests.
- [ ] Additional modes based on feedback.
