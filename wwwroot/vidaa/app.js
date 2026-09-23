(function(){
'use strict';

var STORAGE_KEY='displayserver.vidaa.tvId';
var RETRY_MS=5000;
var AUTO_SCROLL_EDGE=130;
var AUTO_SCROLL_STEP=22;
var AUTO_SCROLL_INTERVAL_MS=40;

var player=document.getElementById('player');
var overlay=document.getElementById('overlay');
var list=document.getElementById('tvList');
var title=document.getElementById('title');
var subtitle=document.getElementById('subtitle');
var status=document.getElementById('status');
var toast=document.getElementById('toast');
var brand=document.getElementById('brandName');

var tvs=[];
var selectedIndex=0;
var currentTvId='';
var currentTv=null;
var retryTimer=null;
var lastHandledKey='';
var lastHandledAt=0;
var autoScrollDirection=0;

function apiUrl(){return '/api/tvs';}

function loadBranding(){
  var xhr=new XMLHttpRequest();
  xhr.open('GET','/api/system?r='+new Date().getTime(),true);
  xhr.onreadystatechange=function(){
    if(xhr.readyState!==4||xhr.status<200||xhr.status>=300) return;
    try{
      var data=JSON.parse(xhr.responseText);
      if(brand&&data&&data.name) brand.innerHTML=escapeHtml(data.name);
      if(data&&data.name) document.title=data.name+' Player';
    }catch(e){}
  };
  xhr.send(null);
}
function streamUrl(id){return '/hls/tv'+encodeURIComponent(id)+'/index.m3u8';}
function selectorVisible(){return overlay.className.indexOf('hidden')===-1;}

function showOverlay(message){
  overlay.className='overlay';
  if(message) status.innerHTML=message;
}

function hideOverlay(){
  overlay.className='overlay hidden';
  status.innerHTML='';
  autoScrollDirection=0;
}

function showToast(message){
  toast.innerHTML=message;
  toast.className='toast';
  setTimeout(function(){toast.className='toast hidden';},2500);
}

function loadTvs(forceSelector){
  title.innerHTML='Choose this screen';
  subtitle.innerHTML='Move the pointer over the screen you want, then click it.';
  status.innerHTML='';
  showOverlay();

  var xhr=new XMLHttpRequest();
  xhr.open('GET',apiUrl()+'?r='+new Date().getTime(),true);
  xhr.setRequestHeader('Accept','application/json');
  xhr.onreadystatechange=function(){
    if(xhr.readyState!==4) return;

    if(xhr.status<200||xhr.status>=300){
      showLoadError('Server unreachable ('+xhr.status+').',forceSelector);
      return;
    }

    var data=null;
    try{data=JSON.parse(xhr.responseText);}catch(e){
      showLoadError('Invalid server response.',forceSelector);
      return;
    }

    tvs=(data&&data.televisions)||[];
    if(!tvs.length){
      showLoadError('No screens are configured on the server.',forceSelector);
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
  xhr.onerror=function(){showLoadError('Unable to connect to server.',forceSelector);};
  xhr.send(null);
}

function showLoadError(message,forceSelector){
  list.innerHTML='';
  subtitle.innerHTML='Unable to load the screen list.';
  status.innerHTML=message+' Retrying in 5 seconds.';
  clearTimeout(retryTimer);
  retryTimer=setTimeout(function(){loadTvs(forceSelector);},RETRY_MS);
}

function renderSelector(savedId){
  list.innerHTML='';
  subtitle.innerHTML='Mouse + left click to choose. Move the pointer to the top or bottom edge to auto-scroll.';
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
      return function(event){
        selectedIndex=index;
        updateSelection(false);
        confirmSelection();
        if(event) cancelMouse(event);
        return false;
      };
    })(i);

    row.onmouseover=(function(index){
      return function(){
        selectedIndex=index;
        updateSelection(false);
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

  updateSelection(false);
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
    if(moveFocus===true&&current.focus){
      try{current.focus();}catch(e){}
    }
    if(moveFocus===true&&current.scrollIntoView){
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
    if(promise&&promise.catch){promise.catch(function(){showToast('Click once to start playback.');});}
  }catch(e4){showToast('Click once to start playback.');}
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
  if(selectorVisible()) return;
  clearTimeout(retryTimer);
  try{player.pause();}catch(e){}
  loadTvs(true);
}

player.addEventListener('error',function(){showToast('Stream interrupted - reconnecting automatically');recoverPlayback();},false);
player.addEventListener('ended',recoverPlayback,false);
player.addEventListener('stalled',recoverPlayback,false);

// Primary interaction mode: mouse/pointer.
// During playback, a left click anywhere reopens the screen selector.
document.addEventListener('mousedown',function(event){
  event=event||window.event||{};
  var button=(event.button===undefined)?0:event.button;
  if(!selectorVisible()&&button===0){
    openSelector();
    cancelMouse(event);
    return false;
  }
  return true;
},true);

player.addEventListener('click',function(event){
  if(!selectorVisible()){
    openSelector();
    cancelMouse(event);
  }
  return false;
},false);

// VIDAA auto-scroll: moving the pointer to the bottom scrolls down,
// moving it to the top scrolls up.
document.addEventListener('mousemove',function(event){
  if(!selectorVisible()){
    autoScrollDirection=0;
    return;
  }

  event=event||window.event||{};
  var y=event.clientY;
  var height=window.innerHeight||document.documentElement.clientHeight||1080;

  if(typeof y!=='number'){
    autoScrollDirection=0;
    return;
  }

  if(y>=height-AUTO_SCROLL_EDGE){
    autoScrollDirection=1;
  }else if(y<=AUTO_SCROLL_EDGE){
    autoScrollDirection=-1;
  }else{
    autoScrollDirection=0;
  }
},true);

setInterval(function(){
  if(!selectorVisible()||autoScrollDirection===0) return;
  list.scrollTop=list.scrollTop+(autoScrollDirection*AUTO_SCROLL_STEP);
},AUTO_SCROLL_INTERVAL_MS);

function cancelMouse(event){
  if(!event) return;
  if(event.preventDefault) event.preventDefault();
  if(event.stopPropagation) event.stopPropagation();
  event.cancelBubble=true;
  event.returnValue=false;
}

// Remote-control navigation is also supported: Up/Down + OK.
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

  if(code===38||code===103||code===29460) return 'up';
  if(code===40||code===108||code===29461) return 'down';
  if(code===37||code===105||code===4) return 'left';
  if(code===39||code===106||code===5) return 'right';
  if(code===13||code===32||code===29443) return 'ok';
  if(code===8||code===461||code===27||code===10009||code===166) return 'back';

  return 'unknown';
}

function handleRemoteKey(event){
  var action=normalizeKey(event);
  var now=new Date().getTime();

  if(action===lastHandledKey&&(now-lastHandledAt)<100){
    cancelKey(event);
    return false;
  }
  lastHandledKey=action;
  lastHandledAt=now;

  if(selectorVisible()&&action==='up'){
    selectedIndex--;
    updateSelection(true);
    cancelKey(event);
    return false;
  }

  if(selectorVisible()&&action==='down'){
    selectedIndex++;
    updateSelection(true);
    cancelKey(event);
    return false;
  }

  if(action==='ok'){
    if(selectorVisible()) confirmSelection();
    else{
      try{if(player.paused)player.play();}catch(e){}
    }
    cancelKey(event);
    return false;
  }

  if(action==='back'){
    if(!selectorVisible()){
      openSelector();
      cancelKey(event);
      return false;
    }

    if(currentTvId){
      resumeCurrentTv();
      cancelKey(event);
      return false;
    }

    return true;
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

document.addEventListener('keydown',handleRemoteKey,true);
document.addEventListener('keyup',handleRemoteKey,true);
document.addEventListener('keypress',handleRemoteKey,true);
window.addEventListener('keydown',handleRemoteKey,true);
window.addEventListener('keyup',handleRemoteKey,true);

document.addEventListener('visibilitychange',function(){
  if(!document.hidden&&currentTvId&&!selectorVisible()){
    try{player.play();}catch(e){}
  }
},false);

loadBranding();
loadTvs(false);
})();
