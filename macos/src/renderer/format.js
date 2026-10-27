/* Unit + date formatting, ported from SysDash.Core.Units.UnitFormat and the
   clock/weather/timezone view models. */

const INV = 'en-US';

function fixed(value, digits) {
  return Number(value).toFixed(digits);
}

export function bytes(value) {
  const n = Math.max(0, Number(value) || 0);
  if (n < 1024) return `${Math.round(n)} B`;
  const kb = n / 1024;
  if (kb < 1024) return `${trim(kb, 1)} KB`;
  const mb = kb / 1024;
  if (mb < 1024) return `${trim(mb, 1)} MB`;
  const gb = mb / 1024;
  if (gb < 1024) return `${trim(gb, 2)} GB`;
  return `${trim(gb / 1024, 2)} TB`;
}

function trim(value, digits) {
  return Number(value)
    .toFixed(digits)
    .replace(/\.?0+$/, '');
}

export function bytesPerSecond(value) {
  return `${bytes(value)}/s`;
}

export function bitsPerSecond(value) {
  const bits = Math.max(0, Number(value) || 0) * 8;
  if (bits < 1000) return `${Math.round(bits)} bps`;
  const kb = bits / 1000;
  if (kb < 1000) return `${trim(kb, 1)} kbps`;
  const mb = kb / 1000;
  if (mb < 1000) return `${trim(mb, 1)} Mbps`;
  return `${trim(mb / 1000, 2)} Gbps`;
}

export function rate(value, useBits) {
  return useBits ? bitsPerSecond(value) : bytesPerSecond(value);
}

export function percent(value) {
  return `${Math.round(Number(value) || 0)}%`;
}

export function celsius(value) {
  return `${Math.round(Number(value) || 0)}°C`;
}

export function fahrenheit(value) {
  return `${Math.round((Number(value) || 0) * 1.8 + 32)}°F`;
}

export function temperature(value, useFahrenheit) {
  return useFahrenheit ? fahrenheit(value) : celsius(value);
}

export function voltage(value) {
  return `${fixed(Number(value) || 0, 2)} V`;
}

export function rpm(value) {
  return `${Math.round(Number(value) || 0)} RPM`;
}

export function gigahertz(mhz) {
  return `${fixed((Number(mhz) || 0) / 1000, 2)} GHz`;
}

export function megahertz(mhz) {
  return `${Math.round(Number(mhz) || 0)} MHz`;
}

export function clockSpeed(mhz, giga) {
  return giga ? gigahertz(mhz) : megahertz(mhz);
}

export function clamp(value, min, max) {
  const n = Number(value);
  if (!Number.isFinite(n)) return min;
  return Math.min(max, Math.max(min, n));
}

export function titleCase(text) {
  const trimmed = String(text || '').trim();
  if (!trimmed) return '';
  return trimmed === trimmed.toLowerCase() || trimmed === trimmed.toUpperCase()
    ? trimmed
        .toLowerCase()
        .replace(/(^|[\s-])([a-z])/g, (_m, p, c) => p + c.toUpperCase())
    : trimmed;
}

export function shortenCpuName(name) {
  return String(name || '')
    .split('@')[0]
    .replace(/\s+\d+-Core\s+Processor|\s+Processor|\(R\)|\(TM\)|\s+CPU/gi, '')
    .trim();
}

export function shortenGpuName(name) {
  const text = String(name || '');
  for (const marker of ['GeForce', 'Radeon', 'Arc', 'NVIDIA', 'AMD', 'Intel']) {
    const index = text.toLowerCase().indexOf(marker.toLowerCase());
    if (index >= 0) return text.slice(index);
  }
  return text;
}

