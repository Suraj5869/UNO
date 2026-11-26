import LoadingScreen from './LoadingScreen';
import { useEffect, useState, type FormEvent, type ReactNode } from 'react';

const API = (import.meta.env.VITE_API_URL || 'https://localhost:7137').replace(/\/$/, '');
export interface Profile { userId: string; publicPlayerId: string; email: string; username: string; displayName: string; avatarKey: string; cardBackKey?: string; tableThemeKey?: string; level: number; totalXp: number; matchesPlayed: number; wins: number }
interface Session { accessToken: string; expiresIn: number; player: Profile; expiresAt: number }
const KEY = 'uno-account-session';
let memorySession: Session | null = null;
sessionStorage.removeItem(KEY);
function read(): Session | null { return memorySession; }
function cookieLock<T>(action: () => Promise<T>): Promise<T> {
  return navigator.locks ? navigator.locks.request('uno-auth-cookie', action) : action();
}
class ApiError extends Error {
  constructor(public readonly status: number, message: string) { super(message); }
}
const invalidSession = (error: unknown) => error instanceof ApiError && error.status === 401;
const SESSION_EVENT = 'uno-account-session-change';
function publishSession(value: Session | null) {
  memorySession = value;
  if (!value) {
    for (const key of [KEY, 'uno-room-access', 'uno-quick-match']) sessionStorage.removeItem(key);
  }
  window.dispatchEvent(new Event(SESSION_EVENT));
}
async function request<T>(path: string, body?: unknown, token?: string, method?: string): Promise<T> {
  const response = await fetch(API + (path.startsWith('/') ? path : '/api/auth/' + path), { method: method ?? (body === undefined ? 'GET' : 'POST'), credentials: 'include', headers: { 'X-Uno-Browser': '1', 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) {
    const data = await response.json().catch(() => null);
    throw new ApiError(response.status, data?.message || (data?.errors ? Object.values(data.errors).flat().join(' ') : data?.title) || 'Request failed (' + response.status + ')');
  }
  return response.status === 204 ? undefined as T : response.json();
}
function save(s: Omit<Session, 'expiresAt'>): Session {
  const { accessToken, expiresIn, player } = s;
  const value = { accessToken, expiresIn, player, expiresAt: Date.now() + s.expiresIn * 1000 };
  publishSession(value); return value;
}
let rotating: Promise<Session> | null = null;
let restoring: Promise<Session> | null = null;
function restoreSession(): Promise<Session> {
  if (!restoring) restoring = cookieLock(() => request<Session>('refresh', {})).finally(() => { restoring = null; });
  return restoring;
}
function rotate(s: Session): Promise<Session> {
  const latest = read();
  if (!latest || latest.player.userId !== s.player.userId) return Promise.reject(new Error('Session changed.'));
  if (latest.accessToken !== s.accessToken) return Promise.resolve(latest);
  if (!rotating) {
    rotating = cookieLock(() => request<Session>('refresh', {})).then(next => {
      if (read()?.accessToken !== s.accessToken) throw new Error('Session changed.');
      return save(next);
    }).catch(error => {
      if (invalidSession(error) && read()?.accessToken === s.accessToken) publishSession(null);
      throw error;
    }).finally(() => { rotating = null; });
  }
  return rotating;
}
export async function accountAccessToken(): Promise<string> {
  let current = read();
  if (!current) return '';
  if (current.expiresAt <= Date.now() + 60000) current = await rotate(current);
  return current.accessToken;
}
export function hasAccountSession() { return !!read(); }
export async function accountRequest<T>(path: string, body?: unknown, method?: string): Promise<T> {
  let current = read(); if (!current) throw new Error('Please sign in again.');
  if (current.expiresAt <= Date.now() + 60000) current = await rotate(current);
  try { return await request<T>(path, body, current.accessToken, method); }
  catch (error) {
    if (!invalidSession(error)) throw error;
    current = await rotate(current);
    return request<T>(path, body, current.accessToken, method);
  }
}
export default function AuthGate({ children }: { children: (player: Profile | null, logout: () => Promise<void>, updateProfile: (profile: Profile) => void) => ReactNode }) {
  const [session, setSession] = useState<Session | null>(read);
  const [guest, setGuest] = useState(() => sessionStorage.getItem('uno-guest') === 'true');
  const [loading, setLoading] = useState(() => sessionStorage.getItem('uno-guest') !== 'true');
  const [register, setRegister] = useState(false);
  const [identifier, setIdentifier] = useState('');
  const [username, setUsername] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [verifyToken, setVerifyToken] = useState(() => new URLSearchParams(window.location.hash.slice(1)).get('verify') || '');
  const resendVerification = async () => {
    if (!identifier.includes('@')) { setError('Enter your email address to request a verification email.'); return; }
    setBusy(true); setError('');
    try { await request('resend-verification', { email: identifier.trim() }); setNotice('If an eligible account exists, a verification email will be sent.'); }
    catch (e) { setError(e instanceof Error ? e.message : 'Unable to request verification.'); }
    finally { setBusy(false); }
  };
  const verifyEmail = async () => {
    setBusy(true); setError('');
    try { await request('verify-email', { token: verifyToken }); setVerifyToken(''); history.replaceState(null, '', location.pathname + location.search); setNotice('Email verified. Sign in to continue.'); }
    catch (e) { setError(e instanceof Error ? e.message : 'Verification failed.'); }
    finally { setBusy(false); }
  };
  useEffect(() => {
    const sync = () => setSession(read());
    window.addEventListener(SESSION_EVENT, sync);
    return () => window.removeEventListener(SESSION_EVENT, sync);
  }, []);
  useEffect(() => {
    if (sessionStorage.getItem('uno-guest') === 'true') return;
    if (read()) { setLoading(false); return; }
    let active = true;
    (async () => {
      try {
        const restored = await restoreSession();
        if (!active) return;
        save(restored);
        const player = restored.player;
        const current = read();
        if (active && current && current.player.userId === player.userId) {
          publishSession({ ...current, player }); setGuest(false);
        }
      } catch {
        if (active && !read()) publishSession(null);
      } finally { if (active) setLoading(false); }
    })();
    return () => { active = false; };
  }, []);
  useEffect(() => {
    if (!session || loading) return;
    let active = true;
    let timer: number;
    const refresh = async () => {
      if (!active) return;
      const current = read();
      if (!current) return;
      try { await rotate(current); }
      catch {
        if (!active) return;
        if (!read()) setError('Session expired. Please sign in again.');
        else timer = window.setTimeout(refresh, 15000);
      }
    };
    timer = window.setTimeout(refresh, Math.max(1000, session.expiresAt - Date.now() - 60000));
    return () => { active = false; window.clearTimeout(timer); };
  }, [session, loading]);
  const logout = async () => {
    try { const current = rotating ? await rotating : read(); if (current) await cookieLock(() => request('logout', {})); }
    finally { publishSession(null); for (const key of [KEY, 'uno-guest', 'uno-room-access', 'uno-quick-match']) sessionStorage.removeItem(key); setSession(null); setGuest(false); setPassword(''); }
  };
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setError('');
    if (register && password !== confirm) { setError('Passwords do not match.'); return; }
    setBusy(true);
    try {
      if (register) {
        await request('register', { email: identifier.trim(), username: username.trim(), displayName: displayName.trim(), password });
        setRegister(false); setPassword(''); setConfirm(''); setNotice('Check your email to verify your account before signing in.'); return;
      }
      const result = await cookieLock(() => request<Session>(register ? 'register' : 'login', register ? { email: identifier.trim(), username: username.trim(), displayName: displayName.trim(), password } : { identifier: identifier.trim(), password }));
      sessionStorage.removeItem('uno-guest'); sessionStorage.removeItem('uno-room-access'); sessionStorage.removeItem('uno-quick-match');
      setSession(save(result)); setGuest(false); setPassword(''); setConfirm('');
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to sign in.'); }
    finally { setBusy(false); }
  };
  if (verifyToken) return <main className="auth-shell"><section className="auth-panel"><h2>Verify your email</h2><p>Confirm your email address to activate your account.</p>{error && <p role="alert">{error}</p>}<button className="auth-primary" disabled={busy} onClick={verifyEmail}>Verify email</button></section></main>;
  if (loading) return <div className="auth-shell auth-loading"><LoadingScreen title="Restoring your session"/></div>;
  if (session || guest) return <>{children(session?.player ?? null, logout, player => { const current = read(); if (current && current.player.userId === player.userId) { const next = { ...current, player }; publishSession(next); } })}</>;
  return <main className={"auth-shell auth-redesign" + (register ? " auth-register" : "")}><div className="auth-floating" aria-hidden="true"><span>+2</span><span>↻</span><span>4</span><span>7</span></div><div className="auth-layout">
    <section className="auth-intro"><div className="auth-logo" aria-label="UNO">UNO</div><h1>{register ? 'Join the fun!' : 'Welcome back!'}</h1><p>Jump in and play your<br/> favorite <em>card game.</em></p><img className="auth-card-fan" src={import.meta.env.BASE_URL + 'auth-cards.svg'} alt="" /></section>
    <section className="auth-panel"><h2 id="auth-heading">{register ? 'Create Account' : 'Sign In'}</h2>
    {notice && <p role="status">{notice}</p>}
    <form onSubmit={submit} className="auth-form">
      <label>{register ? 'Email' : 'Email or username'}<input required type={register ? 'email' : 'text'} value={identifier} onChange={e => setIdentifier(e.target.value)} maxLength={254} autoComplete="username" placeholder={register ? "Enter your email" : "Enter your email or username"}/></label>
      {register && <><label>Username<input required pattern="[A-Za-z0-9_]{3,24}" value={username} onChange={e => setUsername(e.target.value)} autoComplete="nickname"/></label><label>Display name<input required maxLength={24} value={displayName} onChange={e => setDisplayName(e.target.value)}/></label></>}
      <label>Password<div className="auth-password"><input required type={show ? 'text' : 'password'} minLength={register ? 8 : 1} maxLength={128} value={password} onChange={e => setPassword(e.target.value)} autoComplete={register ? 'new-password' : 'current-password'} placeholder="Enter your password"/><button type="button" aria-label={show ? "Hide password" : "Show password"} onClick={() => setShow(!show)}><svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/>{show && <path d="m3 3 18 18"/>}</svg></button></div></label>
      {register && <label>Confirm password<input required type={show ? 'text' : 'password'} value={confirm} onChange={e => setConfirm(e.target.value)} autoComplete="new-password"/></label>}
      {error && <p className="auth-error" role="alert">{error}</p>}
      <button className="auth-primary" disabled={busy}>{busy ? 'Please wait…' : register ? 'Create account' : 'Sign In'}</button>
    </form>
    <button className="auth-link" disabled={busy} onClick={() => { setRegister(!register); setError(''); setPassword(''); setConfirm(''); }}>{register ? 'Already have an account? Sign in' : 'New here? Create an account'}</button>
    {!register && <button type="button" className="auth-link" disabled={busy} onClick={resendVerification}>Resend verification email</button>}
    <button className="auth-guest" disabled={busy} onClick={() => { sessionStorage.setItem('uno-guest','true'); setGuest(true); }}>Sign In as Guest</button><small>Guest progress is not saved.</small>
    <img className="auth-peeking-mascot" src={import.meta.env.BASE_URL + "auth-mascot-3d.webp"} alt="" onError={e => { e.currentTarget.style.display = "none"; }}/></section></div></main>;
}
