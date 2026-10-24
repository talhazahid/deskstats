'use strict';

const { shell, dialog, app } = require('electron');

const RELEASES_API = 'https://api.github.com/repos/talhazahid/deskstats/releases/latest';
const RELEASES_PAGE = 'https://github.com/talhazahid/deskstats/releases/latest';

function compareVersions(a, b) {
  const parse = (v) => String(v || '0')
    .replace(/^v/i, '')
    .split(/[.+-]/)
    .map((part) => {
      const n = parseInt(part, 10);
      return Number.isFinite(n) ? n : 0;
    });
  const left = parse(a);
  const right = parse(b);
  const length = Math.max(left.length, right.length);
  for (let i = 0; i < length; i += 1) {
    const x = left[i] || 0;
    const y = right[i] || 0;
    if (x > y) return 1;
    if (x < y) return -1;
  }
  return 0;
}

/**
 * Update check. The macOS port is distributed outside the Windows installer, so
 * this checks the upstream release feed, reports a newer version and opens the
 * release page. Automatic checks stay silent unless something is available.
 */
class UpdateService {
  constructor(store) {
    this.store = store;
    this.checking = false;
  }

  async check(manual = false) {
    if (this.checking) return null;
    this.checking = true;
    const current = app.getVersion();
    try {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), 10_000);
      let response;
      try {
        response = await fetch(RELEASES_API, {
          signal: controller.signal,
          headers: {
            Accept: 'application/vnd.github+json',
            'User-Agent': 'DeskStats-macOS',
          },
        });
      } finally {
        clearTimeout(timer);
      }

      if (!response.ok) throw new Error(`GitHub returned ${response.status}`);
      const release = await response.json();
      const latest = String(release.tag_name || release.name || '').replace(/^v/i, '');
      if (latest && compareVersions(latest, current) > 0) {
        // Automatic checks stay silent (a modal at launch would block the app);
        // only a manual "Check for updates…" interrupts with a dialog.
        if (!manual) {
          this.store.log('INFO', `Update available: ${latest} (running ${current})`);
          return { status: 'available', latest, current, releaseUrl: release.html_url || RELEASES_PAGE };
        }
        const notes = String(release.body || '').trim();
        const detail = notes
          ? `${notes.slice(0, 900)}${notes.length > 900 ? '…' : ''}\n\nOpen the release page to download it.`
          : 'Open the release page to download the latest version.';
        const result = await dialog.showMessageBox({
          type: 'info',
          title: 'DeskStats update',
          message: `DeskStats ${latest} is available (you have ${current}).`,
          detail,
          buttons: ['Open release page', 'Later'],
          defaultId: 0,
          cancelId: 1,
          noLink: true,
        });
        if (result.response === 0) {
          shell.openExternal(release.html_url || RELEASES_PAGE).catch(() => {});
        }
        return { status: 'available', latest, current };
      }

      if (manual) {
        await dialog.showMessageBox({
          type: 'info',
          title: 'DeskStats updates',
          message: 'You’re up to date.',
          detail: `DeskStats ${current} is the latest available release.`,
          buttons: ['OK'],
        });
      }
      return { status: 'current', latest: latest || current, current };
    } catch (error) {
      this.store.log('WARN', `Update check failed: ${error.message}`);
      if (manual) {
        await dialog.showMessageBox({
          type: 'warning',
          title: 'DeskStats updates',
          message: 'Could not check for updates.',
          detail: 'Check your internet connection and try again.',
          buttons: ['OK'],
        });
      }
      return { status: 'error', message: error.message };
    } finally {
      this.checking = false;
    }
  }
}

module.exports = { UpdateService, compareVersions };
