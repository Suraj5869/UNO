# Architecture and Product Decisions

## D-001: Start with Classic
Status: Accepted.
Reason: Build and test a complete core loop before adding variant-specific complexity.

## D-002: Shared engine, separate variant rules
Status: Accepted.
Reason: Avoid rewriting room management and turn infrastructure for every variant.

## D-003: Target 2–10 total participants per room
Status: Accepted as a design target.
Qualification: Humans and bots combined; each variant must validate its supported player range and deck/rules. Actual support must be proven by tests.

## D-004: Server-authoritative game state
Status: Accepted.
Reason: Prevent illegal moves, stale turns, and hidden-card leakage.

## D-005: Proposed stack
Status: Proposed; confirm before scaffolding application code.
- ASP.NET Core Web API + SignalR.
- React + TypeScript + Vite for web.
- React Native + Expo for mobile.
- PostgreSQL for persistence.

## D-006: Keep architecture straightforward
Status: Accepted.
Reason: Prefer simple modular boundaries and dependency injection; avoid adding CQRS/MediatR without a demonstrated need.

## D-007: Portable AI project memory
Status: Accepted.
Reason: Keep source code in GitHub and project context in version-controlled docs, including progress and handoff.
