'use strict';

const { app, Menu, dialog } = require('electron');
const { Controller } = require('./controller');
const { TrayMenu } = require('./tray');
const { registerIpc } = require('./ipc');

// A single instance avoids two metrics collectors fighting over the same data
// directory and two trays appearing in the menu bar.
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  bootstrap();
}

let controller = null;
let tray = null;
let quitting = false;

function requestQuit() {
  if (quitting) return;
  quitting = true;
  try {
    controller?.shutdown();
  } catch (error) {
    console.error('Shutdown failed:', error);
  }
  try {
    tray?.destroy();
  } catch {
    /* ignore */
  }
  app.quit();
}

function buildAppMenu() {
  const template = [
    {
      label: 'DeskStats',
      submenu: [
        { label: 'About DeskStats', click: () => controller?.dialogs.about() },
        { type: 'separator' },
        { label: 'App settings…', accelerator: 'CommandOrControl+,', click: () => controller?.dialogs.appSettings() },
        { label: 'Check for updates…', click: () => controller?.checkUpdates(true) },
        { type: 'separator' },
        { role: 'hide' },
        { role: 'hideOthers' },
        { role: 'unhide' },
        { type: 'separator' },
        { label: 'Quit DeskStats', accelerator: 'CommandOrControl+Q', click: requestQuit },
      ],
    },
    { role: 'editMenu' },
    {
      label: 'View',
      submenu: [
        { label: 'Show Sidebar', click: () => controller?.sidebar.show() },
        { label: 'Switch to Widgets', click: () => controller?.switchToWidgets() },
        { label: 'Switch to Sidebar', click: () => controller?.switchToSidebar() },
        { type: 'separator' },
        { label: 'Toggle Theme', click: () => controller?.setTheme(controller.theme === 'dark' ? 'light' : 'dark') },
        { type: 'separator' },
        { role: 'togglefullscreen' },
        { role: 'reload' },
        { role: 'toggleDevTools' },
      ],
    },
    { role: 'windowMenu' },
  ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

async function bootstrap() {
  app.setName('DeskStats');
  if (process.platform === 'darwin' && app.dock) {
    // Tray-first utility: no Dock icon, but keep a normal application menu for
    // copy/paste and Quit shortcuts while a dialog is focused.
    app.dock.hide();
  }

  app.on('second-instance', () => {
    if (!controller) return;
    if (controller.dockedItems().length) controller.sidebar.show();
    else controller.dialogs.appSettings();
  });

  app.on('window-all-closed', () => {
    // The tray app stays alive without windows (e.g. every widget hidden).
  });

  app.on('before-quit', (event) => {
    if (quitting) return;
    event.preventDefault();
    requestQuit();
  });

  app.on('will-quit', () => {
    try {
      tray?.destroy();
    } catch {
      /* ignore */
    }
  });

  await app.whenReady();

  controller = new Controller();
  controller.quitHandler = requestQuit;

  tray = new TrayMenu(controller);
  controller.tray = tray;
  tray.create();

  // Dev-only escape hatch for driving the main process over the Node inspector.
  if (process.env.DESKSTATS_DEBUG) {
    global.__deskstats = { controller, tray, requestQuit };
  }

  // IPC must exist before any renderer loads.
  registerIpc(controller);
  buildAppMenu();

  try {
    await controller.initialize();
  } catch (error) {
    controller.log('ERROR', 'Startup failed', error);
    dialog.showErrorBox('DeskStats could not start', error.stack || error.message);
    requestQuit();
    return;
  }

  const anyVisible = controller.items.some((item) => !item.placement.hidden);
  if (!anyVisible) {
    tray.showHint('DeskStats is running in the menu bar. Add a widget from its menu.');
  }

  if (controller.store.settings.automaticUpdates) {
    setTimeout(() => controller.checkUpdates(false), 8000);
  }
}

process.on('uncaughtException', (error) => {
  console.error('Uncaught exception:', error);
  try {
    controller?.log('ERROR', 'Uncaught exception', error);
  } catch {
    /* ignore */
  }
});

process.on('unhandledRejection', (error) => {
  console.error('Unhandled rejection:', error);
  try {
    controller?.log('ERROR', 'Unhandled rejection', error);
  } catch {
    /* ignore */
  }
});
