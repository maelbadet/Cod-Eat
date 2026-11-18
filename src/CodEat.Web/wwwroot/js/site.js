document.querySelectorAll('[data-submit]').forEach(el=>el.addEventListener('change',()=>el.form.requestSubmit()));
document.getElementById('print')?.addEventListener('click',()=>window.print());
document.getElementById('copy-list')?.addEventListener('click',async()=>{const text=document.getElementById('copy-text');try{await navigator.clipboard.writeText(text.value);document.getElementById('copy-status').textContent='Liste copiée.';}catch{text.classList.remove('sr-only');text.select();document.getElementById('copy-status').textContent='Sélectionnez et copiez le texte ci-dessous.';}});

// Update only the import panel: a completed import must not discard an unsaved recipe form.
const syncPanel = document.querySelector('[data-bookstack-sync]');
if (syncPanel) {
    const message = syncPanel.querySelector('[data-sync-message]');
    const stage = syncPanel.querySelector('[data-sync-stage]');
    const status = syncPanel.querySelector('[data-sync-status]');
    const progress = syncPanel.querySelector('[data-sync-progress]');
    const start = syncPanel.querySelector('[data-sync-start]');
    const refresh = syncPanel.querySelector('[data-sync-refresh]');
    async function pollSync() {
        try {
            const response = await fetch(syncPanel.dataset.statusUrl, {
                cache: 'no-store', redirect: 'error', headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error('Suivi indisponible');
            const result = await response.json();
            message.textContent = result.message;
            stage.textContent = result.stage + (result.isBusy && result.total > 0
                ? ` · ${result.completed} / ${result.total} fiches lues` : '');
            start.disabled = result.isBusy;
            status.classList.toggle('sync-error', result.state === 'Failed');
            progress.max = Math.max(1, result.total);
            progress.value = result.completed;
            progress.hidden = !result.isBusy || result.total <= 0;
            refresh.hidden = result.state !== 'Succeeded';
        } catch {
            stage.textContent = 'Suivi momentanément indisponible. Rechargez la page pour retrouver le statut.';
        }
        window.setTimeout(pollSync, 2000);
    }
    window.setTimeout(pollSync, 500);
}
