/* Widget settings editor — a direct port of SettingsEditorWindow:
   schema-driven rows (toggle / slider / numeric / text / dropdown / city
   lookup) plus the "Window" card with live appearance preview. */

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
  textInput,
  numberInput,
  formatNumber,
  withUnit,
} from './dialog.js';

const instanceId = new URLSearchParams(window.location.search).get('instance') || '';
const headerHost = document.getElementById('header');
const scroll = document.getElementById('scroll');
const footer = document.getElementById('footer');

let data = null;
let values = {};
let pickedCity = null;
let citySearchTimer = null;
let previewFrame = 0;
let message = null;
let saving = false;

/* --------------------------------------------------------------- appearance */

function appearanceValues() {
  return {
    opacity: Number(values.__opacity ?? 1),
    layer: values.__layer || 'normal',
    theme: values.__theme || '',
    clickThrough: Boolean(values.__clickThrough),
  };
}

function queuePreview() {
  if (!data?.appearance || previewFrame) return;
  previewFrame = requestAnimationFrame(() => {
    previewFrame = 0;
    if (!data?.appearance) return;
    invoke('widget-settings:preview', {
      instanceId,
      appearance: { ...appearanceValues(), clickThrough: Boolean(data.appearance.clickThrough) },
    });
  });
}

function buildWindowCard(appearance) {
  const { node, body } = card('Window');
  values.__opacity = Number(appearance.opacity ?? 1);
  values.__layer = appearance.layer || 'normal';
  values.__theme = appearance.theme || '';
  values.__clickThrough = Boolean(appearance.clickThrough);

  const valueText = el('div', 'field-value', `${Math.round(values.__opacity * 100)}%`);
  const opacity = slider({
    min: 0,
    max: 100,
    step: 5,
    value: Math.round(values.__opacity * 100),
    onChange: (next) => {
      values.__opacity = next / 100;
      valueText.textContent = `${Math.round(next)}%`;
      queuePreview();
    },
  });
  body.appendChild(stacked('Background opacity', null, opacity, { valueText }));

  body.appendChild(
    row(
      'Theme',
      'Dark or light for this widget only, or the same as the app.',
      select(
        [
          { value: '', label: 'Same as app' },
          { value: 'dark', label: 'Dark' },
          { value: 'light', label: 'Light' },
        ],
        values.__theme,
        (next) => {
          values.__theme = next;
          queuePreview();
        },
      ),
    ),
  );

  body.appendChild(
    row(
      'Layer',
      'Always on top, a normal window, or pinned to the desktop.',
      select(
        [
          { value: 'top', label: 'Always on top' },
          { value: 'normal', label: 'Normal' },
          { value: 'bottom', label: 'On desktop (behind windows)' },
        ],
        values.__layer,
        (next) => {
          values.__layer = next;
          queuePreview();
        },
      ),
    ),
  );

  body.appendChild(
    row(
      'Click-through',
      'Mouse clicks pass through the widget. Turn it off again from the tray → Widgets menu.',
      toggle(values.__clickThrough, (next) => {
        values.__clickThrough = next;
      }),
    ),
  );

  return node;
}

/* -------------------------------------------------------------- city lookup */

function cityField(field, initial, coordBoxes) {
  const wrap = el('div', 'city-wrap');
  const input = textInput(initial, { placeholder: 'Start typing a city…' });
  wrap.appendChild(input);

  let list = null;
  let requestId = 0;
  const hide = () => {
    list?.remove();
    list = null;
  };

  const applyCity = (city) => {
    pickedCity = city;
    values[field.key] = city.name;
    input.value = city.name;
    values.latitude = city.latitude;
    values.longitude = city.longitude;
    if (coordBoxes.latitude) coordBoxes.latitude.value = String(city.latitude);
    if (coordBoxes.longitude) coordBoxes.longitude.value = String(city.longitude);
    hide();
  };

  const search = async () => {
    const query = input.value.trim();
    if (query.length < 2) {
      hide();
      return;
    }
    const id = ++requestId;
    let results = [];
    try {
      results = await invoke('city:search', { query });
    } catch {
      results = [];
    }
    if (id !== requestId || query !== input.value.trim()) return;
    hide();
    if (!results?.length) return;
    list = el('div', 'suggestions');
    for (const city of results) {
      const item = el('div', 'suggestion');
      item.appendChild(el('div', null, city.name));
      if (city.detail) item.appendChild(el('div', 'detail', city.detail));
      item.addEventListener('mousedown', (event) => {
        event.preventDefault();
        applyCity(city);
      });
      list.appendChild(item);
    }
    wrap.appendChild(list);
  };

  input.addEventListener('input', () => {
    values[field.key] = input.value;
    pickedCity = null;
    clearTimeout(citySearchTimer);
    citySearchTimer = setTimeout(() => {
      citySearchTimer = null;
      search();
    }, 260);
  });
  input.addEventListener('blur', () => setTimeout(hide, 180));

  return wrap;
}

