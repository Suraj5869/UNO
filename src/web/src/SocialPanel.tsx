import { useCallback,useEffect,useRef,useState } from 'react';
import { accountRequest } from './AuthGate';
import './social.css';
type Person={playerId:string;username:string;displayName:string;avatarKey:string;level?:number;relationship?:'friend'|'incoming'|'outgoing';online?:boolean};
type Invitation=Person&{invitationId:string;roomCode:string};
type Overview={friends:Person[];invitations:Invitation[]};
export default function SocialPanel({room,onJoin,inRoom}:{room?:{code:string;token:string};onJoin:(code:string)=>Promise<void>;inRoom:boolean}) {
 const [tab,setTab]=useState<'friends'|'requests'|'add'>('friends');
 const [inviteError,setInviteError]=useState('');
 const [open,setOpen]=useState(false);
 const [data,setData]=useState<Overview>({friends:[],invitations:[]});
 const [query,setQuery]=useState('');
 const [results,setResults]=useState<Person[]>([]);
 const [error,setError]=useState('');
 const [notice,setNotice]=useState('');
 const [busy,setBusy]=useState(false);
 const dialog=useRef<HTMLDialogElement>(null);
 const refresh=useCallback(async()=>{setData(await accountRequest<Overview>('/api/social'));},[]);
 useEffect(()=>{let active=true;const poll=async()=>{try{const next=await accountRequest<Overview>('/api/social');if(active)setData(next);}catch{}};void poll();const timer=window.setInterval(poll,15000);return()=>{active=false;window.clearInterval(timer);};},[]);
 useEffect(()=>{if(open){dialog.current?.showModal();void refresh().catch(e=>setError(e.message));}else dialog.current?.close();},[open,refresh]);
 const action=async(path:string,body:unknown)=>{setBusy(true);setError('');setNotice('');try{await accountRequest('/api/social/'+path,body,'POST');await refresh();setNotice('Updated successfully.');}catch(e){setError(e instanceof Error?e.message:'Request failed.');}finally{setBusy(false);}};
 const search=async()=>{setBusy(true);setError('');try{setResults(await accountRequest<Person[]>('/api/social/search?q='+encodeURIComponent(query.trim())));}catch(e){setError(e instanceof Error?e.message:'Search failed.');}finally{setBusy(false);}};
 const respond=async(i:Invitation,accept:boolean)=>{setBusy(true);setInviteError('');try{const response=await accountRequest<{roomCode:string|null}>('/api/social/invitation',{invitationId:i.invitationId,accept},'POST');await refresh();if(response.roomCode){await onJoin(response.roomCode);setOpen(false);}}catch(e){setInviteError(e instanceof Error?e.message:'Invitation unavailable.');}finally{setBusy(false);}};
 const incoming=data.friends.filter(p=>p.relationship==='incoming');
 const outgoing=data.friends.filter(p=>p.relationship==='outgoing');
 const friends=data.friends.filter(p=>p.relationship==='friend').sort((a,b)=>Number(!!b.online)-Number(!!a.online)||a.displayName.localeCompare(b.displayName));
 const icon=(kind:string)=><svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">{kind==='friends'?<><circle cx="9" cy="7" r="3"/><path d="M2 20v-2a7 7 0 0 1 14 0v2M16 4a3 3 0 0 1 0 6M18 13a5 5 0 0 1 4 5v2"/></>:kind==='requests'?<><rect x="3" y="5" width="18" height="14" rx="3"/><path d="m3 7 9 6 9-6"/></>:<><circle cx="9" cy="7" r="3"/><path d="M2 20v-2a7 7 0 0 1 14 0v2M19 8v6m-3-3h6"/></>}</svg>;
 const identity=(p:Person)=><span className={"social-person"+(p.relationship==="friend"&&!p.online?" social-offline":"")}><span className="social-avatar"><span className="profile-circle" data-cosmetic={p.avatarKey}>{p.displayName.slice(0,1).toUpperCase()}</span></span><span><strong>{p.displayName}</strong><small className="social-level">{p.level!=null?'Level '+p.level:''}{p.relationship!=='friend'?' · @'+p.username:''}</small></span></span>;
 const invitation=data.invitations[0];
 const invitationCard=invitation&&<section className="social-invite-card" role="status"><div className="social-invite-kicker">✦ GAME INVITATION</div>{identity(invitation)}<p>Invited you to play UNO!</p>{inRoom&&<small>Leave your current room before joining.</small>}{inviteError&&<p role="alert">{inviteError}</p>}<div className="social-actions"><button disabled={busy||inRoom} onClick={()=>void respond(invitation,true)}>Join game</button><button disabled={busy} onClick={()=>void respond(invitation,false)}>Decline</button></div></section>;
 return <><button className="social-trigger" onClick={()=>{setTab('friends');setOpen(true);}} aria-label={'Friends'+(incoming.length?' · '+incoming.length+' new requests':'')} title="Friends">{icon('friends')}{incoming.length>0&&<i className="social-dot"/>}</button>
 {!open&&invitationCard}
 <dialog ref={dialog} className="social-dialog" aria-labelledby="social-title" onCancel={e=>{e.preventDefault();setOpen(false);}} onClick={e=>{if(e.target===dialog.current)setOpen(false);}}>
 <div className="social-drawer-inner"><header><h2 id="social-title">Friends</h2><button className="social-icon" onClick={()=>setOpen(false)} aria-label="Close friends">✕</button></header>
 <nav className="social-tabs" aria-label="Social sections">{(['friends','requests','add'] as const).map(t=><button key={t} className="social-icon" aria-label={t==='friends'?'Friends list':t==='requests'?'Friend requests':'Add friend'} aria-pressed={tab===t} title={t==='friends'?'Friends':t==='requests'?'Requests':'Add friend'} onClick={()=>{setTab(t);setError('');setNotice('');}}>{icon(t)}{t==='requests'&&incoming.length>0&&<i className="social-dot"/>}</button>)}</nav>
 {invitationCard}
 {error&&<p role="alert">{error}</p>}{notice&&<p role="status">{notice}</p>}
 <div className="social-scroll">
 {tab==='friends'&&<>{friends.map(p=><div className="social-row" key={p.playerId}>{identity(p)}<div className="social-actions">{room&&p.online&&<button disabled={busy} onClick={()=>void action('invite',{playerId:p.playerId,roomCode:room.code,accessToken:room.token})}>Invite</button>}<button disabled={busy} title="Remove friend" aria-label={'Remove '+p.displayName} onClick={()=>{if(window.confirm('Remove '+p.displayName+' from friends?'))void action('remove',{playerId:p.playerId});}}>×</button></div></div>)}{!friends.length&&<p className="social-empty">No friends yet. Add a player to get started.</p>}</>}
 {tab==='requests'&&<><h3>Received · {incoming.length}</h3>{!incoming.length&&<p className="social-empty">No incoming requests.</p>}{incoming.map(p=><div className="social-row" key={p.playerId}>{identity(p)}<div className="social-actions"><button disabled={busy} onClick={()=>void action('accept',{playerId:p.playerId})}>Accept</button><button disabled={busy} onClick={()=>void action('remove',{playerId:p.playerId})}>Decline</button></div></div>)}<h3>Sent · {outgoing.length}</h3>{outgoing.map(p=><div className="social-row" key={p.playerId}>{identity(p)}<button disabled={busy} onClick={()=>void action('remove',{playerId:p.playerId})}>Cancel</button></div>)}</>}
 {tab==='add'&&<><form onSubmit={e=>{e.preventDefault();void search();}}><label htmlFor="social-search">Find by username or player ID</label><div className="social-actions"><input id="social-search" placeholder="Search players" value={query} onChange={e=>setQuery(e.target.value)} minLength={3} maxLength={64} required/><button disabled={busy}>Search</button></div></form>{results.map(p=><div className="social-row" key={p.playerId}>{identity(p)}<button disabled={busy||data.friends.some(f=>f.playerId===p.playerId)} onClick={()=>void action('request',{playerId:p.playerId})}>Add</button></div>)}</>}
 </div></div></dialog></>;
}
