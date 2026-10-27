'use strict';

const { execFile } = require('node:child_process');
const fs = require('node:fs');
const crypto = require('node:crypto');
const { resourcePath } = require('./macmon');

/**
 * Now Playing via the bundled nowplaying-cli (MIT). Reads the system-wide
 * MediaRemote session without extra permissions on macOS.
 */
class MediaService {
  constructor() {
    this.bin = resourcePath('nowplaying-cli', '2.1.0', 'bin', 'nowplaying-cli');
    this.timer = null;
    this.busy = false;
    this.meta = {
      hasSession: false,
      title: 'Nothing playing',
      artist: '',
      appName: '',
      status: '',
      thumbnail: null,
      updated: 0,
    };
    this.artHash = '';
    this.listeners = new Set();
    this.lastGood = 0;
  }

  start() {
    if (this.timer) return;
    this.timer = setInterval(() => this.poll(), 2000);
    setTimeout(() => this.poll(), 300);
  }

  stop() {
    clearInterval(this.timer);
    this.timer = null;
  }

  onUpdate(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  emit(artChanged) {
    for (const listener of this.listeners) {
      try {
        listener(this.meta, artChanged);
      } catch {
        /* ignore */
      }
    }
  }

  poll() {
    if (this.busy) return;
    if (!fs.existsSync(this.bin)) {
      this.setIdle('Now Playing is unavailable (helper missing)');
      return;
    }
    this.busy = true;
    execFile(
      this.bin,
      ['get', '--json', 'title', 'artist', 'album', 'artworkData', 'artworkMIMEType', 'playbackRate', 'elapsedTime', 'duration', 'isMusicApp'],
      { timeout: 6000, maxBuffer: 16 * 1024 * 1024 },
      (err, stdout) => {
        this.busy = false;
        if (err) {
          // A non-zero exit means no active session on some macOS versions.
          this.setIdle('');
          return;
        }
        let data;
        try {
          data = JSON.parse(stdout || '{}');
        } catch {
          this.setIdle('');
          return;
        }
        const title = typeof data.title === 'string' ? data.title : '';
        if (!title) {
          this.setIdle('');
          return;
        }
        const artwork = typeof data.artworkData === 'string' ? data.artworkData : '';
        let thumbnail = null;
        if (artwork.length > 64) {
          thumbnail = artwork.startsWith('data:')
            ? artwork
            : `data:${data.artworkMIMEType || 'image/jpeg'};base64,${artwork}`;
        }
        const hash = thumbnail ? crypto.createHash('sha1').update(thumbnail).digest('hex') : '';
        const artChanged = hash !== this.artHash;
        this.artHash = hash;

        const appName = data.isMusicApp ? 'Music' : 'Now Playing';
        const titleChanged =
          this.meta.title !== title ||
          this.meta.artist !== (data.artist || '') ||
          this.meta.appName !== appName ||
          this.meta.status !== (Number(data.playbackRate) > 0 ? 'Playing' : 'Paused');

        this.meta = {
          hasSession: true,
          title,
          artist: typeof data.artist === 'string' ? data.artist : '',
          album: typeof data.album === 'string' ? data.album : '',
          appName,
          status: Number(data.playbackRate) > 0 ? 'Playing' : 'Paused',
          elapsed: Number(data.elapsedTime) || 0,
          duration: Number(data.duration) || 0,
          thumbnail,
          updated: Date.now(),
        };
        this.lastGood = Date.now();
        if (titleChanged || artChanged) this.emit(artChanged);
      },
    );
  }

  setIdle(reason) {
    const unchanged = !this.meta.hasSession && this.meta.title === 'Nothing playing';
    if (unchanged) return;
    this.meta = {
      hasSession: false,
      title: 'Nothing playing',
      artist: '',
      album: '',
      appName: reason || '',
      status: '',
      thumbnail: null,
      updated: Date.now(),
    };
    this.artHash = '';
    this.emit(true);
  }

  metaForSnapshot() {
    return {
      hasSession: this.meta.hasSession,
      title: this.meta.title,
      artist: this.meta.artist,
      album: this.meta.album,
      appName: this.meta.appName,
      status: this.meta.status,
      elapsed: this.meta.elapsed,
      duration: this.meta.duration,
    };
  }
}

module.exports = { MediaService };
