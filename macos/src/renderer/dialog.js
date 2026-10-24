/* Shared helpers for the native-framed HTML dialogs. */

export const invoke = (channel, payload) => window.deskstats.invoke(channel, payload);

export function applyVars(vars) {
  if (!vars) return;
  for (const [key, value] of Object.entries(vars)) {
    document.documentElement.style.setProperty(key, value);
  }
}

export function applyTheme(theme) {
  const light = String(theme || '').toLowerCase() === 'light';
  document.documentElement.style.setProperty('color-scheme', light ? 'light' : 'dark');
  document.body.classList.toggle('light-theme', light);
}

/** Applies the live app palette before the first paint. */
export async function bootTheme() {
  document.body.classList.add('dialog-body');
  try {
    const theme = await invoke('dialog:theme');
    if (theme) {
      applyVars(theme.vars);
      applyTheme(theme.theme);
    }
  } catch (error) {
    console.error('Could not load dialog theme:', error);
  }
}

export function closeDialog() {
  return invoke('dialog:close');
}

export function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}

export function dialogHeader(title, description) {
  const head = el('div', 'dialog-header');
  head.appendChild(el('div', 'dialog-title', title));
  if (description) head.appendChild(el('div', 'dialog-desc', description));
  return head;
}

export function scrollArea() {
  return el('div', 'dialog-scroll');
}

export function footer(actions) {
  const bar = el('div', 'dialog-footer');
  for (const node of actions) bar.appendChild(node);
  return bar;
}

export function button(label, { primary = false, danger = false, subtle = false } = {}) {
  const node = el('button', `btn${primary ? ' primary' : ''}${danger ? ' danger' : ''}${subtle ? ' subtle' : ''}`, label);
  node.type = 'button';
  return node;
}

export function card(title, { plainTitle = false } = {}) {
  const node = el('div', 'dialog-card');
  if (title) {
    const heading = el('h2', plainTitle ? 'plain-title' : null, title);
    node.appendChild(heading);
  }
  const body = el('div', 'card-body');
  node.appendChild(body);
  return { node, body };
}

export function help(text) {
  return el('div', 'field-help', text);
}

/** Label (+ optional help) on the left, control on the right. */
export function row(label, helpText, control) {
  const wrap = el('div', 'field');
  const left = el('div', 'field-left');
  left.appendChild(el('div', 'field-label', label));
  if (helpText) left.appendChild(help(helpText));
  wrap.appendChild(left);
  const right = el('div', 'field-controls');
  if (control) right.appendChild(control);
  wrap.appendChild(right);
  return wrap;
}

/** Label (+ help) above a full-width control (sliders, text fields). */
export function stacked(label, helpText, control, { valueText = null } = {}) {
  const wrap = el('div', 'field-col');
  const top = el('div', 'field-col-top');
  top.appendChild(el('div', 'field-label', label));
  if (valueText) top.appendChild(valueText);
  wrap.appendChild(top);
  if (control) wrap.appendChild(control);
  if (helpText) wrap.appendChild(help(helpText));
  return wrap;
}

export function toggle(checked, onChange) {
  const node = el('button', `toggle${checked ? ' on' : ''}`);
  node.type = 'button';
  node.setAttribute('role', 'switch');
  node.setAttribute('aria-checked', String(Boolean(checked)));
  node.addEventListener('click', () => {
    const next = !node.classList.contains('on');
    node.classList.toggle('on', next);
    node.setAttribute('aria-checked', String(next));
    onChange?.(next);
  });
  return node;
}

export function select(options, value, onChange) {
  const node = el('select');
  for (const option of options) {
    const item = el('option', null, option.label);
    item.value = option.value == null ? '' : String(option.value);
    node.appendChild(item);
  }
  node.value = value == null ? '' : String(value);
  if (node.value !== (value == null ? '' : String(value))) node.value = '';
  node.addEventListener('change', () => onChange?.(node.value));
  return node;
}

export function slider({ min, max, step = 1, value, onChange }) {
  const node = el('input', 'range');
  node.type = 'range';
  node.min = String(min);
  node.max = String(max);
  node.step = String(step);
  node.value = String(value);
  const paint = () => {
    const numeric = Number(node.value);
    const fill = max > min ? ((numeric - min) / (max - min)) * 100 : 0;
    node.style.setProperty('--fill', `${Math.max(0, Math.min(100, fill))}%`);
  };
  paint();
  node.addEventListener('input', () => {
    paint();
    onChange?.(Number(node.value));
  });
  return node;
}

export function textInput(value, { placeholder = '', className = '' } = {}) {
  const node = el('input', className);
  node.type = 'text';
  node.value = value == null ? '' : String(value);
  node.placeholder = placeholder;
  node.spellcheck = false;
  return node;
}

export function numberInput(value, { className = 'short right' } = {}) {
  const node = el('input', className);
  node.type = 'number';
  node.value = value == null ? '' : String(value);
  node.spellcheck = false;
  return node;
}

export function formatNumber(value) {
  if (!Number.isFinite(value)) return '0';
  return Math.abs(value % 1) < 0.0001 ? String(Math.round(value)) : String(Number(value.toFixed(2)));
}

export function withUnit(value, unit) {
  const text = formatNumber(value);
  return unit ? `${text} ${unit}` : text;
}
