'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { app } = require('electron');

const DEFAULT_SETTINGS = {
  installationInstance: '',
  automaticUpdates: true,
  pollIntervalMs: 1000,
  theme: 'dark',
  activeLayout: 'Default',
  sidebarWidth: 320,
  sidebarEdge: 'right',
  sidebarAutoHide: true,
  sidebarPinned: false,
  sidebarLayer: 'normal',
  startWithWindows: false,
  sensorDriverPromptShown: false,
  sensorDriverChoiceMade: false,
  sidebarScreenId: '',
  displayMode: 'widgets',
  cardFrameV2: false,
  sidebarBackground: true,
  sidebarShowTitle: true,
  sidebarCardOpacity: 1.0,
  loadGpu: true,
  loadDrives: true,
  loadNetwork: true,
  // macOS port additions (ignored by the Windows build if the file is shared)
  lastSidebarVisible: false,
};

class Store {
  constructor() {
    this.dataDir = path.join(app.getPath('appData'), 'DeskStats');
    this.layoutsDir = path.join(this.dataDir, 'layouts');
    this.logsDir = path.join(this.dataDir, 'logs');
    this.settingsPath = path.join(this.dataDir, 'settings.json');
    fs.mkdirSync(this.layoutsDir, { recursive: true });
    fs.mkdirSync(this.logsDir, { recursive: true });
    this.settings = this.loadSettings();
    this.ensureBackup();
  }

  loadSettings() {
    let parsed = null;
    try {
      if (fs.existsSync(this.settingsPath)) {
        parsed = JSON.parse(fs.readFileSync(this.settingsPath, 'utf8'));
      }
    } catch {
      try {
        const bak = `${this.settingsPath}.bak`;
        if (fs.existsSync(bak)) parsed = JSON.parse(fs.readFileSync(bak, 'utf8'));
      } catch {
        parsed = null;
      }
    }
    return { ...DEFAULT_SETTINGS, ...(parsed && typeof parsed === 'object' ? parsed : {}) };
  }

  saveSettings() {
    try {
      this.writeAtomic(this.settingsPath, JSON.stringify(this.settings, null, 2));
      if (!this.settings.installationInstance) {
        this.settings.installationInstance = crypto.randomUUID();
        this.writeAtomic(this.settingsPath, JSON.stringify(this.settings, null, 2));
      }
    } catch (err) {
      console.error('Could not save settings:', err);
    }
  }

  patchSettings(patch) {
    Object.assign(this.settings, patch);
    this.saveSettings();
    return this.settings;
  }

  /** Keep one copy of settings/layouts when the app version changes. */
  ensureBackup() {
    try {
      const stampPath = path.join(this.dataDir, 'version.txt');
      const current = app.getVersion();
      const previous = fs.existsSync(stampPath) ? fs.readFileSync(stampPath, 'utf8').trim() : '';
      if (previous && previous !== current) {
        const dir = path.join(this.dataDir, 'backups', previous.replace(/[^\w.-]/g, '_'));
        fs.mkdirSync(dir, { recursive: true });
        if (fs.existsSync(this.settingsPath)) fs.copyFileSync(this.settingsPath, path.join(dir, 'settings.json'));
        for (const file of fs.readdirSync(this.layoutsDir)) {
          if (file.endsWith('.json')) {
            fs.copyFileSync(path.join(this.layoutsDir, file), path.join(dir, `layout-${file}`));
          }
        }
      }
      this.writeAtomic(stampPath, current);
    } catch (err) {
      console.warn('Backup skipped:', err.message);
    }
  }

  writeAtomic(file, text) {
    const tmp = `${file}.tmp`;
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(tmp, text, 'utf8');
    try {
      if (fs.existsSync(file)) fs.copyFileSync(file, `${file}.bak`);
    } catch {
      /* best effort */
    }
    fs.renameSync(tmp, file);
  }

  log(level, message, error) {
    const line = `${new Date().toISOString()} [${level}] ${message}${error ? ` :: ${error.stack || error}` : ''}\n`;
    try {
      fs.appendFileSync(path.join(this.logsDir, 'deskstats.log'), line);
    } catch {
      /* logging is best effort */
    }
    if (level === 'ERROR') console.error(message, error || '');
    else console.log(message);
  }
}

module.exports = { Store, DEFAULT_SETTINGS };
