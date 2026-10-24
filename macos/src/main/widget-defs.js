'use strict';

const crypto = require('node:crypto');

const CATEGORY_ORDER = ['hardware', 'time', 'internet', 'system'];
const CATEGORY_LABELS = {
  hardware: 'Hardware',
  time: 'Time',
  internet: 'Internet',
  system: 'System',
};

const HISTORY_FIELD = (order = 30) => ({
  key: 'historyCapacity',
  label: 'Graph time span',
  group: 'Graph',
  order,
  type: 'int',
  min: 30,
  max: 300,
  step: 10,
  unit: 's',
  description: 'How far back the graph goes (one bar per second at the default refresh rate).',
});

const WIDGETS = [
  {
    id: 'cpu',
    name: 'CPU',
    description: 'Total and per-core CPU load, temperature, clock speed',
    category: 'hardware',
    defaults: {
      showGraph: true,
      showPerCore: true,
      maxCoresShown: 256,
      showDetails: true,
      showClock: true,
      showTemperature: true,
      tempAlertC: 85,
      useFahrenheit: false,
      historyCapacity: 90,
    },
    fields: [
      { key: 'showGraph', label: 'Load graph', group: 'Sections', order: 1, type: 'bool' },
      { key: 'showPerCore', label: 'Per-core rings', group: 'Sections', order: 2, type: 'bool', description: 'One ring per logical processor (thread).' },
      { key: 'maxCoresShown', label: 'Cores shown', group: 'Sections', order: 3, type: 'int', min: 1, max: 256, maxSource: 'cpuThreads', unit: 'cores' },
      { key: 'showDetails', label: 'Details list', group: 'Sections', order: 4, type: 'bool', description: 'Temperature, clock, power, voltage and fan.' },
      { key: 'showClock', label: 'Clock speed in header', group: 'Header', order: 10, type: 'bool' },
      { key: 'showTemperature', label: 'Temperature in header', group: 'Header', order: 11, type: 'bool' },
      { key: 'tempAlertC', label: 'Hot warning above', group: 'Alerts', order: 20, type: 'int', min: 60, max: 100, step: 1, unit: '°C', description: 'Shows a red HOT badge when the CPU gets this warm.' },
      { key: 'useFahrenheit', label: 'Fahrenheit', group: 'Alerts', order: 21, type: 'bool' },
      HISTORY_FIELD(),
    ],
  },
  {
    id: 'gpu',
    name: 'GPU',
    description: 'GPU load, VRAM usage, temperature and clock speed',
    category: 'hardware',
    defaults: {
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
    },
    fields: [
      { key: 'gpuIndex', label: 'Graphics card', group: 'Source', order: 0, type: 'int', hidden: true },
      { key: 'showGraph', label: 'Load graph', group: 'Sections', order: 1, type: 'bool' },
      { key: 'showRings', label: 'Gauges', group: 'Sections', order: 2, type: 'bool', description: 'Load / VRAM / temperature / clock rings.' },
      { key: 'showDetails', label: 'Details list', group: 'Sections', order: 3, type: 'bool' },
      { key: 'showVram', label: 'Video memory', group: 'Values', order: 10, type: 'bool' },
      { key: 'showTemperature', label: 'Temperature', group: 'Values', order: 11, type: 'bool' },
      { key: 'showClock', label: 'Clock speed', group: 'Values', order: 12, type: 'bool' },
      { key: 'tempAlertC', label: 'Hot warning above', group: 'Alerts', order: 20, type: 'int', min: 60, max: 100, step: 1, unit: '°C', description: 'Shows a red HOT badge when the GPU gets this warm.' },
      { key: 'useFahrenheit', label: 'Fahrenheit', group: 'Alerts', order: 21, type: 'bool' },
      HISTORY_FIELD(),
    ],
  },
  {
    id: 'ram',
    name: 'Memory',
    description: 'RAM usage percent, used/total amount and history',
    category: 'hardware',
    defaults: {
      showUsageGraph: false,
      showDetails: true,
      showProcesses: true,
      processCount: 5,
      usageAlertPercent: 90,
      historyCapacity: 90,
    },
    fields: [
      { key: 'showUsageGraph', label: 'Usage graph', group: 'Sections', order: 1, type: 'bool', description: 'Memory use history under the rings.' },
      { key: 'showDetails', label: 'Memory breakdown', group: 'Sections', order: 2, type: 'bool', description: 'In use, available, swap and installed memory.' },
      { key: 'showProcesses', label: 'Top apps', group: 'Sections', order: 3, type: 'bool', description: 'Apps using the most memory, refreshed every 3 seconds.' },
      { key: 'processCount', label: 'Apps listed', group: 'Sections', order: 4, type: 'int', min: 1, max: 30, unit: 'apps' },
      { key: 'usageAlertPercent', label: 'Warning above', group: 'Alerts', order: 20, type: 'int', min: 50, max: 100, step: 1, unit: '%', description: 'Shows a red badge when memory use passes this level.' },
      HISTORY_FIELD(),
    ],
  },
  {
    id: 'network',
    name: 'Network',
    description: 'Download and upload rates, adapter and IP address',
    category: 'hardware',
    defaults: {
      adapterName: null,
      useFastestAdapter: true,
      useBits: false,
      showGraph: true,
      showPublicIp: true,
      showIp: true,
      showTotals: true,
      historyCapacity: 90,
    },
    fields: [
      { key: 'adapterName', label: 'Network adapter', group: 'Source', order: 0, type: 'string', options: 'adapters', description: 'Automatic follows the adapter with the most traffic.' },
      { key: 'useFastestAdapter', label: 'Follow busiest adapter', hidden: true, group: 'Source', order: 1, type: 'bool' },
      { key: 'useBits', label: 'Speed in bits (Mbps)', group: 'Units', order: 5, type: 'bool', description: 'Matches how internet plans and speed tests are measured. Off = bytes (MB/s).' },
      { key: 'showGraph', label: 'Traffic graph', group: 'Sections', order: 10, type: 'bool' },
      { key: 'showPublicIp', label: 'Public IP address', group: 'Sections', order: 11, type: 'bool', description: 'Looked up at api.ipify.org every 30 minutes.' },
      { key: 'showIp', label: 'Local IP address', group: 'Sections', order: 12, type: 'bool' },
      { key: 'showTotals', label: 'Data used this session', group: 'Sections', order: 13, type: 'bool' },
      HISTORY_FIELD(),
    ],
  },
  {
    id: 'drive',
    name: 'Storage',
    description: 'Drive capacity usage and disk read/write rates',
    category: 'hardware',
    defaults: {
      preferredDrive: null,
      showAllDrives: true,
      showDrives: true,
      showActivity: true,
      lowSpacePercent: 10,
      historyCapacity: 90,
    },
    fields: [
      { key: 'preferredDrive', label: 'Drives', group: 'Source', order: 0, type: 'string', options: 'drives', description: 'Show every drive, or just one.' },
      { key: 'showAllDrives', label: 'All drives', hidden: true, group: 'Source', order: 1, type: 'bool' },
      { key: 'showDrives', label: 'Drive list', group: 'Sections', order: 10, type: 'bool' },
      { key: 'showActivity', label: 'Read / write activity', group: 'Sections', order: 11, type: 'bool' },
      { key: 'lowSpacePercent', label: 'Low space warning below', group: 'Alerts', order: 20, type: 'int', min: 1, max: 50, step: 1, unit: '% free', description: 'Rings turn red when a drive gets this full.' },
      HISTORY_FIELD(),
    ],
  },
  {
    id: 'sensors',
    name: 'Sensors',
    description: 'CPU / GPU temperatures, fans and every temperature sensor',
    category: 'hardware',
    defaults: {
      showTemperatures: true,
      maxTemperatureRows: 12,
      showFans: true,
      fanFullScaleRpm: 2500,
      tempAlertC: 85,
      useFahrenheit: false,
    },
    fields: [
      { key: 'showTemperatures', label: 'Temperature list', group: 'Sections', order: 1, type: 'bool' },
      { key: 'maxTemperatureRows', label: 'Sensors listed', group: 'Sections', order: 2, type: 'int', min: 4, max: 30, unit: 'rows', description: 'The hottest sensors are listed first.' },
      { key: 'showFans', label: 'Fans', group: 'Sections', order: 3, type: 'bool' },
      { key: 'fanFullScaleRpm', label: 'Max fan speed', group: 'Sections', order: 4, type: 'int', min: 500, max: 5000, step: 100, unit: 'RPM', description: 'Fan rings are full at this speed.' },
      { key: 'tempAlertC', label: 'Hot warning above', group: 'Alerts', order: 10, type: 'int', min: 60, max: 100, step: 1, unit: '°C' },
      { key: 'useFahrenheit', label: 'Fahrenheit', group: 'Alerts', order: 11, type: 'bool' },
    ],
  },
  {
    id: 'clock',
    name: 'Date & Time',
    description: 'Clock with date and a month calendar',
    category: 'time',
    defaults: {
      showSeconds: true,
      use24Hour: false,
      showDate: true,
      showCalendar: true,
      weekStartsMonday: true,
      formatOverride: null,
    },
    fields: [
      { key: 'showSeconds', label: 'Seconds', group: 'Clock', order: 1, type: 'bool' },
      { key: 'use24Hour', label: '24-hour time', group: 'Clock', order: 2, type: 'bool' },
      { key: 'showDate', label: 'Date', group: 'Clock', order: 3, type: 'bool' },
      { key: 'showCalendar', label: 'Month calendar', group: 'Calendar', order: 10, type: 'bool' },
      { key: 'weekStartsMonday', label: 'Week starts Monday', group: 'Calendar', order: 11, type: 'bool' },
      { key: 'formatOverride', label: 'Custom time format', group: 'Advanced', order: 20, type: 'string', description: 'Format string, e.g. HH:mm:ss. Leave empty for automatic.' },
    ],
  },
  {
    id: 'timezone',
    name: 'World Clock',
    description: 'Current time in several configurable time zones',
    category: 'time',
    defaults: {
      timeZoneIds: [
        'UTC',
        'Eastern Standard Time',
        'Central European Standard Time',
        'Tokyo Standard Time',
        'Pacific Standard Time',
      ],
      showSeconds: false,
    },
    fields: [
      { key: 'timeZoneIds', label: 'Time zones', group: 'General', order: 1, type: 'list' },
      { key: 'showSeconds', label: 'Show seconds', group: 'General', order: 2, type: 'bool' },
    ],
  },
  {
    id: 'weather',
    name: 'Weather',
    description: 'Current conditions, air quality, hourly and daily forecast. Weather data by Open-Meteo.com (CC BY 4.0).',
    category: 'internet',
    defaults: {
      locationName: 'New York',
      resolvedLocation: 'New York',
      latitude: 40.7128,
      longitude: -74.006,
      showHourly: true,
      showDaily: true,
      dailyDays: 4,
      useFahrenheit: false,
      refreshMinutes: 30,
    },
    fields: [
      { key: 'locationName', label: 'City', group: 'Location', order: 1, type: 'string', lookup: 'city', description: 'Start typing (e.g. "kar") and pick your city from the list.' },
      { key: 'resolvedLocation', label: 'Resolved location', group: 'Location', order: 2, type: 'string', hidden: true },
      { key: 'latitude', label: 'Latitude', group: 'Advanced', order: 50, type: 'double', description: 'Filled in automatically from the city.' },
      { key: 'longitude', label: 'Longitude', group: 'Advanced', order: 51, type: 'double' },
      { key: 'showHourly', label: 'Hourly forecast', group: 'Display', order: 10, type: 'bool' },
      { key: 'showDaily', label: 'Daily forecast', group: 'Display', order: 11, type: 'bool', description: 'Next days with high / low temperature.' },
      { key: 'dailyDays', label: 'Days to show', group: 'Display', order: 12, type: 'int', min: 1, max: 6, step: 1, unit: 'days' },
      { key: 'useFahrenheit', label: 'Fahrenheit', group: 'Display', order: 13, type: 'bool' },
      { key: 'refreshMinutes', label: 'Refresh every', group: 'Display', order: 14, type: 'int', min: 10, max: 180, step: 5, unit: 'min' },
    ],
  },
  {
    id: 'media',
    name: 'Now Playing',
    description: 'Currently playing media title, artist and album art',
    category: 'system',
    defaults: { showAppName: true, showThumbnail: true },
    fields: [
      { key: 'showAppName', label: 'Show app name', group: 'General', order: 1, type: 'bool' },
      { key: 'showThumbnail', label: 'Show album art', group: 'General', order: 2, type: 'bool' },
    ],
  },
  {
    id: 'recyclebin',
    name: 'Trash',
    description: 'Trash item count and size with a one-click empty action',
    category: 'system',
    defaults: { showSize: true, refreshSeconds: 5 },
    fields: [
      { key: 'showSize', label: 'Show size', group: 'General', order: 1, type: 'bool' },
      { key: 'refreshSeconds', label: 'Refresh every', group: 'General', order: 2, type: 'int', min: 2, max: 300, step: 1, unit: 's' },
    ],
  },
];

