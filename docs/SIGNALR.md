# SignalR Room Protocol

## Endpoint

The API maps the SignalR hub at:

`/hubs/uno-room`

Use the full API origin plus this path. The hub uses the same process-local `RoomStore` as the REST API, so one API instance is required for this milestone.

## Connection flow

1. Create or join a room through the REST API.
2. Store the returned `AccessToken` securely for that participant. Do not log it or share it with other players.
3. Connect to `/hubs/uno-room`.
4. Invoke `JoinRoom(code, accessToken)`. This validates the token and joins the caller to the room and player-specific SignalR groups.
5. Listen for `RoomUpdated` and `GameStateUpdated`.
6. Invoke a game action with the room code and the same participant access token.

## Client events

- `RoomUpdated(RoomResponse)`: public lobby summary, player names/count, selected rules, and room/game status.
- `GameStateUpdated(GameStateResponse)`: state filtered for the receiving participant. The receiving player gets their own hand; opponents only expose their card counts.

Do not send a shared `GameStateUpdated` payload to the whole room group. The publisher deliberately sends one filtered payload to each player-specific group.

## Hub methods

All methods that accept a token validate it against the room.

- `JoinRoom(code, accessToken)`: join the lobby and return its summary. If a game is already running, the caller also receives a private `GameStateUpdated` event.
- `GetGameState(code, accessToken)`: fetch the caller's filtered game state.
- `PlayCard(code, accessToken, cardIndex, chosenColor)`: play a card by its current index in the caller's hand. For non-wild cards, use `CardColor.None`; wild cards require Red, Yellow, Green, or Blue.
- `DrawCard(code, accessToken)`
- `PassAfterDraw(code, accessToken)`
- `AcceptDrawPenalty(code, accessToken)`
- `AcceptWildDrawFour(code, accessToken)`
- `ChallengeWildDrawFour(code, accessToken)`
- `CallUno(code, accessToken)`
- `CatchUno(code, accessToken, targetRoomPlayerId)`: `targetRoomPlayerId` is the room-level player ID, not the engine's internal player ID.

The hub delegates game actions to `UnoGameEngine`; it does not accept a client-provided card object or client-provided engine player ID.

## Frontend CORS

Development defaults allow `http://localhost:5173` and `https://localhost:5173`. Configure production origins through indexed environment settings, for example:

`Cors__AllowedOrigins__0=https://your-frontend.example`

Add each additional allowed origin using the next index. Do not use a wildcard origin with credentials enabled.

## Current limitations

- Rooms, access tokens, and game state are in memory and are lost when the API process restarts.
- Tokens do not yet expire or support revocation.
- Reconnect/resynchronization, disconnection cleanup, player rejoining, room expiry, authentication/accounts, and distributed hosting are not implemented.
- SignalR is not documented in Swagger's HTTP operation list; use a SignalR client to invoke hub methods.


## Animated game-action events

The server broadcasts a room-wide `GameAction` event after a successful authoritative action and before publishing updated player-specific game states. The event envelope contains a per-room sequence number, action name, room-level player ID, optional public played-card data, draw count, and server timestamp.

- `GameAction(CardPlayed)`: includes the played card because it is now public on the discard pile.
- `GameAction(CardDrawn)` / `GameAction(CardsDrawn)`: includes only the target player ID and number of cards drawn. It never includes card identities, including for opponents.

Clients ignore duplicate/older sequence numbers and use events only for transient animation. `GameStateUpdated` remains authoritative. On reconnect, fetch the latest filtered state and do not replay old animations. The web client renders the shared countdown ring from the server-issued deadline; timeout enforcement remains server-side.
