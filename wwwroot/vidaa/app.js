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
var retryTimer=null;

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
  subtitle.innerHTML='Choisis le nom correspondant à cet écran.';
  selectedIndex=0;

  for(var i=0;i<tvs.length;i++){
    if(String(tvs[i].id)===String(savedId)) selectedIndex=i;

    var row=document.createElement('div');
    row.className='tv-item';
    row.setAttribute('data-index',String(i));

    var name=document.createElement('span');
    name.innerHTML=escapeHtml(tvs[i].name||('TV '+tvs[i].id));

    var id=document.createElement('span');
    id.className='id';
    id.innerHTML='TV '+escapeHtml(tvs[i].id);

    row.appendChild(name);
    row.appendChild(id);
    list.appendChild(row);
  }

  updateSelection();
}

function escapeHtml(value){
  return String(value===undefined||value===null?'':value)
    .replace(/&/g,'&amp;')
    .replace(/</g,'&lt;')
    .replace(/>/g,'&gt;')
    .replace(/"/g,'&quot;')
    .replace(/'/g,'&#039;');
}

function updateSelection(){
  var rows=list.getElementsByClassName('tv-item');
  if(!rows.length) return;
  if(selectedIndex<0) selectedIndex=rows.length-1;
  if(selectedIndex>=rows.length) selectedIndex=0;

  for(var i=0;i<rows.length;i++){
    rows[i].className=(i===selectedIndex)?'tv-item selected':'tv-item';
  }

  var current=rows[selectedIndex];
  if(current&&current.scrollIntoView){
    try{current.scrollIntoView(false);}catch(e){}
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

window.addEventListener('keydown',function(event){
  event=event||window.event;
  var code=event.keyCode||event.which;
  var selectorVisible=overlay.className.indexOf('hidden')===-1;

  if(selectorVisible&&code===38){selectedIndex--;updateSelection();cancelKey(event);return false;}
  if(selectorVisible&&code===40){selectedIndex++;updateSelection();cancelKey(event);return false;}

  if(code===13){
    if(selectorVisible) confirmSelection();
    else{try{if(player.paused)player.play();}catch(e){}}
    cancelKey(event);return false;
  }

  if(code===461||code===8||code===27||code===403||code===18){
    if(!selectorVisible) openSelector();
    cancelKey(event);return false;
  }

  return true;
},false);

function cancelKey(event){
  if(event.preventDefault) event.preventDefault();
  event.returnValue=false;
}

document.addEventListener('visibilitychange',function(){
  if(!document.hidden&&currentTvId){try{player.play();}catch(e){}}
},false);

loadTvs(false);
})();
