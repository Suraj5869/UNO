import { useEffect, useState, type FormEvent, type ReactNode } from 'react';

const API = (import.meta.env.VITE_API_URL || 'https://localhost:7137').replace(/\/$/, '');
export interface Profile { userId: string; publicPlayerId: string; email: string; username: string; displayName: string; avatarKey: string; level: number; totalXp: number; matchesPlayed: number; wins: number }
interface Session { accessToken: string; refreshToken: string; expiresIn: number; player: Profile; expiresAt: number }
const KEY = 'uno-account-session';
function read(): Session | null { try { return JSON.parse(sessionStorage.getItem(KEY) || 'null'); } catch { return null; } }
async function request<T>(path: string, body?: unknown, token?: string): Promise<T> {
  const response = await fetch(API + '/api/auth/' + path, { method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) {
    const data = await response.json().catch(() => null);
    throw new Error(data?.message || (data?.errors ? Object.values(data.errors).flat().join(' ') : data?.title) || 'Request failed (' + response.status + ')');
  }
  return response.status === 204 ? undefined as T : response.json();
}
function save(s: Omit<Session, 'expiresAt'>): Session {
  const value = { ...s, expiresAt: Date.now() + s.expiresIn * 1000 };
  sessionStorage.setItem(KEY, JSON.stringify(value)); return value;
}
let rotating: Promise<Session> | null = null;
function rotate(s: Session) {
  if (!rotating) rotating = request<Session>('refresh', { refreshToken: s.refreshToken }).then(save).finally(() => { rotating = null; });
  return rotating;
}
export default function AuthGate({ children }: { children: (player: Profile | null, logout: () => Promise<void>) => ReactNode }) {
  const [session, setSession] = useState<Session | null>(read);
  const [guest, setGuest] = useState(() => sessionStorage.getItem('uno-guest') === 'true');
  const [loading, setLoading] = useState(() => !!read());
  const [register, setRegister] = useState(false);
  const [identifier, setIdentifier] = useState('');
  const [username, setUsername] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => {
    const saved = read();
    if (!saved) return;
    let active = true;
    (async () => {
      try {
        let current = saved.expiresAt <= Date.now() + 60000 ? await rotate(saved) : saved;
        let player: Profile;
        try { player = await request<Profile>('me', undefined, current.accessToken); }
        catch { current = await rotate(current); player = await request<Profile>('me', undefined, current.accessToken); }
        if (active) { setSession({ ...current, player }); setGuest(false); }
      } catch { if (active) { sessionStorage.removeItem(KEY); setSession(null); setError('Session could not be restored. Please sign in again.'); } }
      finally { if (active) setLoading(false); }
    })();
    return () => { active = false; };
  }, []);
  useEffect(() => {
    if (!session || loading) return;
    let active = true;
    const timer = window.setTimeout(() => {
      void rotate(session).then(next => { if (active) setSession(next); }).catch(() => {
        if (active) { sessionStorage.removeItem(KEY); sessionStorage.removeItem('uno-room-access'); sessionStorage.removeItem('uno-quick-match'); setSession(null); setError('Session expired. Please sign in again.'); }
      });
    }, Math.max(0, session.expiresAt - Date.now() - 60000));
    return () => { active = false; window.clearTimeout(timer); };
  }, [session, loading]);
  const logout = async () => {
    try { const current = rotating ? await rotating : read(); if (current) await request('logout', { refreshToken: current.refreshToken }); }
    finally { for (const key of [KEY, 'uno-guest', 'uno-room-access', 'uno-quick-match']) sessionStorage.removeItem(key); setSession(null); setGuest(false); setPassword(''); }
  };
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setError('');
    if (register && password !== confirm) { setError('Passwords do not match.'); return; }
    setBusy(true);
    try {
      const result = await request<Session>(register ? 'register' : 'login', register ? { email: identifier.trim(), username: username.trim(), displayName: displayName.trim(), password } : { identifier: identifier.trim(), password });
      sessionStorage.removeItem('uno-guest'); sessionStorage.removeItem('uno-room-access'); sessionStorage.removeItem('uno-quick-match');
      setSession(save(result)); setGuest(false); setPassword(''); setConfirm('');
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to sign in.'); }
    finally { setBusy(false); }
  };
  if (loading) return <div className="auth-shell auth-loading" role="status"><div className="auth-spinner"/>Restoring your session…</div>;
  if (session || guest) return <>{children(session?.player ?? null, logout)}</>;
  return <main className="auth-shell"><div className="auth-layout">
    <section className="auth-intro"><span className="auth-kicker">CLASSIC UNO · PLAY TOGETHER</span><h1>One deck.<br/><em>Endless fun.</em></h1><p>Your friends, your cards, your next victory.</p><div className="card-art" aria-hidden="true"><div className="art-card art-red"><b>7</b><span>UNO</span></div><div className="art-card art-blue"><b>↻</b><span>UNO</span></div><div className="art-card art-yellow"><b>+2</b><span>UNO</span></div><div className="art-card art-green"><b>5</b><span>UNO</span></div></div></section>
    <section className="auth-panel"><span className="auth-kicker">YOUR NEXT MATCH AWAITS</span><h2>{register ? 'Join the fun!' : 'Welcome back!'}</h2><p>{register ? 'Create your player account.' : 'Sign in to keep your progress.'}</p>
    <form onSubmit={submit} className="auth-form">
      <label>{register ? 'Email' : 'Email or username'}<input required type={register ? 'email' : 'text'} value={identifier} onChange={e => setIdentifier(e.target.value)} maxLength={254} autoComplete="username"/></label>
      {register && <><label>Username<input required pattern="[A-Za-z0-9_]{3,24}" value={username} onChange={e => setUsername(e.target.value)} autoComplete="nickname"/></label><label>Display name<input required maxLength={24} value={displayName} onChange={e => setDisplayName(e.target.value)}/></label></>}
      <label>Password<div className="auth-password"><input required type={show ? 'text' : 'password'} minLength={register ? 8 : 1} maxLength={128} value={password} onChange={e => setPassword(e.target.value)} autoComplete={register ? 'new-password' : 'current-password'}/><button type="button" onClick={() => setShow(!show)}>{show ? 'Hide' : 'Show'}</button></div></label>
      {register && <label>Confirm password<input required type={show ? 'text' : 'password'} value={confirm} onChange={e => setConfirm(e.target.value)} autoComplete="new-password"/></label>}
      {error && <p className="auth-error" role="alert">{error}</p>}
      <button className="auth-primary" disabled={busy}>{busy ? 'Please wait…' : register ? 'Create account' : 'Sign in'}</button>
    </form>
    <button className="auth-link" disabled={busy} onClick={() => { setRegister(!register); setError(''); setPassword(''); setConfirm(''); }}>{register ? 'Already have an account? Sign in' : 'New here? Create an account'}</button>
    <button className="auth-guest" disabled={busy} onClick={() => { sessionStorage.setItem('uno-guest','true'); setGuest(true); }}>Play as guest</button><small>Guest progress is not saved.</small>
    </section></div></main>;
}
