'use strict';

const path = require('node:path');
const { BrowserWindow, Menu, screen } = require('electron');

const PRELOAD = path.join(__dirname, '../preload/preload.js');
const RENDERER = path.join(__dirname, '../renderer');

const SNAP_STEP = 6;

/**
 * Owns one transparent, frameless BrowserWindow per floating widget.
 *
 * Dragging and width-resizing are driven from the renderer (pointer capture)
 * while the main process polls the real cursor, so a widget keeps following the
 * pointer even outside its own bounds — something window-relative mouse events
 * cannot do. The 6 px top strip also has `-webkit-app-region: drag` as a native
 * fallback and its position is persisted through the `moved` event.
 */
class FloatingWindows {
  constructor(controller) {
    this.controller = controller;
    this.windows = new Map(); // instanceId -> { win, item, rescue, ... }
    this.drag = null;
    this.resize = null;
    this.timer = null;
    this.clickTimer = null;
    this.hoverSince = new Map();
    this.rescueVisibleFor = null;
  }

  start() {
    this.clickTimer = setInterval(() => this.watchClickThrough(), 150);
  }

  stop() {
    clearInterval(this.clickTimer);
    this.clickTimer = null;
    clearInterval(this.timer);
    for (const id of [...this.windows.keys()]) this.destroy(id);
  }

  get(instanceId) {
    return this.windows.get(instanceId) || null;
  }

  /** Creates/loads the window for a layout item (or returns the existing one). */
  create(item) {
    const existing = this.windows.get(item.instanceId);
    if (existing) {
      this.applyItem(item);
      return existing.win;
    }

    const c = this.controller;
    const settings = c.settingsFor(item);
    const placement = item.placement || {};
    const width = Math.round(Math.max(260, Math.min(1200, placement.width || 320)));
    const height = Math.round(Math.max(120, Math.min(2000, placement.height || 200)));
    const pos = this.initialPosition(item, width, height);

    const win = new BrowserWindow({
      x: pos.x,
      y: pos.y,
      width,
      height,
      frame: false,
      transparent: true,
      backgroundColor: '#00000000',
      hasShadow: false,
      resizable: false,
      movable: true,
      minimizable: false,
      maximizable: false,
      fullscreenable: false,
      skipTaskbar: true,
      acceptFirstMouse: true,
      show: false,
      title: item.titleOverride || c.titleFor(item),
      webPreferences: {
        preload: PRELOAD,
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: false,
        backgroundThrottling: false,
        spellcheck: false,
      },
    });

    const entry = {
      win,
      item,
      rescue: null,
      restoring: true,
      movedTimer: null,
      suppressMoved: false,
      destroyed: false,
      lastWidth: width,
      lastHeight: height,
      large: Boolean(placement.largeSize),
      zoom: placement.largeSize ? 1.35 : 1,
    };
    this.windows.set(item.instanceId, entry);

    win.webContents.setZoomFactor(entry.zoom);
    win.setAlwaysOnTop((placement.layer || (placement.alwaysOnTop ? 'top' : 'normal')) === 'top', 'floating');
    if (placement.clickThrough) win.setIgnoreMouseEvents(true, { forward: true });

    win.once('ready-to-show', () => {
      if (entry.destroyed) return;
      win.showInactive();
      entry.restoring = false;
      c.log('INFO', `Widget window '${c.titleFor(item)}' shown`);
    });

    win.on('moved', () => {
      if (entry.destroyed || entry.restoring || this.drag?.instanceId === item.instanceId || this.resize?.instanceId === item.instanceId) return;
      clearTimeout(entry.movedTimer);
      entry.movedTimer = setTimeout(() => this.persistNativeMove(item.instanceId), 260);
    });

    win.on('closed', () => {
      entry.destroyed = true;
      clearTimeout(entry.movedTimer);
      this.destroyRescue(item.instanceId);
      this.windows.delete(item.instanceId);
      if (!c.shuttingDown) c.onWidgetWindowClosed(item.instanceId);
    });

    win.loadFile(path.join(RENDERER, 'widget.html'), {
      query: { instance: item.instanceId },
    });

    this.applyItem(item);
    this.startClickWatchIfNeeded();
    return win;
  }

  initialPosition(item, width, height) {
    const c = this.controller;
    if (item.placement?.monitorDeviceId) {
      return c.screenPositionFor(item, width, height);
    }
    const work = c.workAreaForItem(item);
    const x = Number.isFinite(Number(item.placement?.x)) ? Number(item.placement.x) : work.x + work.width - width - 16;
    const y = Number.isFinite(Number(item.placement?.y)) ? Number(item.placement.y) : work.y + 16;
    return { x: Math.round(x), y: Math.round(y) };
  }

