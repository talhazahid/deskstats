'use strict';

const fs = require('node:fs');
const fsp = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');

function trashDirs() {
  const dirs = [path.join(os.homedir(), '.Trash')];
  try {
    for (const volume of fs.readdirSync('/Volumes')) {
      const dir = path.join('/Volumes', volume, '.Trashes', String(process.getuid()));
      if (fs.existsSync(dir)) dirs.push(dir);
    }
  } catch {
    /* best effort */
  }
  return dirs;
}

async function sizeOf(entry, depth = 0) {
  if (depth > 24) return 0;
  let stat;
  try {
    stat = await fsp.lstat(entry);
  } catch {
    return 0;
  }
  if (stat.isSymbolicLink()) return stat.size;
  if (!stat.isDirectory()) return stat.size;
  let total = stat.size;
  let names;
  try {
    names = await fsp.readdir(entry);
  } catch {
    return total;
  }
  // Keep the event loop responsive on huge trash folders.
  for (const name of names) {
    total += await sizeOf(path.join(entry, name), depth + 1);
  }
  return total;
}

class TrashService {
  constructor() {
    this.cache = { count: 0, size: 0, updated: 0 };
    this.pending = null;
  }

  async info(force = false) {
    if (!force && Date.now() - this.cache.updated < 3000) return this.cache;
    if (this.pending) return this.pending;
    this.pending = (async () => {
      let count = 0;
      let size = 0;
      for (const dir of trashDirs()) {
        let names = [];
        try {
          names = await fsp.readdir(dir);
        } catch {
          continue;
        }
        for (const name of names) {
          if (name === '.DS_Store') continue;
          count += 1;
          size += await sizeOf(path.join(dir, name));
        }
      }
      this.cache = { count, size, updated: Date.now() };
      return this.cache;
    })().finally(() => {
      this.pending = null;
    });
    return this.pending;
  }

  async empty() {
    for (const dir of trashDirs()) {
      let names = [];
      try {
        names = await fsp.readdir(dir);
      } catch {
        continue;
      }
      for (const name of names) {
        if (name === '.DS_Store') continue;
        try {
          await fsp.rm(path.join(dir, name), { recursive: true, force: true, maxRetries: 2 });
        } catch {
          /* keep going: one locked item must not stop the rest */
        }
      }
    }
    return this.info(true);
  }
}

module.exports = { TrashService };
