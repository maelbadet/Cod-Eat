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
const calorieFilter = document.querySelector('[data-calorie-filter]');
if (calorieFilter) {
    const min = calorieFilter.querySelector('[data-calorie-handle-min]');
    const max = calorieFilter.querySelector('[data-calorie-handle-max]');
    const minValue = calorieFilter.querySelector('[data-calorie-value-min]');
    const maxValue = calorieFilter.querySelector('[data-calorie-value-max]');
    const ceiling = Number(max.max);
    minValue.disabled = maxValue.disabled = false;
    calorieFilter.querySelector('.calorie-slider').hidden = false;
    function update(source) {
        if (Number(min.value) > Number(max.value)) {
            if (source === min) max.value = min.value; else min.value = max.value;
        }
        if (source) {
            minValue.value = Number(min.value) === 0 ? '' : min.value;
            maxValue.value = Number(max.value) === ceiling ? '' : max.value;
        }
        const minText = minValue.value === '' ? 'Sans minimum' : `${min.value} kcal`;
        const maxText = maxValue.value === '' ? 'Sans maximum' : `${max.value} kcal`;
        calorieFilter.querySelector('[data-calorie-min]').textContent = minText;
        calorieFilter.querySelector('[data-calorie-max]').textContent = maxText;
        min.setAttribute('aria-valuetext', minText); max.setAttribute('aria-valuetext', maxText);
        const fill = calorieFilter.querySelector('[data-calorie-fill]');
        fill.style.left = `${Number(min.value) / ceiling * 100}%`;
        fill.style.width = `${(Number(max.value) - Number(min.value)) / ceiling * 100}%`;
        min.classList.toggle('calorie-top', Number(min.value) >= ceiling / 2);
    }
    min.addEventListener('input', () => update(min));
    max.addEventListener('input', () => update(max));
    update();
}
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
            ? `${people * (leftovers ? 2 : 1)} portions à cuisiner pour ${people} personne${people > 1 ? 's' : ''}${leftovers ? `, dont ${people} pour demain midi` : ''}.` : 'Choisissez le nombre de personnes.';
    };
    recipePlan.addEventListener('input', update); update();
}
// A planned half-day opens its saved recipe. Editing controls keep their own actions.
document.addEventListener('click', event => {
    const meal = event.target.closest('.meal-planned');
    if (!meal || event.target.closest('a, button, input, select, textarea, summary, details, form, label')
        || window.getSelection()?.toString()) return;
    const recipe = meal.querySelector('.planned-recipe-link');
    if (recipe) location.assign(recipe.href);
});
// Optional photos disappear if a file becomes unavailable after the page rendered.
function hideBrokenRecipePhoto(event) {
    if (!event.target.matches?.('[data-recipe-photo]')) return;
    const frame = event.target.closest('.recipe-photo');
    const link = frame?.closest('.recipe-photo-link');
    (link ?? frame)?.remove();
}
document.addEventListener('error', hideBrokenRecipePhoto, true);
const weekPicker = document.querySelector('[data-week-picker]');
if (weekPicker) {
    const date = weekPicker.querySelector('[data-week-date]');
    weekPicker.querySelector('summary').addEventListener('click', () => {
        if (weekPicker.open) return;
        requestAnimationFrame(() => {
            if (!weekPicker.open) return;
            date.focus({preventScroll:true});
            try { date.showPicker?.(); } catch { /* The date field remains usable. */ }
        });
    });
    date.addEventListener('change', () => {
        if (date.value && date.validity.valid) date.form.requestSubmit();
    });
    document.addEventListener('click', event => {
        if (!weekPicker.contains(event.target)) weekPicker.open = false;
    });
    weekPicker.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            weekPicker.open = false;
            weekPicker.querySelector('summary').focus();
        }
    });
}

