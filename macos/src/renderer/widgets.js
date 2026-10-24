/* All eleven DeskStats widgets, rendered as DOM. The structure follows the WPF
   XAML closely: card → responsive columns → sections → rows/rings/graphs. */

import {
  bytes,
  bytesPerSecond,
  rate,
  percent,
  temperature,
  voltage,
  rpm,
  clockSpeed,
  gigahertz,
  megahertz,
  clamp,
  titleCase,
  shortenCpuName,
  shortenGpuName,
  zoneTime,
  zoneLabel,
  monthTitle,
  weekdayNames,
  dateLine,
  clockParts,
  weatherCode,
  aqiLabel,
  shortHourLabel,
  shortDayLabel,
  weatherTemp,
  updatedLabel,
} from './format.js';
import { ring, ringWithText, barGraph } from './graphs.js';

const h = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
};

function icon(kind) {
  const svgNS = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(svgNS, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('width', '15');
  svg.setAttribute('height', '15');
  svg.style.fill = 'var(--accent)';
  svg.style.flex = '0 0 auto';
  const path = document.createElementNS(svgNS, 'path');
  const paths = {
    network: 'M3 14h3v7H3zm6-5h3v12H9zm6-4h3v16h-3zm4-3h2v19h-2z',
    timezone:
      'M12 2a7 7 0 0 0-7 7c0 5.2 7 13 7 13s7-7.8 7-13a7 7 0 0 0-7-7zm0 9.5A2.5 2.5 0 1 1 12 6a2.5 2.5 0 0 1 0 5.5z',
    recycle:
      'M9 2h6l1.2 2H22v2H2V4h5.8zM5 10h14l-1 10.4A2 2 0 0 1 16 22H8a2 2 0 0 1-2-1.6z',
    clock: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 5h-2v6l5 3 1-1.7-4-2.3z',
  };
  path.setAttribute('d', paths[kind] || paths.clock);
  svg.appendChild(path);
  return svg;
}

/** Fan speed, or an explicit "No fan" on passively cooled Macs. */
function fanText(snap, value) {
  if (value > 0) return rpm(value);
  return snap?.fanless ? 'No fan' : '—';
}

function section({ title, right, badge, fullRow = false, className = '', children = [] }) {
  const node = h('div', `section ${className}`.trim());
  if (fullRow) node.dataset.fullRow = '1';
  if (title != null || right != null || badge) {
    const head = h('div', 'section-head');
    const left = h('div', 'section-head-left');
    if (title != null) left.appendChild(h('div', 'section-title', title));
    if (badge) left.appendChild(h('div', 'alert-badge', badge));
    head.appendChild(left);
    if (right != null && right !== '') {
      const value = h('div', 'section-right', right);
      head.appendChild(value);
    }
    node.appendChild(head);
  }
  const fill = h('div', 'section-fill');
  for (const child of children) if (child) fill.appendChild(child);
  node.appendChild(fill);
  return node;
}

function row(label, value, { dot, muted = false, wrap = false } = {}) {
  const node = h('div', 'row');
  const left = h('div', 'row-label');
  if (dot) {
    const dotNode = h('span', 'legend-dot');
    dotNode.style.background = dot;
    left.appendChild(dotNode);
  }
  const text = h('span', null, label);
  if (!wrap) text.style.overflow = 'hidden';
  left.appendChild(text);
  node.appendChild(left);
  const val = h('div', `row-value${muted ? ' muted' : ''}`, value == null ? '—' : String(value));
  node.appendChild(val);
  return node;
}

function bar(values, options = {}) {
  return barGraph({
    values,
    secondary: options.secondary ?? null,
    maximum: options.maximum ?? 0,
    capacity: options.capacity ?? 0,
    height: options.height ?? 64,
    primary: options.primary ?? 'var(--accent)',
    secondaryColor: options.secondaryColor ?? 'var(--secondary)',
  });
}

function stat(label, value, dot) {
  const wrap = h('div');
  const top = h('div', 'stat-row');
  if (dot) {
    const d = h('span', 'legend-dot');
    d.style.background = dot;
    top.appendChild(d);
  }
  top.appendChild(h('span', null, value));
  wrap.appendChild(top);
  wrap.appendChild(h('div', 'stat-label', label));
  return wrap;
}

/** Big centred value + legend dot label (Network / Drive activity). */
function bigStat(label, value, dot) {
  const wrap = h('div', 'stat-block');
  wrap.appendChild(h('div', 'big-number', value));
  const line = h('div', 'stat-label-row');
  const d = h('span', 'legend-dot');
  d.style.background = dot;
  line.appendChild(d);
  line.appendChild(h('span', 'row-label', label));
  wrap.appendChild(line);
  return wrap;
}

function historyBuffer(state, key, capacity) {
  if (!state.history[key]) state.history[key] = [];
  const list = state.history[key];
  if (list.capacity !== capacity) {
    list.capacity = capacity;
    if (list.length > capacity) list.splice(0, list.length - capacity);
  }
  return list;
}

function pushHistory(list, value, capacity) {
  list.push(Number.isFinite(Number(value)) ? Number(value) : 0);
  while (list.length > capacity) list.shift();
}

function settingsOf(state, defaults) {
  return { ...defaults, ...(state.settings || {}) };
}

/* ------------------------------------------------------------------ CPU */

function cpuWidget(state) {
  const card = h('div', 'widget-card');
  const columns = h('div', 'columns');
  card.appendChild(columns);

  const render = () => {
    const s = settingsOf(state, {
      showGraph: true,
      showPerCore: true,
      maxCoresShown: 256,
      showDetails: true,
      showClock: true,
      showTemperature: true,
      tempAlertC: 85,
      useFahrenheit: false,
      historyCapacity: 90,
    });
    const capacity = clamp(s.historyCapacity, 30, 300);
    const snap = state.snapshot;
    const cpu = snap?.cpu || null;
    const load = clamp(cpu?.load ?? 0, 0, 100);
    let history = historyBuffer(state, 'load', capacity);
    if (state.snapshotNew) pushHistory(history, load, capacity);

    const temp = cpu?.temperatureC;
    const tempText = temp != null ? temperature(temp, s.useFahrenheit) : null;
    const clockMhz = cpu?.clockMhz;
    const clockText = clockMhz != null ? gigahertz(clockMhz) : null;
    const header = [];
    if (s.showClock && clockText) header.push(clockText);
    if (s.showTemperature && tempText) header.push(tempText);
    const alert = temp != null && temp >= s.tempAlertC ? `HOT ${temperature(temp, s.useFahrenheit)}` : '';

    columns.replaceChildren();

    columns.appendChild(
      section({
        title: 'CPU',
        right: header.join(' · '),
        badge: alert,
        children: [
          s.showGraph ? (() => {
            const wrap = h('div', 'graph-wrap');
            wrap.appendChild(bar(history, { maximum: 100, capacity, height: 64 }));
            return wrap;
          })() : null,
          row('Load', percent(load), { dot: 'var(--accent)' }),
          cpu?.name ? h('div', 'cpu-name', shortenCpuName(cpu.name)) : null,
        ],
      }),
    );

    const cores = Array.isArray(cpu?.perCoreLoad) ? cpu.perCoreLoad : [];
    if (s.showPerCore && cores.length) {
      const shown = clamp(s.maxCoresShown, 1, cores.length);
      const busiest = Math.max(...cores.slice(0, shown).map((v) => clamp(v, 0, 100)));
      const grid = h('div', 'ring-grid');
      for (let i = 0; i < shown; i += 1) {
        const value = clamp(cores[i], 0, 100);
        const wrap = ringWithText(
          { value, size: 28, thickness: 3.5, color: 'var(--accent)' },
          { text: String(Math.round(value)), valueSize: 8.5, textColor: 'var(--text-secondary)' },
        );
        wrap.classList.add('core-ring');
        // UniformGrid-style cell: keep 28 px when there is room, shrink instead
        // of spilling outside the card when the widget is scaled up.
        wrap.style.width = '100%';
        wrap.style.maxWidth = '28px';
        wrap.style.height = 'auto';
        wrap.style.aspectRatio = '1';
        wrap.title = `Core ${i + 1}: ${Math.round(value)}%`;
        grid.appendChild(wrap);
      }
      columns.appendChild(
        section({
          title: 'CORES',
          right: `${cores.length} threads · max ${Math.round(busiest)}%`,
          children: [grid],
        }),
      );
    }

    if (s.showDetails) {
      const details = h('div');
      details.appendChild(row('Temperature', tempText || '—'));
      details.appendChild(row('Clock speed', clockText || '—'));
      details.appendChild(row('Package power', cpu?.packagePowerW != null ? `${Number(cpu.packagePowerW).toFixed(1)} W` : '—'));
      details.appendChild(row('Core voltage', cpu?.voltage > 0 ? voltage(cpu.voltage) : '—'));
      details.appendChild(row('CPU fan', fanText(snap, cpu?.fanRpm)));
      columns.appendChild(section({ title: 'DETAILS', children: [details], className: 'last' }));
    }
  };

  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

/* ------------------------------------------------------------------ GPU */

function gpuWidget(state) {
  const card = h('div', 'widget-card');
  const columns = h('div', 'columns');
  card.appendChild(columns);
  if (!state.extra) state.extra = { maxClock: 1 };

  const render = () => {
    const s = settingsOf(state, {
      gpuIndex: 0,
      showGraph: true,
      showRings: true,
      showDetails: true,
      showVram: true,
      showTemperature: true,
      showClock: true,
      tempAlertC: 83,
      useFahrenheit: false,
      historyCapacity: 90,
    });
    const capacity = clamp(s.historyCapacity, 30, 300);
    const snap = state.snapshot;
    const gpus = Array.isArray(snap?.gpus) ? snap.gpus : [];
    const gpu = gpus[clamp(s.gpuIndex, 0, Math.max(0, gpus.length - 1))] || null;
    const load = clamp(gpu?.coreLoad ?? 0, 0, 100);
    const history = historyBuffer(state, 'load', capacity);
    if (state.snapshotNew) pushHistory(history, load, capacity);

    const temp = gpu?.temperatureC;
    const tempText = temp != null ? temperature(temp, s.useFahrenheit) : null;
    const alert = temp != null && temp >= s.tempAlertC
      ? `HOT ${Math.round(s.useFahrenheit ? temp * 1.8 + 32 : temp)}°`
      : '';

    const vramPercent = gpu?.vramPercent;
    const vramText =
      gpu?.vramUsedBytes != null && gpu?.vramTotalBytes > 0
        ? `${bytes(gpu.vramUsedBytes)} / ${bytes(gpu.vramTotalBytes)}`
        : vramPercent != null
          ? percent(vramPercent)
          : '—';

    let clockText = '—';
    let clockPercent = 0;
    let clockShort = '—';
    if (gpu?.coreClockMhz != null) {
      state.extra.maxClock = Math.max(state.extra.maxClock, gpu.coreClockMhz);
      clockPercent = clamp((gpu.coreClockMhz / state.extra.maxClock) * 100, 0, 100);
      clockShort = (gpu.coreClockMhz / 1000).toFixed(2);
      clockText = megahertz(gpu.coreClockMhz);
    }

    columns.replaceChildren();

    columns.appendChild(
      section({
        title: 'GPU',
        right: gpu?.name ? shortenGpuName(gpu.name) : 'No GPU sensors',
        badge: alert,
        children: [
          s.showGraph ? (() => {
            const wrap = h('div', 'graph-wrap tight');
            wrap.appendChild(bar(history, { maximum: 100, capacity, height: 56 }));
            return wrap;
          })() : null,
        ],
      }),
    );

    if (s.showRings) {
      const rings = h('div', 'rings-4');
      rings.appendChild(
        ringWithText(
          { value: load, size: 58, thickness: 5, color: 'var(--accent)' },
          { text: percent(load), caption: 'LOAD', valueSize: 13 },
        ),
      );
      if (s.showVram) {
        rings.appendChild(
          ringWithText(
            { value: vramPercent || 0, size: 58, thickness: 5, color: 'var(--purple)' },
            { text: vramPercent != null ? percent(vramPercent) : '—', caption: 'VRAM', valueSize: 13 },
          ),
        );
      }
      if (s.showTemperature) {
        rings.appendChild(
          ringWithText(
            { value: temp != null ? clamp(temp, 0, 100) : 0, size: 58, thickness: 5, color: 'var(--warning)' },
            { text: temp != null ? `${Math.round(s.useFahrenheit ? temp * 1.8 + 32 : temp)}°` : '—', caption: 'TEMP', valueSize: 13 },
          ),
        );
      }
      if (s.showClock) {
        rings.appendChild(
          ringWithText(
            { value: clockPercent, size: 58, thickness: 5, color: 'var(--success)' },
            { text: clockShort, caption: 'GHZ', valueSize: 13 },
          ),
        );
      }
      columns.appendChild(section({ children: [rings] }));
    }

    if (s.showDetails) {
      const details = h('div');
      if (s.showVram) details.appendChild(row('Video memory', vramText, { dot: 'var(--purple)' }));
      if (s.showTemperature) details.appendChild(row('Temperature', tempText || '—', { dot: 'var(--warning)' }));
      if (s.showClock) {
        details.appendChild(row('Core clock', clockText, { dot: 'var(--success)' }));
        details.appendChild(
          row(
            'Memory clock',
            gpu?.vramDataRate ? `${gpu.vramDataRate} MT/s` : gpu?.vramClockMhz != null ? megahertz(gpu.vramClockMhz) : '—',
          ),
        );
      }
      details.appendChild(row('Fan', gpu?.fanPercent != null ? percent(gpu.fanPercent) : (snap?.fanless ? 'No fan' : '—')));
      details.appendChild(row('Voltage', gpu?.voltage > 0 ? voltage(gpu.voltage) : '—'));
      if (gpu?.memoryPowerW != null) details.appendChild(row('Memory power', `${Number(gpu.memoryPowerW).toFixed(2)} W`));
      columns.appendChild(section({ title: 'DETAILS', children: [details] }));
    }
  };

  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

/* ------------------------------------------------------------------ RAM */

function ramWidget(state) {
  const card = h('div', 'widget-card');
  const columns = h('div', 'columns');
  card.appendChild(columns);

  const render = () => {
    const s = settingsOf(state, {
      showUsageGraph: false,
      showDetails: true,
      showProcesses: true,
      processCount: 5,
      usageAlertPercent: 90,
      historyCapacity: 90,
    });
    const capacity = clamp(s.historyCapacity, 30, 300);
    const snap = state.snapshot;
    const ram = snap?.ram || null;
    const usedPercent = clamp(ram?.usedPercent ?? 0, 0, 100);
    const history = historyBuffer(state, 'used', capacity);
    if (state.snapshotNew) pushHistory(history, usedPercent, capacity);
    const alert = usedPercent >= s.usageAlertPercent ? `${Math.round(usedPercent)}% USED` : '';
    const virtualPercent = ram?.virtualPercent;

    columns.replaceChildren();

    const rings = h('div', 'rings-2');
    rings.appendChild(
      ringWithText(
        { value: usedPercent, size: 86, thickness: 7, color: 'var(--accent)' },
        { text: percent(usedPercent), caption: 'MEMORY', valueSize: 20 },
      ),
    );
    rings.appendChild(
      ringWithText(
        { value: virtualPercent || 0, size: 86, thickness: 7, color: 'var(--pink)' },
        { text: virtualPercent != null ? percent(virtualPercent) : '—', caption: 'COMMIT', valueSize: 20 },
      ),
    );

    const children = [rings];
    if (s.showUsageGraph) {
      const wrap = h('div', 'graph-wrap');
      wrap.style.margin = '8px 0 0';
      wrap.appendChild(bar(history, { maximum: 100, capacity, height: 22 }));
      children.push(wrap);
    }
    columns.appendChild(section({ title: 'MEMORY', badge: alert, children }));

    if (s.showDetails) {
      const details = h('div');
      details.appendChild(row('In use', ram?.usedBytes != null ? bytes(ram.usedBytes) : '—', { dot: 'var(--accent)' }));
      details.appendChild(row('Available', ram?.freeBytes != null ? bytes(ram.freeBytes) : '—', { dot: 'var(--secondary)' }));
      details.appendChild(
        row(
          'Committed',
          ram?.virtualUsedBytes != null && ram?.virtualTotalBytes != null
            ? `${bytes(ram.virtualUsedBytes)} / ${bytes(ram.virtualTotalBytes)}`
            : '—',
          { dot: 'var(--pink)' },
        ),
      );
      details.appendChild(row('Installed', ram?.totalBytes != null ? bytes(ram.totalBytes) : '—'));
      columns.appendChild(section({ children: [details] }));
    }

    if (s.showProcesses) {
      const processes = (snap?.processes || []).slice(0, clamp(s.processCount, 1, 30));
      if (processes.length) {
        const list = h('div');
        for (const process of processes) list.appendChild(row(process.name, bytes(process.bytes)));
        columns.appendChild(section({ title: 'PROCESSES', children: [list] }));
      }
    }
  };

  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

/* -------------------------------------------------------------- Network */

function networkWidget(state) {
  const card = h('div', 'widget-card');
  const columns = h('div', 'columns');
  card.appendChild(columns);
  if (!state.extra) state.extra = { autoAdapter: null, sent: 0, received: 0, lastAt: 0 };

  const virtualMarkers = ['vEthernet', 'Hyper-V', 'VMware', 'VirtualBox', 'Virtual', 'WSL', 'Docker', 'TAP', 'Npcap', 'Loopback', 'Bluetooth'];

  const pickAdapter = (adapters, settings) => {
    if (!adapters.length) return null;
    if (settings.adapterName) {
      const named = adapters.find((a) => a.name.toLowerCase() === String(settings.adapterName).toLowerCase());
      if (named) return named;
    }
    const active = adapters.filter((a) => a.isUp || a.downBytesPerSec + a.upBytesPerSec > 0);
    const pool = active.length ? active : adapters;
    const sorted = [...pool].sort((a, b) => {
      const av = virtualMarkers.some((m) => a.name.includes(m)) ? 1 : 0;
      const bv = virtualMarkers.some((m) => b.name.includes(m)) ? 1 : 0;
      if (av !== bv) return av - bv;
      const ai = a.ipV4 ? 1 : 0;
      const bi = b.ipV4 ? 1 : 0;
      if (ai !== bi) return bi - ai;
      return b.downBytesPerSec + b.upBytesPerSec - (a.downBytesPerSec + a.upBytesPerSec);
    });
    const busiest = sorted[0] || null;
    const current = state.extra.autoAdapter
      ? pool.find((a) => a.name === state.extra.autoAdapter && a.isUp)
      : null;
    if (current && busiest && current.name !== busiest.name) {
      const currentRate = current.downBytesPerSec + current.upBytesPerSec;
      const busiestRate = busiest.downBytesPerSec + busiest.upBytesPerSec;
      if (busiestRate < Math.max(currentRate * 2, 20000)) return current;
    }
    state.extra.autoAdapter = busiest?.name || null;
    return busiest;
  };

  const render = () => {
    const s = settingsOf(state, {
      adapterName: null,
      useBits: false,
      showGraph: true,
      showPublicIp: true,
      showIp: true,
      showTotals: true,
      historyCapacity: 90,
    });
    const capacity = clamp(s.historyCapacity, 30, 300);
    const snap = state.snapshot;
    const adapter = pickAdapter(snap?.adapters || [], s);
    const downHistory = historyBuffer(state, 'down', capacity);
    const upHistory = historyBuffer(state, 'up', capacity);
    const down = adapter?.downBytesPerSec ?? 0;
    const up = adapter?.upBytesPerSec ?? 0;
    if (state.snapshotNew) {
      pushHistory(downHistory, down, capacity);
      pushHistory(upHistory, up, capacity);
      const now = snap?.timestamp || Date.now();
      if (state.extra.lastAt) {
        const seconds = clamp((now - state.extra.lastAt) / 1000, 0, 10);
        state.extra.sent += up * seconds;
        state.extra.received += down * seconds;
      }
      state.extra.lastAt = now;
    }

    const rateText = (value) => (adapter ? rate(value, s.useBits) : '—');
    const peakDown = downHistory.length ? Math.max(...downHistory) : 0;
    const peakUp = upHistory.length ? Math.max(...upHistory) : 0;

    columns.replaceChildren();

    const glance = h('div');
    const numbers = h('div');
    numbers.style.display = 'flex';
    numbers.style.justifyContent = 'space-between';
    numbers.appendChild(bigStat('Upload', rateText(up), 'var(--accent)'));
    numbers.appendChild(bigStat('Download', rateText(down), 'var(--secondary)'));
    glance.appendChild(numbers);

    if (s.showGraph) {
      const wrap = h('div', 'graph-wrap');
      wrap.style.margin = '8px 0 6px';
      wrap.appendChild(
        bar(upHistory, {
          secondary: downHistory,
          capacity,
          height: 72,
          primary: 'var(--accent)',
          secondaryColor: 'var(--secondary)',
        }),
      );
      glance.appendChild(wrap);
    }

    const peaks = h('div', 'row');
    const peakLeft = h('div', 'row-label');
    peakLeft.style.display = 'block';
    peakLeft.append('Peak ↑ ');
    const upStrong = h('b', null, adapter ? rate(peakUp, s.useBits) : '—');
    upStrong.style.color = 'var(--text-primary)';
    peakLeft.appendChild(upStrong);
    const peakRight = h('div', 'row-label');
    peakRight.style.display = 'block';
    peakRight.append('Peak ↓ ');
    const downStrong = h('b', null, adapter ? rate(peakDown, s.useBits) : '—');
    downStrong.style.color = 'var(--text-primary)';
    peakRight.appendChild(downStrong);
    peaks.appendChild(peakLeft);
    peaks.appendChild(peakRight);
    glance.appendChild(peaks);
    columns.appendChild(section({ children: [glance] }));

    const adapterSection = h('div', 'row');
    const adapterLeft = h('div', 'row-label');
    adapterLeft.style.gap = '8px';
    adapterLeft.appendChild(icon('network'));
    const adapterName = h('span', null, adapter?.name || 'No active adapter');
    adapterName.style.overflow = 'hidden';
    adapterName.style.textOverflow = 'ellipsis';
    adapterName.style.maxWidth = '170px';
    adapterName.style.whiteSpace = 'nowrap';
    adapterLeft.appendChild(adapterName);
    adapterSection.appendChild(adapterLeft);
    adapterSection.appendChild(h('div', 'row-value', adapter ? (adapter.isUp ? 'Connected' : 'Disconnected') : ''));
    columns.appendChild(section({ children: [adapterSection] }));

    if (s.showPublicIp) {
      const value = h('div', 'row-value', snap?.publicIp || 'Looking up…');
      value.style.textAlign = 'left';
      value.style.fontSize = '13.5px';
      value.style.margin = '0';
      columns.appendChild(section({ title: 'PUBLIC IP ADDRESS', children: [value] }));
    }

    if (s.showIp) {
      const value = h('div', 'row-value', adapter?.ipV4 || '—');
      value.style.textAlign = 'left';
      value.style.fontSize = '13.5px';
      value.style.margin = '0';
      columns.appendChild(section({ title: 'IP ADDRESS', children: [value] }));
    }

    if (s.showTotals) {
      const totals = h('div');
      totals.appendChild(row('Sent', bytes(state.extra.sent), { dot: 'var(--accent)' }));
      totals.appendChild(row('Received', bytes(state.extra.received), { dot: 'var(--secondary)' }));
      columns.appendChild(section({ title: 'DATA USED THIS SESSION', children: [totals] }));
    }
  };

  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

/* ---------------------------------------------------------------- Drive */

function driveWidget(state) {
  const card = h('div', 'widget-card');
  const columns = h('div', 'columns');
  card.appendChild(columns);

  const render = () => {
    const s = settingsOf(state, {
      preferredDrive: null,
      showDrives: true,
      showActivity: true,
      lowSpacePercent: 10,
      historyCapacity: 90,
    });
    const capacity = clamp(s.historyCapacity, 30, 300);
    const snap = state.snapshot;
    let drives = Array.isArray(snap?.drives) ? snap.drives : [];
    if (s.preferredDrive) {
      const preferred = drives.find((d) => d.name.toLowerCase() === String(s.preferredDrive).toLowerCase());
      if (preferred) drives = [preferred];
    }
    const readHistory = historyBuffer(state, 'read', capacity);
    const writeHistory = historyBuffer(state, 'write', capacity);
    const read = drives.reduce((sum, d) => sum + (d.readBytesPerSec || 0), 0);
    const write = drives.reduce((sum, d) => sum + (d.writeBytesPerSec || 0), 0);
    if (state.snapshotNew) {
      pushHistory(readHistory, read, capacity);
      pushHistory(writeHistory, write, capacity);
    }

    columns.replaceChildren();

    if (s.showDrives) {
      const list = h('div');
      for (const drive of drives) {
        const freePercent = drive.totalBytes > 0 ? (drive.freeBytes / drive.totalBytes) * 100 : 100;
        const low = freePercent < s.lowSpacePercent;
        const line = h('div', 'row');
        line.style.margin = '4px 0';
        const ringNode = ringWithText(
          { value: clamp(drive.usedPercent, 0, 100), size: 40, thickness: 4, color: low ? 'var(--danger)' : 'var(--accent)' },
          { text: percent(drive.usedPercent), valueSize: 10.5 },
        );
        line.appendChild(ringNode);
        const middle = h('div');
        middle.style.flex = '1';
        middle.style.minWidth = '0';
        middle.style.marginLeft = '10px';
        const name = h('div', null, drive.name);
        name.style.fontSize = '13px';
        name.style.fontWeight = '600';
        name.style.color = 'var(--text-primary)';
        name.style.overflow = 'hidden';
        name.style.textOverflow = 'ellipsis';
        name.style.whiteSpace = 'nowrap';
        const free = h('div', 'row-label', `${bytes(drive.freeBytes)} available`);
        free.style.margin = '0';
        middle.appendChild(name);
        middle.appendChild(free);
        line.appendChild(middle);
        const total = h('div', 'row-value muted', bytes(drive.totalBytes));
        total.style.fontWeight = '400';
        line.appendChild(total);
        list.appendChild(line);
      }
      if (!drives.length) list.appendChild(row('No drives', '—', { muted: true }));
      columns.appendChild(section({ title: 'DRIVES', children: [list] }));
    }

    if (s.showActivity) {
      const numbers = h('div');
      numbers.style.display = 'flex';
      numbers.style.justifyContent = 'space-between';
      numbers.appendChild(bigStat('Read', bytesPerSecond(read), 'var(--accent)'));
      numbers.appendChild(bigStat('Write', bytesPerSecond(write), 'var(--secondary)'));

      const wrap = h('div', 'graph-wrap');
      wrap.style.margin = '8px 0 6px';
      wrap.appendChild(
        bar(readHistory, {
          secondary: writeHistory,
          capacity,
          height: 64,
          primary: 'var(--accent)',
          secondaryColor: 'var(--secondary)',
        }),
      );

      const peaks = h('div', 'row');
      const left = h('div', 'row-label');
      left.style.display = 'block';
      left.append('Peak read ');
      const r = h('b', null, bytesPerSecond(readHistory.length ? Math.max(...readHistory) : 0));
      r.style.color = 'var(--text-primary)';
      left.appendChild(r);
      const right = h('div', 'row-label');
      right.style.display = 'block';
      right.append('Peak write ');
      const w = h('b', null, bytesPerSecond(writeHistory.length ? Math.max(...writeHistory) : 0));
      w.style.color = 'var(--text-primary)';
      right.appendChild(w);
      peaks.appendChild(left);
      peaks.appendChild(right);

      columns.appendChild(section({ children: [numbers, wrap, peaks] }));
    }
  };

  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

/* -------------------------------------------------------------- Sensors */

function sensorsWidget(state) {
  const card = h('div', 'widget-card');
  const columns = h('div', 'columns');
  card.appendChild(columns);

  const render = () => {
    const s = settingsOf(state, {
      showTemperatures: true,
      maxTemperatureRows: 12,
      showFans: true,
      fanFullScaleRpm: 2500,
      tempAlertC: 85,
      useFahrenheit: false,
    });
    const snap = state.snapshot;
    const cpuTemp = snap?.cpu?.temperatureC ?? null;
    const gpuTemp = snap?.gpus?.[0]?.temperatureC ?? null;
    const sensors = Array.isArray(snap?.sensors) ? snap.sensors : [];
    const fanReadings = sensors.filter((sensor) => sensor.kind === 'fan');
    const maxFan = fanReadings.length ? Math.max(...fanReadings.map((fan) => fan.value)) : 0;
    const fullScale = Math.max(500, s.fanFullScaleRpm);
    const fanPercent = clamp((maxFan / fullScale) * 100, 0, 100);
    const fanLabel = maxFan >= 1 ? (maxFan >= 1000 ? `${(maxFan / 1000).toFixed(1)}k` : String(Math.round(maxFan))) : 'OFF';
    const fanless = Boolean(snap?.fanless);
    const hottest = Math.max(cpuTemp ?? 0, gpuTemp ?? 0);
    const alert = hottest >= s.tempAlertC
      ? `HOT ${Math.round(s.useFahrenheit ? hottest * 1.8 + 32 : hottest)}°`
      : '';

    const shortTemp = (value) =>
      value != null ? `${Math.round(s.useFahrenheit ? value * 1.8 + 32 : value)}°` : '—';
    const longTemp = (value) =>
      value != null ? `${Math.round(s.useFahrenheit ? value * 1.8 + 32 : value)}°${s.useFahrenheit ? 'F' : 'C'}` : '—';

    columns.replaceChildren();

    if (snap && snap.sensorsAvailable === false && snap.sensorHint) {
      const hint = h('div', 'row-label', snap.sensorHint);
      hint.style.whiteSpace = 'normal';
      hint.style.lineHeight = '1.35';
      columns.appendChild(section({ fullRow: true, children: [hint] }));
    }

    const rings = h('div', 'rings-3');
    rings.appendChild(
      ringWithText(
        { value: clamp(cpuTemp ?? 0, 0, 100), size: 74, thickness: 5, color: 'var(--warning)' },
        { text: shortTemp(cpuTemp), caption: 'CPU', valueSize: 16 },
      ),
    );
    rings.appendChild(
      ringWithText(
        { value: clamp(gpuTemp ?? 0, 0, 100), size: 74, thickness: 5, color: 'var(--warning)' },
        { text: shortTemp(gpuTemp), caption: 'GPU', valueSize: 16 },
      ),
    );
    rings.appendChild(
      ringWithText(
        { value: fanless ? 0 : fanPercent, size: 74, thickness: 5, color: 'var(--accent)' },
        fanless
          ? { text: 'No fan', caption: 'FANLESS', valueSize: 11.5 }
          : { text: fanLabel, caption: 'FANS', valueSize: 16 },
      ),
    );
    columns.appendChild(section({ title: 'SENSORS', badge: alert, children: [rings] }));

    const temperatures = sensors
      .filter((sensor) => sensor.kind === 'temperature')
      .sort((a, b) => b.value - a.value)
      .slice(0, clamp(s.maxTemperatureRows, 4, 30));
    if (s.showTemperatures && temperatures.length) {
      const list = h('div');
      for (const sensor of temperatures) {
        const line = h('div', 'row');
        const label = h('div', 'row-label', sensor.name.startsWith(sensor.group) ? sensor.name : `${sensor.group} · ${sensor.name}`);
        line.appendChild(label);
        line.appendChild(h('div', 'row-value', longTemp(sensor.value)));
        line.appendChild(ring({ value: clamp(sensor.value, 0, 100), size: 14, thickness: 2.5, color: 'var(--accent)' }));
        line.lastChild.style.marginLeft = '8px';
        list.appendChild(line);
      }
      columns.appendChild(section({ title: 'TEMPERATURE', children: [list] }));
    }

    if (s.showFans && fanReadings.filter((fan) => fan.value >= 1).length) {
      const list = h('div');
      for (const fan of fanReadings.filter((item) => item.value >= 1)) {
        const line = h('div', 'row');
        line.appendChild(h('div', 'row-label', fan.name.startsWith(fan.group) ? fan.name : `${fan.group} · ${fan.name}`));
        line.appendChild(h('div', 'row-value', rpm(fan.value)));
        const gauge = ring({
          value: clamp((fan.value / fullScale) * 100, 0, 100),
          size: 14,
          thickness: 2.5,
          color: 'var(--accent)',
        });
        gauge.style.marginLeft = '8px';
        line.appendChild(gauge);
        list.appendChild(line);
      }
      columns.appendChild(section({ title: 'FANS', children: [list] }));
    }
  };

  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

/* ---------------------------------------------------------------- Clock */

function clockWidget(state) {
  const card = h('div', 'widget-card');
  const columns = h('div', 'columns');
  card.appendChild(columns);
  const today = new Date();
  let shownMonth = new Date(today.getFullYear(), today.getMonth(), 1);
  let lastKey = '';

  const render = () => {
    const s = settingsOf(state, {
      showSeconds: true,
      use24Hour: false,
      showDate: true,
      showCalendar: true,
      weekStartsMonday: true,
      formatOverride: null,
    });
    const now = new Date();
    const parts = clockParts(now, s);
    const key = `${shownMonth.getFullYear()}-${shownMonth.getMonth()}-${parts.time}-${parts.seconds}-${s.showCalendar}-${s.weekStartsMonday}-${new Intl.DateTimeFormat('en-US').format(now)}`;
    if (key === lastKey) return;
    lastKey = key;

    columns.replaceChildren();

    const timeWrap = h('div', 'clock-wrap');
    const line = h('div');
    const time = h('span', 'clock-time', parts.time);
    line.appendChild(time);
    if (s.showSeconds && parts.seconds) line.appendChild(h('span', 'clock-seconds', parts.seconds));
    timeWrap.appendChild(line);
    if (s.showDate) timeWrap.appendChild(h('div', 'clock-date', dateLine(now)));
    columns.appendChild(section({ children: [timeWrap] }));

    if (s.showCalendar) {
      const cal = h('div');
      const head = h('div', 'cal-head');
      const title = h('button', 'cal-title', monthTitle(shownMonth));
      title.title = 'Back to today';
      title.addEventListener('click', () => {
        shownMonth = new Date(new Date().getFullYear(), new Date().getMonth(), 1);
        lastKey = '';
        render();
      });
      head.appendChild(title);
      const nav = h('div');
      const prev = h('button', 'cal-nav', '‹');
      prev.title = 'Previous month';
      prev.addEventListener('click', () => {
        shownMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() - 1, 1);
        lastKey = '';
        render();
      });
      const next = h('button', 'cal-nav', '›');
      next.title = 'Next month';
      next.addEventListener('click', () => {
        shownMonth = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + 1, 1);
        lastKey = '';
        render();
      });
      nav.appendChild(prev);
      nav.appendChild(next);
      head.appendChild(nav);
      cal.appendChild(head);

      const names = h('div', 'cal-weekdays');
      for (const name of weekdayNames(s.weekStartsMonday)) names.appendChild(h('div', 'cal-weekday', name));
      cal.appendChild(names);

      const grid = h('div', 'cal-grid');
      const first = shownMonth.getDay();
      const startDow = s.weekStartsMonday ? 1 : 0;
      const offset = (first - startDow + 7) % 7;
      const start = new Date(shownMonth.getFullYear(), shownMonth.getMonth(), 1 - offset);
      const todayDate = new Date();
      for (let i = 0; i < 42; i += 1) {
        const day = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
        const sameMonth = day.getMonth() === shownMonth.getMonth() && day.getFullYear() === shownMonth.getFullYear();
        const isToday = day.toDateString() === todayDate.toDateString();
        const weekend = day.getDay() === 0 || day.getDay() === 6;
        const cell = h('div', `cal-cell${weekend ? ' weekend' : ''}${sameMonth ? '' : ' outside'}${isToday ? ' today' : ''}`, String(day.getDate()));
        grid.appendChild(cell);
      }
      cal.appendChild(grid);
      columns.appendChild(section({ children: [cal] }));
    }
  };

  render();
  return {
    element: card,
    update() {
      render();
    },
    tick() {
      render();
    },
  };
}

/* ------------------------------------------------------------- Timezone */

function timezoneWidget(state) {
  const card = h('div', 'widget-card worldclock');
  const render = () => {
    const s = settingsOf(state, { showSeconds: false, timeZoneIds: ['UTC', 'Eastern Standard Time', 'Central European Standard Time', 'Tokyo Standard Time', 'Pacific Standard Time'] });
    const ids = Array.isArray(s.timeZoneIds) && s.timeZoneIds.length
      ? s.timeZoneIds
      : ['UTC', 'Eastern Standard Time', 'Central European Standard Time', 'Tokyo Standard Time'];
    card.replaceChildren();
    const header = h('div', 'widget-title');
    header.appendChild(icon('timezone'));
    header.appendChild(h('span', null, 'WORLD CLOCK'));
    card.appendChild(header);
    const list = h('div', 'wc-list');
    const now = new Date();
    for (const id of ids) {
      const rowNode = h('div', 'wc-row');
      rowNode.appendChild(h('div', 'wc-label', zoneLabel(id)));
      rowNode.appendChild(h('div', 'wc-time', zoneTime(id, s.showSeconds, now)));
      list.appendChild(rowNode);
    }
    card.appendChild(list);
  };
  render();
  return {
    element: card,
    update() {
      render();
    },
    tick() {
      render();
    },
  };
}

/* -------------------------------------------------------------- Weather */

function weatherWidget(state) {
  const card = h('div', 'widget-card');
  const columns = h('div', 'columns');
  card.appendChild(columns);

  const fmtTemp = (value, s, decimals = false) => weatherTemp(value, s.useFahrenheit, decimals);

  const render = () => {
    const s = settingsOf(state, {
      locationName: 'New York',
      showHourly: true,
      showDaily: true,
      dailyDays: 4,
      useFahrenheit: false,
      refreshMinutes: 30,
    });
    const weather = state.aux?.weather;
    columns.replaceChildren();

    const current = weather?.ok ? weather.current : null;
    let condition = 'Loading…';
    let iconEmoji = '☁';
    let updated = '';
    if (weather && !weather.ok) {
      condition = weather.reason === 'city-not-found' ? 'City not found' : 'Offline';
      updated = weather.reason === 'city-not-found'
        ? `Could not find "${weather.city || s.locationName}" — check the spelling in Settings`
        : 'Offline — retrying';
    } else if (weather?.ok) {
      const [label, emoji] = weatherCode(current?.code, current?.isDay === false);
      condition = label;
      iconEmoji = emoji;
      updated = updatedLabel(weather.updated);
      if (weather.stale) updated += ' · stale';
    }

    if (current) {
      const now = h('div');
      const top = h('div', 'weather-now-grid');
      top.appendChild(h('div', 'weather-temp', fmtTemp(current.temperature, s, true)));
      top.appendChild(h('div', 'weather-icon', iconEmoji));
      now.appendChild(top);

      const locRow = h('div', 'weather-loc-row');
      locRow.appendChild(h('div', 'weather-loc', titleCase(s.locationName)));
      locRow.appendChild(h('div', 'weather-cond', condition));
      now.appendChild(locRow);

      const glance = h('div', 'weather-glance');
      const hourPrecip = Array.isArray(weather.hourly?.precipitation) ? weather.hourly.precipitation[0] : null;
      glance.appendChild(stat('Humidity', current.humidity != null ? percent(current.humidity) : '—', null));
      glance.firstChild.firstChild.insertBefore(h('span', null, '💧'), glance.firstChild.firstChild.firstChild);
      const rain = stat('Rain chance', hourPrecip != null ? percent(hourPrecip) : '—', null);
      rain.firstChild.insertBefore(h('span', null, '☔'), rain.firstChild.firstChild);
      glance.appendChild(rain);
      const wind = stat('Wind', current.windSpeed != null ? `${Math.round(current.windSpeed)} km/h` : '—', null);
      wind.firstChild.insertBefore(h('span', null, '🌬'), wind.firstChild.firstChild);
      glance.appendChild(wind);
      now.appendChild(glance);

      const air = h('div', 'weather-air');
      air.appendChild(h('div', 'row-label', 'Air quality'));
      const airValue = h('div', 'row-label');
      airValue.style.textAlign = 'right';
      airValue.style.justifyContent = 'flex-end';
      airValue.style.fontWeight = '600';
      airValue.style.color = 'var(--text-primary)';
      airValue.textContent = weather.air?.aqi != null ? `${Math.round(weather.air.aqi)} · ${aqiLabel(weather.air.aqi)}` : '—';
      air.appendChild(airValue);
      now.appendChild(air);

      const dew = Array.isArray(weather.hourly?.dewPoint) ? weather.hourly.dewPoint[0] : null;
      const vis = Array.isArray(weather.hourly?.visibility) ? weather.hourly.visibility[0] : null;
      const uv = Array.isArray(weather.hourly?.uv) ? weather.hourly.uv[0] : null;
      const details = h('div', 'weather-detail-grid');
      const colA = h('div');
      colA.appendChild(richRow('Dew point', dew != null ? fmtTemp(dew, s, true) : '—'));
      colA.appendChild(richRow('Visibility', vis != null ? (vis >= 1000 ? `${Math.round(vis / 1000)} km` : `${Math.round(vis)} m`) : '—'));
      const colB = h('div');
      const feels = richRow('Feels like', current.apparent != null ? fmtTemp(current.apparent, s, true) : '—');
      feels.classList.add('right');
      colB.appendChild(feels);
      const uvRow = richRow('UV index', uv != null ? String(Math.round(uv)) : '—');
      uvRow.classList.add('right');
      colB.appendChild(uvRow);
      details.appendChild(colA);
      details.appendChild(colB);
      now.appendChild(details);
      columns.appendChild(section({ children: [now] }));
    } else {
      const now = h('div');
      const top = h('div', 'weather-now-grid');
      top.appendChild(h('div', 'weather-temp', '—'));
      top.appendChild(h('div', 'weather-icon', '☁'));
      now.appendChild(top);
      const locRow = h('div', 'weather-loc-row');
      locRow.appendChild(h('div', 'weather-loc', titleCase(s.locationName)));
      locRow.appendChild(h('div', 'weather-cond', condition));
      now.appendChild(locRow);
      if (updated) {
        const note = h('div', 'weather-summary', updated);
        now.appendChild(note);
      }
      columns.appendChild(section({ children: [now] }));
    }

    if (s.showHourly && weather?.ok && Array.isArray(weather.hourly?.time) && weather.hourly.time.length) {
      const strip = h('div', 'weather-hourly');
      const times = weather.hourly.time;
      const temps = weather.hourly.temperature || [];
      const codes = weather.hourly.code || [];
      const precips = weather.hourly.precipitation || [];
      let count = 0;
      for (let i = 0; i < times.length && i < temps.length && count < 6; i += 1) {
        if (temps[i] == null) continue;
        const [, hourIcon] = weatherCode(codes[i], false);
        const item = h('div', 'weather-hour');
        item.appendChild(h('div', 'label', shortHourLabel(times[i])));
        item.appendChild(h('div', 'precip', precips[i] != null ? percent(precips[i]) : ''));
        item.appendChild(h('div', 'icon', hourIcon));
        item.appendChild(h('div', 'temp', fmtTemp(temps[i], s, false)));
        strip.appendChild(item);
        count += 1;
      }
      columns.appendChild(section({ children: [strip] }));
    }

    if (s.showDaily && weather?.ok && Array.isArray(weather.daily?.time)) {
      const strip = h('div', 'weather-daily');
      const times = weather.daily.time;
      const highs = weather.daily.high || [];
      const lows = weather.daily.low || [];
      const codes = weather.daily.code || [];
      let count = 0;
      for (let i = 1; i < times.length && count < clamp(s.dailyDays, 1, 6); i += 1) {
        const [, dayIcon] = weatherCode(codes[i], false);
        const item = h('div', 'weather-day');
        item.appendChild(h('div', 'day', shortDayLabel(times[i])));
        item.appendChild(h('div', 'icon', dayIcon));
        const range = h('div', 'range');
        range.appendChild(h('b', null, highs[i] != null ? fmtTemp(highs[i], s, false) : '—'));
        range.appendChild(document.createTextNode(` ${lows[i] != null ? fmtTemp(lows[i], s, false) : '—'}`));
        item.appendChild(range);
        strip.appendChild(item);
        count += 1;
      }
      if (count) columns.appendChild(section({ children: [strip] }));
    }

    if (weather?.ok && weather.daily?.time?.length) {
      const daily = weather.daily;
      const today = h('div');
      const head = h('div', 'weather-today-head');
      const left = h('div', 'weather-today-left');
      const [, todayIcon] = weatherCode(daily.code?.[0] ?? current?.code, false);
      left.appendChild(h('div', 'weather-today-icon', todayIcon));
      left.appendChild(h('div', 'weather-today-name', 'Today'));
      const rain = daily.precipitation?.[0];
      if (rain != null) left.appendChild(h('div', 'weather-today-precip', percent(rain)));
      head.appendChild(left);
      const low = daily.low?.[0];
      const high = daily.high?.[0];
      head.appendChild(h('div', 'row-value', low != null && high != null ? `${fmtTemp(low, s, false)}  ${fmtTemp(high, s, false)}` : ''));
      today.appendChild(head);
      const summary = rain != null && rain >= 50
        ? `${condition}. Rain likely today.`
        : rain != null && rain >= 20
          ? `${condition}. Possible rain later.`
          : `${condition}.`;
      today.appendChild(h('div', 'weather-summary', summary));
      if (updated) today.appendChild(h('div', 'weather-updated', updated));
      columns.appendChild(section({ children: [today] }));
    }
  };

  const richRow = (label, value) => {
    const node = h('div', 'row-label');
    node.append(`${label} `);
    const strong = h('b', null, value);
    strong.style.color = 'var(--text-primary)';
    node.appendChild(strong);
    return node;
  };

  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

/* ---------------------------------------------------------------- Media */

function mediaWidget(state) {
  const card = h('div', 'widget-card media');
  const render = () => {
    const s = settingsOf(state, { showAppName: true, showThumbnail: true });
    const media = state.aux?.media || state.snapshot?.media || { hasSession: false, title: 'Nothing playing' };
    card.replaceChildren();

    const body = h('div', 'media-body');
    const art = h('div', 'media-art');
    art.appendChild(document.createTextNode('🎵'));
    if (s.showThumbnail && media.thumbnail) {
      const img = document.createElement('img');
      img.src = media.thumbnail;
      img.alt = '';
      art.appendChild(img);
    }
    body.appendChild(art);

    const info = h('div', 'media-info');
    const top = h('div', 'media-top');
    if (media.status) top.appendChild(h('div', 'media-status', media.status));
    if (s.showAppName && media.appName) top.appendChild(h('div', 'media-app', media.appName));
    info.appendChild(top);
    info.appendChild(h('div', 'media-title', media.title || 'Nothing playing'));
    if (media.artist) info.appendChild(h('div', 'media-artist', media.artist));
    body.appendChild(info);
    card.appendChild(body);
  };
  render();
  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

/* ---------------------------------------------------------------- Trash */

function trashWidget(state) {
  const card = h('div', 'widget-card trash');
  const render = () => {
    const s = settingsOf(state, { showSize: true, refreshSeconds: 5 });
    const trash = state.snapshot?.trash || { count: 0, size: 0 };
    const count = trash.count || 0;
    card.replaceChildren();

    const header = h('div', 'widget-title');
    header.appendChild(icon('recycle'));
    header.appendChild(h('span', null, 'TRASH'));
    card.appendChild(header);

    const body = h('div', 'trash-body');
    body.appendChild(h('div', 'trash-count', `${count.toLocaleString()} item${count === 1 ? '' : 's'}`));
    if (s.showSize) body.appendChild(h('div', 'trash-size', bytes(trash.size || 0)));
    card.appendChild(body);

    if (count > 0) {
      const button = h('button', 'trash-empty', 'Empty trash');
      button.addEventListener('click', (event) => {
        event.stopPropagation();
        window.deskstats?.invoke('trash:empty');
      });
      card.appendChild(button);
    }
  };
  render();
  return {
    element: card,
    update() {
      render();
    },
    tick() {},
  };
}

const FACTORIES = {
  cpu: cpuWidget,
  gpu: gpuWidget,
  ram: ramWidget,
  network: networkWidget,
  drive: driveWidget,
  sensors: sensorsWidget,
  clock: clockWidget,
  timezone: timezoneWidget,
  weather: weatherWidget,
  media: mediaWidget,
  recyclebin: trashWidget,
};

/**
 * Creates one widget renderer. The returned object keeps settings/history in
 * closure, so callers only forward state.
 */
export function createWidget(widgetId, initial = {}) {
  const factory = FACTORIES[widgetId];
  if (!factory) {
    const card = h('div', 'widget-card');
    card.appendChild(section({ children: [h('div', 'row-label', `Unknown widget: ${widgetId}`)] }));
    return { element: card, update() {}, tick() {} };
  }
  const state = {
    settings: initial.settings || {},
    snapshot: initial.snapshot || null,
    aux: initial.aux || {},
    history: {},
    snapshotNew: false,
    extra: null,
  };
  const widget = factory(state);

  const applyAppearance = (appearance) => {
    if (!appearance?.vars) return;
    for (const [key, value] of Object.entries(appearance.vars)) {
      widget.element.style.setProperty(key, value);
    }
  };

  applyAppearance(initial.appearance);

  return {
    element: widget.element,
    update({ snapshot = state.snapshot, aux = state.aux, settings, appearance, isNewSnapshot = true } = {}) {
      if (settings) state.settings = settings;
      applyAppearance(appearance);
      state.aux = aux;
      if (snapshot !== state.snapshot) {
        state.snapshot = snapshot;
        state.snapshotNew = isNewSnapshot;
      } else {
        state.snapshotNew = false;
      }
      widget.update();
      state.snapshotNew = false;
      if (appearance?.scale && appearance.scale !== 1) {
        widget.element.style.zoom = appearance.scale;
      } else {
        widget.element.style.zoom = '';
      }
    },
    setSnapshot(snapshot) {
      if (snapshot?.timestamp && snapshot.timestamp === state.snapshot?.timestamp) {
        state.snapshotNew = false;
        return false;
      }
      state.snapshot = snapshot;
      state.snapshotNew = true;
      return true;
    },
    setAux(aux) {
      state.aux = { ...state.aux, ...aux };
      return true;
    },
    tick() {
      widget.tick();
    },
    get element() {
      return widget.element;
    },
    get state() {
      return state;
    },
  };
}

/**
 * Port of ResponsiveColumns: 1 column normally, 2–3 as the card widens, with
 * balanced bands so every column ends on the same line.
 */
export function layoutResponsive(card) {
  const columns = card.querySelector(':scope > .columns');
  if (!columns) return { columns: 1, useful: 1 };

  const sections = [...columns.children].filter((node) => node.classList.contains('section'));
  const flowing = sections.filter((node) => !node.dataset.fullRow);
  const width = columns.clientWidth || card.clientWidth;
  if (!width) return { columns: 1, useful: Math.max(1, flowing.length) };

  // Reset inline layout so measurement uses natural heights.
  columns.classList.remove('masonry');
  columns.style.height = '';
  for (const node of sections) {
    node.style.position = '';
    node.style.left = '';
    node.style.top = '';
    node.style.width = '';
    node.style.height = '';
    node.style.visibility = '';
  }

  const gap = 8;
  const minCol = 264;
  const fit = Math.floor((width + gap) / (minCol + gap));
  const maxColumns = Math.min(3, Math.max(1, flowing.length));
  const count = clamp(fit, 1, maxColumns);
  if (count <= 1 || sections.length === 0) {
    return { columns: 1, useful: maxColumns };
  }

  columns.classList.add('masonry');
  const columnWidth = Math.max(1, (width - gap * (count - 1)) / count);

  const measure = (node, measureWidth) => {
    node.style.position = 'absolute';
    node.style.left = '0px';
    node.style.top = '0px';
    node.style.width = `${measureWidth}px`;
    node.style.height = 'auto';
    node.style.visibility = 'hidden';
    return node.offsetHeight;
  };

  const heights = new Array(count).fill(0);
  let band = [];
  const slots = [];

  const closeBand = () => {
    if (count < 2 || band.length === 0) {
      band = [];
      return;
    }
    const target = Math.max(...heights);
    for (let c = 0; c < count; c += 1) {
      const inColumn = band.filter((slot) => slot.column === c);
      const extra = target - heights[c];
      if (inColumn.length === 0 || extra < 0.5) continue;
      const share = extra / inColumn.length;
      for (const slot of inColumn) slot.height += share;
      heights[c] = target;
    }
    band = [];
  };

  for (const node of sections) {
    if (node.dataset.fullRow) {
      closeBand();
      const top = Math.max(...heights);
      const height = measure(node, width);
      slots.push({ node, column: null, x: 0, y: top, width, height });
      heights.fill(top + height + gap);
      continue;
    }
    let shortest = 0;
    for (let c = 1; c < count; c += 1) if (heights[c] < heights[shortest] - 0.5) shortest = c;
    const height = measure(node, columnWidth);
    const slot = {
      node,
      column: shortest,
      x: shortest * (columnWidth + gap),
      y: heights[shortest],
      width: columnWidth,
      height,
    };
    slots.push(slot);
    band.push(slot);
    heights[shortest] += height + gap;
  }
  closeBand();

  for (const slot of slots) {
    slot.node.style.position = 'absolute';
    slot.node.style.left = `${slot.x}px`;
    slot.node.style.top = `${slot.y}px`;
    slot.node.style.width = `${slot.width}px`;
    slot.node.style.height = `${slot.height}px`;
    slot.node.style.visibility = '';
  }
  columns.style.height = `${Math.max(0, Math.max(...heights) - gap)}px`;
  return { columns: count, useful: maxColumns };
}

/** Width limits used by the resize handles (window-space, includes chrome). */
export function widthLimits(card) {
  const columns = card.querySelector(':scope > .columns');
  if (!columns) return { min: 260, max: 520 };
  const flowing = [...columns.children].filter((node) => node.classList.contains('section') && !node.dataset.fullRow).length;
  const useful = Math.min(3, Math.max(1, flowing));
  const min = 264 + 40;
  const max = useful * 340 + (useful - 1) * 8 + 40;
  return { min: Math.round(min), max: Math.round(Math.max(min, max)) };
}