  applyItem(item) {
    const entry = this.windows.get(item.instanceId);
    if (!entry || entry.destroyed) return;
    entry.item = item;
    const placement = item.placement || {};
    const win = entry.win;
    const large = Boolean(placement.largeSize);
    const zoom = large ? 1.35 : 1;
    if (zoom !== entry.zoom) {
      entry.zoom = zoom;
      win.webContents.setZoomFactor(zoom);
    }
    if (entry.large !== large) entry.large = large;
    const layer = placement.layer || (placement.alwaysOnTop ? 'top' : 'normal');
    win.setAlwaysOnTop(layer === 'top', 'floating');
    const clickThrough = Boolean(placement.clickThrough);
    if (win.isDestroyed() === false) {
      // Only call when the state actually changes; re-applying ignoreMouseEvents
      // while the rescue pill is visible can make the pill flicker.
      if (win.__deskstatsClickThrough !== clickThrough) {
        win.__deskstatsClickThrough = clickThrough;
        win.setIgnoreMouseEvents(clickThrough, { forward: true });
        if (!clickThrough) this.destroyRescue(item.instanceId);
      }
    }
    if (win.getTitle() !== (item.titleOverride || this.controller.titleFor(item))) {
      win.setTitle(item.titleOverride || this.controller.titleFor(item));
    }
    this.startClickWatchIfNeeded();
  }

  /** Pushes one complete renderer state (settings, theme, metrics, aux). */
  pushState(item) {
    const entry = this.windows.get(item.instanceId);
    if (!entry || entry.destroyed) return;
    if (entry.win.webContents.isDestroyed()) return;
    const c = this.controller;
    entry.win.webContents.send('deskstats:event', {
      name: 'state',
      data: {
        instanceId: item.instanceId,
        widgetId: item.widgetId,
        title: item.titleOverride || c.titleFor(item),
        settings: c.settingsFor(item),
        appearance: c.appearanceFor(item, { scale: 1 }),
        snapshot: c.snapshot,
        aux: c.auxFor(item),
        clickThrough: Boolean(item.placement?.clickThrough),
        large: Boolean(item.placement?.largeSize),
      },
    });
  }

  pushEvent(instanceId, name, data) {
    const entry = this.windows.get(instanceId);
    if (!entry || entry.destroyed || entry.win.webContents.isDestroyed()) return;
    entry.win.webContents.send('deskstats:event', { name, data });
  }

  broadcast(name, data) {
    for (const entry of this.windows.values()) {
      if (entry.destroyed || entry.win.webContents.isDestroyed()) continue;
      entry.win.webContents.send('deskstats:event', { name, data });
    }
  }

  /** Renderer measurement → window height (CSS px × page zoom factor). */
  setMeasuredHeight(instanceId, cssHeight) {
    const entry = this.windows.get(instanceId);
    if (!entry || entry.destroyed || this.resize?.instanceId === instanceId) return;
    const work = this.controller.workAreaForItem(entry.item);
    const zoom = entry.win.webContents.getZoomFactor() || 1;
    const max = Math.max(120, work.height - 8);
    const height = Math.max(120, Math.min(max, Math.round(Number(cssHeight) * zoom)));
    const bounds = entry.win.getBounds();
    if (Math.abs(bounds.height - height) < 1) return;
    entry.suppressMoved = true;
    entry.win.setBounds({ x: bounds.x, y: bounds.y, width: bounds.width, height }, false);
    entry.suppressMoved = false;
    entry.lastHeight = height;
    this.controller.setDetachedSize(instanceId, bounds.width, height);
  }

  setWidth(instanceId, width, height) {
    const entry = this.windows.get(instanceId);
    if (!entry || entry.destroyed) return;
    const b = entry.win.getBounds();
    entry.win.setBounds({ x: b.x, y: b.y, width: Math.round(width), height: Math.round(height ?? b.height) }, false);
  }

  // ---------------------------------------------------------------- dragging

