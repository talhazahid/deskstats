import { invoke, applyVars, applyTheme } from './dialog.js';

const params = new URLSearchParams(window.location.search);
document.getElementById('label').textContent = params.get('label') || 'Screen';

applyTheme(params.get('theme'));
invoke('dialog:theme')
  .then((theme) => applyVars(theme?.vars))
  .catch(() => {});
