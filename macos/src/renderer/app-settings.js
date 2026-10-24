/* App-level preferences (upstream AppSettingsWindow). Everything here is
   applied live by the main process; the note text reflects that difference
   from the Windows build (where sidebar geometry needed a restart). */

import {
  invoke,
  bootTheme,
  closeDialog,
  el,
  dialogHeader,
  button,
  card,
  row,
  stacked,
  toggle,
  select,
  slider,
  numberInput,
} from './dialog.js';

const headerHost = document.getElementById('header');
const scroll = document.getElementById('scroll');
const footer = document.getElementById('footer');

let data = null;
let values = {};
let message = null;
let updateStatus = null;

function addToggle(body, label, helpText, key) {
  body.appendChild(
    row(
      label,
      helpText,
      toggle(Boolean(values[key]), (next) => {
        values[key] = next;
      }),
    ),
  );
}

function sectionMonitoring() {
  const { node, body } = card('Monitoring', { plainTitle: true });
  const poll = numberInput(values.pollIntervalMs, { className: 'short right' });
  poll.min = '200';
  poll.max = '60000';
  poll.addEventListener('input', () => {
    values.pollIntervalMs = Number(poll.value);
  });
  body.appendChild(row('Sensor poll interval (ms)', 'How often CPU, memory and network readings refresh.', poll));
  addToggle(body, 'Monitor GPU sensors', 'GPU load, memory, clock and temperature from the IOAccelerator registry.', 'loadGpu');
  addToggle(body, 'Monitor drive usage', 'Capacity and read/write activity for mounted volumes.', 'loadDrives');
  addToggle(body, 'Monitor network throughput', 'Per-interface download and upload rates.', 'loadNetwork');
  body.appendChild(el('div', 'field-help', 'Sensor selection applies immediately on macOS.'));
  return node;
}

function sectionAppearance() {
  const { node, body } = card('Appearance', { plainTitle: true });
  body.appendChild(
    row(
      'Theme',
      null,
      select(
        [
          { value: 'dark', label: 'Dark' },
          { value: 'light', label: 'Light' },
        ],
        values.theme,
        (next) => {
          values.theme = next;
        },
      ),
    ),
  );
  addToggle(body, 'Show card frame', 'Outer background and border around widgets.', 'cardFrameV2');
  body.appendChild(el('div', 'field-help', 'Off: only the inner sections are drawn — a lighter, frameless look.'));
  return node;
}

function sectionSidebar() {
  const { node, body } = card('Sidebar', { plainTitle: true });

  const screenOptions = [{ value: '', label: 'Main screen (automatic)' }];
  for (const monitor of data.monitors || []) screenOptions.push({ value: monitor.id, label: monitor.label });
  const screenSelect = select(screenOptions, values.sidebarScreenId || '', (next) => {
    values.sidebarScreenId = next;
    if (next) invoke('app-settings:identify', { ids: [next] });
  });
  body.appendChild(row('Screen', null, screenSelect));
  body.appendChild(
    el(
      'div',
      'field-help',
      'Screens are named by where they sit and their model. Identify shows the name on each screen.',
    ),
  );

  const identify = button('Identify screens', { subtle: true });
  identify.addEventListener('click', () => {
    const id = values.sidebarScreenId;
    invoke('app-settings:identify', { ids: id ? [id] : [] });
  });
  const identifyRow = el('div', 'field');
  identifyRow.appendChild(el('div', 'field-left'));
  const identifyRight = el('div', 'field-controls');
  identifyRight.appendChild(identify);
  identifyRow.appendChild(identifyRight);
  body.appendChild(identifyRow);

  body.appendChild(
    row(
      'Screen edge',
      null,
      select(
        [
          { value: 'right', label: 'Right' },
          { value: 'left', label: 'Left' },
        ],
        values.sidebarEdge,
        (next) => {
          values.sidebarEdge = next;
        },
      ),
    ),
  );

  values.sidebarCardOpacity = Number(values.sidebarCardOpacity ?? 1);
  const opacityText = el('div', 'field-value', `${Math.round(values.sidebarCardOpacity * 100)}%`);
  const opacity = slider({
    min: 0,
    max: 100,
    step: 5,
    value: Math.round(values.sidebarCardOpacity * 100),
    onChange: (next) => {
      values.sidebarCardOpacity = next / 100;
      opacityText.textContent = `${Math.round(next)}%`;
    },
  });
  body.appendChild(stacked('Card background opacity', null, opacity, { valueText: opacityText }));

  addToggle(body, 'Show “DeskStats” title', null, 'sidebarShowTitle');
  addToggle(body, 'Show sidebar background', 'Frosted panel behind the docked cards.', 'sidebarBackground');
  addToggle(body, 'Auto-hide when the mouse leaves', null, 'sidebarAutoHide');
  addToggle(body, 'Pin (disable auto-hide)', null, 'sidebarPinned');
  body.appendChild(
    el(
      'div',
      'field-help',
      'The sidebar only appears while it holds docked widgets.',
    ),
  );
  return node;
}