  startDrag(instanceId) {
    const entry = this.windows.get(instanceId);
    if (!entry || entry.destroyed || entry.item.placement?.clickThrough) return;
    const cursor = screen.getCursorScreenPoint();
    const b = entry.win.getBounds();
    clearInterval(this.timer);
    this.drag = {
      instanceId,
      offsetX: cursor.x - b.x,
      offsetY: cursor.y - b.y,
      startCursor: cursor,
      startBounds: b,
      moved: false,
    };
    this.controller.beginManualPlacement(instanceId);
    this.timer = setInterval(() => {
      if (!this.drag) return;
      const p = screen.getCursorScreenPoint();
      const x = Math.round(p.x - this.drag.offsetX);
      const y = Math.round(p.y - this.drag.offsetY);
      if (Math.abs(p.x - this.drag.startCursor.x) > 2 || Math.abs(p.y - this.drag.startCursor.y) > 2) this.drag.moved = true;
      entry.win.setPosition(x, y, false);
    }, 8);
    entry.win.focus();
    // If the pointer is released while the cursor is outside the moving window
    // and no pointerup reaches the renderer, losing focus ends the drag safely.
    entry.dragBlur = () => setTimeout(() => this.endDrag(instanceId), 0);
    entry.win.once('blur', entry.dragBlur);
  }

  endDrag(instanceId) {
    if (!this.drag || this.drag.instanceId !== instanceId) return;
    clearInterval(this.timer);
    this.timer = null;
    const entry = this.windows.get(instanceId);
    if (entry?.dragBlur) {
      entry.win.removeListener('blur', entry.dragBlur);
      delete entry.dragBlur;
    }
    const drag = this.drag;
    this.drag = null;
    if (!entry || entry.destroyed) return;

    const bounds = entry.win.getBounds();
    let { x, y } = bounds;
    if (this.controller.isGridSnapEnabled(instanceId)) {
      x = Math.round(x / SNAP_STEP) * SNAP_STEP;
      y = Math.round(y / SNAP_STEP) * SNAP_STEP;
      entry.win.setPosition(x, y, false);
    }
    const final = entry.win.getBounds();
    this.controller.placeDetached(instanceId, final.x, final.y, final.width, final.height, { userMove: drag.moved });
  }

  persistNativeMove(instanceId) {
    const entry = this.windows.get(instanceId);
    if (!entry || entry.destroyed) return;
    if (this.drag?.instanceId === instanceId || this.resize?.instanceId === instanceId) return;
    const bounds = entry.win.getBounds();
    let { x, y } = bounds;
    if (this.controller.isGridSnapEnabled(instanceId)) {
      const sx = Math.round(x / SNAP_STEP) * SNAP_STEP;
      const sy = Math.round(y / SNAP_STEP) * SNAP_STEP;
      if (sx !== x || sy !== y) {
        x = sx;
        y = sy;
        entry.win.setPosition(x, y, false);
      }
    }
    this.controller.placeDetached(instanceId, x, y, bounds.width, bounds.height, { userMove: true });
  }

  // ---------------------------------------------------------------- resizing

  startResize(instanceId, options = {}) {
    const entry = this.windows.get(instanceId);
    if (!entry || entry.destroyed) return;
    const side = options.side === 'left' ? 'left' : 'right';
    const cursor = screen.getCursorScreenPoint();
    const b = entry.win.getBounds();
    clearInterval(this.timer);
    this.resize = {
      instanceId,
      side,
      startCursor: cursor,
      startBounds: b,
      min: Math.max(220, Math.round(Number(options.min) || 304)),
      max: Math.max(320, Math.round(Number(options.max) || 520)),
    };
    this.controller.beginManualPlacement(instanceId);
    this.timer = setInterval(() => {
      if (!this.resize) return;
      const p = screen.getCursorScreenPoint();
      const dx = p.x - this.resize.startCursor.x;
      const { startBounds, min, max } = this.resize;
      let width = this.resize.side === 'right' ? startBounds.width + dx : startBounds.width - dx;
      width = Math.round(Math.max(min, Math.min(max, width)));
      let x = startBounds.x;
      if (this.resize.side === 'left') x = startBounds.x + (startBounds.width - width);
      entry.win.setBounds({ x, y: startBounds.y, width, height: entry.win.getBounds().height }, false);
    }, 8);
    entry.win.focus();
  }

  endResize(instanceId) {
    if (!this.resize || this.resize.instanceId !== instanceId) return;
    clearInterval(this.timer);
    this.timer = null;
    this.resize = null;
    const entry = this.windows.get(instanceId);
    if (!entry || entry.destroyed) return;
    const b = entry.win.getBounds();
    this.controller.placeDetached(instanceId, b.x, b.y, b.width, b.height, { userMove: true });
  }

  setResizeMode(instanceId, on) {
    this.pushEvent(instanceId, 'resize-mode', { on: Boolean(on) });
  }