const byId = new Map(WIDGETS.map((w) => [w.id, w]));

const DISPLAY_NAMES = Object.fromEntries(WIDGETS.map((w) => [w.id, w.name]));
const DESCRIPTIONS = Object.fromEntries(WIDGETS.map((w) => [w.id, w.description]));

const DEFAULT_WIDGET_IDS = ['cpu', 'gpu', 'ram', 'network', 'drive', 'sensors'];

const PRESET_NAMES = ['Default', 'Minimal', 'Gaming', 'Work', 'Streaming'];

const PRESET_DEFINITIONS = {
  Default: { theme: 'dark', items: ['cpu', 'gpu', 'ram', 'network', 'drive', 'sensors'] },
  Minimal: { theme: 'dark', items: ['clock', 'cpu', 'ram', 'network'] },
  Gaming: { theme: 'dark', items: ['cpu', 'gpu', 'sensors', 'ram', 'network', 'drive'] },
  Work: { theme: 'light', items: ['clock', 'cpu', 'ram', 'drive', 'network', 'timezone'] },
  Streaming: { theme: 'dark', items: ['clock', 'cpu', 'gpu', 'ram', 'network', 'drive', 'media'] },
};

function widget(id) {
  return byId.get(String(id).toLowerCase()) || null;
}

function createDefaultSettings(id) {
  const def = widget(id);
  return def ? JSON.parse(JSON.stringify(def.defaults)) : {};
}

