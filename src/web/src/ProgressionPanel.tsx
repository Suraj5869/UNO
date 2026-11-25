import { useEffect, useState } from 'react';
import { accountRequest, type Profile } from './AuthGate';
import './progression.css';

type Progress = { level: number; totalXp: number; nextLevelXp: number; season: string; seasonalXp: number; seasonalLevel: number; seasonEndsAt: string; matchesPlayed: number; wins: number; losses: number; winRate: number };
type History = { matchId: string; completedAt: string; variant: string; ruleSet: number; won: boolean; xp: number };

export function ProgressionPanel() {
 const [progress,setProgress]=useState<Progress>();
 const [error,setError]=useState('');
 const [attempt,retry]=useState(0);
 useEffect(()=>{let active=true;setError('');accountRequest<Progress>('/api/progression').then(p=>{if(active)setProgress(p)}).catch(e=>{if(active)setError(e.message)});return()=>{active=false}},[attempt]);
 if(error)return <div className="collection-retry" role="alert"><p>{error}</p><button className="collection-primary" onClick={()=>retry(n=>n+1)}>Retry progression</button></div>;
 if(!progress)return <p role="status">Loading progression…</p>;
 const start=50*progress.level*(progress.level-1);
 const percent=Math.min(100,Math.max(0,100*(progress.totalXp-start)/Math.max(1,progress.nextLevelXp-start)));
 return <div className="hub-overview"><h3>Lifetime statistics</h3><div className="hub-stat-grid">{[[progress.matchesPlayed,'Matches'],[progress.wins,'Wins'],[progress.losses,'Losses'],[progress.winRate.toFixed(1)+'%','Win rate']].map(([v,k])=><div key={k}><strong>{v}</strong><span>{k}</span></div>)}</div><h3>Lifetime progression</h3><div className="hub-stat-card"><strong>Level {progress.level} · {progress.totalXp} XP</strong><div className="hub-xp-track" role="progressbar" aria-label="Lifetime level progress" aria-valuenow={Math.round(percent)} aria-valuemin={0} aria-valuemax={100}><span style={{width:percent+'%'}}/></div><small>{Math.max(0,progress.nextLevelXp-progress.totalXp)} XP to next level</small></div><h3>Seasonal progression</h3><div className="hub-stat-card"><strong>Season level {progress.seasonalLevel}</strong><span>{progress.seasonalXp} seasonal XP</span><small>Ends {new Date(progress.seasonEndsAt).toLocaleDateString()}</small></div></div>;
}
export function MatchHistory(){
 const [history,setHistory]=useState<History[]>([]);
 const [offset,setOffset]=useState(0);
 const [attempt,retry]=useState(0);
 const [loading,setLoading]=useState(true);
 const [error,setError]=useState('');
 useEffect(()=>{let active=true;setLoading(true);setError('');accountRequest<History[]>('/api/progression/history?offset='+offset+'&limit=10').then(h=>{if(active)setHistory(h)}).catch(e=>{if(active)setError(e.message)}).finally(()=>{if(active)setLoading(false)});return()=>{active=false}},[offset,attempt]);
 return <div className="hub-history"><h3>Match history</h3>{error?<div className="collection-retry" role="alert"><p>{error}</p><button className="collection-primary" onClick={()=>retry(n=>n+1)}>Retry history</button></div>:loading?<p role="status">Loading matches…</p>:<>{!history.length&&<div className="hub-stat-card"><strong>No completed matches yet</strong><small>{offset===0?'Your recorded matches will appear here.':'No more matches on this page.'}</small></div>}{history.map(h=><article className="hub-history-row" key={h.matchId}><div><strong>{h.variant==='classic'?'Classic UNO':h.variant}</strong><small>{h.ruleSet===1?'House':'Official'} · {new Date(h.completedAt).toLocaleDateString()}</small><span className={h.won?'hub-win':'hub-loss'}>{h.won?'Victory':'Defeat'}</span></div><strong className="hub-history-xp">+{h.xp} XP</strong></article>)}</>}{(offset>0||history.length>=10)&&<div className="progression-pagination"><button className="collection-primary" disabled={loading||offset===0} onClick={()=>setOffset(n=>Math.max(0,n-10))}>Previous</button><button className="collection-primary" disabled={loading||!!error||history.length<10} onClick={()=>setOffset(n=>n+10)}>Next</button></div>}</div>;
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
