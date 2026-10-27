'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { app, screen, shell, Menu } = require('electron');

const { Store } = require('./store');
const { LayoutStore, clone } = require('./layout-store');
const defs = require('./widget-defs');
const screens = require('./screens');
const weather = require('./weather');
const { MetricsService } = require('./metrics');
const { FloatingWindows } = require('./floating');
const { Sidebar } = require('./sidebar');
const { Dialogs } = require('./dialogs');
const { appearanceFor } = require('./appearance');
const { UpdateService } = require('./updates');
const { AutoStartService } = require('./autostart');

const clamp = (value, min, max) => Math.max(min, Math.min(max, value));

/**
 * Central app controller: layout persistence and every mutation that can be
 * triggered from a widget, the sidebar, the tray or a dialog. Renderers are
 * pure views; they receive complete state pushes from here.
 */
class Controller {
  constructor() {
    this.store = new Store();
    this.layouts = new LayoutStore(this.store);
    this.layouts.ensureDefaults();
    this.normalizeLayout(this.active);

    this.snapshot = null;
    this.mediaMeta = {
      hasSession: false,
      title: 'Nothing playing',
      artist: '',
      album: '',
      appName: '',
      status: '',
      elapsed: 0,
      duration: 0,
      thumbnail: null,
    };
    this.weatherState = new Map();
    this.weatherBusy = new Set();
    this.saveTimer = null;
    this.arrangeTimer = null;
    this.weatherTimer = null;
    this.previewOrigins = new Map();
    this.initialArrangement = new Set();
    this.resizeMode = new Set();
    this.suppressClosed = new Set();
    this.shuttingDown = false;
    this.lastDisplaySignature = '';

    this.metrics = new MetricsService(this.store);
    this.floating = new FloatingWindows(this);
    this.sidebar = new Sidebar(this);
    this.dialogs = new Dialogs(this);
    this.updates = new UpdateService(this.store);
    this.autostart = new AutoStartService(this.store);
    this.tray = null;
    this.metrics.setLoadFlags({
      gpu: this.store.settings.loadGpu,
      drives: this.store.settings.loadDrives,
      network: this.store.settings.loadNetwork,
    });
  }

  // --------------------------------------------------------------- accessors

  get active() {
    return this.layouts.active;
  }

  get items() {
    return this.active.items;
  }

  get settings() {
    return this.store.settings;
  }

  get theme() {
    return String(this.store.settings.theme || 'dark').toLowerCase() === 'light' ? 'light' : 'dark';
  }

  get cardFrame() {
    return Boolean(this.store.settings.cardFrameV2);
  }

  get isSidebarMode() {
    return String(this.store.settings.displayMode || 'widgets').toLowerCase() === 'sidebar';
  }

  log(level, message, error) {
    this.store.log(level, message, error);
  }

  titleFor(item) {
    if (!item) return 'Widget';
    return item.titleOverride || defs.DISPLAY_NAMES[item.widgetId] || item.widgetId;
  }

  itemFor(instanceId) {
    return this.items.find((item) => item.instanceId === instanceId) || null;
  }

  dockedItems() {
    return this.items.filter((item) => item.placement?.docked && !item.placement?.hidden);
  }

  floatingItems() {
    return this.items.filter((item) => !item.placement?.docked && !item.placement?.hidden);
  }

  settingsFor(item) {
    const defaults = defs.createDefaultSettings(item.widgetId);
    let parsed = null;
    try {
      if (item.settingsJson && typeof item.settingsJson === 'object') parsed = item.settingsJson;
      else if (typeof item.settingsJson === 'string' && item.settingsJson.trim()) parsed = JSON.parse(item.settingsJson);
    } catch {
      parsed = null;
    }
    return defs.normalizeSettings(item.widgetId, parsed || defaults);
  }

  saveSettingsFor(item, settings) {
    item.settingsJson = JSON.stringify(defs.normalizeSettings(item.widgetId, settings));
  }

  appearanceFor(item, options = {}) {
    const placement = { ...(item.placement || {}) };
    // Docked cards use the sidebar's own opacity setting (upstream
    // SidebarWindow.ApplyLook → CardAppearance.Apply).
    if (placement.docked) placement.opacity = this.store.settings.sidebarCardOpacity;
    return appearanceFor(this.theme, placement, this.store.settings, options);
  }

  /** CSS variables for native-framed HTML dialogs (they follow the app theme). */
  dialogTheme() {
    const appearance = appearanceFor(this.theme, { opacity: 1, theme: null }, this.store.settings);
    return { theme: this.theme, vars: appearance.vars };
  }

  // ------------------------------------------------------------ boot / layout

  normalizeItem(item) {
    if (!item.instanceId) item.instanceId = require('node:crypto').randomUUID();
    item.widgetId = String(item.widgetId || '').toLowerCase();
    item.titleOverride = item.titleOverride || null;
    if (item.settingsJson == null) item.settingsJson = '';
    item.placement = defs.placement({ ...(item.placement || {}) });
    if (item.placement.layer == null) item.placement.layer = item.placement.alwaysOnTop ? 'top' : 'normal';
    return item;
  }

