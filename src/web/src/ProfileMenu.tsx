import { useEffect, useRef, useState } from 'react';
import { type Profile } from './AuthGate';
import ProfileScreen from './ProfileScreen';

export default function ProfileMenu({ player, logout, inRoom, onUpdate }: { player: Profile; logout: () => Promise<void>; inRoom: boolean; onUpdate: (p: Profile) => void }) {
  const [screen, setScreen] = useState(false);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => { if (!root.current?.contains(event.target as Node)) setOpen(false); };
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { setOpen(false); trigger.current?.focus(); } };
    document.addEventListener('pointerdown', outside);
    document.addEventListener('keydown', escape);
    return () => { document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', escape); };
  }, [open]);
  return <div className="profile-control" ref={root}>
    <button ref={trigger} type="button" className="profile-trigger" aria-label="Player profile" aria-expanded={open} aria-controls="player-profile-popup" onClick={() => setOpen(!open)}>
      <span className="profile-circle">{player.displayName.slice(0,1).toUpperCase()}</span>
      <span className="profile-xp">{player.totalXp.toLocaleString()} XP</span>
    </button>
    {open && <section id="player-profile-popup" className="profile-popup" aria-label="Player details">
      <div className="profile-popup-heading"><span className="profile-circle">{player.displayName.slice(0,1).toUpperCase()}</span><div><strong>{player.displayName}</strong><small>@{player.username}</small><small>Level {player.level}</small></div></div>
      <div className="profile-stats"><div><strong>{player.totalXp.toLocaleString()}</strong><small>XP</small></div><div><strong>{player.wins}</strong><small>Wins</small></div><div><strong>{player.matchesPlayed}</strong><small>Matches</small></div></div>
      <button type="button" className="profile-signout" disabled={inRoom} onClick={() => { setOpen(false); setScreen(true); }}>Profile &amp; collections</button>
      <button type="button" className="profile-signout" disabled={busy || inRoom} onClick={async () => { setBusy(true); try { await logout(); } catch { /* AuthGate clears the local session even if revocation fails. */ } finally { setBusy(false); } }}>{busy ? 'Signing out…' : 'Sign out'}</button>
      {inRoom && <small className="profile-note">Leave the room before signing out.</small>}
    </section>}
    {screen && <ProfileScreen player={player} onUpdate={onUpdate} onClose={() => setScreen(false)}/>}
  </div>;
}
