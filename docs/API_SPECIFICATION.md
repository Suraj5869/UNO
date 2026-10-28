# API and Real-Time Contract (Draft)

This is a planning document, not an implemented API.

## Candidate HTTP endpoints
- `POST /api/rooms` — create a room.
- `POST /api/rooms/{roomId}/join` — join a room.
- `POST /api/rooms/{roomId}/leave` — leave a room.
- `GET /api/rooms/{roomId}` — get the caller's filtered room/game view.
- `POST /api/matches/{matchId}/start` — host starts a match.
- `GET /api/players/me` — current profile.
- `GET /api/matches/history` — completed match history.

## Candidate SignalR hub
- Hub: `/hubs/game`.
- Client intentions: join room, submit game action, request resync.
- Server events: room updated, game started, game state updated, action rejected, player connected/disconnected, match completed.

## Contract principles
- Exact names and payload schemas are not finalized.
- Authenticate and authorize room membership.
- Validate all actions against server state and selected variant.
- Send per-player filtered state.
- Include match/version or turn identifiers to reject stale actions.
- Define reconnect/resync behavior before implementation.
