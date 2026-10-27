import { invoke, bootTheme, closeDialog } from './dialog.js';

const input = document.getElementById('name');
const error = document.getElementById('error');
const save = document.getElementById('save');

async function submit() {
  const name = input.value.trim();
  if (!name) {
    error.textContent = 'Enter a name for the layout.';
    input.focus();
    return;
  }
  save.disabled = true;
  const result = await invoke('layout:save-as', { name });
  save.disabled = false;
  if (!result || result.__error || result.ok === false) {
    error.textContent = result?.error || result?.__error || 'Could not save the layout.';
    return;
  }
  closeDialog();
}

document.getElementById('cancel').addEventListener('click', () => closeDialog());
save.addEventListener('click', submit);
input.addEventListener('keydown', (event) => {
  if (event.key === 'Enter') submit();
  else if (event.key === 'Escape') closeDialog();
});

bootTheme().then(() => {
  input.focus();
  input.select();
});
