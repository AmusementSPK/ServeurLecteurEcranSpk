const state = { televisions: [], uploading: new Set() };

const tvGrid = document.getElementById('tvGrid');
const messageBox = document.getElementById('message');
const addTvDialog = document.getElementById('addTvDialog');

function showMessage(text, type = 'ok') {
    messageBox.textContent = text;
    messageBox.className = `message ${type}`;
    clearTimeout(showMessage.timer);
    showMessage.timer = setTimeout(() => messageBox.classList.add('hidden'), 7000);
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
    if (state.uploading.size > 0) return;

    const hasSelectedFile = [...document.querySelectorAll('.file-input')]
        .some(input => input.files && input.files.length > 0);
    if (hasSelectedFile) return;

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

                <input
                    class="file-input hidden"
                    id="file-${escapeHtml(tv.id)}"
                    type="file"
                    accept="video/*,.mp4,.mov,.m4v,.mkv,.webm,.avi,.mpeg,.mpg">

                <div class="file-row">
                    <button class="secondary choose-video" data-tv="${escapeAttr(tv.id)}">Choisir / changer le fichier</button>
                    <button class="primary upload-video" data-tv="${escapeAttr(tv.id)}" disabled>Envoyer et appliquer</button>
                </div>

                <div class="selected-file" id="selected-file-${escapeHtml(tv.id)}">Aucun fichier choisi</div>
                <div class="upload-state" id="upload-state-${escapeHtml(tv.id)}"></div>

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

    document.querySelectorAll('.choose-video').forEach(button => {
        button.addEventListener('click', () => {
            const id = button.dataset.tv;
            const input = document.getElementById(`file-${id}`);
            input.value = '';
            input.click();
        });
    });

    document.querySelectorAll('.file-input').forEach(input => {
        input.addEventListener('change', () => {
            const id = input.id.substring('file-'.length);
            const file = input.files && input.files[0];
            const label = document.getElementById(`selected-file-${id}`);
            const uploadButton = document.querySelector(`.upload-video[data-tv="${cssEscape(id)}"]`);
            const uploadState = document.getElementById(`upload-state-${id}`);

            if (!file) {
                label.textContent = 'Aucun fichier choisi';
                uploadButton.disabled = true;
                uploadState.textContent = '';
                return;
            }

            label.textContent = `${file.name} · ${formatBytes(file.size)}`;
            uploadButton.disabled = false;
            uploadState.textContent = 'Prêt à envoyer.';
        });
    });

    document.querySelectorAll('.upload-video').forEach(button =>
        button.addEventListener('click', () => uploadVideo(button.dataset.tv)));

    document.querySelectorAll('.copy-url').forEach(button => {
        button.addEventListener('click', async () => {
            try {
                await navigator.clipboard.writeText(button.dataset.url);
                showMessage('Adresse du flux copiée.');
            } catch {
                showMessage('Impossible de copier automatiquement. Sélectionne l’adresse manuellement.', 'error');
            }
        });
    });
}

function uploadVideo(id) {
    if (state.uploading.has(id)) return;

    const input = document.getElementById(`file-${id}`);
    const file = input.files && input.files[0];
    if (!file) return showMessage('Choisis d’abord un fichier vidéo.', 'error');

    const allowed = ['.mp4', '.mov', '.m4v', '.mkv', '.webm', '.avi', '.mpeg', '.mpg'];
    const lowerName = file.name.toLowerCase();
    if (!allowed.some(ext => lowerName.endsWith(ext))) {
        return showMessage('Format non supporté. Utilise MP4, MOV, M4V, MKV, WEBM, AVI, MPEG ou MPG.', 'error');
    }

    const form = new FormData();
    form.append('video', file, file.name);

    const wrap = document.getElementById(`progress-wrap-${id}`);
    const bar = document.getElementById(`progress-${id}`);
    const uploadState = document.getElementById(`upload-state-${id}`);
    const uploadButton = document.querySelector(`.upload-video[data-tv="${cssEscape(id)}"]`);
    const chooseButton = document.querySelector(`.choose-video[data-tv="${cssEscape(id)}"]`);

    state.uploading.add(id);
    uploadButton.disabled = true;
    chooseButton.disabled = true;
    wrap.style.display = 'block';
    bar.style.width = '0%';
    uploadState.textContent = 'Envoi du fichier au serveur…';

    const xhr = new XMLHttpRequest();
    xhr.open('POST', `/api/tvs/${encodeURIComponent(id)}/video`);

    xhr.upload.onprogress = event => {
        if (event.lengthComputable) {
            const percent = Math.round(event.loaded / event.total * 100);
            bar.style.width = `${percent}%`;
            uploadState.textContent = `Envoi au serveur : ${percent} %`;
        }
    };

    xhr.upload.onload = () => {
        bar.style.width = '100%';
        uploadState.textContent = 'Fichier reçu. Conversion automatique pour les télés en cours…';
    };

    xhr.onload = async () => {
        let data = {};
        try { data = JSON.parse(xhr.responseText); } catch {}

        state.uploading.delete(id);
        chooseButton.disabled = false;

        if (xhr.status >= 200 && xhr.status < 300) {
            uploadState.textContent = 'Conversion terminée. Nouvelle vidéo en diffusion.';
            showMessage(`TV ${id} : vidéo convertie et mise en ligne.`);
            input.value = '';
            document.getElementById(`selected-file-${id}`).textContent = 'Aucun fichier choisi';

            setTimeout(() => {
                wrap.style.display = 'none';
                bar.style.width = '0%';
                loadTvs();
            }, 1200);
        } else {
            uploadButton.disabled = false;
            wrap.style.display = 'none';
            uploadState.textContent = 'Échec de la conversion ou de l’envoi.';
            showMessage(data.error || data.detail || data.title || 'Échec de l’envoi vidéo.', 'error');
        }
    };

    xhr.onerror = () => {
        state.uploading.delete(id);
        uploadButton.disabled = false;
        chooseButton.disabled = false;
        wrap.style.display = 'none';
        uploadState.textContent = 'Connexion interrompue.';
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

function cssEscape(value) {
    if (window.CSS && CSS.escape) return CSS.escape(String(value));
    return String(value).replace(/[^a-zA-Z0-9_-]/g, '\\$&');
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
    showMessage(`TV ${data.id} « ${data.name} » créée.`);
    await loadTvs();
});

loadTvs();
