'use strict';

const path = require('node:path');
const { BrowserWindow, app, screen } = require('electron');
const { appearanceFor } = require('./appearance');

const PRELOAD = path.join(__dirname, '../preload/preload.js');
const RENDERER = path.join(__dirname, '../renderer');

function workAreaCenter(width, height) {
  const display = screen.getPrimaryDisplay();
  const work = display.workArea;
  return {
    x: Math.round(work.x + (work.width - width) / 2),
    y: Math.round(work.y + (work.height - height) / 2),
  };
}

/**
 * Native-framed utility dialogs. Keeping the native title bar means macOS
 * provides moving/closing/keyboard behaviour for free; the page itself is the
 * same themed HTML/CSS as the widgets.
 */
class Dialogs {
  constructor(controller) {
    this.controller = controller;
    this.widgetWindows = new Map(); // instanceId -> BrowserWindow
    this.appSettingsWindow = null;
    this.nameWindow = null;
    this.aboutWindow = null;
    this.identifyWindows = [];
  }

  themeBackground(theme) {
    return String(theme).toLowerCase() === 'light' ? '#eef2f8' : '#141418';
  }

  create(file, options = {}) {
    const theme = this.controller.theme;
    const work = screen.getPrimaryDisplay().workArea;
    const width = Math.min(options.width || 480, Math.max(360, work.width - 40));
    const height = Math.min(options.height || 720, Math.max(320, work.height - 40));
    const pos = options.center === false ? {} : workAreaCenter(width, height);
    const win = new BrowserWindow({
      ...pos,
      width,
      height,
      minWidth: options.minWidth || 360,
      minHeight: options.minHeight || 220,
      title: options.title || 'DeskStats',
      frame: true,
      transparent: false,
      backgroundColor: this.themeBackground(theme),
      resizable: Boolean(options.resizable),
      minimizable: false,
      maximizable: false,
      fullscreenable: false,
      show: false,
      skipTaskbar: false,
      webPreferences: {
        preload: PRELOAD,
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: false,
        spellcheck: false,
      },
    });
    win.setAlwaysOnTop(true, 'floating');
    win.loadFile(path.join(RENDERER, file), { query: options.query || {} });
    const reveal = () => {
      if (process.env.DESKSTATS_TEST_HIDDEN) {
        // Automated UI checks: paint the page but never steal the pointer.
        win.setIgnoreMouseEvents(true);
        win.showInactive();
        return;
      }
      win.show();
      win.focus();
      app.focus({ steal: true });
    };
    win.once('ready-to-show', () => {
      if (!options.autoHeight) {
        reveal();
        return;
      }
      // Upstream AboutWindow uses SizeToContent=Height: fit the window to the
      // page instead of clipping the licence/credits block.
      win.webContents
        .executeJavaScript('Math.ceil(document.body.scrollHeight)', true)
        .then((contentHeight) => {
          if (win.isDestroyed()) return;
          const [currentWidth] = win.getContentSize();
          const target = Math.max(options.minHeight || 220, Math.min(work.height - 60, contentHeight));
          win.setContentSize(currentWidth, target);
          win.center();
        })
        .catch(() => {})
        .finally(reveal);
    });
    return win;
  }

  widgetSettings(instanceId) {
    const existing = this.widgetWindows.get(instanceId);
    if (existing && !existing.isDestroyed()) {
      existing.show();
      existing.focus();
      return existing;
    }
    const item = this.controller.itemFor(instanceId);
    if (!item) return null;
    const win = this.create('settings.html', {
      title: `${this.controller.titleFor(item)} — Settings`,
      width: 480,
      height: 740,
      resizable: true,
      minWidth: 420,
      minHeight: 480,
      query: { instance: instanceId },
    });
    this.widgetWindows.set(instanceId, win);
    let saved = true;
    const original = this.controller.appearanceStateFor(instanceId);
    this.controller.beginAppearancePreview(instanceId);
    win.on('close', () => {
      if (!saved) this.controller.applyAppearanceState(instanceId, original, { persist: true });
      this.controller.endAppearancePreview(instanceId);
      this.widgetWindows.delete(instanceId);
    });
    win.__deskstatsSavedHook = () => {
      saved = true;
    };
    win.__deskstatsPreviewHook = () => {
      saved = false;
    };
    return win;
  }

