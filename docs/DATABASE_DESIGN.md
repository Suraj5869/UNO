# Database Design (Initial Proposal)

Potential tables; finalize after defining authentication and persistence scope:
- `users`: account identity and status.
- `player_profiles`: display name, avatar, public profile fields.
- `game_rooms`: room code, visibility, host, selected variant, room settings, status.
- `game_players`: room/session membership, seat/order, participant type, connection status.
- `game_sessions`: variant, status, timestamps, ruleset version.
- `game_results`: winner, placements, duration, final statistics.
- `player_statistics`: aggregate wins/losses and other derived statistics.
- `friendships`: optional future friend requests/relationships.
- `game_events` or `game_snapshots`: optional future recovery/replay support.

Security and data minimization:
- Never persist passwords in plain text.
- Do not expose internal database IDs unnecessarily to clients.
- Do not store hidden card state in client-readable result endpoints.
- Decide retention and privacy behavior before production.
- Active-game persistence and recovery are required before treating the service as production-resilient.
