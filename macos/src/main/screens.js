'use strict';

const { screen } = require('electron');

/**
 * Monitor helpers. Electron reports display bounds in DIPs (points), which is
 * exactly what BrowserWindow uses, so no DPI conversion is needed. Layouts keep
 * a stable display id plus the offset from that display's work-area origin,
 * mirroring the way the Windows build anchors widgets.
 */

function all() {
  return screen.getAllDisplays();
}

function primary() {
  return screen.getPrimaryDisplay();
}

function idOf(display) {
  return display ? `display-${display.id}` : '';
}

function displayById(id) {
  if (!id) return null;
  return all().find((d) => idOf(d) === String(id)) || null;
}

function workArea(display) {
  const d = display || primary();
  const area = d.workArea || d.bounds;
  return { x: area.x, y: area.y, width: area.width, height: area.height };
}

function primaryWorkArea() {
  return workArea(primary());
}

function workAreaOfId(id) {
  return workArea(displayById(id));
}

/** Human label used by App settings and "Identify screens". */
function labelOf(display) {
  const d = display;
  const size = `${Math.round(d.bounds.width)}×${Math.round(d.bounds.height)}`;
  const name = String(d.label || '').trim();
  if (d.id === primary().id) {
    return `${name ? `${name} — ` : 'Main screen — '}${size}`;
  }
  const p = primary().bounds;
  let position = 'Screen';
  if (d.bounds.x < p.x) position = 'Left screen';
  else if (d.bounds.x > p.x) position = 'Right screen';
  else if (d.bounds.y < p.y) position = 'Above';
  else if (d.bounds.y > p.y) position = 'Below';
  return `${name ? `${position}: ${name} — ` : `${position} — `}${size}`;
}

function describeMonitors() {
  return all().map((d) => ({ id: idOf(d), display: d, label: labelOf(d) }));
}

function monitorAt(point) {
  const found = screen.getDisplayNearestPoint({ x: Math.round(point.x), y: Math.round(point.y) });
  return found || primary();
}

function isMainScreen(id) {
  return !id || String(id) === idOf(primary());
}

function clampToWorkArea(rect, area) {
  const work = area || primaryWorkArea();
  // Keep at least 48 px of the widget reachable in both axes (upstream rule).
  const minX = work.x - Math.max(0, rect.width - 48);
  const maxX = work.x + work.width - 48;
  const minY = work.y - Math.max(0, rect.height - 48);
  const maxY = work.y + work.height - 48;
  return {
    x: Math.round(Math.min(Math.max(rect.x, minX), maxX)),
    y: Math.round(Math.min(Math.max(rect.y, minY), maxY)),
    width: Math.round(rect.width),
    height: Math.round(rect.height),
  };
}

/** Convert a screen-space rect to a monitor-anchored offset. */
function anchorFor(rect) {
  const display = monitorAt({ x: rect.x + rect.width / 2, y: rect.y + Math.min(40, rect.height) / 2 });
  const work = workArea(display);
  return {
    monitorDeviceId: idOf(display),
    monitorIndex: display.id,
    monitorOffsetX: Math.round((rect.x - work.x) * 100) / 100,
    monitorOffsetY: Math.round((rect.y - work.y) * 100) / 100,
  };
}

/** Restore a saved monitor offset, keeping intentional edge placement reachable. */
function restoreForAnchor(anchor, width, height) {
  const saved = displayById(anchor.monitorDeviceId);
  const display = saved || primary();
  const work = workArea(display);
  const x = Math.min(
    Math.max(anchor.monitorOffsetX || 0, -Math.max(0, width - 48)),
    Math.max(0, work.width - 48),
  );
  const y = Math.min(
    Math.max(anchor.monitorOffsetY || 0, -Math.max(0, height - 48)),
    Math.max(0, work.height - 48),
  );
  return { x: Math.round(work.x + x), y: Math.round(work.y + y) };
}

/** Initial column arrangement (port of FloatingPlacement.ArrangeInitial). */
function arrangeInitial(work, sizes) {
  const GAP = 12;
  const MARGIN = 16;
  if (!sizes.length) return [];
  const columns = Math.min(
    sizes.length,
    Math.min(3, Math.max(1, Math.floor((work.width - 2 * MARGIN + GAP) / (220 + GAP)))),
  );
  const rows = Math.ceil(sizes.length / columns);
  const width = Math.min(320, Math.max(40, (work.width - 2 * MARGIN - (columns - 1) * GAP) / columns));
  const cap = Math.max(24, (work.height - 2 * MARGIN - (rows - 1) * GAP) / rows);
  const result = new Array(sizes.length);
  for (let column = 0; column < columns; column += 1) {
    let y = work.y + MARGIN;
    for (let row = 0; row < rows; row += 1) {
      const i = column * rows + row;
      if (i >= sizes.length) break;
      const height = Math.min(cap, Math.max(24, sizes[i].height));
      result[i] = {
        x: Math.round(work.x + work.width - MARGIN - width - column * (width + GAP)),
        y: Math.round(y),
        width: Math.round(width),
        height: Math.round(height),
        heightLimit: Math.round(cap),
      };
      y += height + GAP;
    }
  }
  return result;
}

/**
 * Tidy auto-placement used for a single widget added while others are already
 * on screen: start at the right edge, stack down, move left when a column is
 * full (port of LayoutController.ResolveAutoPlace, without measuring).
 */
function resolveAutoPlace(work, size, occupied, sidebarReserve) {
  const GAP = 12;
  const MARGIN = 16;
  const w = Math.min(1200, Math.max(200, Math.round(size.width || 320)));
  const h = Math.min(1200, Math.max(120, Math.round(size.height || 200)));
  let rightLimit = work.x + work.width - MARGIN;
  let leftLimit = work.x + MARGIN;
  if (sidebarReserve && sidebarReserve.enabled) {
    const side = Math.max(0, sidebarReserve.width) + MARGIN;
    if (sidebarReserve.edge === 'left') leftLimit = work.x + side;
    else rightLimit = work.x + work.width - side;
  }

  let bestX = Math.max(work.x, rightLimit - w);
  let bestY = work.y + MARGIN;
  let found = false;
  for (let x = rightLimit - w; x >= leftLimit - 0.5 && !found; x -= w + GAP) {
    let y = work.y + MARGIN;
    while (y + h <= work.y + work.height - MARGIN + 0.5) {
      const probe = { x: x + 1, y: y + 1, width: w - 2, height: h - 2 };
      const blocker = occupied.find((o) => intersects(o, probe));
      if (!blocker) {
        bestX = x;
        bestY = y;
        found = true;
        break;
      }
      y = blocker.y + blocker.height + GAP;
    }
  }
  if (!found) {
    bestX = Math.max(work.x, Math.min(work.x + MARGIN + occupied.length * 28, work.x + work.width - w - MARGIN));
    bestY = Math.max(work.y, Math.min(work.y + MARGIN + occupied.length * 28, work.y + work.height - h - MARGIN));
  }
  return {
    x: Math.round(bestX),
    y: Math.round(bestY),
    width: w,
    height: h,
    heightLimit: Math.round(work.height - 2 * MARGIN),
  };
}

function intersects(a, b) {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
}

module.exports = {
  all,
  primary,
  idOf,
  displayById,
  workArea,
  primaryWorkArea,
  workAreaOfId,
  labelOf,
  describeMonitors,
  monitorAt,
  isMainScreen,
  clampToWorkArea,
  anchorFor,
  restoreForAnchor,
  arrangeInitial,
  resolveAutoPlace,
  intersects,
};