  /** Called from renderer save to mark the preview as committed. */
  markWidgetSettingsSaved(webContents) {
    const win = BrowserWindow.fromWebContents(webContents);
    win?.__deskstatsSavedHook?.();
  }

  markWidgetSettingsPreview(webContents) {
    const win = BrowserWindow.fromWebContents(webContents);
    win?.__deskstatsPreviewHook?.();
  }

  appSettings() {
    if (this.appSettingsWindow && !this.appSettingsWindow.isDestroyed()) {
      this.appSettingsWindow.show();
      this.appSettingsWindow.focus();
      return this.appSettingsWindow;
    }
    const win = this.create('app-settings.html', {
      title: 'DeskStats — App settings',
      width: 500,
      height: 800,
      resizable: true,
      minWidth: 460,
      minHeight: 520,
    });
    this.appSettingsWindow = win;
    win.on('closed', () => {
      this.appSettingsWindow = null;
    });
    return win;
  }

  saveLayoutAs() {
    if (this.nameWindow && !this.nameWindow.isDestroyed()) {
      this.nameWindow.show();
      this.nameWindow.focus();
      return this.nameWindow;
    }
    const win = this.create('name.html', {
      title: 'Save layout as…',
      width: 400,
      height: 210,
      resizable: false,
      minHeight: 180,
    });
    this.nameWindow = win;
    win.on('closed', () => {
      this.nameWindow = null;
    });
    return win;
  }

  about() {
    if (this.aboutWindow && !this.aboutWindow.isDestroyed()) {
      this.aboutWindow.show();
      this.aboutWindow.focus();
      return this.aboutWindow;
    }
    const win = this.create('about.html', {
      title: 'About DeskStats',
      width: 420,
      height: 380,
      resizable: false,
      minHeight: 320,
      autoHeight: true,
    });
    this.aboutWindow = win;
    win.on('closed', () => {
      this.aboutWindow = null;
    });
    return win;
  }

  /** Shows a large label on each requested screen for 2.5 seconds. */
  identify(labels) {
    this.closeIdentify();
    const theme = this.controller.theme;
    const list = Array.isArray(labels) && labels.length
      ? labels
      : this.controller.monitorLabels();
    for (const entry of list) {
      const bounds = entry.bounds;
      if (!bounds) continue;
      const win = new BrowserWindow({
        x: bounds.x,
        y: bounds.y,
        width: bounds.width,
        height: bounds.height,
        frame: false,
        transparent: true,
        backgroundColor: '#00000000',
        hasShadow: false,
        resizable: false,
        movable: false,
        focusable: false,
        skipTaskbar: true,
        alwaysOnTop: true,
        show: false,
        webPreferences: {
          preload: PRELOAD,
          contextIsolation: true,
          nodeIntegration: false,
          sandbox: false,
        },
      });
      win.setAlwaysOnTop(true, 'screen-saver');
      win.setIgnoreMouseEvents(true);
      win.loadFile(path.join(RENDERER, 'identify.html'), {
        query: { label: entry.label, theme },
      });
      win.once('ready-to-show', () => win.showInactive());
      this.identifyWindows.push(win);
    }
    setTimeout(() => this.closeIdentify(), 2600);
  }

  closeIdentify() {
    for (const win of this.identifyWindows) {
      if (!win.isDestroyed()) win.destroy();
    }
    this.identifyWindows = [];
  }

  closeForWebContents(webContents, { previewRevert = true } = {}) {
    const win = BrowserWindow.fromWebContents(webContents);
    if (!win || win.isDestroyed()) return;
    if (previewRevert) win.__deskstatsPreviewHook?.();
    win.close();
  }

  closeAll() {
    this.closeIdentify();
    for (const win of this.widgetWindows.values()) {
      if (!win.isDestroyed()) win.destroy();
    }
    this.widgetWindows.clear();
    for (const win of [this.appSettingsWindow, this.nameWindow, this.aboutWindow]) {
      if (win && !win.isDestroyed()) win.destroy();
    }
    this.appSettingsWindow = null;
    this.nameWindow = null;
    this.aboutWindow = null;
  }
}

module.exports = { Dialogs };