const WINDOWS_ZONES = {
  UTC: 'UTC',
  'Eastern Standard Time': 'America/New_York',
  'Central Standard Time': 'America/Chicago',
  'Mountain Standard Time': 'America/Denver',
  'Pacific Standard Time': 'America/Los_Angeles',
  'Alaskan Standard Time': 'America/Anchorage',
  'Hawaiian Standard Time': 'Pacific/Honolulu',
  'Atlantic Standard Time': 'America/Halifax',
  'Newfoundland Standard Time': 'America/St_Johns',
  'GMT Standard Time': 'Europe/London',
  'W. Europe Standard Time': 'Europe/Berlin',
  'Central European Standard Time': 'Europe/Warsaw',
  'Central Europe Standard Time': 'Europe/Budapest',
  'Romance Standard Time': 'Europe/Paris',
  'E. Europe Standard Time': 'Europe/Chisinau',
  'FLE Standard Time': 'Europe/Kiev',
  'GTB Standard Time': 'Europe/Bucharest',
  'Russian Standard Time': 'Europe/Moscow',
  'Turkey Standard Time': 'Europe/Istanbul',
  'Israel Standard Time': 'Asia/Jerusalem',
  'Arabian Standard Time': 'Asia/Dubai',
  'Iran Standard Time': 'Asia/Tehran',
  'India Standard Time': 'Asia/Kolkata',
  'Pakistan Standard Time': 'Asia/Karachi',
  'Bangladesh Standard Time': 'Asia/Dhaka',
  'SE Asia Standard Time': 'Asia/Bangkok',
  'China Standard Time': 'Asia/Shanghai',
  'Hong Kong Standard Time': 'Asia/Hong_Kong',
  'Singapore Standard Time': 'Asia/Singapore',
  'Taipei Standard Time': 'Asia/Taipei',
  'Tokyo Standard Time': 'Asia/Tokyo',
  'Korea Standard Time': 'Asia/Seoul',
  'AUS Eastern Standard Time': 'Australia/Sydney',
  'New Zealand Standard Time': 'Pacific/Auckland',
  'South Africa Standard Time': 'Africa/Johannesburg',
  'Egypt Standard Time': 'Africa/Cairo',
  'Brazil Standard Time': 'America/Sao_Paulo',
  'Argentina Standard Time': 'America/Argentina/Buenos_Aires',
  'SA Pacific Standard Time': 'America/Bogota',
  'Pacific SA Standard Time': 'America/Santiago',
  'Cen. Australia Standard Time': 'Australia/Adelaide',
  'W. Australia Standard Time': 'Australia/Perth',
  'E. South America Standard Time': 'America/Sao_Paulo',
  'Mexico Standard Time': 'America/Mexico_City',
  'Central America Standard Time': 'America/Guatemala',
  'US Eastern Standard Time': 'America/New_York',
  'US Mountain Standard Time': 'America/Phoenix',
  'Canada Central Standard Time': 'America/Regina',
  'Morocco Standard Time': 'Africa/Casablanca',
  'West Asia Standard Time': 'Asia/Tashkent',
  'Nepal Standard Time': 'Asia/Kathmandu',
  'Sri Lanka Standard Time': 'Asia/Colombo',
  'Myanmar Standard Time': 'Asia/Yangon',
  'Malay Peninsula Standard Time': 'Asia/Kuala_Lumpur',
  'Taiwan Standard Time': 'Asia/Taipei',
  'Cen. Australia Standard Time ': 'Australia/Adelaide',
};

export function ianaZone(id) {
  const key = String(id || '').trim();
  if (WINDOWS_ZONES[key]) return WINDOWS_ZONES[key];
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: key }).format(new Date());
    return key;
  } catch {
    return null;
  }
}

export function zoneLabel(id) {
  const text = String(id || '');
  const slash = text.lastIndexOf('/');
  if (slash >= 0) return text.slice(slash + 1).replace(/_/g, ' ');
  return text;
}

