export default function LoadingScreen({title='Getting ready to play',compact=false}:{title?:string;compact?:boolean}) {
 return <div className={'uno-loading '+(compact?'compact':'')} role="status"><div className="loading-deck" aria-hidden="true"><i>7</i><i>+2</i><i>UNO</i></div><strong>{title}…</strong><span className="loading-track" aria-hidden="true"><i/></span><small>Every card counts.</small></div>
}