  // ------------------------------------------------------- click-through rescue

  startClickWatchIfNeeded() {
    // A single global timer always runs; watchClickThrough() is a cheap loop.
  }

  watchClickThrough() {
    const cursor = screen.getCursorScreenPoint();
    let overWidget = null;
    let overRescue = null;

    for (const entry of this.windows.values()) {
      if (entry.destroyed || !entry.item.placement?.clickThrough || !entry.win.isVisible()) {
        if (this.rescueVisibleFor === entry.item.instanceId) this.hideRescue();
        continue;
      }
      const b = entry.win.getBounds();
      const inside = cursor.x >= b.x && cursor.x < b.x + b.width && cursor.y >= b.y && cursor.y < b.y + b.height;

      if (entry.rescue && !entry.rescue.isDestroyed() && entry.rescue.isVisible()) {
        const rb = entry.rescue.getBounds();
        if (cursor.x >= rb.x && cursor.x < rb.x + rb.width && cursor.y >= rb.y && cursor.y < rb.y + rb.height) {
          overRescue = entry;
        }
      }
      if (inside) overWidget = entry;
    }

    if (overWidget) {
      const id = overWidget.item.instanceId;
      if (!this.hoverSince.has(id)) this.hoverSince.set(id, Date.now());
      if (Date.now() - this.hoverSince.get(id) >= 400) this.showRescue(overWidget);
    }

    if (overRescue) {
      // Keep the pill open while the pointer is on it.
      if (overRescue.rescue && !overRescue.rescue.isDestroyed()) this.showRescue(overRescue);
      return;
    }

    for (const [id] of this.hoverSince) {
      const entry = this.windows.get(id);
      if (!entry || entry.destroyed) {
        this.hoverSince.delete(id);
        continue;
      }
      const b = entry.win.getBounds();
      const inside = cursor.x >= b.x && cursor.x < b.x + b.width && cursor.y >= b.y && cursor.y < b.y + b.height;
      if (!inside) {
        this.hoverSince.delete(id);
        if (this.rescueVisibleFor === id) this.hideRescue();
      }
    }

    if (!overWidget && !overRescue && this.rescueVisibleFor) {
      const entry = this.windows.get(this.rescueVisibleFor);
      if (!entry) this.hideRescue();
    }
  }

  showRescue(entry) {
    if (!entry.rescue || entry.rescue.isDestroyed()) {
      const theme = this.controller.appearanceFor(entry.item, { scale: 1 }).theme;
      const rescue = new BrowserWindow({
        width: 232,
        height: 40,
        frame: false,
        transparent: true,
        backgroundColor: '#00000000',
        hasShadow: false,
        resizable: false,
        movable: false,
        minimizable: false,
        maximizable: false,
        fullscreenable: false,
        skipTaskbar: true,
        focusable: true,
        alwaysOnTop: true,
        show: false,
        webPreferences: {
          preload: PRELOAD,
          contextIsolation: true,
          nodeIntegration: false,
          sandbox: false,
          backgroundThrottling: false,
        },
      });
      rescue.setAlwaysOnTop(true, 'screen-saver');
      rescue.loadFile(path.join(RENDERER, 'rescue.html'), {
        query: { instance: entry.item.instanceId, theme },
      });
      rescue.on('closed', () => {
        entry.rescue = null;
        if (this.rescueVisibleFor === entry.item.instanceId) this.rescueVisibleFor = null;
      });
      entry.rescue = rescue;
    }

    const b = entry.win.getBounds();
    const rb = entry.rescue.getBounds();
    const x = Math.round(b.x + Math.max(8, b.width - rb.width - 18));
    const y = Math.round(b.y + 16);
    entry.rescue.setBounds({ x, y, width: 232, height: 40 });
    if (!entry.rescue.isVisible()) entry.rescue.showInactive();
    this.rescueVisibleFor = entry.item.instanceId;
  }

  hideRescue() {
    if (!this.rescueVisibleFor) return;
    const entry = this.windows.get(this.rescueVisibleFor);
    if (entry?.rescue && !entry.rescue.isDestroyed()) entry.rescue.hide();
    this.rescueVisibleFor = null;
  }

  destroyRescue(instanceId) {
    const entry = this.windows.get(instanceId);
    if (!entry) return;
    if (entry.rescue && !entry.rescue.isDestroyed()) entry.rescue.destroy();
    entry.rescue = null;
    if (this.rescueVisibleFor === instanceId) this.rescueVisibleFor = null;
    this.hoverSince.delete(instanceId);
  }