export function zoneTime(id, showSeconds, date = new Date()) {
  const zone = ianaZone(id);
  if (!zone) return '—';
  try {
    return new Intl.DateTimeFormat(INV, {
      timeZone: zone,
      hour: '2-digit',
      minute: '2-digit',
      ...(showSeconds ? { second: '2-digit' } : {}),
      hour12: false,
      hourCycle: 'h23',
    }).format(date);
  } catch {
    return '—';
  }
}

const padded = (n) => String(n).padStart(2, '0');

/** Converts the common .NET custom date tokens used by the clock widget. */
export function formatCustom(date, pattern) {
  const hours = date.getHours();
  const hour12 = hours % 12 === 0 ? 12 : hours % 12;
  const longDay = new Intl.DateTimeFormat(INV, { weekday: 'long' }).format(date);
  const shortDay = new Intl.DateTimeFormat(INV, { weekday: 'short' }).format(date);
  const longMonth = new Intl.DateTimeFormat(INV, { month: 'long' }).format(date);
  const shortMonth = new Intl.DateTimeFormat(INV, { month: 'short' }).format(date);
  const ampm = hours < 12 ? 'AM' : 'PM';
  const tokens = [
    ['HHHH', null],
    ['HH', padded(hours)],
    ['H', String(hours)],
    ['hh', padded(hour12)],
    ['h', String(hour12)],
    ['mm', padded(date.getMinutes())],
    ['m', String(date.getMinutes())],
    ['ss', padded(date.getSeconds())],
    ['s', String(date.getSeconds())],
    ['tt', ampm],
    ['t', ampm[0]],
    ['dddd', longDay],
    ['ddd', shortDay],
    ['dd', padded(date.getDate())],
    ['d', String(date.getDate())],
    ['MMMM', longMonth],
    ['MMM', shortMonth],
    ['MM', padded(date.getMonth() + 1)],
    ['M', String(date.getMonth() + 1)],
    ['yyyy', String(date.getFullYear())],
    ['yy', String(date.getFullYear()).slice(-2)],
  ];
  let output = '';
  for (let i = 0; i < pattern.length; ) {
    if (pattern[i] === '\\' && i + 1 < pattern.length) {
      output += pattern[i + 1];
      i += 2;
      continue;
    }
    if (pattern[i] === '"' || pattern[i] === "'") {
      const quote = pattern[i];
      let j = i + 1;
      while (j < pattern.length && pattern[j] !== quote) j += 1;
      output += pattern.slice(i + 1, j);
      i = j + 1;
      continue;
    }
    let matched = false;
    for (const [token, value] of tokens) {
      if (value != null && pattern.startsWith(token, i)) {
        output += value;
        i += token.length;
        matched = true;
        break;
      }
    }
    if (!matched) {
      output += pattern[i];
      i += 1;
    }
  }
  return output;
}

/** Main big clock text + separate seconds column, exactly like ClockViewModel. */
export function clockParts(date, settings) {
  const showSeconds = settings.showSeconds !== false;
  const use24Hour = settings.use24Hour === true;
  const override = String(settings.formatOverride || '').trim();
  if (override) {
    return { time: formatCustom(date, override), seconds: '' };
  }
  const h = use24Hour ? padded(date.getHours()) : String(date.getHours() % 12 || 12);
  const m = padded(date.getMinutes());
  const s = padded(date.getSeconds());
  const suffix = use24Hour ? '' : ` ${date.getHours() < 12 ? 'AM' : 'PM'}`;
  if (!showSeconds) return { time: `${h}:${m}${suffix}`, seconds: '' };
  return { time: `${h}:${m}${suffix}`, seconds: s };
}

export function dateLine(date) {
  const weekday = new Intl.DateTimeFormat(INV, { weekday: 'long' }).format(date);
  const month = new Intl.DateTimeFormat(INV, { month: 'long' }).format(date);
  return `${weekday}, ${date.getDate()} ${month} ${date.getFullYear()}`;
}

export function monthTitle(date) {
  return new Intl.DateTimeFormat(INV, { month: 'long', year: 'numeric' }).format(date).toUpperCase();
}

