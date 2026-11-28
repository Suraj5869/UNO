import { expect, test } from '@playwright/test';
import { HubConnectionBuilder } from '@microsoft/signalr';

const origin = 'http://localhost:7137';
type Snapshot = {
  actionRevision: string;
  viewerPlayerId: string;
  currentPlayerId: string;
  players: { playerId: string; cardCount: number; hand: unknown[] | null }[];
};

test('private broadcasts stay with their seat and concurrent plays advance exactly once', async ({ request }) => {
  const create = async () => {
    const response = await request.post(origin + '/api/rooms', {
      data: { creatorDisplayName: 'Host', maximumPlayers: 2 },
    });
    expect(response.status()).toBe(201);
    return response.json();
  };
  const host = await create();
  const outsider = await create();
  const code = host.room.code;
  const url = origin + '/api/rooms/' + code;
  const join = await request.post(url + '/join', { data: { displayName: 'Guest' } });
  expect(join.ok()).toBeTruthy();
  const guest = await join.json();
  expect((await request.get(url)).status()).toBe(404);
  expect((await request.get(url, { headers: { 'X-Room-Token': outsider.accessToken } })).status()).toBe(404);
  const summaryResponse = await request.get(url, { headers: { 'X-Room-Token': host.accessToken } });
  expect(summaryResponse.status()).toBe(200);
  const summary = await summaryResponse.json();
  for (const seat of summary.players) {
    expect(Object.keys(seat).sort()).toEqual(['displayName', 'gamePlayerId', 'id', 'isCreator']);
  }

  const identities = [host, guest, host];
  const connections = identities.map(() => new HubConnectionBuilder().withUrl(origin + '/hubs/uno-room').build());
  const received: Snapshot[][] = connections.map(() => []);
  const publicDraws: { action: string; card: unknown; drawCount: number; playerId: string }[] = [];
  connections[2].on('GameAction', event => {
    if (event.action === 'CardDrawn') publicDraws.push(event);
  });
  try {
    for (const [i, hub] of connections.entries()) {
      hub.on('GameStateUpdated', (state: Snapshot) => received[i].push(state));
      await hub.start();
      await hub.invoke('JoinRoom', code, identities[i].accessToken);
    }
    const start = await request.post(url + '/start', {
      data: { accessToken: host.accessToken }, headers: { 'X-Test-Deal': 'numbers' },
    });
    expect(start.ok()).toBeTruthy();
    await expect(connections[2].invoke('GetGameState', code, outsider.accessToken)).rejects.toThrow();
    // Rejoining the same seat replaces its previous connection.
    await expect(connections[0].invoke('GetGameState', code, host.accessToken)).rejects.toThrow();
    const initial = await connections[2].invoke<Snapshot>('GetGameState', code, host.accessToken);
    await expect(connections[1].invoke('PlayCard', code, guest.accessToken, initial.actionRevision, 0, 0)).rejects.toThrow();
    const results = await Promise.allSettled([connections[2], connections[2]].map(
      hub => hub.invoke('PlayCard', code, host.accessToken, initial.actionRevision, 0, 0),
    ));
    expect(results.filter(result => result.status === 'fulfilled')).toHaveLength(1);
    for (const [i, updates] of received.entries()) {
      if (i === 0) { expect(updates).toHaveLength(0); continue; }
      await expect.poll(() => updates.at(-1)?.currentPlayerId).toBe(guest.playerId);
      expect(updates.at(-1)?.players.map(seat => seat.cardCount)).toEqual([6, 7]);
      for (const state of updates) {
        expect(state.viewerPlayerId).toBe(identities[i].playerId);
        expect(state).not.toHaveProperty('drawPile');
        for (const seat of state.players) {
          if (seat.playerId === state.viewerPlayerId) expect(seat.hand).toHaveLength(seat.cardCount);
          else expect(seat.hand).toBeNull();
        }
      }
    }
    await expect(connections[0].invoke('PlayCard', code, host.accessToken, initial.actionRevision, 0, 0)).rejects.toThrow();
    await expect(connections[1].invoke('DrawCard', code, guest.accessToken, initial.actionRevision)).rejects.toThrow();
    // Every gameplay endpoint must reject stale revisions before engine validation.
    for (const [method, args] of [
      ['PlayCard', [0, 0]], ['DrawCard', []], ['PassAfterDraw', []],
      ['AcceptDrawPenalty', []], ['AcceptWildDrawFour', []],
      ['ChallengeWildDrawFour', []], ['CallUno', []], ['CatchUno', [host.playerId]],
    ] as [string, unknown[]][]) {
      const before = await connections[1].invoke<Snapshot>('GetGameState', code, guest.accessToken);
      await expect(connections[1].invoke(method, code, guest.accessToken, initial.actionRevision, ...args)).rejects.toThrow();
      const after = await connections[1].invoke<Snapshot>('GetGameState', code, guest.accessToken);
      expect(after.actionRevision).toBe(before.actionRevision);
    }
    const next = await connections[1].invoke<Snapshot>('GetGameState', code, guest.accessToken);
    await connections[1].invoke('DrawCard', code, guest.accessToken, next.actionRevision);
    await expect(connections[1].invoke('DrawCard', code, guest.accessToken, next.actionRevision)).rejects.toThrow();
    await expect.poll(() => publicDraws.length).toBe(1);
    expect(publicDraws[0]).toMatchObject({ card: null, drawCount: 1, playerId: guest.playerId });
    await expect.poll(() => received[2].at(-1)?.players[1].cardCount).toBe(8);
    expect(received[2].at(-1)?.players[1].hand).toBeNull();
    expect(received[2].at(-1)).toHaveProperty('justDrawnCard', null);

    expect(received[0]).toHaveLength(0);
    await connections[1].invoke('LeaveRoom', code, guest.accessToken);
    await connections[1].stop();
    await connections[1].start();
    await expect(connections[1].invoke('JoinRoom', code, guest.accessToken)).rejects.toThrow();
    await expect(connections[1].invoke('GetGameState', code, guest.accessToken)).rejects.toThrow();
  } finally {
    await Promise.all(connections.map(hub => hub.stop()));
  }
});

