const state = { televisions: [] };

const tvGrid = document.getElementById('tvGrid');
const messageBox = document.getElementById('message');
const addTvDialog = document.getElementById('addTvDialog');

function showMessage(text, type = 'ok') {
    messageBox.textContent = text;
    messageBox.className = `message ${type}`;
    clearTimeout(showMessage.timer);
    showMessage.timer = setTimeout(() => messageBox.classList.add('hidden'), 5000);
}

function formatBytes(bytes) {
    if (!bytes) return 'Aucune vidéo';
    const units = ['o', 'Ko', 'Mo', 'Go'];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
        value /= 1024;
        unit++;
    }
    return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function absoluteStreamUrl(path) {
    return `${location.protocol}//${location.host}${path}`;
}

async function loadTvs() {
    try {
        const response = await fetch('/api/tvs', { cache: 'no-store' });
        if (!response.ok) throw new Error('Impossible de charger les télévisions.');
        const data = await response.json();
        state.televisions = data.televisions ?? [];
        render();
        document.getElementById('serverStatus').textContent = 'En ligne';
    } catch (error) {
        document.getElementById('serverStatus').textContent = 'Erreur';
        showMessage(error.message, 'error');
    }
}

function render() {
    document.getElementById('tvCount').textContent = state.televisions.length;
    document.getElementById('runningCount').textContent = state.televisions.filter(x => x.running).length;
    document.getElementById('addTvBtn').disabled = false;

    if (state.televisions.length === 0) {
        tvGrid.innerHTML = '<div class="empty">Aucune télévision configurée.</div>';
        return;
    }

    tvGrid.innerHTML = '';

    for (const tv of state.televisions) {
        const card = document.createElement('article');
        card.className = 'tv-card';

        let statusClass = 'missing';
        let statusText = 'Pas de vidéo';
        if (tv.hasVideo && tv.running) {
            statusClass = 'running';
            statusText = 'En diffusion';
        } else if (tv.hasVideo) {
            statusClass = '';
            statusText = 'Démarrage…';
        }

        card.innerHTML = `
            <div class="tv-card-head">
                <div class="tv-id">TV ${escapeHtml(tv.id)}</div>
                <div class="status ${statusClass}">${statusText}</div>
            </div>
            <div class="field">
                <label for="name-${escapeHtml(tv.id)}">Nom affiché</label>
                <div class="name-row">
                    <input id="name-${escapeHtml(tv.id)}" type="text" maxlength="80" value="${escapeAttr(tv.name)}">
                    <button class="secondary save-name" data-tv="${escapeAttr(tv.id)}">Enregistrer</button>
                </div>
            </div>
            <div class="video-box">
                <div class="video-meta">
                    <strong>Vidéo actuelle :</strong> ${escapeHtml(tv.fileName)}<br>
                    ${tv.hasVideo
                        ? `${formatBytes(tv.fileSize)}${tv.lastModified ? ` · modifiée ${new Date(tv.lastModified).toLocaleString('fr-CA')}` : ''}`
                        : 'Aucun fichier vidéo pour cette TV'}
                </div>
                <div class="file-row">
                    <input class="file-input" id="file-${escapeHtml(tv.id)}" type="file" accept="video/mp4,.mp4">
                    <button class="primary upload-video" data-tv="${escapeAttr(tv.id)}">Remplacer la vidéo</button>
                </div>
                <div class="progress-wrap" id="progress-wrap-${escapeHtml(tv.id)}">
                    <div class="progress" id="progress-${escapeHtml(tv.id)}"></div>
                </div>
            </div>
            <div class="stream-row">
                <div class="stream-url" title="${escapeAttr(absoluteStreamUrl(tv.streamUrl))}">${escapeHtml(absoluteStreamUrl(tv.streamUrl))}</div>
                <button class="ghost copy-url" data-url="${escapeAttr(absoluteStreamUrl(tv.streamUrl))}">Copier</button>
            </div>`;

        tvGrid.appendChild(card);
    }

    bindCardEvents();
}

function bindCardEvents() {
    document.querySelectorAll('.save-name').forEach(button => {
        button.addEventListener('click', async () => {
            const id = button.dataset.tv;
            const input = document.getElementById(`name-${id}`);
            const response = await fetch(`/api/tvs/${encodeURIComponent(id)}/name`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: input.value })
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) return showMessage(data.error || 'Impossible de renommer la TV.', 'error');
            showMessage(`TV ${id} renommée « ${data.name} ».`);
            await loadTvs();
        });
    });

    document.querySelectorAll('.upload-video').forEach(button =>
        button.addEventListener('click', () => uploadVideo(button.dataset.tv)));

    document.querySelectorAll('.copy-url').forEach(button => {
        button.addEventListener('click', async () => {
            await navigator.clipboard.writeText(button.dataset.url);
            showMessage('Adresse du flux copiée.');
        });
    });
}

function uploadVideo(id) {
    const input = document.getElementById(`file-${id}`);
    const file = input.files[0];
    if (!file) return showMessage('Choisis d’abord un fichier MP4.', 'error');
    if (!file.name.toLowerCase().endsWith('.mp4')) return showMessage('Seuls les fichiers MP4 sont acceptés.', 'error');

    const form = new FormData();
    form.append('video', file);

    const wrap = document.getElementById(`progress-wrap-${id}`);
    const bar = document.getElementById(`progress-${id}`);
    wrap.style.display = 'block';
    bar.style.width = '0%';

    const xhr = new XMLHttpRequest();
    xhr.open('POST', `/api/tvs/${encodeURIComponent(id)}/video`);
    xhr.upload.onprogress = event => {
        if (event.lengthComputable) bar.style.width = `${Math.round(event.loaded / event.total * 100)}%`;
    };
    xhr.onload = async () => {
        let data = {};
        try { data = JSON.parse(xhr.responseText); } catch {}

        if (xhr.status >= 200 && xhr.status < 300) {
            bar.style.width = '100%';
            showMessage(`Nouvelle vidéo envoyée sur TV ${id}.`);
            input.value = '';
            setTimeout(() => {
                wrap.style.display = 'none';
                bar.style.width = '0%';
            }, 900);
            await loadTvs();
        } else {
            wrap.style.display = 'none';
            showMessage(data.error || data.detail || 'Échec de l’envoi vidéo.', 'error');
        }
    };
    xhr.onerror = () => {
        wrap.style.display = 'none';
        showMessage('Connexion interrompue pendant l’envoi.', 'error');
    };
    xhr.send(form);
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}

function escapeAttr(value) {
    return escapeHtml(value);
}

document.getElementById('refreshBtn').addEventListener('click', loadTvs);
document.getElementById('addTvBtn').addEventListener('click', () => {
    document.getElementById('newTvName').value = '';
    addTvDialog.showModal();
});
document.getElementById('cancelAddTv').addEventListener('click', () => addTvDialog.close());
document.getElementById('addTvForm').addEventListener('submit', async event => {
    event.preventDefault();
    const name = document.getElementById('newTvName').value;
    const response = await fetch('/api/tvs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name })
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) return showMessage(data.error || 'Impossible de créer la TV.', 'error');
    addTvDialog.close();
    showMessage(`TV ${data.id} « ${data.name} » créée. Elle est maintenant disponible dans l’app Roku.`);
    await loadTvs();
});

loadTvs();
setInterval(loadTvs, 8000);
