'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { buildPreset, isPreset, PRESET_NAMES } = require('./widget-defs');

function clone(value) {
  return JSON.parse(JSON.stringify(value));
}

function sanitize(name) {
  const cleaned = String(name || 'layout').replace(/[\\/:*?"<>|\0]/g, '_').trim();
  return cleaned.length ? cleaned : 'layout';
}

class LayoutStore {
  constructor(store) {
    this.store = store;
    this.dir = store.layoutsDir;
    this.activeName = 'Default';
    this.active = buildPreset('Default');
    this.listeners = new Set();
  }

  ensureDefaults() {
    fs.mkdirSync(this.dir, { recursive: true });
    const activeFile = path.join(this.dir, 'active.txt');
    if (fs.existsSync(activeFile)) {
      try {
        const name = fs.readFileSync(activeFile, 'utf8').trim();
        if (name) this.activeName = name;
      } catch {
        /* ignore */
      }
    }
    this.active = this.load(this.activeName) || buildPreset(this.activeName) || buildPreset('Default');
    this.activeName = this.active.name;
  }

  fileFor(name) {
    return path.join(this.dir, `${sanitize(name)}.json`);
  }

  readFile(file) {
    if (!fs.existsSync(file)) return null;
    const candidates = [file, `${file}.bak`];
    for (const candidate of candidates) {
      try {
        if (!fs.existsSync(candidate)) continue;
        const data = JSON.parse(fs.readFileSync(candidate, 'utf8'));
        if (data && typeof data === 'object' && data.name) {
          data.hasSavedState = true;
          if (!Array.isArray(data.items)) data.items = [];
          return data;
        }
      } catch {
        /* try the backup */
      }
    }
    return null;
  }

  load(name) {
    return this.readFile(this.fileFor(name));
  }

  get(name) {
    return clone(this.load(name) || buildPreset(name));
  }

  get names() {
    const names = new Set(PRESET_NAMES);
    try {
      for (const file of fs.readdirSync(this.dir)) {
        if (!file.endsWith('.json')) continue;
        try {
          const data = JSON.parse(fs.readFileSync(path.join(this.dir, file), 'utf8'));
          if (data?.name) names.add(data.name);
        } catch {
          /* ignore damaged file — the backup is read when activated */
        }
      }
    } catch {
      /* no layouts yet */
    }
    return [...names].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));
  }

  activate(name) {
    const layout = this.load(name) || buildPreset(name);
    if (!layout) return null;
    this.active = clone(layout);
    this.activeName = name;
    this.writeActiveName(name);
    this.emit();
    return clone(this.active);
  }

  saveActive(layout) {
    if (!layout?.name) layout.name = this.activeName;
    layout.hasSavedState = true;
    this.active = clone(layout);
    this.activeName = layout.name;
    this.writeFile(layout);
    this.writeActiveName(layout.name);
    this.emit();
  }

  saveAs(layout, name) {
    if (!name || !String(name).trim() || isPreset(name)) return false;
    layout.name = String(name).trim();
    layout.id = layout.name.toLowerCase();
    this.writeFile(layout);
    return true;
  }

  resetToPreset(name) {
    if (!isPreset(name)) return null;
    const fresh = buildPreset(name);
    this.active = clone(fresh);
    this.activeName = fresh.name;
    this.writeFile(fresh);
    this.writeActiveName(fresh.name);
    this.emit();
    return clone(fresh);
  }

  writeFile(layout) {
    fs.mkdirSync(this.dir, { recursive: true });
    this.store.writeAtomic(this.fileFor(layout.name), JSON.stringify(layout, null, 2));
  }

  writeActiveName(name) {
    try {
      this.store.writeAtomic(path.join(this.dir, 'active.txt'), String(name));
    } catch (err) {
      this.store.log('WARN', `Could not write active layout name: ${err.message}`);
    }
  }

  emit() {
    for (const listener of this.listeners) {
      try {
        listener();
      } catch {
        /* ignore */
      }
    }
  }

  onChanged(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }
}

module.exports = { LayoutStore, clone };
