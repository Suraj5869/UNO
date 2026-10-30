import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import './styles.css';

const API = (import.meta.env.VITE_API_URL || 'https://localhost:7137').replace(/\/$/, '');
const HUB = import.meta.env.VITE_SIGNALR_URL || API + '/hubs/uno-room';
type RuleSet = 0 | 1;
type Color = 0 | 1 | 2 | 3 | 4;
interface Player { id: string; displayName: string; isCreator: boolean; gamePlayerId: string | null }
interface Room { id: string; code: string; maximumPlayers: number; ruleSet: RuleSet; status: number; players: Player[]; gameStatus: number | null }
interface Access { room: Room; playerId: string; accessToken: string }
interface Card { color: Color; type: number; number: number | null }
interface GamePlayer { playerId: string; displayName: string; cardCount: number; hand: Card[] | null }
interface Game { roomCode: string; roomStatus: number; gameStatus: number; ruleSet: RuleSet; viewerPlayerId: string; currentPlayerId: string; activeColor: Color; topDiscard: Card; drawPileCount: number; discardPileCount: number; turnDirection: number; pendingDrawAmount: number; pendingDrawType: number | null; isWildDrawFourChallengePending: boolean; playerAwaitingUnoCallId: string | null; players: GamePlayer[] }

const ruleName = (r: RuleSet) => r === 1 ? 'House Rules' : 'Official Rules';
const colorName = (c: Color) => ['Wild', 'Red', 'Yellow', 'Green', 'Blue'][c] || 'Wild';
const colorClass = (c: Color) => c === 0 ? 'wild' : colorName(c).toLowerCase();
const cardLabel = (c: Card) => c.type === 4 ? 'WILD' : c.type === 5 ? '+4' : c.type === 1 ? 'SKIP' : c.type === 2 ? '↻' : c.type === 3 ? '+2' : String(c.number ?? '?');
const roomStatus = (s: number) => ['Waiting for players', 'In progress', 'Completed', 'Cancelled'][s] || 'Unknown';

async function messageFor(response: Response) {
  const body = await response.json().catch(() => null);
  return body?.detail || body?.title || 'Request failed (' + response.status + ')';
}

