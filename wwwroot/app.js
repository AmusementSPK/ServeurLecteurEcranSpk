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
    if (!bytes) return 'No media';
    const units = ['B', 'KB', 'MB', 'GB'];
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

async function loadSystemInfo() {
    try {
        const response = await fetch('/api/system', { cache: 'no-store' });
        if (!response.ok) return;
        const data = await response.json();
        const name = data.name || 'Local Display Server';
        const tagline = data.tagline || 'Self-hosted media signage';
        const brand = document.getElementById('brandName');
        const taglineElement = document.getElementById('brandTagline');
        if (brand) brand.textContent = name;
        if (taglineElement) taglineElement.textContent = tagline;
        document.title = name + ' - Screen management';
    } catch {
    }
}

async function loadTvs() {
    if (state.uploading.size > 0) return;

    const hasSelectedFile = [...document.querySelectorAll('.file-input')]
        .some(input => input.files && input.files.length > 0);
    if (hasSelectedFile) return;

    try {
        const response = await fetch('/api/tvs', { cache: 'no-store' });
        if (!response.ok) throw new Error('Unable to load screens.');
        const data = await response.json();
        state.televisions = data.televisions ?? [];
        render();
        document.getElementById('serverStatus').textContent = 'Online';
    } catch (error) {
        document.getElementById('serverStatus').textContent = 'Error';
        showMessage(error.message, 'error');
    }
}

