import { invoke, applyVars, applyTheme } from './dialog.js';

const params = new URLSearchParams(window.location.search);
const instanceId = params.get('instance') || '';

applyTheme(params.get('theme'));
invoke('dialog:theme')
  .then((theme) => applyVars(theme?.vars))
  .catch(() => {});

document.getElementById('unlock').addEventListener('click', () => {
  invoke('rescue:unlock', { instanceId });
});

document.getElementById('menu').addEventListener('click', () => {
  invoke('rescue:menu', { instanceId });
});