function normalizeSettings(id, value) {
  const defaults = createDefaultSettings(id);
  if (!value || typeof value !== 'object') return defaults;
  const out = { ...defaults };
  for (const field of widget(id)?.fields || []) {
    if (Object.prototype.hasOwnProperty.call(value, field.key) && value[field.key] !== undefined) {
      out[field.key] = value[field.key];
    } else if (Object.prototype.hasOwnProperty.call(value, field.key)) {
      out[field.key] = value[field.key];
    }
  }
  // Keep unknown keys from older/newer layouts (forward compatible).
  for (const [key, v] of Object.entries(value)) {
    if (!(key in out)) out[key] = v;
  }
  return out;
}

/** Groups + fields, in the same order the WPF settings editor used. */
function schemaFor(id) {
  const def = widget(id);
  if (!def) return [];
  const groups = new Map();
  for (const field of def.fields) {
    const meta = { ...field };
    const group = meta.group || 'General';
    if (!groups.has(group)) groups.set(group, []);
    groups.get(group).push(meta);
  }
  const order = (f) => (f.order == null ? 1000 : f.order);
  return [...groups.entries()]
    .map(([name, fields]) => ({ name, order: Math.min(...fields.map(order)), fields: fields.sort((a, b) => order(a) - order(b)) }))
    .sort((a, b) => a.order - b.order);
}

