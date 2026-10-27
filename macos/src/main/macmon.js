'use strict';

const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const { app } = require('electron');

function resourcePath(...parts) {
  // With asar disabled the bundle lives in Contents/Resources/app, so the
  // project's own resources/ folder sits next to src/ in both dev and packaged
  // builds. (process.resourcesPath points one level above that and would miss
  // macmon/nowplaying in the packaged app.)
  return path.join(app.getAppPath(), 'resources', ...parts);
}

/**
 * Wrapper around the bundled macmon helper (MIT, https://github.com/vladkens/macmon).
 * It streams one JSON object per line with CPU/GPU power, per-core load, clocks,
 * temperatures and memory — all without sudo on Apple Silicon.
 */
class Macmon {
  constructor(intervalMs = 1000) {
    this.intervalMs = intervalMs;
    this.bin = resourcePath('macmon-bin');
    this.proc = null;
    this.buffer = '';
    this.latest = null;
    this.available = false;
    this.lastError = '';
    this.restartTimer = null;
    this.stopped = false;
  }

  start() {
    this.stopped = false;
    if (this.proc) return;
    if (!fs.existsSync(this.bin)) {
      this.lastError = `Sensor helper not found at ${this.bin}`;
      this.available = false;
      return;
    }
    try {
      fs.chmodSync(this.bin, 0o755);
    } catch {
      /* already executable */
    }
    const args = ['pipe', '-i', String(Math.max(250, Math.round(this.intervalMs)))];
    const proc = spawn(this.bin, args, { stdio: ['ignore', 'pipe', 'pipe'] });
    this.proc = proc;
    proc.stdout.setEncoding('utf8');
    proc.stdout.on('data', (chunk) => this.onData(chunk));
    proc.stderr.setEncoding('utf8');
    proc.stderr.on('data', (chunk) => {
      this.lastError = chunk.trim().split('\n').slice(-1)[0] || this.lastError;
    });
    proc.on('error', (err) => {
      this.lastError = err.message;
      this.available = false;
      this.proc = null;
    });
    proc.on('close', () => {
      this.proc = null;
      this.available = false;
      if (!this.stopped) {
        clearTimeout(this.restartTimer);
        this.restartTimer = setTimeout(() => this.start(), 5000);
      }
    });
  }

  onData(chunk) {
    this.buffer += chunk;
    let index;
    while ((index = this.buffer.indexOf('\n')) >= 0) {
      const line = this.buffer.slice(0, index).trim();
      this.buffer = this.buffer.slice(index + 1);
      if (!line) continue;
      try {
        this.latest = JSON.parse(line);
        this.available = true;
        this.lastError = '';
      } catch {
        /* partial / non-JSON line */
      }
    }
  }

  setIntervalMs(ms) {
    const next = Math.max(250, Math.round(ms));
    if (next === this.intervalMs) return;
    this.intervalMs = next;
    this.stop();
    this.start();
  }

  stop() {
    this.stopped = true;
    clearTimeout(this.restartTimer);
    if (this.proc) {
      try {
        this.proc.kill('SIGTERM');
      } catch {
        /* ignore */
      }
      this.proc = null;
    }
    this.available = false;
  }
}

module.exports = { Macmon, resourcePath };
