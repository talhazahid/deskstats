'use strict';

const { ipcMain, BrowserWindow } = require('electron');

/**
 * Single IPC entry point. The preload exposes `invoke(channel, payload)` and the
 * dispatcher below. Widget/sidebar/dialog pages therefore share one tiny API
 * surface instead of dozens of bridge methods.
 */
function registerIpc(controller) {
  const handlers = {
    // ---- floating widgets -------------------------------------------------
    'widget:bootstrap': (payload) => controller.bootstrapWidget(payload?.instanceId),
    'widget:height': (payload) => {
      controller.floating.setMeasuredHeight(payload?.instanceId, payload?.height);
    },
    'widget:drag-start': (payload) => controller.floating.startDrag(payload?.instanceId),
    'widget:drag-end': (payload) => controller.floating.endDrag(payload?.instanceId),
    'widget:resize-start': (payload) => controller.floating.startResize(payload?.instanceId, payload),
    'widget:resize-end': (payload) => {
      controller.floating.endResize(payload?.instanceId);
      controller.endResizeMode(payload?.instanceId);
    },
    'widget:resize-cancel': (payload) => {
      controller.floating.endResize(payload?.instanceId);
      controller.endResizeMode(payload?.instanceId);
    },
    'widget:context-menu': (payload) => controller.floating.showContextMenu(payload?.instanceId),
    'widget:click-through-unlock': (payload) => controller.setClickThroughForInstance(payload?.instanceId, false),
    'widget:settings': (payload) => Boolean(controller.openWidgetSettings(payload?.instanceId)),
    'widget:dock': (payload) => controller.dockInstance(payload?.instanceId),
    'widget:detach': (payload) => controller.detachInstance(payload?.instanceId),
    'widget:remove': (payload) => controller.removeInstance(payload?.instanceId),
    'widget:set-large': (payload) => controller.setLargeForInstance(payload?.instanceId, payload?.large),
    'widget:set-theme': (payload) => controller.setThemeForInstance(payload?.instanceId, payload?.theme),
    'widget:set-layer': (payload) => controller.setLayerForInstance(payload?.instanceId, payload?.layer),
    'widget:set-opacity': (payload) => controller.setOpacityForInstance(payload?.instanceId, payload?.opacity),

    // ---- sidebar ----------------------------------------------------------
    'sidebar:get': () => controller.sidebar.state(),
    'sidebar:card-menu': (payload) => controller.showSidebarCardMenu(payload?.instanceId),
    'sidebar:app-menu': () => controller.popupAppMenu(controller.sidebar.win),
    'sidebar:toggle': () => controller.sidebar.toggle(),

    // ---- widget settings dialog ------------------------------------------
    'widget-settings:data': (payload) => controller.widgetDialogData(payload?.instanceId),
    'widget-settings:save': (payload, event) => {
      const result = controller.saveWidgetSettings(payload?.instanceId, payload);
      if (result?.ok) controller.dialogs.markWidgetSettingsSaved(event.sender);
      return result;
    },
    'widget-settings:preview': (payload, event) => {
      controller.applyAppearanceState(payload?.instanceId, payload?.appearance, { persist: false });
      controller.dialogs.markWidgetSettingsPreview(event.sender);
      return { ok: true };
    },
    'widget-settings:refresh-weather': (payload) => {
      controller.refreshWeatherFor(payload?.instanceId);
      return { ok: true };
    },

    // ---- app settings dialog ---------------------------------------------
    'app-settings:data': () => controller.appSettingsData(),
    'app-settings:save': (payload) => controller.saveAppSettings(payload),
    'app-settings:identify': (payload) => controller.identifyScreens(payload?.ids),
    'app-settings:check-updates': () => controller.checkUpdates(true),

    // ---- shared dialogs ---------------------------------------------------
    'layout:save-as': (payload) => controller.saveCurrentAs(payload?.name),
    'city:search': (payload) => controller.searchCities(payload?.query),
    'about:data': () => controller.aboutData(),
    'dialog:theme': () => controller.dialogTheme(),
    'dialog:close': (_payload, event) => controller.dialogs.closeForWebContents(event.sender),
    'updates:check': (payload) => controller.checkUpdates(payload?.manual !== false),

    // ---- rescue pill ------------------------------------------------------
    'rescue:unlock': (payload) => controller.setClickThroughForInstance(payload?.instanceId, false),
    'rescue:menu': (payload) => controller.floating.showContextMenu(payload?.instanceId),

    // ---- widgets that act on the system ----------------------------------
    'trash:empty': () => controller.emptyTrash(),

    // ---- app --------------------------------------------------------------
    'app:quit': () => controller.requestQuit(),
    'app:show-sidebar': () => controller.sidebar.show(),
    'app:open-settings': () => Boolean(controller.dialogs.appSettings()),
    'app:open-about': () => Boolean(controller.dialogs.about()),
    'app:open-data-folder': () => {
      const { shell } = require('electron');
      shell.openPath(controller.store.dataDir).catch(() => {});
    },
    'app:open-report': () => controller.hardwareReport(),
    'app:open-external': (payload) => {
      const url = String(payload?.url || '');
      if (!/^https?:\/\//i.test(url)) return false;
      const { shell } = require('electron');
      shell.openExternal(url).catch(() => {});
      return true;
    },
  };

  ipcMain.handle('deskstats:invoke', async (event, message) => {
    const channel = message?.channel;
    const handler = handlers[channel];
    if (!handler) {
      controller.log('WARN', `Unknown IPC channel '${channel}'`);
      return { __error: `Unknown channel: ${channel}` };
    }
    try {
      const value = await handler(message.payload || {}, event);
      return value === undefined ? null : value;
    } catch (error) {
      controller.log('ERROR', `IPC '${channel}' failed: ${error.message}`, error);
      return { __error: error.message };
    }
  });

  return () => {
    ipcMain.removeHandler('deskstats:invoke');
  };
}

module.exports = { registerIpc };