  normalizeLayout(layout) {
    if (!layout) return;
    if (!Array.isArray(layout.items)) layout.items = [];
    for (const item of layout.items) this.normalizeItem(item);
    layout.items = layout.items.filter((item) => defs.widget(item.widgetId));
    if (layout.theme) {
      this.store.settings.theme = String(layout.theme).toLowerCase() === 'light' ? 'light' : 'dark';
    }
    this.store.settings.activeLayout = layout.name;
  }

  ensureDefaultWidgets() {
    if (this.items.length > 0 || this.active.hasSavedState) return;
    for (const widgetId of defs.DEFAULT_WIDGET_IDS) {
      const created = defs.item(widgetId);
      if (this.isSidebarMode) {
        created.placement.docked = true;
        created.placement.autoPlace = false;
        created.placement.layer = 'normal';
      }
      this.items.push(this.normalizeItem(created));
    }
    this.log('INFO', `Layout '${this.active.name}' was empty — added default hardware widgets.`);
    this.scheduleSave();
  }

  async initialize() {
    this.ensureDefaultWidgets();
    this.normalizeLayout(this.active);

    this.metrics.onSnapshot((snapshot) => this.onSnapshot(snapshot));
    this.metrics.media.onUpdate((meta) => {
      this.mediaMeta = {
        ...meta,
        thumbnail: meta.thumbnail || null,
      };
      this.pushMedia();
    });

    this.floating.start();
    this.sidebar.create();
    this.materialize();

    screen.on('display-added', () => this.onDisplaysChanged());
    screen.on('display-removed', () => this.onDisplaysChanged());
    screen.on('display-metrics-changed', () => this.onDisplaysChanged());

    await this.metrics.start();
    this.syncWeather(true);
    this.weatherTimer = setInterval(() => this.syncWeather(false), 60_000);
    this.autostart.apply();
    this.store.saveSettings();
    this.log('INFO', `DeskStats ${app.getVersion()} started (${this.items.length} widgets, theme ${this.theme})`);
  }

  materialize() {
    this.normalizeLayout(this.active);

    for (const item of this.items) {
      this.destroyFloating(item.instanceId);
    }

    const floating = this.floatingItems();
    const allAuto = floating.length > 0 && floating.every((item) => item.placement.autoPlace);
    if (allAuto) {
      const work = screens.primaryWorkArea();
      const sizes = floating.map((item) => ({
        width: clamp(Number(item.placement.width) || 320, 220, 1200),
        height: clamp(Number(item.placement.height) || 200, 120, 1600),
      }));
      const arranged = screens.arrangeInitial(work, sizes);
      arranged.forEach((rect, index) => {
        const item = floating[index];
        Object.assign(item.placement, rect);
        item.placement.autoPlace = false;
        item.placement.x = rect.x;
        item.placement.y = rect.y;
        this.captureAnchor(item);
        this.initialArrangement.add(item.instanceId);
      });
      this.queueInitialArrange();
    }

    for (const item of this.items) {
      if (item.placement.hidden) continue;
      if (item.placement.docked) continue;
      if (!item.placement.monitorDeviceId) this.captureAnchor(item);
      this.floating.create(item);
    }

    this.sidebar.refresh();
    this.pushAllStates();
    this.updateTrashNeed();
    this.syncWeather(false);
    this.scheduleSave();
  }

  queueInitialArrange() {
    clearTimeout(this.arrangeTimer);
    this.arrangeTimer = setTimeout(() => {
      if (this.initialArrangement.size === 0) return;
      const ids = [...this.initialArrangement];
      const floating = this.floatingItems().filter((item) => ids.includes(item.instanceId));
      if (!floating.length) {
        this.initialArrangement.clear();
        return;
      }
      const work = screens.primaryWorkArea();
      const sizes = floating.map((item) => ({
        width: clamp(Number(item.placement.width) || 320, 220, 1200),
        height: clamp(Number(item.placement.height) || 200, 120, 1600),
      }));
      const arranged = screens.arrangeInitial(work, sizes);
      arranged.forEach((rect, index) => {
        const item = floating[index];
        Object.assign(item.placement, rect);
        item.placement.autoPlace = false;
        this.captureAnchor(item);
        const entry = this.floating.get(item.instanceId);
        if (entry) {
          const b = entry.win.getBounds();
          entry.restoring = true;
          entry.win.setBounds({ x: rect.x, y: rect.y, width: b.width, height: b.height }, false);
          entry.restoring = false;
        }
      });
      this.initialArrangement.clear();
      this.scheduleSave();
      this.log('INFO', 'Initial widget arrangement applied');
    }, 650);
  }

  beginManualPlacement(instanceId) {
    if (instanceId) this.initialArrangement.delete(instanceId);
    this.active.hasSavedState = true;
  }

  onSnapshot(snapshot) {
    this.snapshot = snapshot;
    this.sidebar.pushMetrics(snapshot, null);
    for (const item of this.floatingItems()) this.floating.pushState(item);
    this.updateTrashNeed();
    this.syncWeather(false);
  }