function render() {
    document.getElementById('tvCount').textContent = state.televisions.length;
    document.getElementById('runningCount').textContent = state.televisions.filter(x => x.running).length;
    document.getElementById('addTvBtn').disabled = false;

    if (state.televisions.length === 0) {
        tvGrid.innerHTML = '<div class="empty">No screens configured.</div>';
        return;
    }

    tvGrid.innerHTML = '';

    for (const tv of state.televisions) {
        const card = document.createElement('article');
        card.className = 'tv-card';

        let statusClass = 'missing';
        let statusText = 'No media';
        if (tv.hasVideo && tv.running) {
            statusClass = 'running';
            statusText = 'Streaming';
        } else if (tv.hasVideo) {
            statusClass = '';
            statusText = 'Starting…';
        }

        card.innerHTML = `
            <div class="tv-card-head">
                <div class="tv-id">TV ${escapeHtml(tv.id)}</div>
                <div class="status ${statusClass}">${statusText}</div>
            </div>
            <div class="field">
                <label for="name-${escapeHtml(tv.id)}">Display name</label>
                <div class="name-row">
                    <input id="name-${escapeHtml(tv.id)}" type="text" maxlength="80" value="${escapeAttr(tv.name)}">
                    <button class="secondary save-name" data-tv="${escapeAttr(tv.id)}">Save</button>
                </div>
            </div>
            <div class="video-box">
                <div class="video-meta">
                    <strong>Current media:</strong> ${escapeHtml(tv.fileName)}<br>
                    ${tv.hasVideo
                        ? `${formatBytes(tv.fileSize)}${tv.lastModified ? ` · modified ${new Date(tv.lastModified).toLocaleString()}` : ''}`
                        : 'No media pour cette TV'}
                </div>

                <input
                    class="file-input hidden"
                    id="file-${escapeHtml(tv.id)}"
                    type="file"
                    accept="video/*,image/*,.mp4,.mov,.m4v,.mkv,.webm,.avi,.mpeg,.mpg,.wmv,.flv,.mts,.m2ts,.3gp,.jpg,.jpeg,.png,.webp,.bmp,.gif,.tif,.tiff,.avif,.heic,.heif,.jfif">

                <div class="file-row">
                    <button class="secondary choose-video" data-tv="${escapeAttr(tv.id)}">Choose / change file</button>
                    <button class="primary upload-video" data-tv="${escapeAttr(tv.id)}" disabled>Upload and apply</button>
                </div>

                <div class="selected-file" id="selected-file-${escapeHtml(tv.id)}">No file selected</div>
                <div class="upload-state" id="upload-state-${escapeHtml(tv.id)}"></div>

                <div class="progress-wrap" id="progress-wrap-${escapeHtml(tv.id)}">
                    <div class="progress" id="progress-${escapeHtml(tv.id)}"></div>
                </div>
            </div>
            <div class="stream-row">
                <div class="stream-url" title="${escapeAttr(absoluteStreamUrl(tv.streamUrl))}">${escapeHtml(absoluteStreamUrl(tv.streamUrl))}</div>
                <button class="ghost copy-url" data-url="${escapeAttr(absoluteStreamUrl(tv.streamUrl))}">Copy</button>
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
            if (!response.ok) return showMessage(data.error || 'Unable to rename screen.', 'error');
            showMessage(`TV ${id} renamed « ${data.name} ».`);
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
                label.textContent = 'No file selected';
                uploadButton.disabled = true;
                uploadState.textContent = '';
                return;
            }

            label.textContent = `${file.name} · ${formatBytes(file.size)}`;
            uploadButton.disabled = false;
            uploadState.textContent = file.type && file.type.startsWith('image/')
                ? 'Ready to upload. The image will be converted to a 10-second still MP4.'
                : 'Ready to upload. The server will normalize the file automatically.';
        });
    });

    document.querySelectorAll('.upload-video').forEach(button =>
        button.addEventListener('click', () => uploadMedia(button.dataset.tv)));

    document.querySelectorAll('.copy-url').forEach(button => {
        button.addEventListener('click', async () => {
            try {
                await navigator.clipboard.writeText(button.dataset.url);
                showMessage('Stream URL copied.');
            } catch {
                showMessage('Could not copy automatically. Select the URL manually.', 'error');
            }
        });
    });
}

function uploadMedia(id) {
    if (state.uploading.has(id)) return;

    const input = document.getElementById(`file-${id}`);
    const file = input.files && input.files[0];
    if (!file) return showMessage('Choose a file first.', 'error');

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
    uploadState.textContent = 'Uploading file to server…';

    const xhr = new XMLHttpRequest();
    xhr.open('POST', `/api/tvs/${encodeURIComponent(id)}/video`);

    xhr.upload.onprogress = event => {
        if (event.lengthComputable) {
            const percent = Math.round(event.loaded / event.total * 100);
            bar.style.width = `${percent}%`;
            uploadState.textContent = `Upload: ${percent} %`;
        }
    };

    xhr.upload.onload = () => {
        bar.style.width = '100%';
        uploadState.textContent = 'File received. Automatic MP4 conversion in progress…';
    };

    xhr.onload = () => {
        let data = {};
        try { data = JSON.parse(xhr.responseText); } catch {}

        state.uploading.delete(id);
        chooseButton.disabled = false;

        if (xhr.status >= 200 && xhr.status < 300) {
            uploadState.textContent = `Done: ${data.fileName || 'média.mp4'} is now streaming.`;
            showMessage(`TV ${id} : ${data.fileName || 'the media'} is now online.`);
            input.value = '';
            document.getElementById(`selected-file-${id}`).textContent = 'No file selected';

            setTimeout(() => {
                wrap.style.display = 'none';
                bar.style.width = '0%';
                loadTvs();
            }, 1200);
        } else {
            uploadButton.disabled = false;
            wrap.style.display = 'none';
            uploadState.textContent = 'Conversion or upload failed.';
            showMessage(data.error || data.detail || data.title || 'Media upload failed.', 'error');
        }
    };

    xhr.onerror = () => {
        state.uploading.delete(id);
        uploadButton.disabled = false;
        chooseButton.disabled = false;
        wrap.style.display = 'none';
        uploadState.textContent = 'Connection interrupted.';
        showMessage('Connection interrupted during upload.', 'error');
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
    if (!response.ok) return showMessage(data.error || 'Unable to create screen.', 'error');
    addTvDialog.close();
    showMessage(`TV ${data.id} « ${data.name} » created.`);
    await loadTvs();
});

loadSystemInfo();
loadTvs();
