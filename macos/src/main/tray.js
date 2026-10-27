'use strict';

const { Menu, Tray, Notification } = require('electron');
const defs = require('./widget-defs');
const { createTrayIcon } = require('./trayicon');

/**
 * Menu-bar controller. The menu is rebuilt from live state on every open, so
 * hidden widgets and checkbox states are never stale (upstream TrayService).
 */
class TrayMenu {
  constructor(controller) {
    this.controller = controller;
    this.tray = null;
    this.clickTimer = null;
    this.menu = null;
  }

  create() {
    if (this.tray) return this.tray;
    this.tray = new Tray(createTrayIcon());
    this.tray.setToolTip('DeskStats');
    this.tray.setIgnoreDoubleClickEvents(false);

    this.tray.on('click', () => {
      clearTimeout(this.clickTimer);
      this.clickTimer = setTimeout(() => this.popup(), 180);
    });
    this.tray.on('right-click', () => this.popup());
    this.tray.on('double-click', () => {
      clearTimeout(this.clickTimer);
      const c = this.controller;
      if (!c.items.some((item) => item.placement.docked && !item.placement.hidden)) {
        this.showHint('No widgets are docked in the sidebar. Use Add widget or Switch to Sidebar.');
        return;
      }
      c.sidebar.toggle();
    });

    return this.tray;
  }

  showHint(message) {
    try {
      new Notification({ title: 'DeskStats', body: message, silent: true }).show();
    } catch {
      /* notifications may be unavailable */
    }
  }

  popup(win) {
    const menu = Menu.buildFromTemplate(this.template());
    this.menu = menu;
    menu.popup(win ? { window: win } : {});
  }

  template() {
    const c = this.controller;
    const settings = c.store.settings;

    const addWidget = {
      label: 'Add widget',
      submenu: [],
    };
    for (const category of defs.CATEGORY_ORDER) {
      const widgets = defs.WIDGETS.filter((widget) => widget.category === category).sort((a, b) =>
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
      );
      if (!widgets.length) continue;
      addWidget.submenu.push({
        label: defs.CATEGORY_LABELS[category] || category,
        submenu: widgets.map((widget) => ({
          label: widget.name,
          type: 'checkbox',
          checked: c.isWidgetShown(widget.id),
          toolTip: widget.description,
          click: () => c.toggleWidget(widget.id),
        })),
      });
    }
    addWidget.submenu.push({ type: 'separator' });
    addWidget.submenu.push({ label: 'Show all hardware widgets', click: () => c.showAllHardwareWidgets() });

    const layouts = {
      label: 'Layouts',
      submenu: c.layouts.names.map((name) => ({
        label: name,
        type: 'checkbox',
        checked: name.toLowerCase() === String(c.active.name).toLowerCase(),
        click: () => c.activateLayout(name),
      })),
    };
    layouts.submenu.push({ type: 'separator' });
    layouts.submenu.push({ label: 'Save current as…', click: () => c.dialogs.saveLayoutAs() });
    if (defs.isPreset(c.active.name)) {
      layouts.submenu.push({
        label: `Reset '${c.active.name}' to preset`,
        click: () => c.resetToPreset(),
      });
    }

    const items = {
      label: 'Widgets',
      enabled: c.items.length > 0,
      submenu: c.items.map((item) => {
        const title = c.titleFor(item);
        const submenu = [];
        if (item.placement.hidden) {
          submenu.push({ label: 'Show', click: () => c.showInstance(item.instanceId) });
        } else {
          submenu.push({ label: 'Hide', click: () => c.hideInstance(item.instanceId) });
          if (item.placement.docked) {
            submenu.push({ label: 'Detach window', click: () => c.detachInstance(item.instanceId) });
          } else {
            submenu.push({ label: 'Dock to sidebar', click: () => c.dockInstance(item.instanceId) });
            submenu.push({
              label: 'Click-through',
              type: 'checkbox',
              checked: Boolean(item.placement.clickThrough),
              click: (menuItem) => c.setClickThroughForInstance(item.instanceId, menuItem.checked),
            });
          }
          submenu.push({ label: 'Settings…', click: () => c.openWidgetSettings(item.instanceId) });
        }
        submenu.push({ type: 'separator' });
        submenu.push({ label: 'Remove', click: () => c.removeInstance(item.instanceId) });
        return { label: title, submenu };
      }),
    };

    return [
      addWidget,
      layouts,
      { type: 'separator' },
      {
        label: 'Switch to Widgets',
        type: 'checkbox',
        checked: !c.isSidebarMode,
        click: () => c.switchToWidgets(),
      },
      {
        label: 'Switch to Sidebar',
        type: 'checkbox',
        checked: c.isSidebarMode,
        click: () => c.switchToSidebar(),
      },
      { type: 'separator' },
      {
        label: 'Theme',
        submenu: [
          {
            label: 'Dark',
            type: 'checkbox',
            checked: settings.theme !== 'light',
            click: () => c.setTheme('dark'),
          },
          {
            label: 'Light',
            type: 'checkbox',
            checked: settings.theme === 'light',
            click: () => c.setTheme('light'),
          },
        ],
      },
      items,
      {
        label: 'Reset widget positions',
        enabled: c.items.some((item) => !item.placement.docked && !item.placement.hidden),
        click: () => c.resetFloatPositions(),
      },
      { type: 'separator' },
      { label: 'Hardware report…', click: () => c.hardwareReport() },
      { label: 'App settings…', click: () => c.dialogs.appSettings() },
      { label: 'Check for updates…', click: () => c.checkUpdates(true) },
      { label: 'About DeskStats', click: () => c.dialogs.about() },
      { type: 'separator' },
      { label: 'Exit', click: () => c.requestQuit() },
    ];
  }

  destroy() {
    clearTimeout(this.clickTimer);
    if (this.tray) this.tray.destroy();
    this.tray = null;
  }
}

module.exports = { TrayMenu };