  onDisplaysChanged() {
    const signature = screens
      .all()
      .map((display) => `${display.id}:${display.bounds.x},${display.bounds.y},${display.bounds.width},${display.bounds.height}`)
      .join('|');
    if (signature === this.lastDisplaySignature) return;
    this.lastDisplaySignature = signature;
    this.sidebar.applySettings();
    for (const item of this.floatingItems()) {
      if (!item.placement.monitorDeviceId) continue;
      const pos = this.screenPositionFor(item, item.placement.width, item.placement.height);
      const entry = this.floating.get(item.instanceId);
      if (entry) {
        const b = entry.win.getBounds();
        entry.restoring = true;
        entry.win.setPosition(pos.x, pos.y, false);
        entry.win.setBounds({ x: pos.x, y: pos.y, width: b.width, height: b.height }, false);
        entry.restoring = false;
        this.placeDetached(item.instanceId, pos.x, pos.y, b.width, b.height, { userMove: false });
      }
    }
  }

  // -------------------------------------------------------------- placements

  workAreaForItem(item) {
    const id = item?.placement?.monitorDeviceId;
    if (id) {
      const work = screens.workAreaOfId(id);
      if (work) return work;
    }
    return screens.primaryWorkArea();
  }

  screenPositionFor(item, width, height) {
    const p = item.placement;
    if (p.monitorDeviceId) {
      return screens.restoreForAnchor(p, width, height);
    }
    const work = this.workAreaForItem(item);
    return screens.clampToWorkArea(
      { x: Number(p.x) || 0, y: Number(p.y) || 0, width: width || 320, height: height || 200 },
      work,
    );
  }

  captureAnchor(item) {
    const b = this.floating.get(item.instanceId)?.win.getBounds() || {
      x: item.placement.x,
      y: item.placement.y,
      width: item.placement.width,
      height: item.placement.height,
    };
    const anchor = screens.anchorFor(b);
    Object.assign(item.placement, anchor);
    item.placement.x = b.x;
    item.placement.y = b.y;
    item.placement.width = b.width || item.placement.width;
    item.placement.height = b.height || item.placement.height;
  }

  placeDetached(instanceId, x, y, width, height, { userMove = true } = {}) {
    const item = this.itemFor(instanceId);
    if (!item) return;
    item.placement.x = Math.round(x);
    item.placement.y = Math.round(y);
    if (width) item.placement.width = Math.round(width);
    if (height) item.placement.height = Math.round(height);
    item.placement.autoPlace = false;
    if (userMove) item.placement.hidden = false;
    this.captureAnchor(item);
    this.active.hasSavedState = true;
    this.scheduleSave();
  }

  setDetachedSize(instanceId, width, height) {
    const item = this.itemFor(instanceId);
    if (!item) return;
    if (Math.abs((item.placement.width || 0) - width) < 0.5 && Math.abs((item.placement.height || 0) - height) < 0.5) return;
    item.placement.width = Math.round(width);
    item.placement.height = Math.round(height);
    if (!item.placement.monitorDeviceId) this.captureAnchor(item);
    this.scheduleSave();
    if (this.initialArrangement.has(instanceId)) this.queueInitialArrange();
  }

  isGridSnapEnabled(instanceId) {
    return this.itemFor(instanceId)?.placement?.snapToGrid !== false;
  }

  isResizing(instanceId) {
    return this.resizeMode.has(instanceId);
  }

  toggleResizeMode(instanceId) {
    if (this.resizeMode.has(instanceId)) this.resizeMode.delete(instanceId);
    else this.resizeMode.add(instanceId);
    this.floating.setResizeMode(instanceId, this.resizeMode.has(instanceId));
  }

  endResizeMode(instanceId) {
    if (!this.resizeMode.delete(instanceId)) return;
    this.floating.setResizeMode(instanceId, false);
  }

  onWidgetWindowClosed(instanceId) {
    if (this.shuttingDown) return;
    if (this.suppressClosed.has(instanceId)) return;
    this.removeInstance(instanceId);
  }

  destroyFloating(instanceId) {
    this.suppressClosed.add(instanceId);
    this.floating.destroy(instanceId);
    setTimeout(() => this.suppressClosed.delete(instanceId), 0);
  }

  // ------------------------------------------------------------ layout edits

  resolveAutoPlace(item) {
    const work = this.workAreaForItem(item);
    const size = {
      width: clamp(Number(item.placement.width) || 320, 220, 1200),
      height: clamp(Number(item.placement.height) || 200, 120, 1600),
    };
    const occupied = this.floatingItems()
      .filter((other) => other.instanceId !== item.instanceId)
      .map((other) => {
        const b = this.floating.get(other.instanceId)?.win.getBounds();
        return b || {
          x: other.placement.x,
          y: other.placement.y,
          width: other.placement.width || 320,
          height: other.placement.height || 200,
        };
      });
    const reserve = {
      enabled: this.sidebar.isVisible,
      edge: this.store.settings.sidebarEdge,
      width: this.sidebar.currentWidth,
    };
    const rect = screens.resolveAutoPlace(work, size, occupied, reserve);
    Object.assign(item.placement, rect);
    item.placement.autoPlace = false;
    this.captureAnchor(item);
    this.scheduleSave();
    return rect;
  }

