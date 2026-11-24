import { useEffect, useState } from 'react';
import { accountRequest, type Profile } from './AuthGate';
import './progression.css';

type Progress = { level: number; totalXp: number; nextLevelXp: number; season: string; seasonalXp: number; seasonalLevel: number; seasonEndsAt: string; matchesPlayed: number; wins: number; losses: number; winRate: number };
type History = { matchId: string; completedAt: string; variant: string; ruleSet: number; won: boolean; xp: number };

export function ProgressionPanel() {
  const [progress, setProgress] = useState<Progress>();
  const [history, setHistory] = useState<History[]>([]);
  const [error, setError] = useState('');
  const [attempt, retry] = useState(0);
  const [offset, setOffset] = useState(0);
  useEffect(() => {
    let active = true;
    setError('');
    Promise.all([accountRequest<Progress>('/api/progression'), accountRequest<History[]>('/api/progression/history?offset=' + offset + '&limit=10')])
      .then(([p,h]) => { if (active) { setProgress(p); setHistory(h); } })
      .catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [attempt, offset]);
  if (error) return <section className="progression-panel"><p role="alert">{error}</p><button type="button" onClick={() => retry(n => n+1)}>Retry progression</button></section>;
  if (!progress) return <p role="status">Loading progression…</p>;
  const start = 50 * progress.level * (progress.level - 1);
  return <section className="progression-panel"><h2>Player progression</h2>
    <strong>Lifetime level {progress.level} · {progress.totalXp} XP</strong>
    <progress aria-label="Lifetime level progress" max={Math.max(1,progress.nextLevelXp-start)} value={Math.max(0,progress.totalXp-start)}/>
    <small>{Math.max(0,progress.nextLevelXp-progress.totalXp)} XP to next level</small>
    <strong>Season {progress.season} · Level {progress.seasonalLevel}</strong>
    <small>{progress.seasonalXp} XP · Ends {new Date(progress.seasonEndsAt).toLocaleDateString()}</small>
    <p>{progress.wins} wins · {progress.losses} losses · {progress.winRate.toFixed(1)}% win rate</p>
    <h3>Match history</h3>
    {history.length === 0 && <p>No completed matches on this page.</p>}
    {history.map(h => <div className="progression-history" key={h.matchId}><span>{h.won ? 'Victory' : 'Defeat'} · {h.ruleSet === 1 ? 'House' : 'Official'}<small>{new Date(h.completedAt).toLocaleDateString()}</small></span><strong>+{h.xp} XP</strong></div>)}
    <div className="progression-pagination"><button type="button" disabled={offset===0} onClick={() => setOffset(n => Math.max(0,n-10))}>Previous</button><button type="button" disabled={history.length<10} onClick={() => setOffset(n => n+10)}>Next</button></div>
  </section>;
}

export function MatchReward({matchId, player, onUpdate}: {matchId: string; player: Profile; onUpdate: (p: Profile)=>void}) {
  const [reward,setReward] = useState<History>();
  const [levelUp,setLevelUp] = useState(false);
  const [unavailable,setUnavailable] = useState(false);
  useEffect(() => {
    let active = true;
    let timer: number;
    let tries = 0;
    const initialLevel = player.level;
    const poll = async () => {
      try {
        const result = await accountRequest<History>('/api/progression/reward/' + matchId);
        if (!active) return;
        setReward(result);
        const profile = await accountRequest<Profile>('me');
        if (active) { setLevelUp(profile.level > initialLevel); onUpdate(profile); }
      } catch {
        if (!active) return;
        if (++tries < 8) timer = window.setTimeout(poll, 2000);
        else setUnavailable(true);
      }
    };
    void poll();
    return () => { active=false; window.clearTimeout(timer); };
  }, [matchId, player.userId]);
  return <div className="match-reward" role="status">{reward ? <><strong>+{reward.xp} XP</strong>{levelUp && <span className="progression-level-up">Level up!</span>}</> : unavailable ? 'No saved reward yet. Check match history later.' : 'Checking match rewards…'}</div>;
}
