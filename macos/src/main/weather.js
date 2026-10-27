'use strict';

/**
 * Open-Meteo (free, no API key) weather + air quality + city search.
 * The renderer receives the raw fields it needs and formats them exactly like
 * the Windows widget view-model.
 */

const FORECAST_URL = 'https://api.open-meteo.com/v1/forecast';
const AIR_URL = 'https://air-quality-api.open-meteo.com/v1/air-quality';
const GEO_URL = 'https://geocoding-api.open-meteo.com/v1/search';

const cache = new Map();

function num(value) {
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

async function fetchJson(url, timeout = 9000) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeout);
  try {
    const response = await fetch(url, {
      signal: controller.signal,
      headers: { 'User-Agent': 'DeskStats-macOS/1.0' },
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return await response.json();
  } finally {
    clearTimeout(timer);
  }
}

async function resolveCity(city) {
  const query = String(city || '').trim();
  if (!query) return null;
  const url = `${GEO_URL}?count=1&language=en&format=json&name=${encodeURIComponent(query)}`;
  const data = await fetchJson(url);
  const first = Array.isArray(data?.results) ? data.results[0] : null;
  if (!first) return null;
  return {
    name: first.name || query,
    latitude: Number(first.latitude),
    longitude: Number(first.longitude),
    resolved: query,
  };
}

/** Type-ahead places for the settings dialog. */
async function searchCities(query) {
  const text = String(query || '').trim();
  if (text.length < 2) return [];
  const url = `${GEO_URL}?count=8&language=en&format=json&name=${encodeURIComponent(text)}`;
  const data = await fetchJson(url);
  const seen = new Set();
  const results = [];
  for (const row of Array.isArray(data?.results) ? data.results : []) {
    const name = String(row.name || '').trim();
    const latitude = num(row.latitude);
    const longitude = num(row.longitude);
    if (!name || latitude == null || longitude == null) continue;
    const parts = [row.admin1, row.country].filter((v) => typeof v === 'string' && v.trim());
    const detail = parts.join(', ');
    const key = `${name}|${detail}`.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    results.push({ name, detail, latitude, longitude });
  }
  return results;
}

function first(values) {
  return Array.isArray(values) && values.length ? num(values[0]) : null;
}

function at(values, index) {
  return Array.isArray(values) && index < values.length ? num(values[index]) : null;
}

function cleanList(values, map = (v) => num(v)) {
  return Array.isArray(values) ? values.map(map) : [];
}

/**
 * @param {{latitude:number,longitude:number,locationName?:string,resolvedLocation?:string,
 *          showHourly?:boolean,dailyDays?:number,useFahrenheit?:boolean,refreshMinutes?:number}} options
 */
async function forecast(options = {}) {
  let latitude = num(options.latitude);
  let longitude = num(options.longitude);
  const city = String(options.locationName || '').trim();
  let resolved = String(options.resolvedLocation || '');

  // A typed city that differs from the resolved one is looked up first.
  if (city && city.toLowerCase() !== resolved.toLowerCase()) {
    try {
      const match = await resolveCity(city);
      if (!match) {
        return { ok: false, reason: 'city-not-found', city };
      }
      latitude = match.latitude;
      longitude = match.longitude;
      resolved = match.resolved;
    } catch {
      return { ok: false, reason: 'offline', city };
    }
  }

  if (latitude == null || longitude == null) {
    latitude = 40.7128;
    longitude = -74.006;
  }

  const key = `${latitude.toFixed(3)},${longitude.toFixed(3)}`;
  const hit = cache.get(key);
  if (hit && Date.now() - hit.at < 60_000) {
    return { ...hit.data, resolved };
  }

  const weatherUrl =
    `${FORECAST_URL}?latitude=${latitude}&longitude=${longitude}` +
    '&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m,pressure_msl,is_day' +
    '&hourly=temperature_2m,weather_code,precipitation_probability,uv_index,visibility,dew_point_2m' +
    '&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weather_code' +
    '&forecast_hours=7&forecast_days=7&timezone=auto';

  let raw;
  try {
    raw = await fetchJson(weatherUrl);
  } catch {
    if (hit) return { ...hit.data, resolved, stale: true };
    return { ok: false, reason: 'offline', city };
  }

  const current = raw.current || {};
  const hourly = raw.hourly || {};
  const daily = raw.daily || {};

  const data = {
    ok: true,
    latitude,
    longitude,
    resolved,
    timezone: raw.timezone || '',
    current: {
      time: current.time || '',
      temperature: num(current.temperature_2m),
      humidity: num(current.relative_humidity_2m),
      apparent: num(current.apparent_temperature),
      code: num(current.weather_code),
      windSpeed: num(current.wind_speed_10m),
      windDirection: num(current.wind_direction_10m),
      pressure: num(current.pressure_msl),
      isDay: num(current.is_day) === 1,
    },
    hourly: {
      time: Array.isArray(hourly.time) ? hourly.time : [],
      temperature: cleanList(hourly.temperature_2m),
      code: cleanList(hourly.weather_code),
      precipitation: cleanList(hourly.precipitation_probability),
      uv: cleanList(hourly.uv_index),
      visibility: cleanList(hourly.visibility),
      dewPoint: cleanList(hourly.dew_point_2m),
    },
    daily: {
      time: Array.isArray(daily.time) ? daily.time : [],
      high: cleanList(daily.temperature_2m_max),
      low: cleanList(daily.temperature_2m_min),
      precipitation: cleanList(daily.precipitation_probability_max),
      code: cleanList(daily.weather_code),
    },
    air: null,
    updated: Date.now(),
  };

  try {
    const airUrl =
      `${AIR_URL}?latitude=${latitude}&longitude=${longitude}&current=us_aqi,pm2_5&timezone=auto`;
    const air = await fetchJson(airUrl, 6000);
    data.air = {
      aqi: num(air?.current?.us_aqi),
      pm25: num(air?.current?.pm2_5),
    };
  } catch {
    data.air = null;
  }

  cache.set(key, { at: Date.now(), data });
  return { ...data, resolved };
}

/** Data needed by the settings window city picker. */
module.exports = { forecast, searchCities, resolveCity, first, at };
