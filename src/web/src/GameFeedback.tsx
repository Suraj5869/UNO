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
export function AudioControls(){
 const s=useSyncExternalStore(subscribeAudio,getAudioSettings);
 const [open,setOpen]=useState(false);
 return <div className="audio-controls"><button type="button" className="button button-quiet button-small" aria-expanded={open} aria-label="Audio and animation settings" onClick={()=>{void unlockAudio();setOpen(v=>!v);}}>⚙ Audio</button>
 {open&&<section className="audio-popover" aria-label="Audio and animation settings" onKeyDown={e=>{if(e.key==='Escape')setOpen(false);}}>
 <header><strong>Game settings</strong><button type="button" aria-label="Close audio settings" onClick={()=>setOpen(false)}>✕</button></header>
 <label><input type="checkbox" checked={s.effects} onChange={e=>setAudioSettings({effects:e.target.checked})}/> Sound effects</label>
 <label>Effects volume <input aria-label="Effects volume" type="range" min="0" max="1" step=".05" value={s.effectsVolume} onChange={e=>setAudioSettings({effectsVolume:Number(e.target.value)})}/></label>
 <label><input type="checkbox" checked={s.music} onChange={e=>{void unlockAudio();setAudioSettings({music:e.target.checked});}}/> Background music</label>
 <label>Music volume <input aria-label="Music volume" type="range" min="0" max="1" step=".05" value={s.musicVolume} onChange={e=>setAudioSettings({musicVolume:Number(e.target.value)})}/></label>
 <label><input type="checkbox" checked={s.motion} onChange={e=>setAudioSettings({motion:e.target.checked})}/> Enhanced animations</label>
 <button type="button" onClick={()=>{void unlockAudio().then(()=>playCue('uno'));}}>Preview sound</button>
 </section>}</div>;
}