  // ------------------------------------------------------------- context menu

  showContextMenu(instanceId) {
    const c = this.controller;
    const item = c.itemFor(instanceId);
    const entry = this.windows.get(instanceId);
    if (!item || !entry) return;
    const p = item.placement;
    const layer = p.layer || (p.alwaysOnTop ? 'top' : 'normal');
    const appearance = c.appearanceFor(item, { scale: 1 });
    const theme = p.theme || '';

    const setLayer = (value) => c.setLayerForInstance(instanceId, value);
    const setOpacity = (value) => c.setOpacityForInstance(instanceId, value);
    const setTheme = (value) => c.setThemeForInstance(instanceId, value);

    const template = [
      {
        label: 'Layer',
        submenu: [
          { label: 'Always on top', type: 'checkbox', checked: layer === 'top', click: () => setLayer('top') },
          { label: 'Normal', type: 'checkbox', checked: layer === 'normal', click: () => setLayer('normal') },
          { label: 'On desktop (behind windows)', type: 'checkbox', checked: layer === 'bottom', click: () => setLayer('bottom') },
        ],
      },
      {
        label: 'Click-through',
        type: 'checkbox',
        checked: Boolean(p.clickThrough),
        click: (menuItem) => c.setClickThroughForInstance(instanceId, menuItem.checked),
      },
      {
        label: 'Background opacity',
        submenu: [0, 0.2, 0.4, 0.6, 0.8, 1].map((value) => ({
          label: value === 0 ? '0% (text only)' : `${Math.round(value * 100)}%`,
          type: 'checkbox',
          checked: Math.abs(Number(p.opacity ?? 1) - value) < 0.01,
          click: () => setOpacity(value),
        })),
      },
      {
        label: 'Theme',
        submenu: [
          { label: 'Same as app', type: 'checkbox', checked: !theme, click: () => setTheme(null) },
          { label: 'Dark', type: 'checkbox', checked: theme === 'dark', click: () => setTheme('dark') },
          { label: 'Light', type: 'checkbox', checked: theme === 'light', click: () => setTheme('light') },
          { type: 'separator' },
          {
            label: 'Card frame (all widgets)',
            type: 'checkbox',
            checked: Boolean(c.cardFrame),
            click: (menuItem) => c.setCardFrame(menuItem.checked),
          },
        ],
      },
      {
        label: 'Size',
        submenu: [
          { label: 'Regular', type: 'checkbox', checked: !p.largeSize, click: () => c.setLargeForInstance(instanceId, false) },
          { label: 'Large', type: 'checkbox', checked: Boolean(p.largeSize), click: () => c.setLargeForInstance(instanceId, true) },
        ],
      },
      { type: 'separator' },
      {
        label: c.isResizing(instanceId) ? 'Done resizing' : 'Resize width',
        click: () => c.toggleResizeMode(instanceId),
      },
      { label: 'Settings…', click: () => c.openWidgetSettings(instanceId) },
      { label: 'Dock to sidebar', click: () => c.dockInstance(instanceId) },
      { type: 'separator' },
      { label: 'Remove widget', click: () => c.removeInstance(instanceId) },
    ];

    this.hideRescue();
    Menu.buildFromTemplate(template).popup({ window: entry.win });
    // Keep `appearance` referenced so linting/readers see why it is computed
    // before the menu: the current theme also drives the popup's native colours.
    void appearance;
  }

  showRescueMenu(instanceId) {
    const entry = this.windows.get(instanceId);
    if (entry?.rescue) {
      // The regular widget menu works while click-through is on because the
      // rescue window is interactive.
      this.showContextMenu(instanceId);
    }
  }

  destroy(instanceId) {
    const entry = this.windows.get(instanceId);
    if (!entry) return;
    entry.destroyed = true;
    clearInterval(this.timer);
    this.destroyRescue(instanceId);
    try {
      if (!entry.win.isDestroyed()) entry.win.destroy();
    } catch {
      /* already gone */
    }
    this.windows.delete(instanceId);
  }

  show(instanceId) {
    const entry = this.windows.get(instanceId);
    if (entry && !entry.destroyed) {
      entry.win.showInactive();
      this.applyItem(entry.item);
    }
  }

  hide(instanceId) {
    const entry = this.windows.get(instanceId);
    if (!entry || entry.destroyed) return;
    this.destroyRescue(instanceId);
    entry.win.hide();
  }
}

module.exports = { FloatingWindows, SNAP_STEP };
