'use strict';

const os = require('node:os');
const fs = require('node:fs');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const { Macmon } = require('./macmon');
const { MediaService } = require('./media');
const { TrashService } = require('./trash');
const dvfs = require('./dvfs');

const execFileAsync = promisify(execFile);

async function run(command, args, timeout = 2500) {
  try {
    const { stdout } = await execFileAsync(command, args, { timeout, maxBuffer: 8 * 1024 * 1024 });
    return stdout;
  } catch {
    return '';
  }
}

function toNumber(value) {
  // macmon emits null for sensors the current chip does not expose (for example
  // gpu_temp_avg on some M-series parts). Number(null) is 0, which would show a
  // fake reading, so treat empty values as missing.
  if (value === null || value === undefined || value === '' || typeof value === 'boolean') return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

class MetricsService {
  constructor(store) {
    this.store = store;
    this.intervalMs = Math.max(200, Math.min(60000, Number(store.settings.pollIntervalMs) || 1000));
    this.timer = null;
    this.collecting = false;
    this.latest = null;
    this.listeners = new Set();
    this.macmon = new Macmon(this.intervalMs);
    this.media = new MediaService();
    this.trash = new TrashService();
    this.needsTrash = false;
    this.load = {
      gpu: store.settings.loadGpu !== false,
      drives: store.settings.loadDrives !== false,
      network: store.settings.loadNetwork !== false,
    };

    this.static = {
      cpuName: os.cpus()?.[0]?.model || 'Apple Silicon',
      totalMemory: os.totalmem(),
      model: '',
      osVersion: os.release(),
      rootVolumeName: 'Macintosh HD',
      ports: new Map(),
    };
    this.staticReady = false;
    this.dvfs = { cpu: [], gpu: null };
    this.memSpec = { mts: null, type: null };

    this.prevCpu = null;
    this.prevNet = new Map();
    this.prevNetAt = 0;
    this.prevDisk = null;
    this.prevDiskAt = 0;
    this.publicIp = 'Looking up…';
    this.publicIpAt = 0;
    this.gpu = { name: this.static.cpuName, util: null, alloc: null };
    this.processCache = { at: 0, rows: [] };
    this.trashCache = { at: 0, info: { count: 0, size: 0 } };
    this.vramTotal = 0;

    this.media.onUpdate((meta, artChanged) => {
      for (const listener of this.listeners) {
        try {
          listener(null, { media: meta, artChanged });
        } catch {
          /* ignore */
        }
      }
    });
  }

  onSnapshot(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  async start() {
    this.macmon.start();
    this.media.start();
    await this.prepareStatic();
    await this.tick();
    this.restartTimer();
  }

  stop() {
    clearInterval(this.timer);
    this.timer = null;
    this.macmon.stop();
    this.media.stop();
  }

  restartTimer() {
    clearInterval(this.timer);
    this.timer = setInterval(() => this.tick(), this.intervalMs);
  }

  setIntervalMs(ms) {
    const next = Math.max(200, Math.min(60000, Math.round(Number(ms) || 1000)));
    if (next === this.intervalMs) return;
    this.intervalMs = next;
    this.macmon.setIntervalMs(next);
    this.restartTimer();
  }

  setNeeds({ trash }) {
    this.needsTrash = Boolean(trash);
  }

  /** App settings → Monitoring toggles (applied live). */
  setLoadFlags(flags = {}) {
    if (flags.gpu != null) this.load.gpu = Boolean(flags.gpu);
    if (flags.drives != null) this.load.drives = Boolean(flags.drives);
    if (flags.network != null) this.load.network = Boolean(flags.network);
  }

  async prepareStatic() {
    if (this.staticReady) return;
    this.staticReady = true;
    const [brand, memsize, model, product, rootName, ports, pmgr, memInfo] = await Promise.all([
      run('sysctl', ['-n', 'machdep.cpu.brand_string']),
      run('sysctl', ['-n', 'hw.memsize']),
      run('sysctl', ['-n', 'hw.model']),
      run('sw_vers', ['-productVersion']),
      run('diskutil', ['info', '/']),
      run('networksetup', ['-listallhardwareports']),
      run('ioreg', ['-a', '-r', '-c', 'AppleARMIODevice', '-n', 'pmgr', '-d', '1'], 4000),
      run('system_profiler', ['SPMemoryDataType'], 6000),
    ]);
    try {
      this.dvfs = dvfs.classify(dvfs.parseVoltageStates(pmgr));
    } catch (err) {
      this.store.log('WARN', `DVFS table parse failed: ${err.message}`);
    }
    const memType = /^\s*Type:\s*(\S+)/m.exec(memInfo)?.[1];
    this.static.cpuName = brand.trim() || this.static.cpuName;
    this.static.gpuName = this.static.cpuName;
    this.vramTotal = Number(memsize.trim()) || os.totalmem();
    this.static.totalMemory = this.vramTotal;
    this.static.model = model.trim();
    this.static.osVersion = product.trim() || os.release();
    const volume = /Volume Name:\s*(.+)/.exec(rootName);
    if (volume) this.static.rootVolumeName = volume[1].trim();
    let currentPort = '';
    for (const line of ports.split('\n')) {
      const port = /^Hardware Port:\s*(.+)$/.exec(line);
      const device = /^Device:\s*(.+)$/.exec(line);
      if (port) currentPort = port[1].trim();
      if (device) this.static.ports.set(device[1].trim(), currentPort);
    }
    this.gpu.name = this.static.gpuName;
    this.memSpec = dvfs.ratedMemory(this.static.cpuName, memType);
    this.store.log(
      'INFO',
      `DVFS: cpu=[${this.dvfs.cpu.map((t) => `${t.key}@${Math.round(t.maxMhz)}`).join(', ')}] gpu=${this.dvfs.gpu ? this.dvfs.gpu.key : 'none'} mem=${this.memSpec.type || '?'} ${this.memSpec.mts || '?'} MT/s`,
    );
  }

  /** Live rail voltages from the DVFS tables + current macmon frequencies. */
  voltages(mac) {
    const out = { cpu: null, perTier: [], gpu: null };
    const tables = this.dvfs.cpu;
    const tiers = mac.tiers || [];
    if (tables.length && tiers.length) {
      tiers.forEach((tier, i) => {
        // Tiers and tables are both ordered low → high performance.
        const table = tiers.length === tables.length ? tables[i] : i === tiers.length - 1 ? tables[tables.length - 1] : tables[Math.min(i, tables.length - 1)];
        const v = dvfs.voltsAt(table, Number(tier.freq_mhz));
        out.perTier.push({ label: tier.label || `T${i}`, mhz: Number(tier.freq_mhz) || 0, volts: v });
      });
      // "Core voltage" is the highest rail any CPU cluster is requesting now.
      const known = out.perTier.map((t) => t.volts).filter((v) => v != null);
      out.cpu = known.length ? Math.max(...known) : null;
    }
    if (this.dvfs.gpu && mac.gpuClockMhz != null) out.gpu = dvfs.voltsAt(this.dvfs.gpu, mac.gpuClockMhz);
    return out;
  }

  cpuLoads() {
    const cpus = os.cpus();
    const now = cpus.map((cpu) => {
      const t = cpu.times;
      return { idle: t.idle, total: t.user + t.nice + t.sys + t.irq + t.idle };
    });
    const perCore = [];
    if (this.prevCpu && this.prevCpu.length === now.length) {
      for (let i = 0; i < now.length; i += 1) {
        const total = now[i].total - this.prevCpu[i].total;
        const idle = now[i].idle - this.prevCpu[i].idle;
        perCore.push(total > 0 ? Math.max(0, Math.min(100, (1 - idle / total) * 100)) : 0);
      }
    } else {
      for (let i = 0; i < now.length; i += 1) perCore.push(0);
    }
    const total = perCore.length ? perCore.reduce((a, b) => a + b, 0) / perCore.length : 0;
    this.prevCpu = now;
    return { load: total, perCore };
  }

  macmonCpu() {
    const data = this.macmon.latest;
    if (!data) return { temperatureC: null, powerW: null, clockMhz: null, fanRpm: null, tiers: [], fans: [] };
    const tiers = Array.isArray(data.cpu_tiers) ? data.cpu_tiers : [];
    let maxClock = 0;
    for (const tier of tiers) {
      for (const core of tier.cores || []) {
        if (Number(core.freq_mhz) > maxClock) maxClock = Number(core.freq_mhz);
      }
      if (Number(tier.freq_mhz) > maxClock) maxClock = Number(tier.freq_mhz);
    }
    const fans = Array.isArray(data.fans) ? data.fans : [];
    const fanValue = (fan) => Number(fan.rpm ?? fan.speed ?? fan.value ?? fan.current ?? 0) || 0;
    const cpuFan = fans.find((fan) => /cpu/i.test(fan.name || fan.label || '')) || fans[0];
    return {
      temperatureC: toNumber(data.temp?.cpu_temp_avg),
      powerW: toNumber(data.cpu_power),
      clockMhz: maxClock || null,
      fanRpm: cpuFan ? fanValue(cpuFan) : null,
      tiers,
      fans,
      gpuTemperatureC: toNumber(data.temp?.gpu_temp_avg),
      gpuClockMhz: toNumber(data.gpu_freq_mhz),
      gpuPowerW: toNumber(data.gpu_power),
      gpuRatio: toNumber(data.gpu_active_ratio),
      memory: data.memory || null,
      ramPowerW: toNumber(data.ram_power),
    };
  }

  async readGpu() {
    const out = await run('ioreg', ['-r', '-d', '1', '-w', '0', '-c', 'IOAccelerator'], 3000);
    const util = /"Device Utilization %"=(\d+)/.exec(out);
    const alloc = /"Alloc system memory"=(\d+)/.exec(out);
    if (util) this.gpu.util = Number(util[1]);
    if (alloc) this.gpu.alloc = Number(alloc[1]);
    return this.gpu;
  }

  async readDiskIo() {
    const out = await run('ioreg', ['-r', '-c', 'IOBlockStorageDriver', '-k', 'Statistics', '-w', '0'], 3000);
    let read = 0;
    let write = 0;
    for (const match of out.matchAll(/"Bytes \(Read\)"=(\d+)/g)) read += Number(match[1]);
    for (const match of out.matchAll(/"Bytes \(Write\)"=(\d+)/g)) write += Number(match[1]);
    const now = Date.now();
    let readRate = 0;
    let writeRate = 0;
    if (this.prevDisk && this.prevDiskAt) {
      const seconds = Math.max(0.2, (now - this.prevDiskAt) / 1000);
      readRate = Math.max(0, (read - this.prevDisk.read) / seconds);
      writeRate = Math.max(0, (write - this.prevDisk.write) / seconds);
    }
    this.prevDisk = { read, write };
    this.prevDiskAt = now;
    return { read: readRate, write: writeRate };
  }

  async readDisks(io) {
    const out = await run('df', ['-kP'], 3000);
    const drives = [];
    const seen = new Set();
    for (const line of out.split('\n').slice(1)) {
      const match = /^(\S+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)%\s+(.+)$/.exec(line.trim());
      if (!match) continue;
      const [, filesystem, blocksText, , availText, , mount] = match;
      if (!filesystem.startsWith('/dev/')) continue;
      if (mount.startsWith('/System/Volumes/')) continue;
      if (mount.startsWith('/private/var/vm')) continue;
      if (seen.has(mount)) continue;
      seen.add(mount);
      const total = Number(blocksText) * 1024;
      const free = Number(availText) * 1024;
      if (!total) continue;
      const used = Math.max(0, total - free);
      let name;
      if (mount === '/') name = this.static.rootVolumeName;
      else name = mount.split('/').filter(Boolean).pop() || mount;
      drives.push({
        name,
        mount,
        totalBytes: total,
        freeBytes: free,
        usedBytes: used,
        usedPercent: total > 0 ? (used / total) * 100 : 0,
        readBytesPerSec: drives.length === 0 ? io.read : 0,
        writeBytesPerSec: drives.length === 0 ? io.write : 0,
      });
    }
    // Spread the physical disk rate across every mounted volume so summing shown
    // drives still reports the real total.
    const count = Math.max(1, drives.length);
    for (const drive of drives) {
      drive.readBytesPerSec = io.read / count;
      drive.writeBytesPerSec = io.write / count;
    }
    return drives;
  }

  async readNetwork() {
    const [out, ifaces] = await Promise.all([run('netstat', ['-ib'], 3000), Promise.resolve(os.networkInterfaces())]);
    const now = Date.now();
    const elapsed = this.prevNetAt ? Math.max(0.2, (now - this.prevNetAt) / 1000) : 0;
    const counters = new Map();
    for (const line of out.split('\n')) {
      const tokens = line.trim().split(/\s+/);
      if (tokens.length < 10) continue;
      const [name, , link] = tokens;
      if (!link.startsWith('<Link#')) continue;
      const ibytes = Number(tokens[6]);
      const obytes = Number(tokens[9]);
      if (!Number.isFinite(ibytes) || !Number.isFinite(obytes)) continue;
      counters.set(name, { ibytes, obytes });
    }

    const skip = /^(lo|gif|stf|anpi|awdl|llw|bridge)/i;
    const adapters = [];
    for (const [name, counter] of counters) {
      if (skip.test(name)) continue;
      const previous = this.prevNet.get(name);
      let down = 0;
      let up = 0;
      if (previous && elapsed > 0) {
        down = Math.max(0, (counter.ibytes - previous.ibytes) / elapsed);
        up = Math.max(0, (counter.obytes - previous.obytes) / elapsed);
      }
      const addresses = ifaces[name] || [];
      const ipv4 = addresses.find((a) => a.family === 'IPv4' && !a.internal)?.address || null;
      const friendly = this.static.ports.get(name);
      const display = friendly ? `${friendly} (${name})` : name;
      adapters.push({
        name: display,
        interfaceName: name,
        isUp: Boolean(ipv4),
        ipV4: ipv4,
        downBytesPerSec: down,
        upBytesPerSec: up,
      });
    }
    this.prevNet = counters;
    this.prevNetAt = now;

    // The default route is always listed first so automatic selection is stable.
    let priority = '';
    const route = await run('route', ['-n', 'get', 'default'], 1500);
    const routeMatch = /interface:\s*(\S+)/.exec(route);
    if (routeMatch) priority = routeMatch[1];
    adapters.sort((a, b) => {
      if (a.interfaceName === priority) return -1;
      if (b.interfaceName === priority) return 1;
      return (b.ipV4 ? 1 : 0) - (a.ipV4 ? 1 : 0) || b.downBytesPerSec + b.upBytesPerSec - (a.downBytesPerSec + a.upBytesPerSec);
    });
    return adapters;
  }

  async readProcesses() {
    if (Date.now() - this.processCache.at < 2800) return this.processCache.rows;
    const out = await run('ps', ['-Ao', 'rss=,comm='], 3000);
    const totals = new Map();
    for (const line of out.split('\n')) {
      const match = /^\s*(\d+)\s+(.+)$/.exec(line);
      if (!match) continue;
      const rss = Number(match[1]) * 1024;
      const comm = match[2].trim();
      const name = comm.split('/').filter(Boolean).pop() || comm;
      totals.set(name, (totals.get(name) || 0) + rss);
    }
    const rows = [...totals.entries()]
      .filter(([name]) => !/^Idle$/i.test(name))
      .sort((a, b) => b[1] - a[1])
      .slice(0, 12)
      .map(([name, bytes]) => ({ name, bytes }));
    this.processCache = { at: Date.now(), rows };
    return rows;
  }

  async refreshPublicIp() {
    const refreshMs = 30 * 60 * 1000;
    if (Date.now() - this.publicIpAt < refreshMs) return;
    let timer = null;
    try {
      const controller = new AbortController();
      timer = setTimeout(() => controller.abort(), 6000);
      const res = await fetch('https://api.ipify.org', { signal: controller.signal });
      const text = (await res.text()).trim();
      this.publicIp = text.length > 0 && text.length < 64 ? text : 'Unavailable';
      this.publicIpAt = Date.now();
    } catch {
      // Keep a previously known address; otherwise say Offline. Either way,
      // retry within 15 s so a flaky connection recovers on its own instead of
      // showing "Offline" for half an hour.
      if (!/^\d{1,3}(\.\d{1,3}){3}$/.test(this.publicIp)) this.publicIp = 'Offline';
      this.publicIpAt = Date.now() - refreshMs + 15_000;
    } finally {
      if (timer) clearTimeout(timer);
    }
  }

  async tick() {
    if (this.collecting) return;
    this.collecting = true;
    try {
      const mac = this.macmonCpu();
      const loads = this.cpuLoads();
      const [gpu, io, adapters, processes] = await Promise.all([
        this.load.gpu ? this.readGpu() : Promise.resolve(this.gpu),
        this.load.drives ? this.readDiskIo() : Promise.resolve({ read: 0, write: 0 }),
        this.load.network ? this.readNetwork() : Promise.resolve([]),
        this.readProcesses(),
      ]);
      const drives = this.load.drives ? await this.readDisks(io) : [];
      this.refreshPublicIp();

      let trashInfo = null;
      if (this.needsTrash && Date.now() - this.trashCache.at > 5000) {
        try {
          trashInfo = await this.trash.info(true);
          this.trashCache = { at: Date.now(), info: trashInfo };
        } catch {
          trashInfo = this.trashCache.info;
        }
      } else if (this.needsTrash) {
        trashInfo = this.trashCache.info;
      }

      const mem = mac.memory;
      const totalMemory = Number(mem?.ram_total) || this.static.totalMemory;
      const usedMemory = Number(mem?.ram_usage) || Math.max(0, this.static.totalMemory - os.freemem());
      const swapTotal = Number(mem?.swap_total) || 0;
      const swapUsed = Number(mem?.swap_usage) || 0;

      const sensors = [];
      if (mac.temperatureC != null) {
        sensors.push({ kind: 'temperature', group: 'CPU', name: 'Average', value: mac.temperatureC });
      }
      if (mac.gpuTemperatureC != null) {
        sensors.push({ kind: 'temperature', group: 'GPU', name: 'Average', value: mac.gpuTemperatureC });
      }
      for (const fan of mac.fans || []) {
        const value = Number(fan.rpm ?? fan.speed ?? fan.value ?? fan.current ?? 0) || 0;
        if (value >= 1) {
          sensors.push({ kind: 'fan', group: 'Fans', name: fan.name || 'Fan', value });
        }
      }

      const volts = this.voltages(mac);
      const gpuLoad = gpu.util != null ? gpu.util : mac.gpuRatio != null ? mac.gpuRatio * 100 : null;
      const vramTotal = this.vramTotal || this.static.totalMemory;
      const vramUsed = gpu.alloc != null ? gpu.alloc : null;
      const gpuReading = {
        name: this.static.gpuName || this.static.cpuName,
        coreLoad: gpuLoad,
        vramUsedBytes: vramUsed,
        vramTotalBytes: vramTotal,
        vramPercent: vramUsed != null && vramTotal > 0 ? (vramUsed / vramTotal) * 100 : null,
        temperatureC: mac.gpuTemperatureC,
        coreClockMhz: mac.gpuClockMhz,
        // Unified LPDDR memory: rated data rate (MT/s) → I/O clock (MHz).
        vramClockMhz: this.memSpec.mts ? this.memSpec.mts / 2 : null,
        vramDataRate: this.memSpec.mts,
        vramType: this.memSpec.type,
        memoryPowerW: mac.ramPowerW,
        fanPercent: null,
        voltage: volts.gpu,
      };

      const snapshot = {
        timestamp: Date.now(),
        cpu: {
          name: this.static.cpuName,
          load: loads.load,
          perCoreLoad: loads.perCore,
          temperatureC: mac.temperatureC,
          clockMhz: mac.clockMhz,
          packagePowerW: mac.powerW,
          voltage: volts.cpu,
          clusterVoltages: volts.perTier,
          fanRpm: mac.fanRpm,
          approximateTemperature: false,
        },
        gpus: [gpuReading],
        ram: {
          usedBytes: usedMemory,
          totalBytes: totalMemory,
          freeBytes: Math.max(0, totalMemory - usedMemory),
          usedPercent: totalMemory > 0 ? (usedMemory / totalMemory) * 100 : null,
          virtualUsedBytes: swapUsed,
          virtualTotalBytes: swapTotal,
          virtualPercent: swapTotal > 0 ? (swapUsed / swapTotal) * 100 : null,
        },
        adapters,
        drives,
        sensors,
        processes,
        publicIp: this.publicIp,
        media: this.media.metaForSnapshot(),
        trash: {
          enabled: this.needsTrash,
          count: trashInfo?.count ?? 0,
          size: trashInfo?.size ?? 0,
        },
        sensorHint: this.macmon.available ? '' : this.macmon.lastError || 'Sensor helper is starting…',
        sensorsAvailable: this.macmon.available,
        // macmon lists no fans on passively cooled Macs (MacBook Air). Widgets
        // use this to say "No fan" instead of an ambiguous dash.
        fanless: this.macmon.available && Array.isArray(mac.fans) && mac.fans.length === 0,
        system: {
          model: this.static.model,
          osVersion: this.static.osVersion,
          totalMemory: this.static.totalMemory,
        },
      };
      this.latest = snapshot;
      for (const listener of this.listeners) {
        try {
          listener(snapshot, null);
        } catch {
          /* ignore */
        }
      }
    } catch (err) {
      this.store.log('WARN', `Metrics tick failed: ${err.message}`);
    } finally {
      this.collecting = false;
    }
  }

  hardwareReport() {
    const fmt = (v, d) => (v == null || !Number.isFinite(Number(v)) ? '—' : Number(v).toFixed(d));
    const s = this.latest;
    const lines = [];
    lines.push('DeskStats — hardware report (macOS)');
    lines.push(`Generated: ${new Date().toISOString()}`);
    lines.push(`App: ${this.store.dataDir}`);
    lines.push('');
    lines.push(`Model: ${this.static.model || '—'}`);
    lines.push(`macOS: ${this.static.osVersion}`);
    lines.push(`CPU: ${this.static.cpuName}`);
    lines.push(`Logical cores: ${os.cpus().length}`);
    lines.push(`Memory: ${(this.static.totalMemory / 1024 ** 3).toFixed(1)} GB`);
    lines.push(`GPU: ${this.static.gpuName || '—'}`);
    lines.push('');
    if (s) {
      lines.push(`CPU load: ${s.cpu.load.toFixed(1)}%`);
      lines.push(`CPU temperature: ${fmt(s.cpu.temperatureC, 1)} °C`);
      lines.push(`CPU clock: ${s.cpu.clockMhz ?? '—'} MHz`);
      lines.push(`CPU package power: ${fmt(s.cpu.packagePowerW, 2)} W`);
      lines.push(`CPU core voltage (DVFS): ${s.cpu.voltage != null ? s.cpu.voltage.toFixed(3) : '—'} V`);
      for (const t of s.cpu.clusterVoltages || []) {
        lines.push(`  ${t.label}-cluster: ${Math.round(t.mhz)} MHz @ ${t.volts != null ? t.volts.toFixed(3) : '—'} V`);
      }
      lines.push(`GPU voltage (DVFS): ${s.gpus[0]?.voltage != null ? s.gpus[0].voltage.toFixed(3) : '—'} V`);
      lines.push(`Memory: ${s.gpus[0]?.vramType || '—'} ${s.gpus[0]?.vramDataRate ? `${s.gpus[0].vramDataRate} MT/s (rated)` : ''}`);
      lines.push(`GPU load: ${fmt(s.gpus[0]?.coreLoad, 0)} %`);
      lines.push(`GPU clock: ${fmt(s.gpus[0]?.coreClockMhz, 0)} MHz`);
      lines.push(`GPU temperature: ${fmt(s.gpus[0]?.temperatureC, 1)} °C`);
      lines.push(`GPU allocated memory: ${s.gpus[0]?.vramUsedBytes ?? '—'} bytes`);
      lines.push(`Memory used: ${s.ram.usedBytes} / ${s.ram.totalBytes} bytes`);
      lines.push(`Swap: ${s.ram.virtualUsedBytes} / ${s.ram.virtualTotalBytes} bytes`);
      lines.push('');
      lines.push('Drives:');
      for (const drive of s.drives) {
        lines.push(`  ${drive.name} (${drive.mount}): ${(drive.usedPercent || 0).toFixed(1)}% used, ${(drive.freeBytes / 1024 ** 3).toFixed(1)} GB free of ${(drive.totalBytes / 1024 ** 3).toFixed(1)} GB`);
      }
      lines.push('');
      lines.push('Network adapters:');
      for (const adapter of s.adapters) {
        lines.push(`  ${adapter.name}: ${adapter.isUp ? 'connected' : 'disconnected'} ${adapter.ipV4 || ''}`);
      }
      lines.push('');
      lines.push('Sensors:');
      for (const sensor of s.sensors) {
        lines.push(`  ${sensor.kind} ${sensor.group} · ${sensor.name}: ${sensor.value.toFixed(1)}`);
      }
      lines.push('');
      lines.push(`Sensor helper: ${this.macmon.available ? `running (${this.macmon.bin})` : `unavailable — ${this.macmon.lastError}`}`);
      lines.push(`Media helper: ${fs.existsSync(this.media.bin) ? this.media.bin : 'missing'}`);
    } else {
      lines.push('Metrics have not completed their first sample yet.');
    }
    lines.push('');
    if (this.latest?.fanless) {
      lines.push('Fans: none — this Mac uses passive (fanless) cooling.');
    }
    lines.push('Note: Apple Silicon exposes fewer sensors than Windows/LibreHardwareMonitor.');
    lines.push('Voltages come from the SoC power manager DVFS tables at the live cluster/GPU frequency.');
    lines.push('Memory clock is the rated LPDDR speed for this chip (macOS does not expose a live DRAM clock).');
    return lines.join('\n');
  }
}

module.exports = { MetricsService };
