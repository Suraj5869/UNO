import {useEffect,useRef,useSyncExternalStore,useState} from 'react';
import {getAudioSettings,subscribeAudio,setAudioSettings,unlockAudio,setMusicPlaying,refreshAudioVisibility,playCue,type Cue} from './gameAudio';
import './feedback.css';
type Snapshot={matchId:string;gameStatus:number;viewerPlayerId:string;currentPlayerId:string;turnDeadlineUtc:string;players:{playerId:string;cardCount:number}[]};
type Action={eventSequence:number;action:string;card:{type:number}|null;drawCount:number};
export function useGameFeedback(game:Snapshot|null,action:Action|null,remainingMs:number){
 const settings=useSyncExternalStore(subscribeAudio,getAudioSettings);
 const previous=useRef<Snapshot|null>(null);
 const lastAction=useRef<number|null>(null);
 const urgent=useRef('');
 useEffect(()=>{
 const unlock=()=>{void unlockAudio();};
 window.addEventListener('pointerdown',unlock);window.addEventListener('keydown',unlock);
 document.addEventListener('visibilitychange',refreshAudioVisibility);
 return()=>{window.removeEventListener('pointerdown',unlock);window.removeEventListener('keydown',unlock);document.removeEventListener('visibilitychange',refreshAudioVisibility);setMusicPlaying(false);};
 },[]);
 useEffect(()=>{setMusicPlaying(!!game&&game.gameStatus===0);return()=>setMusicPlaying(false);},[game?.matchId,game?.gameStatus]);
 useEffect(()=>{
 const old=previous.current;previous.current=game;
 if(!game)return;
 if(game.gameStatus===0&&(!old||old.matchId!==game.matchId)){playCue('deal');return;}
 if(old?.matchId===game.matchId&&old.gameStatus===0&&game.gameStatus===1)playCue(game.players.some(p=>p.playerId===game.viewerPlayerId&&p.cardCount===0)?'win':'finish');
 else if(game.gameStatus===0&&old?.currentPlayerId!==game.currentPlayerId&&game.currentPlayerId===game.viewerPlayerId)playCue('turn');
 },[game]);
 useEffect(()=>{
 if(!action||lastAction.current===action.eventSequence)return;
 lastAction.current=action.eventSequence;
 const cues:Record<number,Cue>={1:'skip',2:'reverse',3:'penalty',4:'wild',5:'penalty'};
 playCue(action.action==='CardPlayed'?(cues[action.card?.type??0]??'play'):action.drawCount>1?'penalty':'draw');
 },[action]);
 useEffect(()=>{
 if(!game||game.gameStatus!==0||game.currentPlayerId!==game.viewerPlayerId||remainingMs>5000||remainingMs<=0)return;
 const key=game.matchId+game.turnDeadlineUtc;
 if(urgent.current!==key){urgent.current=key;playCue('urgent');}
 },[game,remainingMs]);
 return settings;
}
export function SettingsIcon(){return <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m10 2-.7 2.5-2 .9L5 4l-2 3.5 1.8 1.9v2.3L3 14l2 3.5 2.5-.6 2 .9L10 21h4l.7-3.2 2-.9 2.5.6 2-3.5-1.8-2.3V9.4L21 7.5 19 4l-2.3 1.4-2-.9L14 2z"/><circle cx="12" cy="11.5" r="3"/></svg>;}
export function GameSettings({onClose}:{onClose:()=>void}){
 const s=useSyncExternalStore(subscribeAudio,getAudioSettings);
 const [tab,setTab]=useState<'audio'|'visual'>('audio');
 const dialog=useRef<HTMLDialogElement>(null);
 useEffect(()=>{const d=dialog.current;d?.showModal();return()=>d?.close();},[]);
 const toggle=(label:string,description:string,checked:boolean,change:(v:boolean)=>void)=><label className="settings-toggle-row"><span><strong>{label}</strong><small>{description}</small></span><input type="checkbox" role="switch" checked={checked} onChange={e=>change(e.target.checked)}/></label>;
 const volume=(label:string,value:number,enabled:boolean,change:(v:number)=>void)=><label className="settings-volume"><span>{label}<output>{Math.round(value*100)}%</output></span><input aria-label={label} type="range" min="0" max="1" step=".05" disabled={!enabled} value={value} onChange={e=>change(Number(e.target.value))}/></label>;
 return <dialog ref={dialog} className="game-settings-dialog" aria-labelledby="game-settings-title" onCancel={e=>{e.preventDefault();onClose();}} onClick={e=>{if(e.target===dialog.current)onClose();}}>
 <div className="game-settings-inner"><header><h2 id="game-settings-title">Game settings</h2><button className="settings-gear" aria-label="Close game settings" onClick={onClose}>×</button></header>
 <nav className="settings-tabs" aria-label="Settings categories"><button aria-pressed={tab==='audio'} onClick={()=>setTab('audio')}>Audio</button><button aria-pressed={tab==='visual'} onClick={()=>setTab('visual')}>Visuals</button></nav>
 {tab==='audio'?<div className="settings-content"><section>{toggle('Sound effects','Card moves, UNO calls and turn alerts',s.effects,v=>{void unlockAudio();setAudioSettings({effects:v});})}{volume('Effects volume',s.effectsVolume,s.effects,v=>setAudioSettings({effectsVolume:v}))}<button className="settings-preview" disabled={!s.effects||s.effectsVolume===0} onClick={()=>{void unlockAudio().then(()=>playCue('uno'));}}>▶ Preview sound</button></section><section>{toggle('Background music','Music during a match',s.music,v=>{void unlockAudio();setAudioSettings({music:v});})}{volume('Music volume',s.musicVolume,s.music,v=>setAudioSettings({musicVolume:v}))}</section></div>:<div className="settings-content"><section>{toggle('Enhanced animations','Card effects and celebration animations',s.motion,v=>setAudioSettings({motion:v}))}</section></div>}
 <footer><small>Changes are saved automatically on this device.</small><button className="settings-done" onClick={onClose}>Done</button></footer></div></dialog>;
}
export function AudioControls(){
 const [open,setOpen]=useState(false);
 return <><button type="button" className="settings-gear" aria-label="Game settings" onClick={()=>setOpen(true)}><SettingsIcon/></button>{open&&<GameSettings onClose={()=>setOpen(false)}/>}</>;
}