export function weekdayNames(startsMonday) {
  const format = new Intl.DateTimeFormat(INV, { weekday: 'short' });
  const names = [];
  const start = startsMonday ? 1 : 0; // 0 = Sunday
  // 2024-01-07 is a Sunday.
  for (let i = 0; i < 7; i += 1) {
    const day = new Date(2024, 0, 7 + ((start + i) % 7));
    names.push(format.format(day).toUpperCase());
  }
  return names;
}

const WMO = new Map([
  [0, ['Clear', '☀️']],
  [1, ['Mostly clear', '🌤️']],
  [2, ['Partly cloudy', '⛅']],
  [3, ['Overcast', '☁️']],
  [45, ['Fog', '🌫️']],
  [48, ['Fog', '🌫️']],
  [51, ['Drizzle', '🌦️']],
  [53, ['Drizzle', '🌦️']],
  [55, ['Drizzle', '🌦️']],
  [56, ['Freezing drizzle', '🌧️']],
  [57, ['Freezing drizzle', '🌧️']],
  [61, ['Rain', '🌧️']],
  [63, ['Rain', '🌧️']],
  [65, ['Rain', '🌧️']],
  [66, ['Freezing rain', '🌧️']],
  [67, ['Freezing rain', '🌧️']],
  [71, ['Snow', '🌨️']],
  [73, ['Snow', '🌨️']],
  [75, ['Snow', '🌨️']],
  [77, ['Snow grains', '🌨️']],
  [80, ['Rain showers', '🌧️']],
  [81, ['Rain showers', '🌧️']],
  [82, ['Rain showers', '🌧️']],
  [85, ['Snow showers', '🌨️']],
  [86, ['Snow showers', '🌨️']],
  [95, ['Thunderstorm', '⛈️']],
  [96, ['Hail storm', '⛈️']],
  [99, ['Hail storm', '⛈️']],
]);

export function weatherCode(code, night = false) {
  if (night && code === 0) return ['Clear', '🌙'];
  if (night && (code === 1 || code === 2)) {
    return [code === 1 ? 'Mostly clear' : 'Partly cloudy', '☁️'];
  }
  return WMO.get(Number(code)) || ['—', '🌡️'];
}

export function aqiLabel(aqi) {
  const value = Number(aqi);
  if (!Number.isFinite(value)) return '—';
  if (value <= 50) return 'Good';
  if (value <= 100) return 'Moderate';
  if (value <= 150) return 'Unhealthy for sensitive groups';
  if (value <= 200) return 'Unhealthy';
  if (value <= 300) return 'Very unhealthy';
  return 'Hazardous';
}

export function compass(degrees) {
  if (!Number.isFinite(Number(degrees))) return '';
  const arrows = ['↓', '↙', '←', '↖', '↑', '↗', '→', '↘'];
  const d = ((Number(degrees) % 360) + 360) % 360;
  return arrows[Math.round(d / 45) % 8];
}

export function shortHourLabel(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return String(iso || '');
  return `${padded(date.getHours())}:${padded(date.getMinutes())}`;
}

export function shortDayLabel(iso) {
  const date = new Date(`${iso}T12:00:00`);
  if (Number.isNaN(date.getTime())) return String(iso || '');
  return new Intl.DateTimeFormat(INV, { weekday: 'short' }).format(date);
}

export function weatherTemp(value, useFahrenheit, decimals = false) {
  const c = Number(value);
  if (!Number.isFinite(c)) return '—';
  const shown = useFahrenheit ? c * 1.8 + 32 : c;
  return `${shown.toFixed(decimals ? 1 : 0)}°`;
}

export function windDirection(degrees) {
  if (!Number.isFinite(Number(degrees))) return '';
  return compass(degrees);
}

export function updatedLabel(timestamp) {
  const date = new Date(timestamp || Date.now());
  return `Updated ${padded(date.getHours())}:${padded(date.getMinutes())}`;
}
