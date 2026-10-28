# Requirements

## Functional requirements
- FR-01: Create and join rooms.
- FR-02: Support private room codes.
- FR-03: Provide public matchmaking.
- FR-04: Support 2–10 total participants in shared room infrastructure; a variant may define a smaller supported range.
- FR-05: Support humans and bots in the same room.
- FR-06: Enforce turn order and card legality on the server.
- FR-07: Expose only player-specific game views; hide opponent hands and draw-pile order.
- FR-08: Support Classic gameplay before other variants.
- FR-09: Handle reconnects and player departures according to explicit policies.
- FR-10: Store player profiles and completed match results.
- FR-11: Allow web and mobile clients to use the same backend.
- FR-12: Keep a portable project handoff and progress record.

## Non-functional requirements
- NFR-01: Deterministic and testable rules engine.
- NFR-02: Idempotency/concurrency protection for simultaneous or duplicate actions.
- NFR-03: No secrets committed to Git.
- NFR-04: Accessible, responsive interfaces.
- NFR-05: Observability for game errors without logging hidden card data or credentials.
- NFR-06: Design for recovery before production; in-memory state alone is not production-safe.
- NFR-07: Test actual supported player counts and expected concurrency before publishing capacity claims.

## Open questions
- Exact Classic rules and optional house rules.
- Whether guests can later link an account without losing statistics.
- Room start policy and timeout behavior.
- Disconnect grace period and bot takeover policy.
- Matchmaking preferences and region selection.
- Final product name and licensing review.
