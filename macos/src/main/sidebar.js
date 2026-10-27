'use strict';

const path = require('node:path');
const { BrowserWindow, screen } = require('electron');
const { appearanceFor } = require('./appearance');
const screens = require('./screens');

const PRELOAD = path.join(__dirname, '../preload/preload.js');
const RENDERER = path.join(__dirname, '../renderer');
const COLLAPSED_WIDTH = 8;

/**
 * Edge-docked sidebar shell. One BrowserWindow hosts every docked widget as a
 * stack of cards. Auto-hide collapses the window to an 8 px strip and makes it
 * click-through; a cursor poll re-expands it from a ±20 px hot zone.
 */
class Sidebar {
  constructor(controller) {
    this.controller = controller;
    this.win = null;
    this.expandedWidth = 320;
    this.currentWidth = 320;
    this.expanded = true;
    this.pinned = false;
    this.lastInside = Date.now();
    this.timer = null;
    this.anim = null;
    this.visible = false;
    this.ready = false;
    this.ignoreMouse = false;
  }

  get settings() {
    return this.controller.store.settings;
  }

  get isVisible() {
    return Boolean(this.win && !this.win.isDestroyed() && this.win.isVisible());
  }

  get hasDocked() {
    return this.controller.dockedItems().length > 0;
  }

  create() {
    if (this.win && !this.win.isDestroyed()) return this.win;
    const s = this.settings;
    const work = screens.workAreaOfId(s.sidebarScreenId);
    const width = this.computeExpandedWidth();
    this.expandedWidth = width;
    this.currentWidth = width;

    const win = new BrowserWindow({
      x: this.edgeX(work, width),
      y: work.y,
      width,
      height: work.height,
      frame: false,
      transparent: true,
      backgroundColor: '#00000000',
      hasShadow: true,
      resizable: false,
      movable: false,
      minimizable: false,
      maximizable: false,
      fullscreenable: false,
      skipTaskbar: true,
      show: false,
      title: 'DeskStats',
      visualEffectState: 'active',
      webPreferences: {
        preload: PRELOAD,
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: false,
        backgroundThrottling: false,
        spellcheck: false,
      },
    });
    this.win = win;
    this.applyVibrancy();
    this.applyLayer();
    win.setAlwaysOnTop(s.sidebarLayer === 'top', 'floating');

    win.on('ready-to-show', () => {
      this.ready = true;
      if (this.hasDocked) this.show();
    });

    win.on('closed', () => {
      this.win = null;
      this.ready = false;
      this.visible = false;
    });

    win.on('enter-full-screen', () => this.position());
    win.on('leave-full-screen', () => this.position());

    win.loadFile(path.join(RENDERER, 'sidebar.html'));
    this.startPolling();
    return win;
  }

  computeExpandedWidth() {
    const anyLarge = this.controller.dockedItems().some((item) => item.placement?.largeSize);
    const base = Math.max(240, Math.min(560, Number(this.settings.sidebarWidth) || 320));
    return Math.round(base * (anyLarge ? 1.35 : 1));
  }

  edgeX(work, width) {
    const right = !String(this.settings.sidebarEdge || 'right').toLowerCase().startsWith('left');
    return Math.round(right ? work.x + work.width - width : work.x);
  }

  workArea() {
    return screens.workAreaOfId(this.settings.sidebarScreenId);
  }

  position() {
    if (!this.win || this.win.isDestroyed()) return;
    const work = this.workArea();
    const width = Math.max(COLLAPSED_WIDTH, Math.round(this.currentWidth));
    const b = this.win.getBounds();
    const x = this.edgeX(work, width);
    if (b.x === x && b.y === work.y && b.width === width && b.height === work.height) return;
    this.win.setBounds({ x, y: Math.round(work.y), width, height: Math.round(work.height) }, false);
  }

  applyLayer() {
    if (!this.win || this.win.isDestroyed()) return;
    const layer = this.settings.sidebarLayer || 'normal';
    this.win.setAlwaysOnTop(layer === 'top', 'floating');
  }

  applyVibrancy() {
    if (!this.win || this.win.isDestroyed()) return;
    try {
      this.win.setVibrancy(this.settings.sidebarBackground ? 'under-window' : null);
      this.win.setBackgroundColor(this.settings.sidebarBackground ? '#00000000' : '#00000000');
    } catch {
      /* vibrancy is best effort */
    }
  }

  /** Re-reads width/edge/screen/pin/auto-hide/title/background and applies live. */
  applySettings() {
    if (!this.win || this.win.isDestroyed()) return;
    this.pinned = Boolean(this.settings.sidebarPinned);
    this.expandedWidth = this.computeExpandedWidth();
    this.applyLayer();
    this.applyVibrancy();
    this.position();
    if (this.pinned || !this.settings.sidebarAutoHide) {
      this.expanded = true;
      this.currentWidth = this.expandedWidth;
      this.setMouseIgnore(false);
      this.position();
    } else if (!this.expanded) {
      this.setExpanded(false, true);
    } else {
      this.animateWidth(this.expandedWidth);
    }
    this.startPolling();
    this.pushState();
  }

  startPolling() {
    clearInterval(this.timer);
    this.timer = setInterval(() => this.poll(), 150);
  }

