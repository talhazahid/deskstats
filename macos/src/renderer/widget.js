/* Floating widget host.
   Loads one widget instance, applies complete state pushes from the main
   process, measures its own height, and implements dragging + width resizing. */

import { createWidget, layoutResponsive, widthLimits } from './widgets.js';

const invoke = (channel, payload) => window.deskstats.invoke(channel, payload);
const params = new URLSearchParams(window.location.search);
const instanceId = params.get('instance') || '';
const root = document.getElementById('root');
const handles = [...document.querySelectorAll('.resize-handle')];
const badge = document.querySelector('.resize-badge');
const badgeHint = document.getElementById('resize-hint');

let widget = null;
let lastHeight = 0;
let lastWidth = 0;
let lastTimestamp = null;
let lastColumns = 1;
let large = false;
let resizeMode = false;
let resizing = false;
let dragging = false;
let measureQueued = false;

const INTERACTIVE = 'button, a, input, select, textarea, .cal-nav, .cal-title, .trash-empty';

function setVars(vars) {
  if (!vars) return;
  for (const [key, value] of Object.entries(vars)) {
    document.documentElement.style.setProperty(key, value);
  }
}

function setTheme(theme) {
  const light = String(theme || '').toLowerCase() === 'light';
  document.body.classList.toggle('light-theme', light);
  document.documentElement.style.setProperty('color-scheme', light ? 'light' : 'dark');
}

function updateBadge() {
  if (!badgeHint) return;
  const columns = lastColumns === 1 ? '1 column' : `${lastColumns} columns`;
  badgeHint.textContent = `${columns} · drag the edges · Esc to finish`;
}

function measure() {
  if (!widget || measureQueued) return;
  measureQueued = true;
  requestAnimationFrame(() => {
    measureQueued = false;
    if (!widget) return;
    // Root report is CSS px; the main process multiplies by the window's page
    // zoom (1.35 for large widgets), matching what setBounds expects.
    const height = Math.ceil(root.getBoundingClientRect().height + 20);
    if (height < 40 || Math.abs(height - lastHeight) < 1) return;
    lastHeight = height;
    invoke('widget:height', { instanceId, height });
  });
}

function relayout() {
  if (!widget) return;
  const width = root.clientWidth;
  if (width && width !== lastWidth) {
    lastWidth = width;
    const result = layoutResponsive(widget.element);
    lastColumns = result.columns || 1;
    updateBadge();
  }
  measure();
}

function applyState(data) {
  if (!widget || !data) return;
  large = Boolean(data.large);
  if (data.appearance) {
    setVars(data.appearance.vars);
    setTheme(data.appearance.theme);
  }

  const isNew = Boolean(data.snapshot) && data.snapshot.timestamp !== lastTimestamp;
  if (data.snapshot) lastTimestamp = data.snapshot.timestamp;

  widget.update({
    snapshot: data.snapshot,
    aux: data.aux,
    settings: data.settings,
    appearance: data.appearance,
    isNewSnapshot: isNew,
  });

  requestAnimationFrame(relayout);
}

function setResizeMode(on) {
  resizeMode = Boolean(on);
  document.body.classList.toggle('resize-mode', resizeMode);
  for (const handle of handles) handle.hidden = !resizeMode;
  if (badge) badge.hidden = !resizeMode;
  if (!resizeMode) {
    resizing = false;
    document.body.classList.remove('resizing');
  } else {
    const limits = currentLimits();
    void limits;
    updateBadge();
  }
}

function currentLimits() {
  if (!widget) return { min: 300, max: 560 };
  const limits = widthLimits(widget.element);
  const zoom = large ? 1.35 : 1;
  return {
    min: Math.round(limits.min * zoom),
    max: Math.round(limits.max * zoom),
  };
}

/* ---------------------------------------------------------------- dragging */

function endDrag() {
  if (!dragging) return;
  dragging = false;
  document.body.classList.remove('dragging');
  invoke('widget:drag-end', { instanceId });
}

function beginDrag(event) {
  if (event.button !== 0 || resizeMode || dragging) return;
  if (event.target.closest(INTERACTIVE)) return;
  dragging = true;
  document.body.classList.add('dragging');
  try {
    document.body.setPointerCapture(event.pointerId);
  } catch {
    /* capture is best effort */
  }
  invoke('widget:drag-start', { instanceId });
  event.preventDefault();
}

/* ---------------------------------------------------------------- resizing */

function beginResize(event) {
  if (!widget || event.button !== 0) return;
  const handle = event.currentTarget;
  const limits = currentLimits();
  resizing = true;
  document.body.classList.add('resizing');
  try {
    handle.setPointerCapture(event.pointerId);
  } catch {
    /* capture is best effort */
  }
  invoke('widget:resize-start', {
    instanceId,
    side: handle.dataset.side === 'left' ? 'left' : 'right',
    min: limits.min,
    max: limits.max,
  });
  event.preventDefault();
  event.stopPropagation();
}

function endResize(cancelled = false) {
  if (!resizing) return;
  resizing = false;
  document.body.classList.remove('resizing');
  invoke(cancelled ? 'widget:resize-cancel' : 'widget:resize-end', { instanceId });
}

/* ------------------------------------------------------------------ boot */

async function boot() {
  if (!instanceId) {
    root.textContent = 'Missing widget instance.';
    return;
  }
  const data = await invoke('widget:bootstrap', { instanceId });
  if (!data || data.__error) {
    root.textContent = data?.__error || 'This widget is no longer in the layout.';
    return;
  }

  setVars(data.appearance?.vars);
  setTheme(data.appearance?.theme);
  large = Boolean(data.large);

  widget = createWidget(data.widgetId, {
    settings: data.settings,
    snapshot: data.snapshot,
    aux: data.aux,
    appearance: data.appearance,
  });
  lastTimestamp = data.snapshot?.timestamp ?? null;
  root.replaceChildren(widget.element);

  // The resize UI follows the widget's own theme.
  document.body.addEventListener('pointerdown', beginDrag);
  window.addEventListener('pointerup', endDrag);
  window.addEventListener('pointercancel', endDrag);
  window.addEventListener('blur', endDrag);

  for (const handle of handles) {
    handle.hidden = true;
    handle.addEventListener('pointerdown', beginResize);
  }
  window.addEventListener('pointerup', () => endResize(false));
  window.addEventListener('pointercancel', () => endResize(true));

  document.addEventListener('contextmenu', (event) => {
    event.preventDefault();
    if (resizeMode) return;
    invoke('widget:context-menu', { instanceId });
  });

  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    if (resizing) endResize(true);
    else if (resizeMode) invoke('widget:resize-cancel', { instanceId });
    else if (dragging) endDrag();
  });

  // Album art and fonts change the natural height after the first paint.
  const observer = new ResizeObserver(() => {
    const width = root.clientWidth;
    if (widget && width && Math.abs(width - lastWidth) > 0.5) {
      lastWidth = 0;
      relayout();
    } else {
      measure();
    }
  });
  observer.observe(root);

  requestAnimationFrame(() => {
    lastWidth = 0;
    relayout();
  });

  setInterval(() => {
    if (!widget) return;
    widget.tick();
    measure();
  }, 1000);

  window.deskstats.on('state', (state) => applyState(state));
  window.deskstats.on('resize-mode', (payload) => setResizeMode(payload?.on));

  if (data.clickThrough) document.body.classList.add('click-through');
}

boot().catch((error) => {
  console.error('Widget boot failed:', error);
  root.textContent = `DeskStats widget failed to load: ${error.message}`;
});
