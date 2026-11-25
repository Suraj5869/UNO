import { AudioControls, useGameFeedback } from './GameFeedback';
import { playCue } from './gameAudio';
import SocialPanel from './SocialPanel';
import './matchmaking.css';
import './bots.css';
import LoadingScreen from './LoadingScreen';
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type CSSProperties } from 'react';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import './styles.css';
import ProfileMenu from './ProfileMenu';
import { MatchReward } from './ProgressionPanel';
import AuthGate, { accountRequest, type Profile } from './AuthGate';

const API = (import.meta.env.VITE_API_URL || 'https://localhost:7137').replace(/\/$/, '');
const HUB = import.meta.env.VITE_SIGNALR_URL || API + '/hubs/uno-room';
type RuleSet = 0 | 1;
type Color = 0 | 1 | 2 | 3 | 4;
interface Player { id: string; displayName: string; isCreator: boolean; gamePlayerId: string | null }
interface Room { snapshotSequence: number; id: string; code: string; maximumPlayers: number; ruleSet: RuleSet; status: number; players: Player[]; gameStatus: number | null }
interface Access { room: Room; playerId: string; accessToken: string }
interface Card { color: Color; type: number; number: number | null }
interface GamePlayer { playerId: string; displayName: string; cardCount: number; hand: Card[] | null }
interface Game { matchId: string; snapshotSequence: number; roomCode: string; roomStatus: number; gameStatus: number; ruleSet: RuleSet; viewerPlayerId: string; currentPlayerId: string; activeColor: Color; topDiscard: Card; drawPileCount: number; discardPileCount: number; turnDirection: number; pendingDrawAmount: number; pendingDrawType: number | null; isWildDrawFourChallengePending: boolean; canPassAfterDraw: boolean; justDrawnCard: Card | null; turnDeadlineUtc: string; playerAwaitingUnoCallId: string | null; players: GamePlayer[] }
interface GameActionEvent { eventSequence: number; action: 'CardPlayed' | 'CardDrawn' | 'CardsDrawn'; playerId: string; card: Card | null; drawCount: number; drawTargetPlayerId: string | null; occurredAtUtc: string }

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
  return <AuthGate>{(player, logout, updateProfile) => <GameApp key={player?.userId ?? 'guest'} player={player} logout={logout} updateProfile={updateProfile}/>}</AuthGate>;
}

