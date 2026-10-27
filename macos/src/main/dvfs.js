'use strict';

/**
 * Apple Silicon voltage + memory-speed helpers.
 *
 * macOS has no public voltage sensor on Apple Silicon, but the power manager
 * (IORegistry node "pmgr") publishes the DVFS tables the SoC itself uses:
 *
 *   voltage-statesN-sram  →  [frequency Hz (u32), SRAM rail mV (u32)] per state
 *   voltage-statesN       →  [period ticks (u32), core rail mV (u32)] per state
 *
 * Both tables have one entry per performance state, in the same order, so the
 * core voltage of state i is voltage-statesN[i].mV at voltage-statesN-sram[i].Hz.
 * The GPU table carries Hz directly in its non-SRAM table. Combined with the
 * live cluster/GPU frequency from macmon, this gives the voltage the SoC is
 * requesting right now (the same figure tools like asitop/socpowerbud derive).
 * Key numbers moved between chip generations (M5 renumbered them), so tables
 * are classified by content rather than by a hard-coded key number.
 */

function decodePairs(base64) {
  const buf = Buffer.from(base64.replace(/\s+/g, ''), 'base64');
  const pairs = [];
  for (let offset = 0; offset + 8 <= buf.length; offset += 8) {
    pairs.push([buf.readUInt32LE(offset), buf.readUInt32LE(offset + 4)]);
  }
  return pairs;
}

/** Pull every voltage-states* <data> blob out of `ioreg -a` plist XML. */
function parseVoltageStates(xml) {
  const tables = {};
  const re = /<key>(voltage-states\d+(?:-sram)?)<\/key>\s*<data>([\s\S]*?)<\/data>/g;
  let match;
  while ((match = re.exec(xml))) tables[match[1]] = decodePairs(match[2]);
  return tables;
}

const plausibleMv = (mv) => mv >= 300 && mv <= 1600;

/**
 * Turn raw tables into { cpu: [{maxMhz, states:[{mhz, mv}]}...] (low→high), gpu }.
 */
function classify(tables) {
  const cpu = [];
  let gpu = null;
  const numbers = new Set(Object.keys(tables).map((k) => Number(/\d+/.exec(k)[0])));
  for (const n of numbers) {
    const core = tables[`voltage-states${n}`];
    const sram = tables[`voltage-states${n}-sram`];
    if (!core || !sram || core.length !== sram.length || core.length < 2) continue;
    const coreHasHz = core.some(([f]) => f > 1e8);
    const sramHasHz = sram.some(([f]) => f > 1e8);
    if (!sramHasHz) continue;
    const states = [];
    for (let i = 0; i < core.length; i += 1) {
      const hz = coreHasHz ? core[i][0] : sram[i][0];
      const mv = core[i][1];
      if (!plausibleMv(mv) || hz <= 1e7) continue; // skip "off" states
      states.push({ mhz: hz / 1e6, mv });
    }
    if (states.length < 2) continue;
    states.sort((a, b) => a.mhz - b.mhz || a.mv - b.mv);
    // Some GPU tables list two frequencies per voltage step, which leaves the
    // sorted list non-monotonic; a higher clock never needs less voltage.
    for (let i = 1; i < states.length; i += 1) states[i].mv = Math.max(states[i].mv, states[i - 1].mv);
    const maxMhz = states[states.length - 1].mhz;
    // The GPU table is the one whose core table already lists Hz (and starts
    // with an off state); CPU core tables list period ticks instead.
    if (coreHasHz) {
      if (!gpu || maxMhz > gpu.maxMhz) gpu = { key: `voltage-states${n}`, maxMhz, states };
    } else {
      cpu.push({ key: `voltage-states${n}`, maxMhz, states });
    }
  }
  cpu.sort((a, b) => a.maxMhz - b.maxMhz);
  return { cpu, gpu };
}

/**
 * Voltage (V) at an average frequency. macmon's frequency is residency
 * weighted, so interpolate between the two neighbouring DVFS states.
 */
function voltsAt(table, mhz) {
  if (!table || !table.states.length) return null;
  const states = table.states;
  if (!(mhz > 0)) return states[0].mv / 1000; // idle → lowest active state
  if (mhz <= states[0].mhz) return states[0].mv / 1000;
  const last = states[states.length - 1];
  if (mhz >= last.mhz) return last.mv / 1000;
  for (let i = 1; i < states.length; i += 1) {
    const lo = states[i - 1];
    const hi = states[i];
    if (mhz <= hi.mhz) {
      const span = hi.mhz - lo.mhz;
      const t = span > 0 ? (mhz - lo.mhz) / span : 1;
      return (lo.mv + (hi.mv - lo.mv) * t) / 1000;
    }
  }
  return last.mv / 1000;
}

/**
 * Rated LPDDR data rate (MT/s) for the unified memory. macOS reports only the
 * memory type, so the speed comes from Apple's published spec for each chip.
 */
function ratedMemory(chipName, memType) {
  const chip = String(chipName || '');
  const type = String(memType || '').toUpperCase();
  const table = [
    [/\bM1\b(?!\s*(Pro|Max|Ultra))/i, 4266, 'LPDDR4X'],
    [/\bM1\s*(Pro|Max|Ultra)\b/i, 6400, 'LPDDR5'],
    [/\bM2\b/i, 6400, 'LPDDR5'],
    [/\bM3\b/i, 6400, 'LPDDR5'],
    [/\bM4\b(?!\s*(Pro|Max))/i, 7500, 'LPDDR5X'],
    [/\bM4\s*(Pro|Max)\b/i, 8533, 'LPDDR5X'],
    [/\bM5\b(?!\s*(Pro|Max|Ultra))/i, 9600, 'LPDDR5X'],
  ];
  for (const [re, mts, kind] of table) {
    if (re.test(chip)) return { mts, type: type || kind };
  }
  if (type === 'LPDDR4X') return { mts: 4266, type };
  if (type === 'LPDDR5') return { mts: 6400, type };
  return { mts: null, type: type || null };
}

module.exports = { parseVoltageStates, classify, voltsAt, ratedMemory };
