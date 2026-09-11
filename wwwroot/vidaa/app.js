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
  overlay.classList.remove('hidden');
  if(message) status.textContent=message;
}

function hideOverlay(){
  overlay.classList.add('hidden');
  status.textContent='';
}

function showToast(message){
  toast.textContent=message;
  toast.classList.remove('hidden');
  setTimeout(function(){toast.classList.add('hidden');},2500);
}

function loadTvs(forceSelector){
  title.textContent='Choisir cette télévision';
  subtitle.textContent='Chargement de la liste depuis le serveur…';
  status.textContent='';
  showOverlay();

  fetch(apiUrl(),{cache:'no-store'})
    .then(function(response){
      if(!response.ok) throw new Error('Serveur inaccessible ('+response.status+').');
      return response.json();
    })
    .then(function(data){
      tvs=(data&&data.televisions)||[];
      if(!tvs.length) throw new Error('Aucune télévision configurée sur le serveur.');

      var saved='';
      try{saved=localStorage.getItem(STORAGE_KEY)||'';}catch(e){}

      if(saved&&!forceSelector){
        for(var i=0;i<tvs.length;i++){
          if(String(tvs[i].id)===String(saved)){
            startTv(tvs[i]);
            return;
          }
        }
      }

      renderSelector(saved);
    })
    .catch(function(error){
      list.innerHTML='';
      subtitle.textContent='Impossible de récupérer la liste.';
      status.textContent=error.message+' Nouvelle tentative dans 5 secondes.';
      clearTimeout(retryTimer);
      retryTimer=setTimeout(function(){loadTvs(forceSelector);},RETRY_MS);
    });
}

function renderSelector(savedId){
  list.innerHTML='';
  subtitle.textContent='Choisis le nom correspondant à cet écran.';
  selectedIndex=0;

  for(var i=0;i<tvs.length;i++){
    if(String(tvs[i].id)===String(savedId)) selectedIndex=i;

    var row=document.createElement('div');
    row.className='tv-item';
    row.setAttribute('data-index',String(i));

    var name=document.createElement('span');
    name.textContent=tvs[i].name||('TV '+tvs[i].id);

    var id=document.createElement('span');
    id.className='id';
    id.textContent='TV '+tvs[i].id;

    row.appendChild(name);
    row.appendChild(id);
    list.appendChild(row);
  }

  updateSelection();
}

function updateSelection(){
  var rows=list.getElementsByClassName('tv-item');
  if(!rows.length) return;
  if(selectedIndex<0) selectedIndex=rows.length-1;
  if(selectedIndex>=rows.length) selectedIndex=0;

  for(var i=0;i<rows.length;i++){
    if(i===selectedIndex) rows[i].classList.add('selected');
    else rows[i].classList.remove('selected');
  }

  var current=rows[selectedIndex];
  if(current&&current.scrollIntoView) current.scrollIntoView({block:'nearest'});
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

  player.pause();
  player.removeAttribute('src');
  player.load();

  player.muted=true;
  player.src=streamUrl(currentTvId);
  player.load();

  var promise=player.play();
  if(promise&&promise.catch){
    promise.catch(function(){
      showToast('Appuie sur OK pour démarrer la vidéo.');
    });
  }
}

function recoverPlayback(){
  if(!currentTvId) return;
  clearTimeout(retryTimer);
  retryTimer=setTimeout(function(){
    player.src=streamUrl(currentTvId)+'?r='+Date.now();
    player.load();
    var p=player.play();
    if(p&&p.catch) p.catch(function(){});
  },RETRY_MS);
}

function openSelector(){
  clearTimeout(retryTimer);
  player.pause();
  loadTvs(true);
}

player.addEventListener('error',function(){
  showToast('Flux interrompu - reconnexion automatique');
  recoverPlayback();
});

player.addEventListener('ended',function(){
  // Un HLS live normal ne doit jamais finir. Si cela arrive, on reconnecte.
  recoverPlayback();
});

player.addEventListener('stalled',recoverPlayback);

window.addEventListener('keydown',function(event){
  var code=event.keyCode||event.which;
  var selectorVisible=!overlay.classList.contains('hidden');

  // Flèches
  if(selectorVisible&&code===38){selectedIndex--;updateSelection();event.preventDefault();return false;}
  if(selectorVisible&&code===40){selectedIndex++;updateSelection();event.preventDefault();return false;}

  // OK / Enter
  if(code===13){
    if(selectorVisible) confirmSelection();
    else if(player.paused) player.play();
    event.preventDefault();return false;
  }

  // Retour / Escape / touche rouge VIDAA-HbbTV / Menu : ouvre le sélecteur.
  if(code===461||code===8||code===27||code===403||code===18){
    if(!selectorVisible) openSelector();
    event.preventDefault();return false;
  }

  return true;
});

document.addEventListener('visibilitychange',function(){
  if(!document.hidden&&currentTvId){
    var p=player.play();
    if(p&&p.catch) p.catch(function(){});
  }
});

loadTvs(false);
})();
