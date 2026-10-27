/* Sidebar renderer: one scrolling stack of docked widget cards.
   Complete layout/theme state arrives as `sidebar-state`; per-second metrics
   arrive as `sidebar-metrics` so hidden/settings changes stay cheap. */

import { createWidget, layoutResponsive } from './widgets.js';

const invoke = (channel, payload) => window.deskstats.invoke(channel, payload);
const body = document.body;
const header = document.getElementById('header');
const stack = document.getElementById('stack');

const entries = new Map(); // instanceId -> { slot, scale, widget, item, aux, timestamp }
let layoutQueued = false;
let settings = {};
let theme = 'dark';

function setVars(vars) {
  if (!vars) return;
  for (const [key, value] of Object.entries(vars)) {
    document.documentElement.style.setProperty(key, value);
  }
}

function setTheme(next) {
  theme = String(next || '').toLowerCase() === 'light' ? 'light' : 'dark';
  body.classList.toggle('light-theme', theme === 'light');
  document.documentElement.style.setProperty('color-scheme', theme);
}

function scheduleLayout() {
  if (layoutQueued) return;
  layoutQueued = true;
  requestAnimationFrame(() => {
    layoutQueued = false;
    for (const entry of entries.values()) {
      layoutResponsive(entry.widget.element);
    }
  });
}

function createEntry(item) {
  const slot = document.createElement('div');
  slot.className = 'sidebar-slot';
  slot.dataset.instance = item.instanceId;

  const scale = document.createElement('div');
  scale.className = 'sidebar-scale';
  slot.appendChild(scale);

  const widget = createWidget(item.widgetId, {
    settings: item.settings,
    snapshot: item.snapshot,
    aux: item.aux,
    appearance: item.appearance,
  });
  scale.appendChild(widget.element);

  slot.addEventListener('contextmenu', (event) => {
    event.preventDefault();
    event.stopPropagation();
    invoke('sidebar:card-menu', { instanceId: item.instanceId });
  });

  stack.appendChild(slot);
  const entry = {
    slot,
    scale,
    widget,
    item,
    aux: item.aux || {},
    timestamp: item.snapshot?.timestamp ?? null,
  };
  entries.set(item.instanceId, entry);
  return entry;
}

function updateEntry(entry, item) {
  entry.item = item;
  const isNew = Boolean(item.snapshot) && item.snapshot.timestamp !== entry.timestamp;
  if (item.snapshot) entry.timestamp = item.snapshot.timestamp;
  if (item.aux !== undefined) entry.aux = item.aux;

  entry.slot.classList.toggle('large', Boolean(item.large));
  entry.widget.update({
    snapshot: item.snapshot,
    aux: entry.aux,
    settings: item.settings,
    appearance: item.appearance,
    isNewSnapshot: isNew,
  });
}

function applyState(state) {
  if (!state) return;
  setTheme(state.theme);
  setVars(state.themeVars);
  settings = state.settings || {};

  body.classList.toggle('has-background', Boolean(settings.sidebarBackground));
  body.classList.toggle('no-title', !settings.sidebarShowTitle);
  body.classList.toggle('collapsed', state.expanded === false);
  header.textContent = 'DeskStats';
  header.style.display = settings.sidebarShowTitle ? '' : 'none';

  const live = new Set();
  for (const item of state.items || []) {
    live.add(item.instanceId);
    const entry = entries.get(item.instanceId);
    if (entry) updateEntry(entry, item);
    else createEntry(item);
  }
  for (const [id, entry] of entries) {
    if (live.has(id)) continue;
    entry.slot.remove();
    entries.delete(id);
  }

  // Keep layout order in sync with the layout file (docking appends).
  for (const item of state.items || []) {
    const entry = entries.get(item.instanceId);
    if (entry) stack.appendChild(entry.slot);
  }

  if (!(state.items || []).length) {
    if (!stack.querySelector('.sidebar-empty')) {
      const note = document.createElement('div');
      note.className = 'sidebar-empty';
      note.textContent = 'No widgets are docked yet. Right-click here or use the menu-bar menu to add one.';
      stack.appendChild(note);
    }
  } else {
    stack.querySelector('.sidebar-empty')?.remove();
  }

  scheduleLayout();
}

function applyMetrics(payload) {
  if (!payload?.snapshot) return;
  const snapshot = payload.snapshot;
  for (const entry of entries.values()) {
    const isNew = snapshot.timestamp !== entry.timestamp;
    entry.timestamp = snapshot.timestamp;
    entry.widget.update({
      snapshot,
      aux: entry.aux,
      settings: entry.item.settings,
      appearance: entry.item.appearance,
      isNewSnapshot: isNew,
    });
  }
  scheduleLayout();
}

/* Right-click on empty sidebar space opens the same menu as the tray icon. */
document.addEventListener('contextmenu', (event) => {
  event.preventDefault();
  if (event.target.closest('.sidebar-slot')) return;
  invoke('sidebar:app-menu');
});

let resizeTimer = null;
window.addEventListener('resize', () => {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(scheduleLayout, 30);
});

window.deskstats.on('sidebar-state', applyState);
window.deskstats.on('sidebar-metrics', applyMetrics);

invoke('sidebar:get')
  .then((state) => applyState(state))
  .catch((error) => {
    console.error('Sidebar boot failed:', error);
  });
