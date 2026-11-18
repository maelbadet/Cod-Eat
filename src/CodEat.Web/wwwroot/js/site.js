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
// The filter choices are present on first render. Only the bounded result grid
// is refreshed; typing keeps focus and stale requests cannot replace new ones.
const recipeSearch = document.querySelector('[data-recipe-search]');
if (recipeSearch) {
    const results = document.querySelector('[data-recipe-results]');
    const status = document.querySelector('[data-recipe-status]');
    let pending, timer;
    async function refreshRecipes(url, scroll = false) {
        pending?.abort();
        const current = new AbortController(); pending = current;
        const request = new URL(url); request.searchParams.set('handler', 'RecipeResults');
        results.setAttribute('aria-busy', 'true'); status.classList.add('sr-only'); status.textContent = 'Recherche en cours…';
        try {
            const response = await fetch(request, { signal: current.signal, redirect: 'error' });
            if (!response.ok) throw new Error('Recherche indisponible');
            const html = await response.text();
            if (pending !== current) return;
            results.innerHTML = html;
            history.pushState(null, '', url);
            status.textContent = results.querySelector('h2')?.textContent ?? 'Résultats actualisés.';
            if(scroll) results.scrollIntoView({block:'start',behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'auto':'smooth'});
        } catch (error) {
            if (error.name !== 'AbortError') { status.classList.remove('sr-only'); status.textContent = 'Recherche momentanément indisponible. Réessayez avec le bouton Rechercher.'; }
        } finally { if (pending === current) results.removeAttribute('aria-busy'); }
    }
    function search() {
        if (!recipeSearch.reportValidity()) return;
        const url = new URL(recipeSearch.action);
        url.search = new URLSearchParams(new FormData(recipeSearch)).toString();
        refreshRecipes(url);
    }
    recipeSearch.addEventListener('submit', event => { event.preventDefault(); clearTimeout(timer); search(); });
    recipeSearch.addEventListener('input', event => {
        if (event.isComposing) return;
        clearTimeout(timer); timer = setTimeout(search, 300);
    });
    recipeSearch.addEventListener('change', event => {
        if (event.target.matches('select, input[type=checkbox]')) { clearTimeout(timer); search(); }
    });
    results.addEventListener('click', event => {
        const link = event.target.closest('[data-recipe-page]');
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault(); refreshRecipes(new URL(link.href), true);
    });
    window.addEventListener('popstate', () => location.reload());
}
const recipePlan = document.querySelector('[data-recipe-plan]');
if (recipePlan) {
    const update = () => {
        const people = Number(recipePlan.elements.People.value);
        const leftovers = recipePlan.querySelector('[name=Leftovers][type=checkbox]').checked;
        recipePlan.querySelector('[data-plan-summary]').textContent = people > 0
            ? `${people * (leftovers ? 2 : 1)} portions à cuisiner pour ${people} personne${people > 1 ? 's' : ''}${leftovers ? `, dont ${people} pour le lendemain` : ''}.` : 'Choisissez le nombre de personnes.';
    };
    recipePlan.addEventListener('input', update); update();
}
