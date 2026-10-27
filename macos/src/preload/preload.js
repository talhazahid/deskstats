'use strict';

const { contextBridge, ipcRenderer } = require('electron');

/**
 * Renderer bridge. One request channel plus one broadcast channel keeps the
 * preload tiny while still letting every window (widgets, sidebar, dialogs)
 * share the same API.
 */
const listeners = new Map();

ipcRenderer.on('deskstats:event', (_event, message) => {
  if (!message || typeof message.name !== 'string') return;
  for (const handler of listeners.get(message.name) || []) {
    try {
      handler(message.data);
    } catch (error) {
      console.error(`Renderer listener for ${message.name} failed:`, error);
    }
  }
});

contextBridge.exposeInMainWorld('deskstats', {
  invoke(channel, payload) {
    return ipcRenderer.invoke('deskstats:invoke', { channel, payload });
  },
  on(name, handler) {
    if (typeof handler !== 'function') return () => {};
    if (!listeners.has(name)) listeners.set(name, new Set());
    listeners.get(name).add(handler);
    return () => listeners.get(name)?.delete(handler);
  },
  platform: process.platform,
});
