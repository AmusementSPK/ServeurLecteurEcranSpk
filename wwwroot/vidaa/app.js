(function(){
'use strict';

var STORAGE_KEY='spk.vidaa.tvId';
var RETRY_MS=5000;
var player=document.getElementById('player');
var overlay=document.getElementById('overlay');
var list=document.getElementById('tvList');
var title=document.getElementById('title');
var subtitle=document.getElementById('subtitle');
var status=document.getElementById('status');
var toast=document.getElementById('toast');

var tvs=[];
var selectedIndex=0;
var currentTvId='';
var currentTv=null;
var retryTimer=null;
var lastHandledKey='';
var lastHandledAt=0;

function apiUrl(){return '/api/tvs';}
function streamUrl(id){return '/hls/tv'+encodeURIComponent(id)+'/index.m3u8';}

function showOverlay(message){
  overlay.className='overlay';
  if(message) status.innerHTML=message;
}

function hideOverlay(){
  overlay.className='overlay hidden';
  status.innerHTML='';
}

function showToast(message){
  toast.innerHTML=message;
  toast.className='toast';
  setTimeout(function(){toast.className='toast hidden';},2500);
}

function loadTvs(forceSelector){
  title.innerHTML='Choisir cette télévision';
  subtitle.innerHTML='Chargement de la liste depuis le serveur…';
  status.innerHTML='';
  showOverlay();

  var xhr=new XMLHttpRequest();
  xhr.open('GET',apiUrl()+'?r='+new Date().getTime(),true);
  xhr.setRequestHeader('Accept','application/json');
  xhr.onreadystatechange=function(){
    if(xhr.readyState!==4) return;

    if(xhr.status<200||xhr.status>=300){
      showLoadError('Serveur inaccessible ('+xhr.status+').',forceSelector);
      return;
    }

    var data=null;
    try{data=JSON.parse(xhr.responseText);}catch(e){
      showLoadError('Réponse serveur invalide.',forceSelector);
      return;
    }

    tvs=(data&&data.televisions)||[];
    if(!tvs.length){
      showLoadError('Aucune télévision configurée sur le serveur.',forceSelector);
      return;
    }

    var saved='';
    try{saved=localStorage.getItem(STORAGE_KEY)||'';}catch(e2){}

    if(saved&&!forceSelector){
      for(var i=0;i<tvs.length;i++){
        if(String(tvs[i].id)===String(saved)){
          startTv(tvs[i]);
          return;
        }
      }
    }

    renderSelector(saved);
  };
  xhr.onerror=function(){showLoadError('Connexion au serveur impossible.',forceSelector);};
  xhr.send(null);
}

function showLoadError(message,forceSelector){
  list.innerHTML='';
  subtitle.innerHTML='Impossible de récupérer la liste.';
  status.innerHTML=message+' Nouvelle tentative dans 5 secondes.';
  clearTimeout(retryTimer);
  retryTimer=setTimeout(function(){loadTvs(forceSelector);},RETRY_MS);
}

function renderSelector(savedId){
  list.innerHTML='';
  subtitle.innerHTML='Utilise ▲ ▼ puis OK pour choisir cet écran.';
  selectedIndex=0;

  for(var i=0;i<tvs.length;i++){
    if(String(tvs[i].id)===String(savedId)) selectedIndex=i;

    var row=document.createElement('button');
    row.type='button';
    row.className='tv-item';
    row.setAttribute('data-index',String(i));
    row.setAttribute('tabindex','0');

    var name=document.createElement('span');
    name.innerHTML=escapeHtml(tvs[i].name||('TV '+tvs[i].id));

    var id=document.createElement('span');
    id.className='id';
    id.innerHTML='TV '+escapeHtml(tvs[i].id);

    row.appendChild(name);
    row.appendChild(id);

    row.onclick=(function(index){
      return function(){
        selectedIndex=index;
        updateSelection();
        confirmSelection();
      };
    })(i);

    row.onfocus=(function(index){
      return function(){
        selectedIndex=index;
        updateSelection(false);
      };
    })(i);

    list.appendChild(row);
  }

  updateSelection(true);
}

function escapeHtml(value){
  return String(value===undefined||value===null?'':value)
    .replace(/&/g,'&amp;')
    .replace(/</g,'&lt;')
    .replace(/>/g,'&gt;')
    .replace(/\"/g,'&quot;')
    .replace(/'/g,'&#039;');
}

function updateSelection(moveFocus){
  var rows=list.getElementsByClassName('tv-item');
  if(!rows.length) return;
  if(selectedIndex<0) selectedIndex=rows.length-1;
  if(selectedIndex>=rows.length) selectedIndex=0;

  for(var i=0;i<rows.length;i++){
    rows[i].className=(i===selectedIndex)?'tv-item selected':'tv-item';
  }

  var current=rows[selectedIndex];
  if(current){
    if(moveFocus!==false&&current.focus){
      try{current.focus();}catch(e){}
    }
    if(current.scrollIntoView){
      try{current.scrollIntoView(false);}catch(e2){}
    }
  }
}

function confirmSelection(){
  if(!tvs.length) return;
  var tv=tvs[selectedIndex];
  if(!tv) return;

  try{localStorage.setItem(STORAGE_KEY,String(tv.id));}catch(e){}
  startTv(tv);
}

function startTv(tv){
  clearTimeout(retryTimer);
  currentTvId=String(tv.id);
  currentTv=tv;
  hideOverlay();

  try{player.pause();}catch(e){}
  player.removeAttribute('src');
  try{player.load();}catch(e2){}

  player.muted=true;
  player.src=streamUrl(currentTvId);
  try{player.load();}catch(e3){}

  try{
    var promise=player.play();
    if(promise&&promise.catch){promise.catch(function(){showToast('Appuie sur OK pour démarrer la vidéo.');});}
  }catch(e4){showToast('Appuie sur OK pour démarrer la vidéo.');}
}

function resumeCurrentTv(){
  hideOverlay();
  if(currentTv){
    try{player.play();}catch(e){}
  }else if(currentTvId){
    player.src=streamUrl(currentTvId);
    try{player.load();player.play();}catch(e2){}
  }
}

function recoverPlayback(){
  if(!currentTvId) return;
  clearTimeout(retryTimer);
  retryTimer=setTimeout(function(){
    player.src=streamUrl(currentTvId)+'?r='+new Date().getTime();
    try{player.load();player.play();}catch(e){}
  },RETRY_MS);
}

function openSelector(){
  clearTimeout(retryTimer);
  try{player.pause();}catch(e){}
  loadTvs(true);
}

player.addEventListener('error',function(){showToast('Flux interrompu - reconnexion automatique');recoverPlayback();},false);
player.addEventListener('ended',recoverPlayback,false);
player.addEventListener('stalled',recoverPlayback,false);

function normalizeKey(event){
  event=event||window.event||{};
  var key=event.key||event.keyIdentifier||'';
  var code=event.keyCode||event.which||event.charCode||0;
  var physical=event.code||'';

  if(key==='ArrowUp'||key==='Up'||key==='U+001E'||physical==='ArrowUp') return 'up';
  if(key==='ArrowDown'||key==='Down'||key==='U+001F'||physical==='ArrowDown') return 'down';
  if(key==='ArrowLeft'||key==='Left'||key==='U+001C'||physical==='ArrowLeft') return 'left';
  if(key==='ArrowRight'||key==='Right'||key==='U+001D'||physical==='ArrowRight') return 'right';
  if(key==='Enter'||key==='OK'||key==='Select'||physical==='Enter'||physical==='NumpadEnter') return 'ok';
  if(key==='Backspace'||key==='Escape'||key==='BrowserBack'||key==='GoBack') return 'back';

  // Mapping VIDAA documenté.
  if(code===38) return 'up';
  if(code===40) return 'down';
  if(code===37) return 'left';
  if(code===39) return 'right';
  if(code===13||code===32) return 'ok';
  if(code===8) return 'back';

  // Variantes observées sur certains moteurs Hisense/Linux OEM.
  if(code===103||code===29460) return 'up';
  if(code===108||code===29461) return 'down';
  if(code===105||code===4) return 'left';
  if(code===106||code===5) return 'right';
  if(code===29443) return 'ok';

  // Retour / HbbTV / plateformes OEM.
  if(code===461||code===27||code===10009||code===166) return 'back';
  if(code===403) return 'red';
  if(code===404) return 'green';
  if(code===405) return 'yellow';
  if(code===406) return 'blue';

  return 'unknown:'+code+':'+key+':'+physical;
}

function handleRemoteKey(event){
  var action=normalizeKey(event);
  var now=new Date().getTime();

  // Certains firmwares envoient le même événement à plusieurs niveaux.
  if(action===lastHandledKey&&(now-lastHandledAt)<100){
    cancelKey(event);
    return false;
  }
  lastHandledKey=action;
  lastHandledAt=now;

  var selectorVisible=overlay.className.indexOf('hidden')===-1;

  if(selectorVisible&&action==='up'){
    selectedIndex--;
    updateSelection(true);
    cancelKey(event);
    return false;
  }

  if(selectorVisible&&action==='down'){
    selectedIndex++;
    updateSelection(true);
    cancelKey(event);
    return false;
  }

  if(action==='ok'){
    if(selectorVisible) confirmSelection();
    else{
      try{if(player.paused)player.play();}catch(e){}
    }
    cancelKey(event);
    return false;
  }

  if(action==='red'){
    if(!selectorVisible) openSelector();
    else if(currentTvId) resumeCurrentTv();
    cancelKey(event);
    return false;
  }

  if(action==='back'){
    if(!selectorVisible){
      // Pendant la lecture : Retour ouvre le choix des écrans.
      openSelector();
      cancelKey(event);
      return false;
    }

    if(currentTvId){
      // Dans le sélecteur avec une TV déjà active : Retour annule le changement.
      resumeCurrentTv();
      cancelKey(event);
      return false;
    }

    // Au tout premier lancement, aucune TV n'est encore choisie.
    // Ne PAS avaler Retour : le navigateur/VIDAA peut revenir à l'écran précédent.
    try{window.close();}catch(e2){}
    return true;
  }

  if(selectorVisible&&action.indexOf('unknown:')===0){
    status.innerHTML='Touche reçue : '+escapeHtml(action.substring(8));
  }

  return true;
}

function cancelKey(event){
  if(!event) return;
  if(event.preventDefault) event.preventDefault();
  if(event.stopPropagation) event.stopPropagation();
  event.cancelBubble=true;
  event.returnValue=false;
}

// VIDAA documente la navigation sur document.keydown.
// On écoute aussi keyup et window pour couvrir les firmwares OEM WELCOME.
document.addEventListener('keydown',handleRemoteKey,true);
document.addEventListener('keyup',handleRemoteKey,true);
document.addEventListener('keypress',handleRemoteKey,true);
window.addEventListener('keydown',handleRemoteKey,true);
window.addEventListener('keyup',handleRemoteKey,true);

document.addEventListener('visibilitychange',function(){
  if(!document.hidden&&currentTvId&&overlay.className.indexOf('hidden')!==-1){
    try{player.play();}catch(e){}
  }
},false);

try{
  document.body.setAttribute('tabindex','-1');
  document.body.focus();
}catch(e){}

loadTvs(false);
})();