  addWidget(widgetId, options = {}) {
    const widget = defs.widget(widgetId);
    if (!widget) return null;
    const docked = options.docked ?? this.isSidebarMode;
    const created = defs.item(widget.id);
    created.settingsJson = JSON.stringify(defs.normalizeSettings(widget.id, options.settings || widget.defaults));
    if (options.titleOverride) created.titleOverride = options.titleOverride;
    created.placement.docked = Boolean(docked);
    created.placement.autoPlace = !docked;
    created.placement.layer = 'normal';
    if (options.placement) Object.assign(created.placement, options.placement);
    this.items.push(this.normalizeItem(created));

    if (docked) {
      this.sidebar.refresh();
    } else {
      this.resolveAutoPlace(created);
      this.floating.create(created);
    }
    this.active.hasSavedState = true;
    this.scheduleSave();
    this.afterLayoutChange();
    this.log('INFO', `Added widget '${widget.id}' (${docked ? 'docked' : 'floating'})`);
    return created;
  }

  toggleWidget(widgetId) {
    const ofType = this.items.filter((item) => item.widgetId === String(widgetId).toLowerCase());
    const shown = ofType.filter((item) => !item.placement.hidden);
    if (shown.length) {
      for (const item of shown) this.hideInstance(item.instanceId);
      return;
    }
    const hidden = ofType[0];
    if (hidden) {
      hidden.placement.docked = this.isSidebarMode;
      if (!this.isSidebarMode) hidden.placement.autoPlace = true;
      this.showInstance(hidden.instanceId);
      return;
    }
    this.addWidget(widgetId);
  }

  isWidgetShown(widgetId) {
    return this.items.some((item) => item.widgetId === String(widgetId).toLowerCase() && !item.placement.hidden);
  }

  showAllHardwareWidgets() {
    for (const widgetId of defs.DEFAULT_WIDGET_IDS) {
      if (this.isWidgetShown(widgetId)) continue;
      const hidden = this.items.find((item) => item.widgetId === widgetId);
      if (hidden) this.showInstance(hidden.instanceId);
      else this.addWidget(widgetId, { docked: this.isSidebarMode });
    }
    this.afterLayoutChange();
  }