test('concurrent joins claim the last seat once and keep rooms independent', async ({ request }) => {
  const roomsUrl = origin + '/api/rooms';
  const invalid = await request.post(roomsUrl, { data: { creatorDisplayName: '   ' } });
  expect(invalid.status()).toBe(400);
  const rooms = await Promise.all(['First host', 'Second host'].map(async creatorDisplayName => {
    const response = await request.post(roomsUrl, { data: { creatorDisplayName, maximumPlayers: 2 } });
    expect(response.status()).toBe(201);
    return await response.json() as { room: { code: string }; accessToken: string };
  }));
  expect(rooms[0].room.code).not.toBe(rooms[1].room.code);
  await Promise.all(rooms.map(async (room, index) => {
    const code = room.room.code;
    const url = roomsUrl + '/' + code;
    const invalidJoin = await request.post(url + '/join', { data: { displayName: ' ' } });
    expect(invalidJoin.status()).toBe(400);
    const names = [code + ' A', code + ' B'];
    const joins = await Promise.all(names.map(displayName =>
      request.post(url + '/join', { data: { displayName } })));
    expect(joins.map(response => response.status()).sort()).toEqual([200, 409]);
    const otherRoom = rooms[1 - index];
    expect((await request.get(url, { headers: { 'X-Room-Token': otherRoom.accessToken } })).status()).toBe(404);
    const summaryResponse = await request.get(url, { headers: { 'X-Room-Token': room.accessToken } });
    expect(summaryResponse.status()).toBe(200);
    const summary = await summaryResponse.json();
    const winningIndex = joins.findIndex(response => response.ok());
    expect(summary.players.map((seat: { displayName: string }) => seat.displayName))
      .toEqual([index === 0 ? 'First host' : 'Second host', names[winningIndex]]);
  }));
});
