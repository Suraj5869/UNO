export type Cue='deal'|'play'|'draw'|'skip'|'reverse'|'penalty'|'wild'|'uno'|'turn'|'urgent'|'win'|'finish';
export type AudioSettings={effects:boolean;music:boolean;effectsVolume:number;musicVolume:number;motion:boolean};
const defaults:AudioSettings={effects:true,music:false,effectsVolume:.55,musicVolume:.18,motion:true};
const key='uno-audio-settings-v1';
function load():AudioSettings {
 try {const s=JSON.parse(localStorage.getItem(key)||'{}');return {...defaults,...Object.fromEntries(Object.entries(s).filter(([k,v])=>k in defaults&&typeof v===typeof defaults[k as keyof AudioSettings]&& (typeof v!=='number'||(v>=0&&v<=1))))};}catch{return {...defaults};}
}
let settings=load();
let ctx:AudioContext|undefined;
let fx:GainNode|undefined;
let music:GainNode|undefined;
let interval:number|undefined;
let playing=false;
let step=0;
const listeners=new Set<()=>void>();
export const getAudioSettings=()=>settings;
export function subscribeAudio(fn:()=>void){listeners.add(fn);return()=>{listeners.delete(fn);};}
export function setAudioSettings(patch:Partial<AudioSettings>){
 settings={...settings,...patch};try{localStorage.setItem(key,JSON.stringify(settings));}catch{}
 sync();listeners.forEach(fn=>fn());
}
function sync(){
 if(ctx&&fx&&music){fx.gain.setTargetAtTime(settings.effects?settings.effectsVolume:0,ctx.currentTime,.03);music.gain.setTargetAtTime(settings.music&&playing&&!document.hidden?settings.musicVolume:0,ctx.currentTime,.08);}
 if(settings.music&&playing&&ctx?.state==='running'&&!document.hidden){if(interval===undefined)interval=window.setInterval(musicBeat,420);}
 else if(interval!==undefined){window.clearInterval(interval);interval=undefined;}
}
export async function unlockAudio(){
 try{
 if(!ctx){ctx=new AudioContext();fx=ctx.createGain();music=ctx.createGain();fx.connect(ctx.destination);music.connect(ctx.destination);fx.gain.value=0;music.gain.value=0;}
 if(ctx.state==='suspended')await ctx.resume();sync();
 }catch{/* Audio is optional; never interrupt gameplay. */}
}
export function setMusicPlaying(value:boolean){playing=value;sync();}
export function refreshAudioVisibility(){sync();}
function tone(bus:GainNode|undefined,freq:number,delay:number,duration:number,amp:number,type:OscillatorType='sine',end=freq){
 if(!ctx||!bus||ctx.state!=='running')return;
 const t=ctx.currentTime+delay;const o=ctx.createOscillator();const g=ctx.createGain();
 o.type=type;o.frequency.setValueAtTime(freq,t);o.frequency.exponentialRampToValueAtTime(Math.max(20,end),t+duration);
 g.gain.setValueAtTime(.0001,t);g.gain.exponentialRampToValueAtTime(amp,t+.012);g.gain.exponentialRampToValueAtTime(.0001,t+duration);
 o.connect(g);g.connect(bus);o.start(t);o.stop(t+duration+.02);o.onended=()=>{o.disconnect();g.disconnect();};
}
export function playCue(cue:Cue){
 if(!settings.effects||document.hidden)return;
 const seq=(notes:number[],spacing=.085)=>notes.forEach((n,i)=>tone(fx,n,i*spacing,.18,.13,'triangle'));
 switch(cue){
 case 'deal':for(let i=0;i<7;i++)tone(fx,520,i*.07,.07,.12,'triangle',180);break;
 case 'play':tone(fx,390,0,.12,.22,'triangle',90);break;
 case 'draw':tone(fx,220,0,.13,.15,'triangle',650);break;
 case 'skip':seq([440,220]);break;
 case 'reverse':seq([330,550,330]);break;
 case 'penalty':seq([180,150,110]);break;
 case 'wild':seq([392,494,587,784]);break;
 case 'uno':seq([523,659,784,1047]);break;
 case 'turn':seq([440,660]);break;
 case 'urgent':tone(fx,760,0,.09,.12);break;
 case 'win':seq([523,659,784,1047,1319],.13);break;
 case 'finish':seq([523,440,392],.15);break;
 }
}
// Original ambient arpeggio, not a recording or external soundtrack.
function musicBeat(){
 const chords=[[130.81,261.63,329.63,392],[110,220,261.63,329.63],[87.31,174.61,261.63,349.23],[98,196,246.94,293.66]];
 const chord=chords[Math.floor(step/8)%chords.length];const n=step%8;
 tone(music,chord[[1,2,3,2,1,3,2,3][n]],0,.38,.11);
 if(n===0||n===4)tone(music,chord[0],0,.7,.12);
 step++;
}