  hideInstance(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item || item.placement.hidden) return false;
    item.placement.hidden = true;
    this.destroyFloating(instanceId);
    this.endResizeMode(instanceId);
    this.initialArrangement.delete(instanceId);
    this.weatherState.delete(instanceId);
    this.active.hasSavedState = true;
    this.scheduleSave();
    this.afterLayoutChange();
    return true;
  }

  showInstance(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item || !item.placement.hidden) return false;
    item.placement.hidden = false;
    if (!item.placement.docked) {
      if (item.placement.autoPlace) this.resolveAutoPlace(item);
      this.floating.create(item);
    }
    this.active.hasSavedState = true;
    this.scheduleSave();
    this.afterLayoutChange();
    return true;
  }

  removeInstance(instanceId) {
    const index = this.items.findIndex((item) => item.instanceId === instanceId);
    if (index < 0) return;
    const [item] = this.items.splice(index, 1);
    this.destroyFloating(instanceId);
    this.endResizeMode(instanceId);
    this.initialArrangement.delete(instanceId);
    this.weatherState.delete(instanceId);
    this.active.hasSavedState = true;
    this.scheduleSave();
    this.afterLayoutChange();
    this.log('INFO', `Removed widget '${this.titleFor(item)}' (${item.widgetId})`);
  }

  dockInstance(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item || item.placement.docked || item.placement.hidden) return false;
    this.destroyFloating(instanceId);
    this.endResizeMode(instanceId);
    this.initialArrangement.delete(instanceId);
    item.placement.docked = true;
    item.placement.autoPlace = false;
    this.active.hasSavedState = true;
    this.scheduleSave();
    this.afterLayoutChange();
    this.log('INFO', `Docked widget '${this.titleFor(item)}'`);
    return true;
  }

  detachInstance(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item || !item.placement.docked || item.placement.hidden) return false;
    item.placement.docked = false;
    item.placement.autoPlace = true;
    if (!item.placement.layer) item.placement.layer = 'normal';
    this.resolveAutoPlace(item);
    this.floating.create(item);
    this.active.hasSavedState = true;
    this.scheduleSave();
    this.afterLayoutChange();
    this.log('INFO', `Detached widget '${this.titleFor(item)}'`);
    return true;
  }

  switchToSidebar() {
    this.store.settings.displayMode = 'sidebar';
    this.store.saveSettings();
    for (const item of [...this.items]) {
      if (!item.placement.docked && !item.placement.hidden) {
        try {
          this.dockInstance(item.instanceId);
        } catch (error) {
          this.log('ERROR', `Could not dock '${item.widgetId}'`, error);
        }
      }
    }
    this.sidebar.applySettings();
    this.afterLayoutChange();
    this.log('INFO', 'Switched to sidebar mode');
  }

  switchToWidgets() {
    this.store.settings.displayMode = 'widgets';
    this.store.saveSettings();
    for (const item of [...this.items]) {
      if (item.placement.docked && !item.placement.hidden) {
        this.detachInstance(item.instanceId);
      }
    }
    this.afterLayoutChange();
    this.log('INFO', 'Switched to widgets mode');
  }

  resetFloatPositions() {
    const floating = this.floatingItems();
    if (!floating.length) return 0;
    for (const item of floating) item.placement.autoPlace = true;
    for (const item of floating) {
      const rect = this.resolveAutoPlace(item);
      item.placement.monitorIndex = 0;
      const entry = this.floating.get(item.instanceId);
      if (entry) {
        const b = entry.win.getBounds();
        entry.restoring = true;
        entry.win.setBounds({ x: rect.x, y: rect.y, width: b.width, height: b.height }, false);
        entry.restoring = false;
        this.placeDetached(item.instanceId, rect.x, rect.y, b.width, b.height, { userMove: true });
      }
    }
    this.scheduleSave();
    this.log('INFO', `Reset positions of ${floating.length} floating widget(s)`);
    return floating.length;
  }

  // ---------------------------------------------------------------- layout IO

  activateLayout(name) {
    const layout = this.layouts.activate(name);
    if (!layout) return false;
    this.normalizeLayout(layout);
    this.store.settings.activeLayout = layout.name;
    if (layout.theme) this.store.settings.theme = String(layout.theme).toLowerCase() === 'light' ? 'light' : 'dark';
    this.store.saveSettings();
    this.materialize();
    this.log('INFO', `Switched to layout '${layout.name}' (${layout.items.length} items)`);
    return true;
  }

  saveCurrentAs(name) {
    const clean = String(name || '').trim();
    if (!clean) return { ok: false, error: 'Enter a name for the layout.' };
    if (defs.isPreset(clean)) return { ok: false, error: 'That name is reserved for a built-in preset. Pick another name.' };
    try {
      const layout = clone(this.active);
      layout.name = clean;
      layout.id = clean.toLowerCase();
      this.layouts.saveAs(layout, clean);
      this.active.name = clean;
      this.active.id = clean.toLowerCase();
      this.layouts.saveActive(clone(this.active));
      this.store.settings.activeLayout = clean;
      this.store.saveSettings();
      this.log('INFO', `Saved current layout as '${clean}'`);
      return { ok: true, name: clean };
    } catch (error) {
      return { ok: false, error: error.message };
    }
  }

  resetToPreset() {
    const fresh = this.layouts.resetToPreset(this.active.name);
    if (!fresh) return false;
    this.normalizeLayout(fresh);
    if (fresh.theme) this.store.settings.theme = fresh.theme;
    this.store.saveSettings();
    this.materialize();
    this.log('INFO', `Layout '${fresh.name}' reset to its preset`);
    return true;
  }

  setTheme(theme) {
    const value = String(theme).toLowerCase() === 'light' ? 'light' : 'dark';
    this.store.settings.theme = value;
    this.active.theme = value;
    this.store.saveSettings();
    this.scheduleSave();
    this.pushAllStates();
    this.sidebar.refresh();
  }

  setCardFrame(show) {
    this.store.settings.cardFrameV2 = Boolean(show);
    this.store.saveSettings();
    this.pushAllStates();
    this.sidebar.refresh();
  }

  // --------------------------------------------------------- widget appearance

  setLayerForInstance(instanceId, layer) {
    const item = this.itemFor(instanceId);
    if (!item) return;
    item.placement.layer = ['top', 'normal', 'bottom'].includes(layer) ? layer : 'normal';
    item.placement.alwaysOnTop = item.placement.layer === 'top';
    this.floating.applyItem(item);
    this.pushItemState(item);
    this.scheduleSave();
  }

  setOpacityForInstance(instanceId, opacity) {
    const item = this.itemFor(instanceId);
    if (!item) return;
    item.placement.opacity = clamp(Number(opacity), 0, 1);
    this.floating.applyItem(item);
    this.pushItemState(item);
    this.scheduleSave();
  }

  setThemeForInstance(instanceId, theme) {
    const item = this.itemFor(instanceId);
    if (!item) return;
    item.placement.theme = theme ? (String(theme).toLowerCase() === 'light' ? 'light' : 'dark') : null;
    this.floating.applyItem(item);
    this.pushItemState(item);
    this.scheduleSave();
  }

  setClickThroughForInstance(instanceId, enabled) {
    const item = this.itemFor(instanceId);
    if (!item) return;
    item.placement.clickThrough = Boolean(enabled);
    this.floating.applyItem(item);
    this.pushItemState(item);
    this.scheduleSave();
  }

  setLargeForInstance(instanceId, large) {
    const item = this.itemFor(instanceId);
    if (!item) return;
    const next = Boolean(large);
    if (Boolean(item.placement.largeSize) === next) return;
    const scale = 1.35;
    item.placement.largeSize = next;
    if (!item.placement.docked) {
      const entry = this.floating.get(instanceId);
      const current = entry ? entry.win.getBounds() : null;
      const width = Math.round((current?.width || item.placement.width || 320) * (next ? scale : 1 / scale));
      item.placement.width = width;
      if (entry) {
        const b = entry.win.getBounds();
        entry.restoring = true;
        entry.win.setBounds({ x: b.x, y: b.y, width, height: b.height }, false);
        entry.restoring = false;
        this.floating.applyItem(item);
      }
    }
    this.sidebar.refresh();
    this.pushItemState(item);
    this.scheduleSave();
  }

  appearanceStateFor(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item) return null;
    return {
      opacity: Number(item.placement.opacity ?? 1),
      layer: item.placement.layer || (item.placement.alwaysOnTop ? 'top' : 'normal'),
      clickThrough: Boolean(item.placement.clickThrough),
      theme: item.placement.theme || '',
    };
  }

  applyAppearanceState(instanceId, state, { persist = false } = {}) {
    const item = this.itemFor(instanceId);
    if (!item || !state) return;
    item.placement.opacity = clamp(Number(state.opacity ?? 1), 0, 1);
    item.placement.layer = ['top', 'normal', 'bottom'].includes(state.layer) ? state.layer : 'normal';
    item.placement.alwaysOnTop = item.placement.layer === 'top';
    item.placement.clickThrough = Boolean(state.clickThrough);
    item.placement.theme = state.theme ? (String(state.theme).toLowerCase() === 'light' ? 'light' : 'dark') : null;
    this.floating.applyItem(item);
    this.pushItemState(item);
    if (persist) this.scheduleSave();
  }

  beginAppearancePreview(instanceId) {
    this.previewOrigins.set(instanceId, this.appearanceStateFor(instanceId));
  }

  endAppearancePreview(instanceId) {
    this.previewOrigins.delete(instanceId);
  }

  // --------------------------------------------------------------- widget data

  bootstrapWidget(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item) return null;
    return {
      instanceId,
      widgetId: item.widgetId,
      title: this.titleFor(item),
      settings: this.settingsFor(item),
      appearance: this.appearanceFor(item, { scale: 1 }),
      snapshot: this.snapshot,
      aux: this.auxFor(item),
      clickThrough: Boolean(item.placement.clickThrough),
      large: Boolean(item.placement.largeSize),
    };
  }

  auxFor(item) {
    const aux = {};
    if (item.widgetId === 'weather') aux.weather = this.weatherState.get(item.instanceId)?.data || this.weatherState.get(item.instanceId) || null;
    if (item.widgetId === 'media') aux.media = this.mediaMeta;
    return aux;
  }

  pushItemState(item) {
    if (!item) return;
    if (item.placement.docked) this.sidebar.pushState();
    else this.floating.pushState(item);
  }

  pushAllStates() {
    for (const item of this.floatingItems()) this.floating.pushState(item);
    this.sidebar.pushState();
  }

  pushMedia() {
    for (const item of this.floatingItems()) {
      if (item.widgetId === 'media') this.floating.pushState(item);
    }
    this.sidebar.pushState();
  }

  // ------------------------------------------------------------------ weather

  syncWeather(force) {
    for (const item of this.items) {
      if (item.widgetId !== 'weather' || item.placement.hidden) continue;
      const settings = this.settingsFor(item);
      const signature = `${settings.locationName}|${settings.resolvedLocation}|${settings.latitude}|${settings.longitude}|${settings.dailyDays}`;
      const entry = this.weatherState.get(item.instanceId);
      const due = !entry || force || entry.signature !== signature || Date.now() - entry.at > Math.max(10, Number(settings.refreshMinutes) || 30) * 60_000;
      if (due) this.requestWeather(item, settings, signature);
    }
  }

  async requestWeather(item, settings, signature) {
    const id = item.instanceId;
    if (this.weatherBusy.has(id)) return;
    this.weatherBusy.add(id);
    try {
      const data = await weather.forecast(settings);
      let finalSignature = signature;
      if (data?.ok) {
        const next = { ...settings, resolvedLocation: data.resolved || settings.locationName };
        if (Number.isFinite(Number(data.latitude))) next.latitude = Number(data.latitude);
        if (Number.isFinite(Number(data.longitude))) next.longitude = Number(data.longitude);
        const changed =
          next.resolvedLocation !== settings.resolvedLocation ||
          Number(next.latitude) !== Number(settings.latitude) ||
          Number(next.longitude) !== Number(settings.longitude);
        if (changed) {
          this.saveSettingsFor(item, next);
          finalSignature = `${next.locationName}|${next.resolvedLocation}|${next.latitude}|${next.longitude}|${next.dailyDays}`;
          this.scheduleSave();
        }
      }
      this.weatherState.set(id, {
        data: data || { ok: false, reason: 'offline' },
        at: Date.now(),
        signature: finalSignature,
      });
      this.pushItemState(item);
    } catch (error) {
      this.weatherState.set(id, { data: { ok: false, reason: 'offline' }, at: Date.now(), signature });
      this.pushItemState(item);
      this.log('WARN', `Weather refresh failed: ${error.message}`);
    } finally {
      this.weatherBusy.delete(id);
    }
  }

  refreshWeatherFor(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item || item.widgetId !== 'weather') return;
    this.weatherState.delete(instanceId);
    const settings = this.settingsFor(item);
    this.requestWeather(item, settings, `${settings.locationName}|${settings.resolvedLocation}|${settings.latitude}|${settings.longitude}|${settings.dailyDays}`);
  }

  // -------------------------------------------------------------------- trash

  updateTrashNeed() {
    const needed = this.items.some((item) => item.widgetId === 'recyclebin' && !item.placement.hidden);
    if (this.metrics.needsTrash !== needed) this.metrics.setNeeds({ trash: needed });
  }

  async emptyTrash() {
    try {
      await this.metrics.trash.empty();
      await this.metrics.tick();
      return true;
    } catch (error) {
      this.log('WARN', `Empty Trash failed: ${error.message}`);
      return false;
    }
  }

  afterLayoutChange() {
    this.sidebar.refresh();
    this.updateTrashNeed();
    this.syncWeather(false);
    this.pushAllStates();
  }

  // -------------------------------------------------------------- dialogs/data

  openWidgetSettings(instanceId) {
    return this.dialogs.widgetSettings(instanceId);
  }

  showSidebarCardMenu(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item) return;
    const large = Boolean(item.placement.largeSize);
    const template = [
      {
        label: 'Size',
        submenu: [
          { label: 'Regular', type: 'checkbox', checked: !large, click: () => this.setLargeForInstance(instanceId, false) },
          { label: 'Large', type: 'checkbox', checked: large, click: () => this.setLargeForInstance(instanceId, true) },
        ],
      },
      { label: 'Detach window', click: () => this.detachInstance(instanceId) },
      { label: 'Settings…', click: () => this.openWidgetSettings(instanceId) },
      { type: 'separator' },
      { label: 'Remove widget', click: () => this.removeInstance(instanceId) },
    ];
    Menu.buildFromTemplate(template).popup({ window: this.sidebar.win || undefined });
  }

  widgetDialogData(instanceId) {
    const item = this.itemFor(instanceId);
    if (!item) return null;
    const widget = defs.widget(item.widgetId);
    const settings = this.settingsFor(item);
    const schema = defs.schemaFor(item.widgetId)
      .map((group) => ({ ...group, fields: group.fields.filter((field) => !field.hidden) }))
      .filter((group) => group.fields.length > 0);

    const adapters = (this.snapshot?.adapters || []).map((adapter) => ({
      value: adapter.name,
      label: adapter.isUp ? adapter.name : `${adapter.name} (disconnected)`,
    }));
    const drives = (this.snapshot?.drives || []).map((drive) => ({ value: drive.name, label: drive.name }));

    return {
      instanceId,
      widgetId: item.widgetId,
      title: this.titleFor(item),
      description: widget?.description || '',
      settings,
      defaults: widget ? widget.defaults : {},
      schema,
      options: {
        adapters,
        drives,
        // Used by settings with MaxSource="cpuThreads" (upstream caps the
        // "Cores shown" slider at the logical processor count).
        cpuThreads: this.snapshot?.cpu?.perCoreLoad?.length || 0,
      },
      appearance: item.placement.docked ? null : this.appearanceStateFor(instanceId),
      theme: this.theme,
      vars: this.appearanceFor(item, { scale: 1 }).vars,
    };
  }

  saveWidgetSettings(instanceId, input = {}) {
    const item = this.itemFor(instanceId);
    if (!item) return { ok: false, error: 'This widget no longer exists.' };
    const next = defs.normalizeSettings(item.widgetId, input.settings || {});
    if (input.pickedCity) {
      next.locationName = input.pickedCity.name;
      next.resolvedLocation = input.pickedCity.name;
      next.latitude = Number(input.pickedCity.latitude);
      next.longitude = Number(input.pickedCity.longitude);
    }
    // Keep manually edited coordinates when the city text was not re-picked.
    for (const key of ['latitude', 'longitude']) {
      if (input.settings && Number.isFinite(Number(input.settings[key]))) next[key] = Number(input.settings[key]);
    }
    this.saveSettingsFor(item, next);
    if (input.appearance) this.applyAppearanceState(instanceId, input.appearance, { persist: true });
    this.refreshWeatherFor(instanceId);
    this.active.hasSavedState = true;
    this.scheduleSave();
    this.pushItemState(item);
    this.sidebar.refresh();
    this.log('INFO', `Settings edited for '${this.titleFor(item)}'`);
    return { ok: true, settings: next };
  }

  appSettingsData() {
    return {
      settings: { ...this.store.settings },
      monitors: screens.describeMonitors().map(({ id, label, display }) => ({
        id,
        label,
        bounds: display.bounds,
      })),
      version: app.getVersion(),
      autostart: this.autostart.isEnabled(),
      dataDir: this.store.dataDir,
    };
  }

  saveAppSettings(input = {}) {
    const s = this.store.settings;
    if (input.pollIntervalMs != null) s.pollIntervalMs = clamp(Math.round(Number(input.pollIntervalMs) || 1000), 200, 60_000);
    if (input.loadGpu != null) s.loadGpu = Boolean(input.loadGpu);
    if (input.loadDrives != null) s.loadDrives = Boolean(input.loadDrives);
    if (input.loadNetwork != null) s.loadNetwork = Boolean(input.loadNetwork);
    if (input.theme != null) s.theme = String(input.theme).toLowerCase() === 'light' ? 'light' : 'dark';
    if (input.cardFrameV2 != null) s.cardFrameV2 = Boolean(input.cardFrameV2);
    if (input.sidebarWidth != null) s.sidebarWidth = clamp(Math.round(Number(input.sidebarWidth) || s.sidebarWidth), 240, 560);
    if (input.sidebarEdge != null) s.sidebarEdge = String(input.sidebarEdge).toLowerCase() === 'left' ? 'left' : 'right';
    if (input.sidebarScreenId != null) s.sidebarScreenId = String(input.sidebarScreenId || '');
    s.sidebarAutoHide = Boolean(input.sidebarAutoHide);
    s.sidebarPinned = Boolean(input.sidebarPinned);
    s.sidebarCardOpacity = clamp(Number(input.sidebarCardOpacity ?? 1), 0, 1);
    s.sidebarShowTitle = Boolean(input.sidebarShowTitle);
    s.sidebarBackground = Boolean(input.sidebarBackground);
    s.sidebarLayer = ['top', 'normal', 'bottom'].includes(input.sidebarLayer) ? input.sidebarLayer : s.sidebarLayer;
    s.startWithWindows = Boolean(input.startWithWindows);
    s.automaticUpdates = Boolean(input.automaticUpdates);

    this.active.theme = s.theme;
    this.store.saveSettings();
    this.metrics.setIntervalMs(s.pollIntervalMs);
    this.metrics.setLoadFlags({ gpu: s.loadGpu, drives: s.loadDrives, network: s.loadNetwork });
    this.autostart.apply();
    this.sidebar.applySettings();
    this.pushAllStates();
    this.scheduleSave();
    return { ok: true, settings: { ...s } };
  }

  searchCities(query) {
    return weather.searchCities(query);
  }

  aboutData() {
    return {
      version: app.getVersion(),
      electron: process.versions.electron,
      chrome: process.versions.chrome,
      node: process.versions.node,
      dataDir: this.store.dataDir,
    };
  }

  monitorLabels() {
    return screens.describeMonitors().map(({ id, label, display }) => ({ id, label, bounds: display.bounds }));
  }

  identifyScreens(ids) {
    const all = this.monitorLabels();
    const labels = Array.isArray(ids) && ids.length ? all.filter((entry) => ids.includes(entry.id)) : all;
    this.dialogs.identify(labels);
  }

  checkUpdates(manual) {
    return this.updates.check(Boolean(manual));
  }

  hardwareReport() {
    const text = this.metrics.hardwareReport();
    const file = path.join(this.store.dataDir, 'hardware-report.txt');
    try {
      fs.writeFileSync(file, text, 'utf8');
      shell.openPath(file).catch(() => {});
    } catch (error) {
      this.log('WARN', `Could not write hardware report: ${error.message}`);
    }
    return file;
  }

  // ---------------------------------------------------------------- menu hook

  popupAppMenu(win) {
    if (this.tray) this.tray.popup(win);
  }

  // --------------------------------------------------------------- persistence

  scheduleSave() {
    if (this.shuttingDown) return;
    this.active.hasSavedState = true;
    clearTimeout(this.saveTimer);
    const snapshot = clone(this.active);
    this.saveTimer = setTimeout(() => {
      if (this.shuttingDown) return;
      try {
        this.layouts.saveActive(snapshot);
      } catch (error) {
        this.log('ERROR', 'Failed to save active layout', error);
      }
    }, 500);
  }

  flush() {
    clearTimeout(this.saveTimer);
    this.saveTimer = null;
    try {
      this.layouts.saveActive(clone(this.active));
      this.store.saveSettings();
    } catch (error) {
      this.log('ERROR', 'Failed to flush layout', error);
    }
  }

  shutdown() {
    if (this.shuttingDown) return;
    this.shuttingDown = true;
    this.flush();
    clearInterval(this.weatherTimer);
    clearTimeout(this.arrangeTimer);
    try {
      this.metrics.stop();
    } catch {
      /* ignore */
    }
    this.floating.stop();
    this.sidebar.destroy();
    this.dialogs.closeAll();
  }

  /** Set by index.js so the tray can exit through one teardown path. */
  requestQuit() {
    if (typeof this.quitHandler === 'function') this.quitHandler();
    else app.quit();
  }
}

module.exports = { Controller };