  poll() {
    if (!this.win || this.win.isDestroyed() || !this.isVisible) return;
    this.pinned = Boolean(this.settings.sidebarPinned);
    if (this.pinned || !this.settings.sidebarAutoHide) {
      if (!this.expanded) this.setExpanded(true);
      return;
    }

    const work = this.workArea();
    const cursor = screen.getCursorScreenPoint();
    const probeWidth = this.expanded ? this.currentWidth : COLLAPSED_WIDTH;
    const right = !String(this.settings.sidebarEdge || 'right').toLowerCase().startsWith('left');
    const leftEdge = right ? work.x + work.width - probeWidth - 20 : work.x - 20;
    const inside =
      cursor.x >= leftEdge &&
      cursor.x <= leftEdge + probeWidth + 40 &&
      cursor.y >= work.y &&
      cursor.y < work.y + work.height;

    if (inside) {
      this.lastInside = Date.now();
      if (!this.expanded) this.setExpanded(true);
      return;
    }

    if (this.expanded && Date.now() - this.lastInside > 600) {
      this.setExpanded(false);
    }
  }

  setExpanded(expand, immediate = false) {
    if (!this.win || this.win.isDestroyed()) return;
    const target = expand ? this.expandedWidth : COLLAPSED_WIDTH;
    if (this.expanded === expand && Math.abs(this.currentWidth - target) < 1) return;
    this.expanded = expand;
    if (immediate) {
      clearInterval(this.anim);
      this.currentWidth = target;
      this.position();
    } else {
      this.animateWidth(target);
    }
    this.setMouseIgnore(!expand && !this.pinned);
    this.pushState();
  }

  animateWidth(target) {
    if (!this.win || this.win.isDestroyed()) return;
    clearInterval(this.anim);
    const from = this.currentWidth;
    const started = Date.now();
    const duration = 180;
    if (Math.abs(from - target) < 1) {
      this.currentWidth = target;
      this.position();
      return;
    }
    this.anim = setInterval(() => {
      if (!this.win || this.win.isDestroyed()) {
        clearInterval(this.anim);
        return;
      }
      const t = Math.min(1, (Date.now() - started) / duration);
      const eased = t < 0.5 ? 2 * t * t : 1 - (-2 * t + 2) ** 2 / 2;
      this.currentWidth = from + (target - from) * eased;
      this.position();
      if (t >= 1) {
        clearInterval(this.anim);
        this.anim = null;
        this.currentWidth = target;
        this.position();
      }
    }, 10);
  }

  setMouseIgnore(ignore) {
    if (!this.win || this.win.isDestroyed()) return;
    if (this.ignoreMouse === ignore) return;
    this.ignoreMouse = ignore;
    this.win.setIgnoreMouseEvents(ignore, { forward: true });
  }

  show() {
    if (!this.hasDocked) return;
    if (!this.win || this.win.isDestroyed()) this.create();
    this.expandedWidth = this.computeExpandedWidth();
    this.currentWidth = this.expandedWidth;
    this.expanded = true;
    this.setMouseIgnore(false);
    this.position();
    this.win.showInactive();
    this.visible = true;
    this.lastInside = Date.now();
    this.pushState();
    // Paint current readings immediately instead of waiting for the next tick.
    if (this.controller.snapshot) this.pushMetrics(this.controller.snapshot, null);
    this.startPolling();
  }

  hide() {
    if (!this.win || this.win.isDestroyed()) return;
    this.win.hide();
    this.visible = false;
    this.setMouseIgnore(false);
  }

  toggle() {
    if (this.isVisible) this.hide();
    else this.show();
  }

  /** Full layout/settings/theme snapshot for the renderer. */
  state() {
    const c = this.controller;
    const items = c.dockedItems();
    const theme = c.theme;
    const themeVars = appearanceFor(theme, { opacity: 1, theme: null }, this.settings);
    return {
      items: items.map((item) => ({
        instanceId: item.instanceId,
        widgetId: item.widgetId,
        title: c.titleFor(item),
        settings: c.settingsFor(item),
        appearance: c.appearanceFor(item, { scale: 1 }),
        large: Boolean(item.placement?.largeSize),
        snapshot: c.snapshot,
        aux: c.auxFor(item),
      })),
      settings: {
        sidebarShowTitle: this.settings.sidebarShowTitle,
        sidebarBackground: this.settings.sidebarBackground,
        sidebarPinned: this.pinned,
        sidebarAutoHide: this.settings.sidebarAutoHide,
        sidebarEdge: this.settings.sidebarEdge,
        sidebarCardOpacity: this.settings.sidebarCardOpacity,
        cardFrameV2: this.settings.cardFrameV2,
      },
      theme,
      themeVars: themeVars.vars,
      expanded: this.expanded,
    };
  }

  pushState() {
    if (!this.win || this.win.isDestroyed() || !this.ready) return;
    if (this.win.webContents.isDestroyed()) return;
    this.win.webContents.send('deskstats:event', { name: 'sidebar-state', data: this.state() });
  }

  pushMetrics(snapshot, aux) {
    if (!this.win || this.win.isDestroyed() || !this.ready) return;
    if (!this.isVisible) return;
    this.win.webContents.send('deskstats:event', {
      name: 'sidebar-metrics',
      data: { snapshot, aux },
    });
  }

  /** Refreshes sidebar visibility + width from the current layout. */
  refresh() {
    this.expandedWidth = this.computeExpandedWidth();
    if (this.hasDocked) {
      if (!this.isVisible) {
        this.show();
      } else {
        // Large widgets widen the stack, so an expanded sidebar must follow the
        // new target width. A collapsed one stays collapsed until hovered.
        if (this.expanded) this.animateWidth(this.expandedWidth);
        else this.position();
        this.pushState();
      }
    } else if (this.isVisible) {
      this.hide();
    }
  }

  destroy() {
    clearInterval(this.timer);
    clearInterval(this.anim);
    this.timer = null;
    this.anim = null;
    if (this.win && !this.win.isDestroyed()) this.win.destroy();
    this.win = null;
    this.ready = false;
  }
}

module.exports = { Sidebar, COLLAPSED_WIDTH };