/* ------------------------------------------------------------------ fields */

function optionList(field) {
  const options = [];
  if (field.options === 'adapters') {
    options.push({ value: '', label: 'Automatic (busiest adapter)' });
    for (const adapter of data.options?.adapters || []) options.push({ value: adapter.value, label: adapter.label });
  } else if (field.options === 'drives') {
    options.push({ value: '', label: 'All drives' });
    for (const drive of data.options?.drives || []) options.push({ value: drive.value, label: drive.label });
  }
  const current = values[field.key];
  if (current && !options.some((option) => option.value === current)) {
    options.push({ value: current, label: `${current} (not found)` });
  }
  return options;
}

/** Caps a slider maximum by something detected on this Mac (upstream MaxSource). */
function resolveMax(field) {
  let max = Number(field.max);
  if (field.maxSource === 'cpuThreads') {
    const threads = Number(data.options?.cpuThreads) || 0;
    if (threads > 0) max = Math.min(max, threads);
  }
  const step = field.step || 1;
  return Math.max(Number(field.min) + step, max);
}

function buildField(field, coordBoxes) {
  const raw = values[field.key];
  if (field.type === 'bool') {
    return row(
      field.label,
      field.description,
      toggle(Boolean(raw), (next) => {
        values[field.key] = next;
      }),
    );
  }

  if ((field.type === 'int' || field.type === 'double') && field.max != null && field.min != null) {
    const step = field.step || 1;
    // Some maxima depend on the machine (e.g. cores shown = thread count).
    const max = resolveMax(field);
    const numeric = Math.min(max, Math.max(field.min, Number(raw ?? field.min)));
    if (numeric !== raw) values[field.key] = numeric;
    const valueText = el('div', 'field-value', withUnit(numeric, field.unit));
    const control = slider({
      min: field.min,
      max,
      step,
      value: numeric,
      onChange: (next) => {
        values[field.key] = next;
        valueText.textContent = withUnit(next, field.unit);
      },
    });
    return stacked(field.label, field.description, control, { valueText });
  }

  if (field.type === 'int' || field.type === 'double') {
    const input = numberInput(raw ?? 0);
    input.addEventListener('input', () => {
      const numeric = Number(input.value);
      values[field.key] = Number.isFinite(numeric) ? numeric : (field.type === 'int' ? 0 : 0);
    });
    return row(field.label, field.description, input);
  }

  if (field.type === 'list') {
    const text = Array.isArray(raw) ? raw.join(', ') : String(raw ?? '');
    const input = textInput(text, { placeholder: 'UTC, Eastern Standard Time, …' });
    input.addEventListener('input', () => {
      values[field.key] = input.value.split(',').map((part) => part.trim()).filter(Boolean);
    });
    return stacked(field.label, field.description, input);
  }

  if (field.options === 'adapters' || field.options === 'drives') {
    return row(
      field.label,
      field.description,
      select(optionList(field), raw ?? '', (next) => {
        values[field.key] = next === '' ? null : next;
      }),
    );
  }

  if (field.lookup === 'city') {
    const fieldNode = cityField(field, raw ?? '', coordBoxes);
    return stacked(field.label, field.description, fieldNode);
  }

  const input = textInput(raw ?? '');
  input.addEventListener('input', () => {
    values[field.key] = input.value;
  });
  return stacked(field.label, field.description, input);
}

function collectFields(schema) {
  const fields = [];
  for (const group of schema) for (const field of group.fields) fields.push(field);
  return fields;
}

