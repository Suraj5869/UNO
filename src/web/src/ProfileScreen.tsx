import { useEffect, useRef, useState } from 'react';
import { accountRequest, type Profile } from './AuthGate';
import { ProgressionPanel, MatchHistory } from './ProgressionPanel';
import './cosmetics.css';
import LoadingScreen from './LoadingScreen';
type Item = { itemKey: string; category: string; displayName: string; owned: boolean; equipped: boolean; requiredLevel?: number; requiredSeasonXp?: number };
const categories = [{key:'avatar',label:'Avatars'},{key:'card_back',label:'Card backs'},{key:'table_theme',label:'Table themes'}];
export default function ProfileScreen({player,onUpdate,onClose}:{player:Profile;onUpdate:(p:Profile)=>void;onClose:()=>void}) {
 const dialog=useRef<HTMLDialogElement>(null);
 const [name,setName]=useState(player.displayName);
 const [tab,setTab]=useState('avatar');
 const [hubTab,setHubTab]=useState<'overview'|'collections'|'history'>('overview');
 const [items,setItems]=useState<Item[]>([]);
 const [loading,setLoading]=useState(true);
 const [collectionError,setCollectionError]=useState('');
 const [attempt,setAttempt]=useState(0);
 const [equipping,setEquipping]=useState(false);
 const [equipError,setEquipError]=useState('');
 const equip=async (item:Item)=>{setEquipping(true);setEquipError('');try{onUpdate(await accountRequest<Profile>('collection/equip',{itemKey:item.itemKey},'PUT'));setAttempt(a=>a+1)}catch(e){setEquipError(e instanceof Error?e.message:'Unable to equip item')}finally{setEquipping(false)}};
 const [busy,setBusy]=useState(false);
 const [error,setError]=useState('');
 const [saved,setSaved]=useState(false);
 useEffect(()=>{dialog.current?.showModal();},[]);
 useEffect(()=>{let alive=true;setLoading(true);setCollectionError('');accountRequest<Item[]>('collection').then(data=>{if(alive)setItems(data)}).catch(e=>{if(alive)setCollectionError(e.message.includes('(404)')?'Collection API not found. Restart the updated backend and retry.':e.message)}).finally(()=>{if(alive)setLoading(false)});return()=>{alive=false}},[attempt]);
 return <dialog ref={dialog} className="collection-screen" aria-labelledby="profile-title" onCancel={e=>{e.preventDefault();onClose()}}>
 <header className="collection-header"><span>UNO · PLAYER HUB</span><button type="button" className="collection-close" aria-label="Close profile" onClick={onClose}><svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18"/></svg></button></header>
 <div className="collection-body"><section className="collection-profile"><span className="collection-kicker">YOUR PLAYER PROFILE</span><h1 id="profile-title">{player.displayName}</h1><div className="collection-portrait profile-circle" data-cosmetic={player.avatarKey}>{player.displayName.slice(0,1).toUpperCase()}</div><strong>Level {player.level}</strong><p>@{player.username}</p><div className="collection-stats">{[[player.totalXp,'XP'],[player.matchesPlayed,'Matches'],[player.wins,'Wins']].map(([v,k])=><div key={k}><strong>{v}</strong><small>{k}</small></div>)}</div>
 <form onSubmit={async e=>{e.preventDefault();setBusy(true);setError('');setSaved(false);try{onUpdate(await accountRequest<Profile>('me',{displayName:name.trim()},'PUT'));setSaved(true)}catch(e){setError(e instanceof Error?e.message:'Unable to save')}finally{setBusy(false)}}}><label>Display name<input value={name} maxLength={24} required onChange={e=>{setName(e.target.value);setSaved(false)}}/></label><button className="collection-primary" disabled={busy||!name.trim()||name.trim()===player.displayName}>{busy?'Saving…':'Save name'}</button>{saved&&<p role="status">Display name updated.</p>}{error&&<p className="collection-error" role="alert">{error}</p>}</form></section>
 <section className="collection-inventory"><nav className="hub-tabs" aria-label="Player hub sections">{([["overview","Overview"],["collections","Collections"],["history","Match history"]] as const).map(([key,label])=><button type="button" key={key} aria-pressed={hubTab===key} onClick={()=>setHubTab(key)}>{label}</button>)}</nav>{hubTab==="overview"?<ProgressionPanel/>:hubTab==="history"?<MatchHistory/>:<><span className="collection-kicker">MAKE IT YOURS</span><h2>My collection</h2><div className="collection-tabs">{categories.map(c=><button type="button" key={c.key} aria-pressed={tab===c.key} onClick={()=>setTab(c.key)}>{c.label}</button>)}</div>
 {collectionError?<div className="collection-retry" role="alert"><p>{collectionError}</p><button type="button" className="collection-primary" onClick={()=>setAttempt(a=>a+1)}>Retry</button></div>:loading?<LoadingScreen title="Opening your collection" compact/>:<div className="collection-grid">{items.filter(i=>i.category===tab).map(i=><article key={i.itemKey} className={'collection-item '+(i.equipped?'equipped':'')}><div className={'collection-art '+i.category} data-cosmetic={i.itemKey}>{i.category==='avatar'?<span className="profile-circle">{player.displayName.slice(0,1).toUpperCase()}</span>:i.category==='card_back'?<span>UNO</span>:<i/>}</div><strong>{i.displayName}</strong><small>{i.equipped?'Equipped':i.owned?'Owned':i.requiredSeasonXp?'Earn '+i.requiredSeasonXp+' seasonal XP':'Reach level '+(i.requiredLevel??1)}</small>{i.owned&&!i.equipped&&<button type="button" className="collection-primary" disabled={equipping} onClick={()=>void equip(i)}>Equip</button>}</article>)}</div>}
 {!loading&&!collectionError&&!items.some(i=>i.category===tab)&&<p>No items available in this collection yet.</p>}{equipError&&<p role="alert">{equipError}</p>}<p className="collection-note">Unlock cosmetics through lifetime levels and seasonal XP. Unlocked items stay in your collection.</p></>}</section></div></dialog>
}
