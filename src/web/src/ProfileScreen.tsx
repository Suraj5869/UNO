import { useEffect, useRef, useState } from 'react';
import { accountRequest, type Profile } from './AuthGate';
import LoadingScreen from './LoadingScreen';
type Item = { itemKey: string; category: string; displayName: string; owned: boolean; equipped: boolean };
const categories = [{key:'avatar',label:'Avatars'},{key:'card_back',label:'Card backs'},{key:'table_theme',label:'Table themes'}];
export default function ProfileScreen({player,onUpdate,onClose}:{player:Profile;onUpdate:(p:Profile)=>void;onClose:()=>void}) {
 const dialog=useRef<HTMLDialogElement>(null);
 const [name,setName]=useState(player.displayName);
 const [tab,setTab]=useState('avatar');
 const [items,setItems]=useState<Item[]>([]);
 const [loading,setLoading]=useState(true);
 const [busy,setBusy]=useState(false);
 const [error,setError]=useState('');
 const [saved,setSaved]=useState(false);
 useEffect(()=>{dialog.current?.showModal();},[]);
 useEffect(()=>{let alive=true;accountRequest<Item[]>('collection').then(data=>{if(alive)setItems(data)}).catch(e=>{if(alive)setError(e.message)}).finally(()=>{if(alive)setLoading(false)});return()=>{alive=false}},[]);
 return <dialog ref={dialog} className="collection-screen" aria-labelledby="profile-title" onCancel={e=>{e.preventDefault();onClose()}}>
 <header className="collection-header"><button type="button" onClick={onClose}>← Back</button><span>UNO · PLAYER HUB</span><button type="button" aria-label="Close profile" onClick={onClose}>×</button></header>
 <div className="collection-body"><section className="collection-profile"><span className="collection-kicker">YOUR PLAYER PROFILE</span><h1 id="profile-title">{player.displayName}</h1><div className="collection-portrait"><img src={import.meta.env.BASE_URL+'auth-mascot-3d.webp'} alt="Default character" onError={e=>{e.currentTarget.hidden=true}}/></div><strong>Level {player.level}</strong><p>@{player.username}</p><div className="collection-stats">{[[player.totalXp,'XP'],[player.matchesPlayed,'Matches'],[player.wins,'Wins']].map(([v,k])=><div key={k}><strong>{v}</strong><small>{k}</small></div>)}</div>
 <form onSubmit={async e=>{e.preventDefault();setBusy(true);setError('');setSaved(false);try{onUpdate(await accountRequest<Profile>('me',{displayName:name.trim()},'PUT'));setSaved(true)}catch(e){setError(e instanceof Error?e.message:'Unable to save')}finally{setBusy(false)}}}><label>Display name<input value={name} maxLength={24} required onChange={e=>{setName(e.target.value);setSaved(false)}}/></label><button className="collection-primary" disabled={busy||!name.trim()||name.trim()===player.displayName}>{busy?'Saving…':'Save name'}</button>{saved&&<p role="status">Display name updated.</p>}</form></section>
 <section className="collection-inventory"><span className="collection-kicker">MAKE IT YOURS</span><h2>My collection</h2><div className="collection-tabs">{categories.map(c=><button type="button" key={c.key} aria-pressed={tab===c.key} onClick={()=>setTab(c.key)}>{c.label}</button>)}</div>
 {loading?<LoadingScreen title="Opening your collection" compact/>:<div className="collection-grid">{items.filter(i=>i.category===tab).map(i=><article key={i.itemKey} className={'collection-item '+(i.equipped?'equipped':'')}><div className={'collection-art '+i.category}>{i.category==='avatar'?<img src={import.meta.env.BASE_URL+'auth-mascot-3d.webp'} alt="" onError={e=>{e.currentTarget.hidden=true}}/>:i.category==='card_back'?<span>UNO</span>:<i/>}</div><strong>{i.displayName}</strong><small>{i.equipped?'Equipped':i.owned?'Owned':'Locked'}</small></article>)}</div>}
 {!loading&&!items.some(i=>i.category===tab)&&<p>No items available in this collection yet.</p>}<p className="collection-note">More cosmetics and unlockable rewards are coming with progression.</p></section></div>{error&&<p className="collection-error" role="alert">{error}</p>}</dialog>
}