function GameApp({ player, logout, updateProfile }: { player: Profile | null; logout: () => Promise<void>; updateProfile: (p: Profile) => void }) {
  const [botCount,setBotCount]=useState(1);
  const [botDifficulty,setBotDifficulty]=useState(1);
  const [name, setName] = useState(player?.displayName ?? '');
  const [entryMode, setEntryMode] = useState<'home' | 'friends' | 'create' | 'match' | 'bots'>('home');
  const [matchPlayers,setMatchPlayers]=useState(2);
  const [matchRules,setMatchRules]=useState<RuleSet>(0);
  const [quickMatch, setQuickMatch] = useState(() => sessionStorage.getItem('uno-quick-match') === 'true');
  const [codeInput, setCodeInput] = useState('');
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
  const [clockNow, setClockNow] = useState(Date.now());
  const [gameAction, setGameAction] = useState<GameActionEvent | null>(null);
  const lastActionSequence = useRef(0);
  const completedGameRef = useRef(false);
  const lastRoomSequence = useRef(0);
  const lastGameSequence = useRef(0);
  const acceptGame = useCallback((value: Game) => {
    if (value.snapshotSequence <= lastGameSequence.current) return;
    lastGameSequence.current = value.snapshotSequence;
    completedGameRef.current = value.gameStatus === 1;
    setGame(value);
  }, []);
  const acceptRoom = useCallback((value: Room) => {
    if (value.snapshotSequence <= lastRoomSequence.current) return false;
    lastRoomSequence.current = value.snapshotSequence;
    if (value.status === 0) lastGameSequence.current = Math.max(lastGameSequence.current, value.snapshotSequence);
    setRoom(value);
    return true;
  }, []);

  // Notifications clear automatically; errors stay visible a little longer.
  useEffect(() => {
    if (!error && !notice) return;
    const timeout = window.setTimeout(() => {
      if (error) setError('');
      if (notice) setNotice('');
    }, error ? 6500 : 3600);
    return () => window.clearTimeout(timeout);
  }, [error, notice]);

  const api = useCallback(async <T,>(path: string, init?: RequestInit): Promise<T> => {
    if (player && init?.method === 'POST' && (path === '/api/rooms' || path === '/api/rooms/matchmaking' || path === '/api/rooms/practice' || path.endsWith('/join'))) return accountRequest<T>(path, init.body ? JSON.parse(String(init.body)) : undefined, 'POST');
    const response = await fetch(API + path, { ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } });
    if (!response.ok) throw new Error(await messageFor(response));
    return response.status === 204 ? undefined as T : await response.json() as T;
  }, [player?.userId]);

  const keepAccess = useCallback((value: Access) => {
    lastRoomSequence.current = value.room.snapshotSequence;
    if (value.room.status === 0) lastGameSequence.current = value.room.snapshotSequence;
    setAccess(value); setRoom(value.room); setRule(value.room.ruleSet);
    sessionStorage.setItem('uno-room-access', JSON.stringify(value));
  }, []);

  const startPractice = async () => {
    if(!name.trim()){setError('Enter a display name first.');return;}
    setBusy(true);setError('');
    try {
      const value=await api<Access>('/api/rooms/practice',{method:'POST',body:JSON.stringify({displayName:name.trim(),botCount,difficulty:botDifficulty,ruleSet:rule})});
      setEntryMode('home');keepAccess(value);
    } catch(e){setError(e instanceof Error?e.message:'Could not start practice.');}
    finally{setBusy(false);}
  };
  const addBots = async () => {
    if(!access)return;
    setBusy(true);setError('');
    try { const updated=await api<Room>('/api/rooms/'+encodeURIComponent(access.room.code)+'/bots',{method:'POST',body:JSON.stringify({accessToken:access.accessToken,botCount:Math.min(botCount,(room?.maximumPlayers??10)-(room?.players.length??1)),difficulty:botDifficulty})});setRoom(updated); }
    catch(e){setError(e instanceof Error?e.message:'Could not add bots.');}
    finally{setBusy(false);}
  };
  const botSettings=(maximum:number)=><div className="bot-settings"><label>Bot opponents<select value={Math.min(botCount,maximum)} onChange={e=>setBotCount(Number(e.target.value))}>{Array.from({length:maximum},(_,i)=><option key={i+1} value={i+1}>{i+1}</option>)}</select></label><label>Difficulty<select value={botDifficulty} onChange={e=>setBotDifficulty(Number(e.target.value))}><option value={0}>Easy</option><option value={1}>Medium</option><option value={2}>Hard</option></select></label></div>;

  const create = async (event: FormEvent) => {
    event.preventDefault(); setError(''); setNotice('');
    if (!name.trim()) { setError('Enter a display name first.'); return; }
    setBusy(true);
    try {
      const value = await api<Access>('/api/rooms', { method: 'POST', body: JSON.stringify({ creatorDisplayName: name.trim(), maximumPlayers: 10, ruleSet: rule }) });
      keepAccess(value); setNotice('Room ' + value.room.code + ' created. Share the code with your friends.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not create room.'); }
    finally { setBusy(false); }
  };

  const findMatch = async () => {
    if (!name.trim()) { setError('Enter a display name first.'); return; }
    setBusy(true); setError('');
    try {
      const value = await api<Access>('/api/rooms/matchmaking', { method: 'POST', body: JSON.stringify({ displayName: name.trim(), maximumPlayers:matchPlayers, ruleSet:matchRules }) });
      sessionStorage.setItem('uno-quick-match', 'true');
      setQuickMatch(true); setEntryMode('home'); keepAccess(value);
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not find a match.'); }
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

  useEffect(() => {
    const interval = window.setInterval(() => setClockNow(Date.now()), 250);
    return () => window.clearInterval(interval);
  }, []);

  const roomCode = access?.room.code;
  const refresh = useCallback(async () => {
    if (!roomCode) return;
    try {
      const value = await api<Room>('/api/rooms/' + encodeURIComponent(roomCode));
      if (!acceptRoom(value)) return;
      setAccess(old => {
        if (!old) return old;
        const next = { ...old, room: value };
        sessionStorage.setItem('uno-room-access', JSON.stringify(next));
        return next;
      });
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not refresh room.'); }
  }, [roomCode, api, acceptRoom]);

  useEffect(() => {
    if (!access) return;
    let active = true;
    lastActionSequence.current = 0;
    const connection = new HubConnectionBuilder().withUrl(HUB).withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    connection.on('RoomUpdated', (value: Room) => {
      if (!active || completedGameRef.current) return;
      if (!acceptRoom(value)) return;
      setAccess(old => {
        if (!old) return old;
        const next = { ...old, room: value };
        sessionStorage.setItem('uno-room-access', JSON.stringify(next));
        return next;
      });
    });
    connection.on('GameStateUpdated', (value: Game) => { if (active) { acceptGame(value); } });
    connection.on('GameAction', (event: GameActionEvent) => {
      if (!active || event.eventSequence <= lastActionSequence.current) return;
      lastActionSequence.current = event.eventSequence;
      setGameAction(event);
      window.setTimeout(() => setGameAction(current => current?.eventSequence === event.eventSequence ? null : current), 900);
    });
    connection.on('UnoCalled', (displayName: string) => {
      if (!active) return;
      playCue('uno');
      const announcement = displayName + ' says UNO!';
      setUnoAnnouncement(announcement);
      setUnoAnimationKey(key => key + 1);
      window.setTimeout(() => setUnoAnnouncement(current => current === announcement ? '' : current), 3200);
    });
    connection.onreconnected(async () => {
      try {
        await connection.invoke('JoinRoom', access.room.code, access.accessToken);
        const value = await connection.invoke<Game>('GetGameState', access.room.code, access.accessToken).catch(() => null);
        if (value && active) { acceptGame(value); }
        if (active) setNotice('Reconnected to the room.');
      } catch (e) { if (active) setError(e instanceof Error ? e.message : 'Could not rejoin room.'); }
    });
    connection.start().then(async () => {
      if (!active) { await connection.stop(); return; }
      await connection.invoke('JoinRoom', access.room.code, access.accessToken);
      if (active) setHub(connection);
      const value = await connection.invoke<Game>('GetGameState', access.room.code, access.accessToken).catch(() => null);
      if (value && active) { acceptGame(value); }
      await refresh();
    }).catch(e => { if (active) setError('Realtime connection failed: ' + (e instanceof Error ? e.message : 'check API URL and HTTPS certificate')); });
    return () => { active = false; setHub(old => old === connection ? null : old); void connection.stop(); };
  }, [access?.playerId, access?.room.code, access?.accessToken, refresh]);

  // A ref closes the gap before React commits disabled button state.
  const pendingAction = useRef<symbol | null>(null);
  const actionAccess = useRef(access);
  actionAccess.current = access;
  const act = useCallback(async (method: string, ...args: unknown[]) => {
    if (pendingAction.current) return;
    if (!hub || hub.state !== 'Connected' || !access) { setError('Realtime connection is not ready. Wait a moment and retry.'); return; }
    const ticket = Symbol(method);
    pendingAction.current = ticket;
    const stillInRoom = () => actionAccess.current?.accessToken === access.accessToken
      && actionAccess.current?.room.code === access.room.code;
    setBusy(true);
    setWildIndex(null);
    setError(''); setNotice('');
    try {
      await hub.invoke(method, access.room.code, access.accessToken, ...args);
    } catch (e) {
      if (stillInRoom()) setError(e instanceof Error ? e.message : 'That action was rejected.');
    } finally {
      // Reconcile the hand before allowing another index-based action.
      try {
        if (stillInRoom()) {
          const latest = await hub.invoke<Game>('GetGameState', access.room.code, access.accessToken);
          if (stillInRoom()) acceptGame(latest);
        }
      } catch { /* Reconnect and broadcast handlers also reconcile state. */ }
      if (pendingAction.current === ticket) {
        pendingAction.current = null;
        if (stillInRoom()) setBusy(false);
      }
    }
  }, [access, hub, acceptGame]);

  const changeRule = async (value: RuleSet) => {
    if (!access || !room) return;
    setBusy(true); setError('');
    try {
      const updated = await api<Room>('/api/rooms/' + encodeURIComponent(room.code) + '/rules', { method: 'PUT', body: JSON.stringify({ accessToken: access.accessToken, ruleSet: value }) });
      acceptRoom(updated); setRule(value); setNotice('Rules set to ' + ruleName(value) + '.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not change rules.'); }
    finally { setBusy(false); }
  };

  const startGame = async () => {
    if (!access) return;
    setBusy(true); setError('');
    try {
      const updated = await api<Room>('/api/rooms/' + encodeURIComponent(access.room.code) + '/start', { method: 'POST', body: JSON.stringify({ accessToken: access.accessToken }) });
      acceptRoom(updated); setNotice('Game started! Good luck.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not start the game.'); }
    finally { setBusy(false); }
  };

  const playAgain = async () => {
    if (!access) return;
    setBusy(true); setError('');
    try {
      const value = await api<Access>('/api/rooms/' + encodeURIComponent(access.room.code) + '/play-again', {
        method: 'POST',
        body: JSON.stringify({ accessToken: access.accessToken })
      });
      keepAccess(value);
      completedGameRef.current = false;
      setGame(null);
      setWildIndex(null);
      setGameAction(null);
      sessionStorage.removeItem('uno-quick-match'); setQuickMatch(false);
      setNotice('You are in the rematch lobby. Invite your friends or wait for them to join.');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not prepare the rematch lobby.');
    } finally {
      setBusy(false);
    }
  };

  const leave = async () => {
    let leaveError = '';
    if(quickMatch && room?.status===0 && access) {
      setBusy(true);
      try { await api<void>('/api/rooms/'+encodeURIComponent(access.room.code)+'/cancel-search',{method:'POST',body:JSON.stringify({accessToken:access.accessToken})}); }
      catch(e){setError(e instanceof Error?e.message:'Unable to cancel search');setBusy(false);void refresh();return;}
    } else if (hub?.state === 'Connected' && access) {
      try { await hub.invoke('LeaveRoom', access.room.code, access.accessToken); }
      catch (e) { leaveError = e instanceof Error ? e.message : 'Could not notify the room that you left.'; }
    }
    await hub?.stop().catch(() => undefined);
    sessionStorage.removeItem('uno-room-access');
    sessionStorage.removeItem('uno-quick-match'); setQuickMatch(false); setEntryMode('home');
    pendingAction.current = null; setBusy(false);
    setAccess(null); setRoom(null); setGame(null); setHub(null); completedGameRef.current = false; lastRoomSequence.current = 0; lastGameSequence.current = 0;
    setError(leaveError);
    setNotice(leaveError ? '' : 'You left the room.');
  };

  const viewer = useMemo(() => game?.players.find(p => p.playerId === game.viewerPlayerId), [game]);
  const myTurn = !!game && game.currentPlayerId === game.viewerPlayerId;
  useEffect(() => { if (!myTurn) setWildIndex(null); }, [myTurn]);
  const isHost = !!access && !!room?.players.find(p => p.id === access.playerId)?.isCreator;
  const winner = game?.gameStatus === 1 ? game.players.find(p => p.cardCount === 0) : undefined;
  const catchTarget = game?.playerAwaitingUnoCallId && game.playerAwaitingUnoCallId !== game.viewerPlayerId ? game.players.find(p => p.playerId === game.playerAwaitingUnoCallId) : null;
  const remainingTurnMs = game ? Math.max(0, new Date(game.turnDeadlineUtc).getTime() - clockNow) : 0;
  const turnProgress = Math.min(1, remainingTurnMs / 30000);
  const turnUrgent = remainingTurnMs <= 10000;
  const feedback = useGameFeedback(game,gameAction,remainingTurnMs);
  const [dealing,setDealing]=useState(false);
  useEffect(()=>{if(!game?.matchId||game.gameStatus!==0){setDealing(false);return;}setDealing(true);const timer=window.setTimeout(()=>setDealing(false),850);return()=>window.clearTimeout(timer);},[game?.matchId,game?.gameStatus]);

  useEffect(() => {
    setCatchUnoReady(false);
    if (!game?.playerAwaitingUnoCallId || game.playerAwaitingUnoCallId === game.viewerPlayerId) return;
    const timer = window.setTimeout(() => setCatchUnoReady(true), 3000);
    return () => window.clearTimeout(timer);
  }, [game?.playerAwaitingUnoCallId, game?.viewerPlayerId]);

  const finalActionCard = viewer?.hand?.length === 1 && viewer.hand[0].type !== 0;
  const canPlayCard = (card: Card) => {
    if (!game || !myTurn || game.gameStatus === 1 || busy || finalActionCard) return false;
    if (game.canPassAfterDraw) return !!game.justDrawnCard && card.color === game.justDrawnCard.color && card.type === game.justDrawnCard.type && card.number === game.justDrawnCard.number;
    if (game.isWildDrawFourChallengePending) return game.ruleSet === 1 && card.type === 5;
    if (game.pendingDrawAmount > 0) return game.ruleSet === 1 && card.type === game.pendingDrawType;
    return true;
  };

  const play = (index: number, card: Card) => {
    if (!canPlayCard(card)) return;
    if (card.type === 4 || card.type === 5) setWildIndex(index);
    else void act('PlayCard', index, 0);
  };
  const chooseColor = (color: Color) => { if (wildIndex === null) return; const index = wildIndex; setWildIndex(null); void act('PlayCard', index, color); };

  const orderedOpponents = game ? (() => {
    const viewerIndex = game.players.findIndex(p => p.playerId === game.viewerPlayerId);
    return game.players.filter(p => p.playerId !== game.viewerPlayerId)
      .sort((a, b) => {
        const ai = game.players.findIndex(p => p.playerId === a.playerId);
        const bi = game.players.findIndex(p => p.playerId === b.playerId);
        const aDistance = (ai - viewerIndex + game.players.length) % game.players.length;
        const bDistance = (bi - viewerIndex + game.players.length) % game.players.length;
        return aDistance - bDistance;
      });
  })() : [];

  return <div className={"app-shell" + (!access || !room ? " dashboard-shell" : room.status === 0 ? " lobby-shell" : " gameplay-shell")}>
    <header className="topbar"><div className="topbar-left"><a className="brand" href="#" onClick={e => e.preventDefault()}><span className="brand-mark">U</span><span>UNO<span className="brand-dot">.</span></span></a></div><div className="topbar-right">{!player && <AudioControls/>}{access && hub?.state !== 'Connected' && <span className="connection-pill">Connecting</span>}{!access && !player && <button className="button button-quiet button-small" onClick={() => void logout().catch(() => undefined)}>Exit guest</button>}{access && <button className="button button-quiet button-small" onClick={() => void leave()}>Leave room</button>}{player && <SocialPanel inRoom={!!access} room={access && room?.status===0 && !quickMatch ? {code:access.room.code,token:access.accessToken}:undefined} onJoin={async code=>{if(access)throw new Error('Leave your current room before joining an invitation.');const value=await api<Access>('/api/rooms/'+encodeURIComponent(code)+'/join',{method:'POST',body:JSON.stringify({displayName:player.displayName})});keepAccess(value);}}/>}{player && <ProfileMenu player={player} logout={logout} inRoom={!!access} onUpdate={p => { updateProfile(p); setName(p.displayName); }}/>}</div></header>
    <main className="main-content">
      {!access || !room ? <section className="batch2-screen">
        {entryMode === 'home' ? <><div className="dashboard-hero"><div><span className="dashboard-kicker">THE CLASSIC CARD GAME</span><h1>Ready to <em>play?</em></h1><p>{player ? 'Welcome back, ' + player.displayName + '!' : 'Welcome, player!'} Your next victory awaits.</p></div><img src={import.meta.env.BASE_URL + 'auth-cards.svg'} alt="" /></div><div className="dashboard-content">{!player && <div className="dashboard-guest"><label htmlFor="display-name">Your display name</label><input id="display-name" value={name} onChange={e => setName(e.target.value)} maxLength={24} placeholder="e.g. CardShark"/></div>}<h2>Choose how to play</h2><div className="dashboard-modes">
          <button className="dashboard-tile tile-match" disabled={busy} onClick={() => {setError("");setEntryMode("match");}}><span className="tile-art" aria-hidden="true"><svg viewBox="0 0 100 100"><path d="M57 6 19 55h30l-7 39 40-54H53z" fill="#fff7e8" stroke="#a86b0c" strokeWidth="3" strokeLinejoin="round"/></svg></span><strong>Play Match</strong><small>Find compatible online opponents</small><span className="tile-arrow">→</span></button>
          <button className="dashboard-tile tile-friends" disabled={busy} onClick={() => setEntryMode('friends')}><span className="tile-art" aria-hidden="true"><svg viewBox="0 0 100 100"><circle cx="34" cy="30" r="16" fill="#ffe5bb"/><circle cx="72" cy="36" r="13" fill="#ffe5bb"/><path d="M8 83V65q0-20 26-20t26 20v18z" fill="#fff7e8"/><path d="M64 55q27-7 29 17v11H65z" fill="#b8f7df"/></svg></span><strong>Play with Friends</strong><small>Create or join a private room</small><span className="tile-arrow">→</span></button>
          <button className="dashboard-tile tile-bots" disabled={busy} onClick={()=>{setError("");setEntryMode("bots");}}><span className="tile-art" aria-hidden="true"><svg viewBox="0 0 100 100"><rect x="15" y="29" width="70" height="52" rx="18" fill="#d7d7ff"/><path d="M50 29V15" stroke="#d7d7ff" strokeWidth="6"/><circle cx="50" cy="12" r="6" fill="#ffc83d"/><circle cx="36" cy="51" r="7" fill="#3436a8"/><circle cx="64" cy="51" r="7" fill="#3436a8"/><path d="M38 66h24" stroke="#3436a8" strokeWidth="4" strokeLinecap="round"/></svg></span><strong>Play vs Bots</strong><small>Practice your skills</small><span className="tile-arrow">→</span></button>
          
        </div><h2>Explore game modes</h2><div className="variant-strip"><div className="variant-active"><strong>Classic UNO</strong><small>Ready to play</small></div><div><strong>UNO Flip</strong><small>Coming soon</small></div><div><strong>No Mercy</strong><small>Coming soon</small></div></div></div></> : entryMode==='match'||entryMode==='bots' ? <div className="batch2-room-layout play-setup"><div className="batch2-room-intro"><button className="batch2-back" disabled={busy} onClick={()=>{setError('');setEntryMode('home');}}>← Back to home</button><h1>{entryMode==='match'?'Play Match':'Play vs Bots'}</h1><p>{entryMode==='match'?'Find online opponents who play by your rules.':'Sharpen your skills at your own pace.'}</p><img src={import.meta.env.BASE_URL+'auth-cards.svg'} alt=""/></div><div className="batch2-room-panel"><h2>Match settings</h2>{!player&&<div className="dashboard-guest"><label htmlFor="display-name">Your display name</label><input id="display-name" value={name} onChange={e=>setName(e.target.value)} maxLength={24} placeholder="e.g. CardShark"/></div>}{entryMode==='match'?<><div className="play-setup-fields"><label>Players per match<select value={matchPlayers} onChange={e=>setMatchPlayers(Number(e.target.value))} disabled={busy}>{Array.from({length:9},(_,i)=><option key={i+2} value={i+2}>{i+2} players</option>)}</select></label><label>Match rules<select value={matchRules} onChange={e=>setMatchRules(Number(e.target.value) as RuleSet)} disabled={busy}><option value={0}>Official</option><option value={1}>House</option></select></label></div><button className="batch2-primary" disabled={busy||!name.trim()} onClick={()=>void findMatch()}>{busy?'Finding match…':'Find match →'}</button></>:<><div className="play-setup-fields">{botSettings(9)}<label>Match rules<select value={rule} disabled={busy} onChange={e=>setRule(Number(e.target.value) as RuleSet)}><option value={0}>Official</option><option value={1}>House</option></select></label></div><small className="batch2-hint">Practice matches do not award ranked XP.</small><button className="batch2-primary" disabled={busy||!name.trim()} onClick={()=>void startPractice()}>{busy?'Starting game…':'Start game →'}</button></>}</div></div> : <div className="batch2-room-layout"><div className="batch2-room-intro"><button className="batch2-back" disabled={busy} onClick={() => setEntryMode(entryMode === 'friends' ? 'home' : 'friends')}>← {entryMode === 'friends' ? 'Back to home' : 'Back to friends'}</button><h1>{entryMode === 'create' ? 'Create Room' : 'Play with Friends'}</h1><p>{entryMode === 'create' ? 'Your table. Your rules. Your crew.' : 'Bring your favorite people to the table.'}</p><img src={import.meta.env.BASE_URL + 'auth-cards.svg'} alt=""/></div><div className="batch2-room-panel">{!player && <div className="dashboard-guest"><label htmlFor="display-name">Your display name</label><input id="display-name" value={name} onChange={e => setName(e.target.value)} maxLength={24} placeholder="e.g. CardShark"/></div>}{entryMode === 'friends' ? <><h2>Private table</h2><button className="batch2-primary" disabled={busy} onClick={() => setEntryMode('create')}>＋ Create private room</button><div className="friends-divider"><span>OR JOIN FRIENDS</span></div><form onSubmit={join} className="join-form batch3-join"><label htmlFor="friend-code">Room code</label><input id="friend-code" aria-label="Room code" value={codeInput} onChange={e => setCodeInput(e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, ''))} maxLength={6} required minLength={6} autoComplete="off" spellCheck={false} placeholder="Enter room code"/><button className="batch2-secondary" disabled={busy || codeInput.trim().length !== 6}>{busy ? "Joining…" : "Join room →"}</button></form></> : <form onSubmit={create} className="batch2-create-form"><h2>Room settings</h2><p className="batch2-hint">Up to 10 players · Start with at least 2 players.</p><fieldset><legend>Starting rules</legend>{([0,1] as RuleSet[]).map(value => <label key={value} className={'batch2-rule ' + (rule === value ? 'selected' : '')}><input type="radio" name="create-rules" checked={rule === value} disabled={busy} onChange={() => setRule(value)}/><span><strong>{ruleName(value)}</strong><small>{value === 0 ? 'Classic rules. No stacking.' : 'Stack matching +2 and +4 cards.'}</small></span></label>)}</fieldset><button className="batch2-primary" disabled={busy}>{busy ? 'Creating room…' : 'Create private room →'}</button><small className="batch2-hint">Share your room code with friends after creating.</small></form>}</div></div>}
      </section> : <section className="room-view">
        <div className="room-heading"><div><span className="eyebrow"><span /> {quickMatch ? "QUICK MATCH" : "PRIVATE TABLE"}</span><h1>Room <span className="code-inline">{room.code}</span></h1><p>{ruleName(room.ruleSet)} <span className="bullet">·</span> {room.players.length}/{room.maximumPlayers} players <span className="bullet">·</span> {roomStatus(room.status)}</p></div>{!quickMatch && <button className="button button-secondary" onClick={() => void copyCode(room.code, setNotice)}>▣ Copy room code</button>}</div>
        {room.status === 0 && quickMatch ? <div className="panel quick-search" role="status"><h2>Finding a match…</h2><p>{room.players.length} of {room.maximumPlayers} players found · {ruleName(room.ruleSet)}</p><progress aria-label="Matchmaking players found" value={room.players.length} max={room.maximumPlayers}/><p>Waiting for {room.maximumPlayers-room.players.length} more. Your match starts automatically.</p><button className="button button-secondary" disabled={busy} onClick={() => void leave()}>Cancel search</button></div> : room.status === 0 ? <div className="lobby-grid batch3-lobby">
          <div className="panel players-panel"><div className="panel-title"><div><span className="mini-label">THE TABLE</span><h2>Players <span className="muted-count">{room.players.length}/{room.maximumPlayers}</span></h2></div><span className="waiting-tag"><i /> Waiting</span></div><div className="player-list lobby-seats">{room.players.map((p, i) => <div className="player-row" key={p.id}><div className={'avatar avatar-' + (i % 4)}>{p.displayName.slice(0,1).toUpperCase()}</div><div className="player-info"><strong>{p.displayName}{p.id === access.playerId ? ' (you)' : ''}</strong><span>{p.isCreator ? 'Room host' : 'Player'}</span></div>{p.isCreator && <span className="host-tag">HOST</span>}</div>)}{Array.from({ length: Math.max(0, room.maximumPlayers - room.players.length) }).map((_, i) => <div className="player-row empty-seat" key={i}><div className="avatar avatar-empty">+</div><div className="player-info"><strong>Open seat</strong><span>Share the room code to invite</span></div></div>)}</div><div className="invite-strip"><span className="invite-icon">↗</span><div><strong>Bring your crew</strong><p>Send them the room code to join this table.</p></div><button className="text-button" onClick={() => void copyCode(room.code, setNotice)}>Copy code</button></div></div>
          <div className="panel settings-panel"><span className="mini-label">ROOM SETTINGS</span><h2>Set the vibe.</h2><p className="panel-description">Choose how this game plays. Settings lock once the host starts the match.</p><label className="rule-option"><input type="radio" checked={room.ruleSet === 0} disabled={!isHost || busy} onChange={() => void changeRule(0)} /><span className="rule-radio" /><span className="rule-copy"><strong>Official Rules</strong><small>No stacking. Draw penalties must be taken.</small></span><span className="rule-check">✓</span></label><label className="rule-option"><input type="radio" checked={room.ruleSet === 1} disabled={!isHost || busy} onChange={() => void changeRule(1)} /><span className="rule-radio" /><span className="rule-copy"><strong>House Rules</strong><small>Stack matching +2 on +2 and +4 on +4.</small></span><span className="rule-check">✓</span></label>{!isHost && <p className="host-note">Only the room host can change rules or start the game.</p>}{isHost && room.players.length<room.maximumPlayers && <section className="bot-practice"><h3>Add computer opponents</h3>{botSettings(room.maximumPlayers-room.players.length)}<button className="button button-secondary" disabled={busy} onClick={()=>void addBots()}>Add bots</button><small>Matches with bots do not award XP.</small></section>}<div className="start-area"><div><strong>{room.players.length < 2 ? 'Waiting for one more player' : 'All set?'}</strong><span>{room.players.length < 2 ? 'At least 2 players are required.' : 'Start now or wait for more friends.'}</span></div><button className="button button-primary" onClick={() => void startGame()} disabled={!isHost || room.players.length < 2 || busy}>{busy ? 'Starting…' : 'Start game'} <span>→</span></button></div></div>
        </div> : game ? <div className="game-layout">
          <div className="panel game-panel batch4-board"><div className="game-toolbar"><div><span className="mini-label">MATCH IN PROGRESS</span><h2>{game.gameStatus === 1 ? 'Game complete' : myTurn ? 'Your turn!' : 'Waiting on ' + (game.players.find(p => p.playerId === game.currentPlayerId)?.displayName || 'next player')}</h2></div></div>
            <div className={"table-felt"+(feedback.motion?"":" feedback-motion-off")+(dealing&&feedback.motion?" deal-feedback":"")} data-table-theme={player?.tableThemeKey} data-card-back={player?.cardBackKey} data-avatar={player?.avatarKey}><div className="arena-table" aria-hidden="true"/>{feedback.motion&&gameAction?.action==='CardPlayed'&&gameAction.card&&gameAction.card.type>0&&<div className="special-card-feedback" key={gameAction.eventSequence} data-kind={gameAction.card.type} aria-hidden="true"><span>{({1:"SKIP",2:"REVERSE",3:"+2",4:"WILD",5:"+4"} as Record<number,string>)[gameAction.card.type]}</span></div>}<div className="opponents">{orderedOpponents.map((p, index) => <div className={'opponent ' + (p.playerId === game.currentPlayerId ? 'is-turn' : '')} key={p.playerId} style={{ "--seat-x": (50 + 43 * Math.cos(Math.PI + Math.PI * (index + 1) / (orderedOpponents.length + 1))) + "%", "--seat-y": (39 + 23 * Math.sin(Math.PI + Math.PI * (index + 1) / (orderedOpponents.length + 1))) + "%" } as CSSProperties}><TurnAvatar label={p.displayName.slice(0,1).toUpperCase()} active={p.playerId === game.currentPlayerId} progress={turnProgress} urgent={turnUrgent} className="opponent-avatar" /><strong>{p.displayName}</strong><span>{p.cardCount} cards</span><div className="back-cards">{Array.from({ length: Math.min(p.cardCount, 5) }).map((_, i) => <span key={i} style={{ transform: 'translateX(' + (i * 7) + 'px) rotate(' + ((i - 2) * 7) + 'deg)' }} />)}</div>{p.playerId === game.currentPlayerId && <em>PLAYING</em>}</div>)}</div>
              <div className="center-piles"><button type="button" className="pile-count draw-pile-button" disabled={!myTurn || game.pendingDrawAmount > 0 || game.isWildDrawFourChallengePending || game.canPassAfterDraw || game.gameStatus === 1 || game.drawPileCount === 0 || busy} onClick={() => void act('DrawCard')} aria-label={myTurn ? "Draw one card from the draw pile" : "Wait for your turn to draw"} title={myTurn ? "Click to draw one card" : "Wait for your turn"}><span className="card-back">UNO</span><strong>{game.drawPileCount}</strong><small>{myTurn ? "DRAW · CLICK" : "DRAW PILE"}</small></button>{gameAction && <div className="action-animation-layer" key={gameAction.eventSequence} aria-hidden="true">{gameAction.action === 'CardPlayed' && gameAction.card ? <><div className={'action-flight play-flight ' + (gameAction.playerId === game.viewerPlayerId ? 'from-self' : 'from-opponent') + ' ' + colorClass(gameAction.card.color)}><span className="card-corner">{cardLabel(gameAction.card)}</span><b>{cardLabel(gameAction.card)}</b><span className="card-logo">UNO</span></div>{gameAction.drawCount > 0 && gameAction.drawTargetPlayerId && <div className={'action-flight draw-flight ' + (gameAction.drawTargetPlayerId === game.viewerPlayerId ? 'to-self' : 'to-opponent')}><span className="card-back">UNO</span><strong>+{gameAction.drawCount}</strong></div>}</> : <div className={'action-flight draw-flight ' + (gameAction.playerId === game.viewerPlayerId ? 'to-self' : 'to-opponent')}><span className="card-back">UNO</span>{gameAction.drawCount > 1 && <strong>+{gameAction.drawCount}</strong>}</div>}</div>}<div className="top-card-wrap"><span className="pile-caption">TOP DISCARD</span><div className={'uno-card top-card ' + colorClass(game.topDiscard.color) + (game.topDiscard.type === 4 || game.topDiscard.type === 5 ? ' selected-wild-color-' + colorClass(game.activeColor) : '')}><span className="card-corner">{cardLabel(game.topDiscard)}</span><b>{cardLabel(game.topDiscard)}</b><span className="card-logo">UNO</span></div></div><div className="pile-count direction-count"><strong>{game.turnDirection === 1 ? '↻' : '↺'}</strong><small>DIRECTION</small><span className={"active-color-chip active-color-" + colorClass(game.activeColor)} role="status" aria-live="polite"><i aria-hidden="true"/>Active color: {colorName(game.activeColor)}</span><span>{game.pendingDrawAmount > 0 ? '+' + game.pendingDrawAmount + ' pending' : 'No penalty'}</span></div></div>
              {catchTarget && <div className="challenge-banner uno-banner">{catchUnoReady ? <>{catchTarget.displayName} missed UNO! <button onClick={() => void act('CatchUno', catchTarget.playerId)}>Catch UNO (+2 cards)</button></> : <>{catchTarget.displayName} has 1 card left — waiting 3 seconds for UNO…</>}</div>}
              <div className="my-seat"><div className="my-seat-label"><div className="my-seat-identity"><TurnAvatar label={viewer?.displayName.slice(0,1).toUpperCase() || 'Y'} active={myTurn} progress={turnProgress} urgent={turnUrgent} className="my-seat-avatar" /><div><span className="mini-label">{myTurn ? 'YOUR TURN' : 'YOUR HAND'}</span><h3>{viewer?.hand?.length ?? 0} cards <span>· {finalActionCard ? 'Your last card must be a number' : myTurn ? 'Play before time runs out' : 'Waiting for your turn'}</span></h3></div></div><button className="uno-call-button" disabled={(viewer?.hand?.length ?? 0) !== 1} onClick={() => void act('CallUno')}>UNO!</button></div><div className="hand-cards" style={{"--hand-size": viewer?.hand?.length ?? 1} as CSSProperties}>{(viewer?.hand ?? []).map((card, i) => <button key={card.color + '-' + card.type + '-' + card.number + '-' + i} className={'uno-card hand-card ' + colorClass(card.color)} style={{'--deal-index': i, '--fan': Math.max(-5,Math.min(5,i-((viewer?.hand?.length ?? 1)-1)/2))} as CSSProperties} disabled={!canPlayCard(card)} onClick={() => play(i, card)} title={'Play ' + colorName(card.color) + ' ' + cardLabel(card)}><span className="card-corner">{cardLabel(card)}</span><b>{cardLabel(card)}</b><span className="card-logo">UNO</span></button>)}</div>
                {wildIndex !== null && <div className="color-picker"><strong>Choose a color</strong>{([1,2,3,4] as Color[]).map(c => <button key={c} className={'color-choice ' + colorClass(c)} onClick={() => chooseColor(c)}>{colorName(c)}</button>)}<button className="text-button" onClick={() => setWildIndex(null)}>Cancel</button></div>}
                {myTurn && game.isWildDrawFourChallengePending && <div className="decision-panel challenge-decision"><div><strong>Wild Draw Four · +{game.pendingDrawAmount}</strong><span>Accept the penalty or challenge the previous player's move.</span>{game.ruleSet === 1 && viewer?.hand?.some(card => card.type === 5) && <small>You can also stack another +4 by playing it from your hand.</small>}</div><div className="decision-actions"><button className="button button-secondary" disabled={busy} onClick={() => void act('AcceptWildDrawFour')}>Accept +{game.pendingDrawAmount}</button><button className="button button-primary" disabled={busy} onClick={() => void act('ChallengeWildDrawFour')}>Challenge</button></div></div>}
                {myTurn && game.pendingDrawAmount > 0 && !game.isWildDrawFourChallengePending && <div className="decision-panel"><div><strong>Draw penalty · {game.pendingDrawAmount} cards</strong><span>Accept the penalty or stack another +2 from your hand.</span></div><div className="decision-actions"><button className="button button-primary" disabled={busy} onClick={() => void act('AcceptDrawPenalty')}>Draw {game.pendingDrawAmount}</button></div></div>}
                {myTurn && game.canPassAfterDraw && <div className="decision-panel pass-decision"><div><strong>Playable card drawn</strong><span>Play the drawn card or pass to end your turn.</span></div><div className="decision-actions"><button className="button button-secondary" disabled={busy} onClick={() => void act('PassAfterDraw')}>Pass</button></div></div>}
              </div>
            </div><div className="game-footnote">Moves are validated by the server. Opponents cannot see your hand.</div>
          </div>
        </div> : <div className="panel loading-panel"><LoadingScreen title="Getting the table ready" compact/><button className="button button-secondary" onClick={() => void refresh()}>Refresh room</button></div>}
      </section>}
      {access && game && game.gameStatus !== 1 && unoAnnouncement && <div className="uno-celebration-layer" key={unoAnimationKey} role="status" aria-live="polite"><div className="uno-celebration-card"><span className="uno-burst" aria-hidden="true">✦</span><span className="uno-word">UNO!</span><strong>{unoAnnouncement}</strong><span className="uno-subtitle">ONE CARD LEFT. MAKE IT COUNT.</span></div></div>}
      {access && game && game.gameStatus === 1 && <div className={"game-over-overlay"+(feedback.motion?"":" feedback-motion-off")} role="dialog" aria-modal="true" aria-labelledby="game-over-title"><div className="game-over-card batch4-victory"><div className="victory-confetti" aria-hidden="true">{Array.from({length:24},(_,i)=><i key={i} style={{left:(i*37%100)+"%",top:(i*23%100)+"%", background:["#ffca27","#ff626b","#24bd98","#7d91ff"][i%4],animationDelay:(i%7)*.13+"s",transform:"rotate("+(i*31)+"deg)"}}/>)}</div><div className="winner-medallion" aria-hidden="true">🏆</div><span className="mini-label">{winner?.playerId === game.viewerPlayerId ? "VICTORY!" : "MATCH COMPLETE"}</span><h2 id="game-over-title">{winner ? winner.displayName + " wins!" : "Game over!"}</h2><p>{winner ? "A perfect finish. The table belongs to " + winner.displayName + "." : "This match has ended. Ready for another round?"}</p><div className="winner-stats"><span><strong>{game.players.length}</strong><small>PLAYERS</small></span><span><strong>{game.discardPileCount}</strong><small>CARDS PLAYED</small></span><span><strong>{game.ruleSet === 1 ? "HOUSE" : "OFFICIAL"}</strong><small>RULE SET</small></span></div>{player && <MatchReward key={game.matchId} matchId={game.matchId} player={player} onUpdate={updateProfile}/>}<div className="winner-actions"><button className="button button-primary winner-exit" disabled={busy} onClick={() => void playAgain()}>{busy ? 'Preparing lobby…' : 'Play Again'} <span>↻</span></button><button className="button button-secondary winner-home" disabled={busy} onClick={() => void leave()}>Back to Home</button></div><p className="result-footnote">Choose your next move. Players who opt into the rematch will meet in a fresh lobby.</p></div></div>}
      {error && <div className="toast toast-error" role="alert"><span>!</span><div><strong>Something went wrong</strong><p>{error}</p></div><button onClick={() => setError('')} aria-label="Dismiss error">×</button></div>}
      {notice && !error && <div className="toast toast-success" role="status"><span>✓</span><p>{notice}</p><button onClick={() => setNotice('')} aria-label="Dismiss notice">×</button></div>}
    </main><footer className="footer"><span>UNO<span className="brand-dot">.</span></span><span>Built for friendly rivalries.</span><span>Classic deck · Real-time play</span></footer>
  </div>;
}

function TurnAvatar({ label, active, progress, urgent, className }: { label: string; active: boolean; progress: number; urgent: boolean; className: string }) {
  const circumference = 144.51;
  return <div className="turn-avatar-frame"><svg className="turn-avatar-ring" viewBox="0 0 54 54" aria-hidden="true"><circle className="turn-ring-track" cx="27" cy="27" r="23" /><circle className={'turn-ring-progress ' + (active ? 'active ' : '') + (urgent && active ? 'urgent' : '')} cx="27" cy="27" r="23" style={{ strokeDashoffset: active ? circumference * (1 - progress) : circumference }} /></svg><div className={className}>{label}</div></div>;
}

async function copyCode(value: string, notify: (message: string) => void) {
  try { await navigator.clipboard.writeText(value); notify('Room code copied to clipboard.'); }
  catch { notify('Room code: ' + value + '. Clipboard access was blocked by the browser.'); }
}