export default function App() {
  const [name, setName] = useState('');
  const [codeInput, setCodeInput] = useState('');
  const [capacity, setCapacity] = useState(4);
  const [rule, setRule] = useState<RuleSet>(0);
  const [access, setAccess] = useState<Access | null>(() => {
    try { const saved = sessionStorage.getItem('uno-room-access'); return saved ? JSON.parse(saved) as Access : null; } catch { return null; }
  });
  const [room, setRoom] = useState<Room | null>(access?.room ?? null);
  const [game, setGame] = useState<Game | null>(null);
  const [hub, setHub] = useState<HubConnection | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [wildIndex, setWildIndex] = useState<number | null>(null);
  const [unoAnnouncement, setUnoAnnouncement] = useState('');
  const [unoAnimationKey, setUnoAnimationKey] = useState(0);
  const [catchUnoReady, setCatchUnoReady] = useState(false);

  const api = useCallback(async <T,>(path: string, init?: RequestInit): Promise<T> => {
    const response = await fetch(API + path, { ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } });
    if (!response.ok) throw new Error(await messageFor(response));
    return response.status === 204 ? undefined as T : await response.json() as T;
  }, []);

  const keepAccess = useCallback((value: Access) => {
    setAccess(value); setRoom(value.room); setRule(value.room.ruleSet);
    sessionStorage.setItem('uno-room-access', JSON.stringify(value));
  }, []);

  const create = async (event: FormEvent) => {
    event.preventDefault(); setError(''); setNotice('');
    if (!name.trim()) { setError('Enter a display name first.'); return; }
    setBusy(true);
    try {
      const value = await api<Access>('/api/rooms', { method: 'POST', body: JSON.stringify({ creatorDisplayName: name.trim(), maximumPlayers: capacity, ruleSet: rule }) });
      keepAccess(value); setNotice('Room ' + value.room.code + ' created. Share the code with your friends.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not create room.'); }
    finally { setBusy(false); }
  };

  const join = async (event: FormEvent) => {
    event.preventDefault(); setError(''); setNotice('');
    if (!name.trim() || !codeInput.trim()) { setError('Enter your name and room code.'); return; }
    setBusy(true);
    try {
      const value = await api<Access>('/api/rooms/' + encodeURIComponent(codeInput.trim()) + '/join', { method: 'POST', body: JSON.stringify({ displayName: name.trim() }) });
      keepAccess(value); setNotice('Joined room ' + value.room.code + '.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not join room.'); }
    finally { setBusy(false); }
  };

  const roomCode = access?.room.code;
  const refresh = useCallback(async () => {
    if (!roomCode) return;
    try {
      const value = await api<Room>('/api/rooms/' + encodeURIComponent(roomCode));
      setRoom(value);
      setAccess(old => {
        if (!old) return old;
        const next = { ...old, room: value };
        sessionStorage.setItem('uno-room-access', JSON.stringify(next));
        return next;
      });
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not refresh room.'); }
  }, [roomCode, api]);

  useEffect(() => {
    if (!access) return;
    let active = true;
    const connection = new HubConnectionBuilder().withUrl(HUB).withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    connection.on('RoomUpdated', (value: Room) => {
      if (!active) return;
      setRoom(value);
      setAccess(old => {
        if (!old) return old;
        const next = { ...old, room: value };
        sessionStorage.setItem('uno-room-access', JSON.stringify(next));
        return next;
      });
    });
    connection.on('GameStateUpdated', (value: Game) => { if (active) setGame(value); });
    connection.on('UnoCalled', (displayName: string) => {
      if (!active) return;
      const announcement = displayName + ' says UNO!';
      setUnoAnnouncement(announcement);
      setUnoAnimationKey(key => key + 1);
      window.setTimeout(() => setUnoAnnouncement(current => current === announcement ? '' : current), 3200);
    });
    connection.onreconnected(async () => {
      try {
        await connection.invoke('JoinRoom', access.room.code, access.accessToken);
        const value = await connection.invoke<Game>('GetGameState', access.room.code, access.accessToken).catch(() => null);
        if (value && active) setGame(value);
        if (active) setNotice('Reconnected to the room.');
      } catch (e) { if (active) setError(e instanceof Error ? e.message : 'Could not rejoin room.'); }
    });
    connection.start().then(async () => {
      if (!active) { await connection.stop(); return; }
      await connection.invoke('JoinRoom', access.room.code, access.accessToken);
      if (active) setHub(connection);
      const value = await connection.invoke<Game>('GetGameState', access.room.code, access.accessToken).catch(() => null);
      if (value && active) setGame(value);
      await refresh();
    }).catch(e => { if (active) setError('Realtime connection failed: ' + (e instanceof Error ? e.message : 'check API URL and HTTPS certificate')); });
    return () => { active = false; setHub(old => old === connection ? null : old); void connection.stop(); };
  }, [access?.playerId, access?.room.code, access?.accessToken, refresh]);

  const act = useCallback(async (method: string, ...args: unknown[]) => {
    if (!hub || hub.state !== 'Connected' || !access) { setError('Realtime connection is not ready. Wait a moment and retry.'); return; }
    setError(''); setNotice('');
    try {
      await hub.invoke(method, access.room.code, access.accessToken, ...args);
      if (method === 'CallUno') {
        const announcement = (game?.players.find(player => player.playerId === game.viewerPlayerId)?.displayName || 'A player') + ' says UNO!';
        setUnoAnnouncement(announcement);
        setUnoAnimationKey(key => key + 1);
        window.setTimeout(() => setUnoAnnouncement(current => current === announcement ? '' : current), 3200);
      }
    }
    catch (e) {
      setError(e instanceof Error ? e.message : 'That action was rejected.');
      try { setGame(await hub.invoke<Game>('GetGameState', access.room.code, access.accessToken)); } catch { /* Keep original error. */ }
    }
  }, [access, hub, game]);

  const changeRule = async (value: RuleSet) => {
    if (!access || !room) return;
    setBusy(true); setError('');
    try {
      const updated = await api<Room>('/api/rooms/' + encodeURIComponent(room.code) + '/rules', { method: 'PUT', body: JSON.stringify({ accessToken: access.accessToken, ruleSet: value }) });
      setRoom(updated); setRule(value); setNotice('Rules set to ' + ruleName(value) + '.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not change rules.'); }
    finally { setBusy(false); }
  };

  const startGame = async () => {
    if (!access) return;
    setBusy(true); setError('');
    try {
      const updated = await api<Room>('/api/rooms/' + encodeURIComponent(access.room.code) + '/start', { method: 'POST', body: JSON.stringify({ accessToken: access.accessToken }) });
      setRoom(updated); setNotice('Game started! Good luck.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not start the game.'); }
    finally { setBusy(false); }
  };

  const leave = async () => {
    await hub?.stop().catch(() => undefined);
    sessionStorage.removeItem('uno-room-access');
    setAccess(null); setRoom(null); setGame(null); setHub(null); setError('');
    setNotice('You left this device. The in-memory room remains until the server restarts.');
  };

  const viewer = useMemo(() => game?.players.find(p => p.playerId === game.viewerPlayerId), [game]);
  const myTurn = !!game && game.currentPlayerId === game.viewerPlayerId;
  const isHost = !!access && !!room?.players.find(p => p.id === access.playerId)?.isCreator;
  const winner = game?.gameStatus === 1 ? game.players.find(p => p.cardCount === 0) : undefined;
  const catchTarget = game?.playerAwaitingUnoCallId && game.playerAwaitingUnoCallId !== game.viewerPlayerId ? game.players.find(p => p.playerId === game.playerAwaitingUnoCallId) : null;

  useEffect(() => {
    setCatchUnoReady(false);
    if (!game?.playerAwaitingUnoCallId || game.playerAwaitingUnoCallId === game.viewerPlayerId) return;
    const timer = window.setTimeout(() => setCatchUnoReady(true), 3000);
    return () => window.clearTimeout(timer);
  }, [game?.playerAwaitingUnoCallId, game?.viewerPlayerId]);

  const play = (index: number, card: Card) => {
    if (card.type === 4 || card.type === 5) setWildIndex(index);
    else void act('PlayCard', index, 0);
  };
  const chooseColor = (color: Color) => { if (wildIndex === null) return; const index = wildIndex; setWildIndex(null); void act('PlayCard', index, color); };

  return <div className="app-shell">
    <header className="topbar"><a className="brand" href="#" onClick={e => e.preventDefault()}><span className="brand-mark">U</span><span>UNO<span className="brand-dot">.</span></span></a><div className="topbar-right"><span className={'connection-pill ' + (hub?.state === 'Connected' ? 'online' : '')}><i />{hub?.state === 'Connected' ? 'Live connection' : access ? 'Connecting' : 'Ready to play'}</span>{access && <button className="button button-quiet button-small" onClick={() => void leave()}>Leave room</button>}</div></header>
    <main className="main-content">
      {!access || !room ? <section className="landing">
        <div className="hero-copy"><span className="eyebrow"><span /> CLASSIC UNO · PLAY WITH FRIENDS</span><h1>One deck.<br /><span>Endless</span> rematches.</h1><p>Create a private room, invite your friends with a short code, and let the server keep every turn fair.</p><div className="hero-stats"><div><strong>2–10</strong><span>players per room</span></div><div><strong>2 modes</strong><span>Official or House Rules</span></div><div><strong>Live</strong><span>multiplayer updates</span></div></div><div className="card-art" aria-hidden="true"><div className="art-card art-red"><b>7</b><span>UNO</span></div><div className="art-card art-blue"><b>↻</b><span>UNO</span></div><div className="art-card art-yellow"><b>+2</b><span>UNO</span></div><div className="art-card art-green"><b>5</b><span>UNO</span></div></div></div>
        <div className="entry-card"><div className="entry-heading"><span className="mini-label">LET'S PLAY</span><h2>Game on.</h2><p>Use your name, then create a table or join one.</p></div><label className="field-label" htmlFor="display-name">Your display name</label><input id="display-name" value={name} onChange={e => setName(e.target.value)} maxLength={24} placeholder="e.g. CardShark" />
          <form onSubmit={create} className="create-form"><div className="section-label"><span>CREATE A ROOM</span><span className="line" /></div><label className="field-label" htmlFor="capacity">Maximum players</label><select id="capacity" value={capacity} onChange={e => setCapacity(Number(e.target.value))}>{[2,3,4,5,6,7,8,9,10].map(n => <option key={n} value={n}>{n} players</option>)}</select><label className="field-label" htmlFor="rules">Starting rules</label><select id="rules" value={rule} onChange={e => setRule(Number(e.target.value) as RuleSet)}><option value={0}>Official Rules — no stacking</option><option value={1}>House Rules — same-card stacking</option></select><button className="button button-primary button-wide" disabled={busy}>{busy ? 'Please wait…' : '＋ Create private room'}<span>→</span></button></form>
          <form onSubmit={join} className="join-form"><div className="section-label"><span>OR JOIN FRIENDS</span><span className="line" /></div><div className="join-row"><input aria-label="Room code" value={codeInput} onChange={e => setCodeInput(e.target.value.toUpperCase())} maxLength={6} placeholder="ROOM CODE" /><button className="button button-secondary" disabled={busy}>Join room</button></div></form>
        </div>
      </section> : <section className="room-view">
        <div className="room-heading"><div><span className="eyebrow"><span /> PRIVATE TABLE</span><h1>Room <span className="code-inline">{room.code}</span></h1><p>{ruleName(room.ruleSet)} <span className="bullet">·</span> {room.players.length}/{room.maximumPlayers} players <span className="bullet">·</span> {roomStatus(room.status)}</p></div><button className="button button-secondary" onClick={() => void copyCode(room.code, setNotice)}>▣ Copy room code</button></div>
        {room.status === 0 ? <div className="lobby-grid">
          <div className="panel players-panel"><div className="panel-title"><div><span className="mini-label">THE TABLE</span><h2>Players <span className="muted-count">{room.players.length}/{room.maximumPlayers}</span></h2></div><span className="waiting-tag"><i /> Waiting</span></div><div className="player-list">{room.players.map((p, i) => <div className="player-row" key={p.id}><div className={'avatar avatar-' + (i % 4)}>{p.displayName.slice(0,1).toUpperCase()}</div><div className="player-info"><strong>{p.displayName}{p.id === access.playerId ? ' (you)' : ''}</strong><span>{p.isCreator ? 'Room host' : 'Player'}</span></div>{p.isCreator && <span className="host-tag">HOST</span>}</div>)}{Array.from({ length: Math.max(0, room.maximumPlayers - room.players.length) }).map((_, i) => <div className="player-row empty-seat" key={i}><div className="avatar avatar-empty">+</div><div className="player-info"><strong>Open seat</strong><span>Share the room code to invite</span></div></div>)}</div><div className="invite-strip"><span className="invite-icon">↗</span><div><strong>Bring your crew</strong><p>Send them the room code to join this table.</p></div><button className="text-button" onClick={() => void copyCode(room.code, setNotice)}>Copy code</button></div></div>
          <div className="panel settings-panel"><span className="mini-label">ROOM SETTINGS</span><h2>Set the vibe.</h2><p className="panel-description">Choose how this game plays. Settings lock once the host starts the match.</p><label className="rule-option"><input type="radio" checked={room.ruleSet === 0} disabled={!isHost || busy} onChange={() => void changeRule(0)} /><span className="rule-radio" /><span className="rule-copy"><strong>Official Rules</strong><small>No stacking. Draw penalties must be taken.</small></span><span className="rule-check">✓</span></label><label className="rule-option"><input type="radio" checked={room.ruleSet === 1} disabled={!isHost || busy} onChange={() => void changeRule(1)} /><span className="rule-radio" /><span className="rule-copy"><strong>House Rules</strong><small>Stack matching +2 on +2 and +4 on +4.</small></span><span className="rule-check">✓</span></label>{!isHost && <p className="host-note">Only the room host can change rules or start the game.</p>}<div className="start-area"><div><strong>{room.players.length < 2 ? 'Waiting for one more player' : 'All set?'}</strong><span>{room.players.length < 2 ? 'At least 2 players are required.' : 'Everyone is in. Let the chaos begin.'}</span></div><button className="button button-primary" onClick={() => void startGame()} disabled={!isHost || room.players.length < 2 || busy}>{busy ? 'Starting…' : 'Start game'} <span>→</span></button></div></div>
        </div> : game ? <div className="game-layout">
          <div className="panel game-panel"><div className="game-toolbar"><div><span className="mini-label">MATCH IN PROGRESS</span><h2>{game.gameStatus === 1 ? 'Game complete' : myTurn ? 'Your turn!' : 'Waiting on ' + (game.players.find(p => p.playerId === game.currentPlayerId)?.displayName || 'next player')}</h2></div><div className="turn-indicator"><span className={'active-color-dot ' + colorClass(game.activeColor)} />{colorName(game.activeColor)} active</div></div>
            <div className="table-felt"><div className="opponents">{game.players.filter(p => p.playerId !== game.viewerPlayerId).map(p => <div className={'opponent ' + (p.playerId === game.currentPlayerId ? 'is-turn' : '')} key={p.playerId}><div className="opponent-avatar">{p.displayName.slice(0,1).toUpperCase()}</div><strong>{p.displayName}</strong><span>{p.cardCount} cards</span><div className="back-cards">{Array.from({ length: Math.min(p.cardCount, 5) }).map((_, i) => <span key={i} style={{ transform: 'translateX(' + (i * 7) + 'px) rotate(' + ((i - 2) * 7) + 'deg)' }} />)}</div>{p.playerId === game.currentPlayerId && <em>PLAYING</em>}</div>)}</div>
              <div className="center-piles"><button type="button" className="pile-count draw-pile-button" disabled={!myTurn || game.pendingDrawAmount > 0 || game.isWildDrawFourChallengePending || game.gameStatus === 1 || game.drawPileCount === 0 || busy} onClick={() => void act('DrawCard')} aria-label={myTurn ? "Draw one card from the draw pile" : "Wait for your turn to draw"} title={myTurn ? "Click to draw one card" : "Wait for your turn"}><span className="card-back">UNO</span><strong>{game.drawPileCount}</strong><small>{myTurn ? "DRAW · CLICK" : "DRAW PILE"}</small></button><div className="top-card-wrap"><span className="pile-caption">TOP DISCARD</span><div className={'uno-card top-card ' + colorClass(game.topDiscard.color)}><span className="card-corner">{cardLabel(game.topDiscard)}</span><b>{cardLabel(game.topDiscard)}</b><span className="card-logo">UNO</span></div></div><div className="pile-count direction-count"><strong>{game.turnDirection === 1 ? '↻' : '↺'}</strong><small>DIRECTION</small><span>{game.pendingDrawAmount > 0 ? '+' + game.pendingDrawAmount + ' pending' : 'No penalty'}</span></div></div>
              {game.isWildDrawFourChallengePending && <div className="challenge-banner">Wild Draw Four played — accept or challenge.</div>}{catchTarget && <div className="challenge-banner uno-banner">{catchUnoReady ? <>{catchTarget.displayName} missed UNO! <button onClick={() => void act('CatchUno', catchTarget.playerId)}>Catch UNO (+2 cards)</button></> : <>{catchTarget.displayName} has 1 card left — waiting 3 seconds for UNO…</>}</div>}
              <div className="my-seat"><div className="my-seat-label"><div><span className="mini-label">YOUR HAND</span><h3>{viewer?.hand?.length ?? 0} cards <span>· {myTurn ? 'Your turn to play' : 'Waiting for your turn'}</span></h3></div><button className="uno-call-button" disabled={(viewer?.hand?.length ?? 0) !== 1} onClick={() => void act('CallUno')}>UNO!</button></div><div className="hand-cards">{(viewer?.hand ?? []).map((card, i) => <button key={card.color + '-' + card.type + '-' + card.number + '-' + i} className={'uno-card hand-card ' + colorClass(card.color)} disabled={!myTurn} onClick={() => play(i, card)} title={'Play ' + colorName(card.color) + ' ' + cardLabel(card)}><span className="card-corner">{cardLabel(card)}</span><b>{cardLabel(card)}</b><span className="card-logo">UNO</span></button>)}</div>
                {wildIndex !== null && <div className="color-picker"><strong>Choose a color</strong>{([1,2,3,4] as Color[]).map(c => <button key={c} className={'color-choice ' + colorClass(c)} onClick={() => chooseColor(c)}>{colorName(c)}</button>)}<button className="text-button" onClick={() => setWildIndex(null)}>Cancel</button></div>}
                <div className="action-bar"><span>{myTurn ? 'Your move' : 'Other player’s turn'}</span><div>{game.isWildDrawFourChallengePending ? <><button className="button button-secondary" onClick={() => void act('AcceptWildDrawFour')}>Accept +4</button><button className="button button-primary" onClick={() => void act('ChallengeWildDrawFour')}>Challenge</button></> : game.pendingDrawAmount > 0 ? <button className="button button-primary" disabled={!myTurn} onClick={() => void act('AcceptDrawPenalty')}>Draw {game.pendingDrawAmount}</button> : <><button className="button button-secondary" disabled={!myTurn} onClick={() => void act('DrawCard')}>Draw card</button><button className="button button-secondary" disabled={!myTurn} onClick={() => void act('PassAfterDraw')}>Pass</button></>}</div></div>
              </div>
            </div><div className="game-footnote">Moves are validated by the server. Opponents cannot see your hand.</div>
          </div><aside className="panel sidebar-panel"><span className="mini-label">MATCH INFO</span><h2>At the table</h2><div className="info-row"><span>Rules</span><strong>{ruleName(game.ruleSet)}</strong></div><div className="info-row"><span>Draw pile</span><strong>{game.drawPileCount} cards</strong></div><div className="info-row"><span>Discard pile</span><strong>{game.discardPileCount} cards</strong></div><div className="info-row"><span>Direction</span><strong>{game.turnDirection === 1 ? 'Clockwise' : 'Counter-clockwise'}</strong></div><div className="info-row"><span>Players</span><strong>{game.players.length}</strong></div><button className="button button-secondary button-wide" onClick={() => void act('GetGameState')}>Refresh state</button><p className="sidebar-note">If a move is rejected, the server's latest state will be requested automatically.</p></aside>
        </div> : <div className="panel loading-panel"><div className="spinner" /><h2>Getting the table ready</h2><p>Waiting for the live game state…</p><button className="button button-secondary" onClick={() => void refresh()}>Refresh room</button></div>}
      </section>}
      {access && game && game.gameStatus !== 1 && unoAnnouncement && <div className="uno-celebration-layer" key={unoAnimationKey} role="status" aria-live="polite"><div className="uno-celebration-card"><span className="uno-burst" aria-hidden="true">✦</span><span className="uno-word">UNO!</span><strong>{unoAnnouncement}</strong><span className="uno-subtitle">ONE CARD LEFT. MAKE IT COUNT.</span></div></div>}
      {access && game && game.gameStatus === 1 && <div className="game-over-overlay" role="dialog" aria-modal="true" aria-labelledby="game-over-title"><div className="game-over-card"><div className="winner-medallion" aria-hidden="true">🏆</div><span className="mini-label">MATCH COMPLETE</span><h2 id="game-over-title">{winner ? winner.displayName + " wins!" : "Game over!"}</h2><p>{winner ? "A perfect finish. The table belongs to " + winner.displayName + "." : "This match has ended. Ready for another round?"}</p><div className="winner-stats"><span><strong>{game.players.length}</strong><small>PLAYERS</small></span><span><strong>{game.discardPileCount}</strong><small>CARDS PLAYED</small></span><span><strong>{game.ruleSet === 1 ? "HOUSE" : "OFFICIAL"}</strong><small>RULE SET</small></span></div><button className="button button-primary winner-exit" onClick={() => void leave()}>Back to room menu <span>→</span></button><p className="result-footnote">GG! Thanks for playing.</p></div></div>}
      {error && <div className="toast toast-error" role="alert"><span>!</span><div><strong>Something went wrong</strong><p>{error}</p></div><button onClick={() => setError('')} aria-label="Dismiss error">×</button></div>}
      {notice && !error && <div className="toast toast-success" role="status"><span>✓</span><p>{notice}</p><button onClick={() => setNotice('')} aria-label="Dismiss notice">×</button></div>}
    </main><footer className="footer"><span>UNO<span className="brand-dot">.</span></span><span>Built for friendly rivalries.</span><span>Classic deck · Real-time play</span></footer>
  </div>;
}

async function copyCode(value: string, notify: (message: string) => void) {
  try { await navigator.clipboard.writeText(value); notify('Room code copied to clipboard.'); }
  catch { notify('Room code: ' + value + '. Clipboard access was blocked by the browser.'); }
}