function sectionStartup() {
  const { node, body } = card('Startup', { plainTitle: true });
  addToggle(body, 'Launch DeskStats at login', 'Uses the macOS login item so the menu-bar icon returns after a restart.', 'startWithWindows');

  body.appendChild(el('h2', 'plain-title', 'Updates'));
  addToggle(body, 'Check for updates at startup', 'Automatic checks stay quiet unless a newer release exists.', 'automaticUpdates');
  body.appendChild(
    el(
      'div',
      'field-help',
      'The macOS build checks the upstream DeskStats releases. Your layout is kept; downloads open on the release page.',
    ),
  );

  updateStatus = el('div', 'field-help', '');
  const check = button('Check for updates…', { subtle: true });
  check.addEventListener('click', async () => {
    check.disabled = true;
    updateStatus.textContent = 'Checking…';
    const result = await invoke('updates:check', { manual: true });
    check.disabled = false;
    if (!result) updateStatus.textContent = 'A check is already running.';
    else if (result.status === 'available') updateStatus.textContent = `Version ${result.latest} is available.`;
    else if (result.status === 'current') updateStatus.textContent = 'You’re up to date.';
    else updateStatus.textContent = 'Could not check for updates. Check your connection.';
  });
  const checkRow = el('div', 'field');
  const left = el('div', 'field-left');
  left.appendChild(check);
  left.appendChild(updateStatus);
  checkRow.appendChild(left);
  body.appendChild(checkRow);
  return node;
}

function render() {
  headerHost.replaceChildren(
    dialogHeader('DeskStats settings', 'Monitoring, appearance, the sidebar and startup.'),
  );

  scroll.replaceChildren();
  scroll.appendChild(sectionMonitoring());
  scroll.appendChild(sectionAppearance());
  scroll.appendChild(sectionSidebar());
  scroll.appendChild(sectionStartup());

  message = el('div', 'dialog-error left');
  const cancel = button('Cancel');
  cancel.addEventListener('click', () => closeDialog());
  const save = button('Save', { primary: true });
  save.addEventListener('click', onSave);
  footer.replaceChildren(message, cancel, save);
}

async function onSave() {
  const poll = Math.round(Number(values.pollIntervalMs));
  values.pollIntervalMs = Number.isFinite(poll) ? Math.max(200, Math.min(60_000, poll)) : 1000;

  const result = await invoke('app-settings:save', values);
  if (!result || result.__error) {
    message.textContent = result?.__error || 'Could not save settings.';
    return;
  }
  closeDialog();
}

bootTheme()
  .then(() => invoke('app-settings:data'))
  .then((loaded) => {
    if (!loaded || loaded.__error) throw new Error(loaded?.__error || 'Could not load app settings.');
    data = loaded;
    values = { ...loaded.settings };
    render();
  })
  .catch((error) => {
    headerHost.replaceChildren(dialogHeader('DeskStats settings', error.message));
    const close = button('Close');
    close.addEventListener('click', () => closeDialog());
    footer.replaceChildren(close);
  });