function buildSettingsCards() {
  pickedCity = null;

  const fields = collectFields(data.schema || []);
  const coordBoxes = {};

  for (const field of fields) {
    const group = (data.schema || []).find((entry) => entry.fields.includes(field));
    const { node } = ensureGroup(group?.name);

    if (field.key === 'latitude' || field.key === 'longitude') {
      const input = numberInput(values[field.key] ?? 0, { className: 'short right' });
      input.addEventListener('input', () => {
        const numeric = Number(input.value);
        if (Number.isFinite(numeric)) values[field.key] = numeric;
      });
      coordBoxes[field.key] = input;
      node.appendChild(row(field.label, field.description, input));
      continue;
    }

    node.appendChild(buildField(field, coordBoxes));
  }
}

/* The settings cards are created lazily per schema group so group order is
   preserved without re-sorting. */
const groups = new Map();
function ensureGroup(name) {
  const key = name || 'General';
  if (groups.has(key)) return groups.get(key);
  const created = card(key);
  groups.set(key, created);
  scroll.appendChild(created.node);
  return created;
}

function resetGroups() {
  groups.clear();
  scroll.replaceChildren();
}

/* -------------------------------------------------------------------- boot */

function render() {
  headerHost.replaceChildren(
    dialogHeader(data.title, data.description || 'Settings for this widget instance.'),
  );

  resetGroups();
  buildSettingsCards();

  if (data.appearance) {
    const windowCard = buildWindowCard(data.appearance);
    scroll.appendChild(windowCard);
  }

  message = el('div', 'dialog-error left');
  const reset = button('Reset to defaults');
  reset.classList.add('left');
  reset.addEventListener('click', () => {
    values = { ...data.defaults, ...appearanceSeed() };
    pickedCity = null;
    resetGroups();
    buildSettingsCards();
    if (data.appearance) scroll.appendChild(buildWindowCard(data.appearance));
    message.textContent = '';
  });

  const close = button('Close');
  close.addEventListener('click', () => closeDialog());

  const save = button('Save', { primary: true });
  save.addEventListener('click', onSave);

  footer.replaceChildren(reset, message, close, save);
}

function appearanceSeed() {
  if (!data?.appearance) return {};
  return {
    __opacity: data.appearance.opacity,
    __layer: data.appearance.layer,
    __theme: data.appearance.theme,
    __clickThrough: data.appearance.clickThrough,
  };
}

async function onSave() {
  if (saving) return;
  saving = true;
  const save = footer.querySelector('.btn.primary');
  if (save) save.disabled = true;
  message.textContent = '';

  const settings = { ...data.settings };
  for (const field of collectFields(data.schema || [])) {
    let value = values[field.key];
    if (field.type === 'int') {
      value = Math.round(Number(value));
      if (!Number.isFinite(value)) value = Number(data.defaults[field.key]) || 0;
    } else if (field.type === 'double') {
      value = Number(value);
      if (!Number.isFinite(value)) value = Number(data.defaults[field.key]) || 0;
    } else if (field.type === 'list') {
      if (typeof value === 'string') value = value.split(',').map((part) => part.trim()).filter(Boolean);
      if (!Array.isArray(value)) value = [];
    } else if (field.type === 'string') {
      value = value == null ? null : String(value);
      if (value === '' && ['formatOverride', 'adapterName', 'preferredDrive'].includes(field.key)) value = null;
    }
    settings[field.key] = value;
  }

  const payload = { instanceId, settings };
  if (pickedCity) payload.pickedCity = pickedCity;
  if (data.appearance) payload.appearance = appearanceValues();

  const result = await invoke('widget-settings:save', payload);
  saving = false;
  if (save) save.disabled = false;
  if (!result || result.__error || result.ok === false) {
    message.textContent = result?.error || result?.__error || 'Could not save these settings.';
    return;
  }
  closeDialog();
}

bootTheme()
  .then(() => invoke('widget-settings:data', { instanceId }))
  .then((loaded) => {
    if (!loaded || loaded.__error) throw new Error(loaded?.__error || 'This widget no longer exists.');
    data = loaded;
    values = { ...loaded.settings, ...appearanceSeed() };
    render();
  })
  .catch((error) => {
    headerHost.replaceChildren(dialogHeader('Widget settings', error.message));
    const close = button('Close');
    close.addEventListener('click', () => closeDialog());
    footer.replaceChildren(close);
  });