function hiddenFields(id) {
  return (widget(id)?.fields || []).filter((f) => f.hidden);
}

function placement(overrides = {}) {
  return {
    docked: true,
    hidden: false,
    x: 0,
    y: 0,
    width: 320,
    height: 200,
    opacity: 1.0,
    alwaysOnTop: false,
    layer: null,
    autoPlace: false,
    clickThrough: false,
    snapToGrid: true,
    monitorIndex: 0,
    monitorDeviceId: null,
    monitorOffsetX: 0,
    monitorOffsetY: 0,
    heightLimit: 0,
    theme: null,
    largeSize: false,
    ...overrides,
  };
}

function item(widgetId, opts = {}) {
  return {
    instanceId: crypto.randomUUID(),
    widgetId,
    titleOverride: null,
    settingsJson: '',
    placement: placement({
      docked: false,
      autoPlace: true,
      layer: 'normal',
      ...opts,
    }),
  };
}

function buildPreset(name) {
  const key = PRESET_NAMES.find((n) => n.toLowerCase() === String(name).toLowerCase());
  if (!key) return null;
  const def = PRESET_DEFINITIONS[key];
  return {
    id: key.toLowerCase(),
    name: key,
    theme: def.theme,
    hasSavedState: false,
    items: def.items.map((id) => item(id)),
  };
}

function isPreset(name) {
  return PRESET_NAMES.some((n) => n.toLowerCase() === String(name).toLowerCase());
}

module.exports = {
  WIDGETS,
  CATEGORY_ORDER,
  CATEGORY_LABELS,
  DEFAULT_WIDGET_IDS,
  DISPLAY_NAMES,
  DESCRIPTIONS,
  PRESET_NAMES,
  widget,
  byId,
  createDefaultSettings,
  normalizeSettings,
  schemaFor,
  hiddenFields,
  placement,
  item,
  buildPreset,
  isPreset,
};
