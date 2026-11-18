document.querySelectorAll('[data-submit]').forEach(el=>el.addEventListener('change',()=>el.form.requestSubmit()));
document.getElementById('print')?.addEventListener('click',()=>window.print());
document.getElementById('copy-list')?.addEventListener('click',async()=>{const text=document.getElementById('copy-text');try{await navigator.clipboard.writeText(text.value);document.getElementById('copy-status').textContent='Liste copiée.';}catch{text.classList.remove('sr-only');text.select();document.getElementById('copy-status').textContent='Sélectionnez et copiez le texte ci-dessous.';}});
