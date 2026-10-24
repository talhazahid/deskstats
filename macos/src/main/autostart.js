'use strict';

const { app } = require('electron');

/**
 * "Start with Windows" becomes "Launch at login" on macOS. Electron wraps the
 * Login Items API, so no helper or launch agent file is needed.
 */
class AutoStartService {
  constructor(store) {
    this.store = store;
  }

  isEnabled() {
    try {
      return Boolean(app.getLoginItemSettings().openAtLogin);
    } catch {
      return Boolean(this.store.settings.startWithWindows);
    }
  }

  apply() {
    const enabled = Boolean(this.store.settings.startWithWindows);
    // Calling setLoginItemSettings(false) when the item is already absent makes
    // macOS log an "Operation not permitted" error for unsigned dev builds.
    if (!enabled && !this.isEnabled()) return enabled;
    try {
      // openAsHidden keeps the menu-bar app quiet at login where macOS honours it.
      app.setLoginItemSettings({ openAtLogin: enabled, openAsHidden: true });
    } catch (error) {
      this.store.log('WARN', `Could not update login item: ${error.message}`);
    }
    return enabled;
  }
}

module.exports = { AutoStartService };
