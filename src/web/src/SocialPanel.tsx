import { useCallback,useEffect,useRef,useState } from 'react';
import { accountRequest } from './AuthGate';
import './social.css';
type Person={playerId:string;username:string;displayName:string;avatarKey:string;relationship?:'friend'|'incoming'|'outgoing';online?:boolean};
type Invitation=Person&{invitationId:string;roomCode:string};
type Overview={friends:Person[];invitations:Invitation[]};
export default function SocialPanel({room,onJoin}:{room?:{code:string;token:string};onJoin:(code:string)=>Promise<void>;inRoom:boolean}) {
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
 const respond=async(i:Invitation,accept:boolean)=>{setBusy(true);setError('');try{const response=await accountRequest<{roomCode:string|null}>('/api/social/invitation',{invitationId:i.invitationId,accept},'POST');await refresh();if(response.roomCode){await onJoin(response.roomCode);setOpen(false);}}catch(e){setError(e instanceof Error?e.message:'Invitation unavailable.');}finally{setBusy(false);}};
 const count=data.invitations.length+data.friends.filter(p=>p.relationship==='incoming').length;
 const identity=(p:Person)=><span className="social-person"><span className="profile-circle" data-cosmetic={p.avatarKey}>{p.displayName.slice(0,1).toUpperCase()}</span><span><strong>{p.displayName}</strong><small>@{p.username}{p.relationship==='friend'?' · '+(p.online?'Online':'Offline'):''}</small></span></span>;
 return <><button className="button button-secondary button-small" onClick={()=>setOpen(true)} aria-label={'Friends'+(count?' · '+count+' notifications':'')}>Friends {count>0&&<span className="social-count">{count}</span>}</button>
 <dialog ref={dialog} className="social-dialog" aria-labelledby="social-title" onCancel={e=>{e.preventDefault();setOpen(false);}}>
 <header><h2 id="social-title">Friends & invitations</h2><button className="button button-quiet" onClick={()=>setOpen(false)} aria-label="Close friends">✕</button></header>
 {error&&<p role="alert">{error}</p>}{notice&&<p role="status">{notice}</p>}
 <form onSubmit={e=>{e.preventDefault();void search();}}><label htmlFor="social-search">Find players by username or player ID</label><div className="social-actions"><input id="social-search" value={query} onChange={e=>setQuery(e.target.value)} minLength={3} maxLength={64} required/><button disabled={busy} className="button button-primary">Search</button></div></form>
 {results.map(p=><div className="social-row" key={p.playerId}>{identity(p)}<button disabled={busy||data.friends.some(f=>f.playerId===p.playerId)} onClick={()=>void action('request',{playerId:p.playerId})}>Add friend</button></div>)}
 <h3>Room invitations</h3>{!data.invitations.length&&<small>No pending invitations.</small>}
 {data.invitations.map(i=><div className="social-row" key={i.invitationId}>{identity(i)}<div className="social-actions"><button disabled={busy} onClick={()=>void respond(i,true)}>Join</button><button disabled={busy} onClick={()=>void respond(i,false)}>Decline</button></div></div>)}
 <h3>Friends and requests</h3>{!data.friends.length&&<small>Find a player to send your first friend request.</small>}
 {data.friends.map(p=><div className="social-row" key={p.playerId}>{identity(p)}<div className="social-actions">{p.relationship==='incoming'&&<button disabled={busy} onClick={()=>void action('accept',{playerId:p.playerId})}>Accept</button>}{p.relationship==='outgoing'&&<small>Pending</small>}{p.relationship==='friend'&&room&&<button disabled={busy} onClick={()=>void action('invite',{playerId:p.playerId,roomCode:room.code,accessToken:room.token})}>Invite</button>}<button disabled={busy} onClick={()=>void action('remove',{playerId:p.playerId})}>{p.relationship==='incoming'?'Decline':p.relationship==='outgoing'?'Cancel':'Remove'}</button></div></div>)}
 </dialog></>;
}
