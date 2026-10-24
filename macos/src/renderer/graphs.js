/* SVG ports of RingGauge and BarGraph. */

const SVG_NS = 'http://www.w3.org/2000/svg';

function svgEl(name, attrs = {}) {
  const node = document.createElementNS(SVG_NS, name);
  for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, String(value));
  return node;
}

function pointOn(cx, cy, radius, degrees) {
  const rad = (degrees * Math.PI) / 180;
  return [cx + radius * Math.cos(rad), cy + radius * Math.sin(rad)];
}

function arcPath(cx, cy, radius, startFraction, sweepFraction) {
  const startAngle = -90 + startFraction * 360;
  const endAngle = startAngle + sweepFraction * 360;
  const [x1, y1] = pointOn(cx, cy, radius, startAngle);
  const [x2, y2] = pointOn(cx, cy, radius, endAngle);
  const large = sweepFraction > 0.5 ? 1 : 0;
  return `M ${x1} ${y1} A ${radius} ${radius} 0 ${large} 1 ${x2} ${y2}`;
}

/**
 * Circular gauge element. `color` and `track` are CSS colour values.
 */
export function ring({ value = 0, size = 58, thickness = 5, color = 'var(--accent)', track = 'var(--track)' }) {
  const wrap = document.createElement('div');
  wrap.className = 'ring';
  wrap.style.width = `${size}px`;
  wrap.style.height = `${size}px`;

  const svg = svgEl('svg', { viewBox: `0 0 ${size} ${size}`, 'aria-hidden': 'true' });
  const cx = size / 2;
  const cy = size / 2;
  const radius = Math.max(1, (size - thickness) / 2);

  svg.appendChild(
    svgEl('circle', {
      cx,
      cy,
      r: radius,
      fill: 'none',
      stroke: track,
      'stroke-width': thickness,
    }),
  );

  const fraction = Math.max(0, Math.min(1, Number(value) / 100 || 0));
  if (fraction > 0.001) {
    if (fraction >= 0.999) {
      svg.appendChild(
        svgEl('circle', {
          cx,
          cy,
          r: radius,
          fill: 'none',
          stroke: color,
          'stroke-width': thickness,
        }),
      );
    } else {
      svg.appendChild(
        svgEl('path', {
          d: arcPath(cx, cy, radius, 0, fraction),
          fill: 'none',
          stroke: color,
          'stroke-width': thickness,
          'stroke-linecap': 'round',
        }),
      );
    }
  }

  wrap.appendChild(svg);
  return wrap;
}

/** Adds a centred value/caption stack (used by every gauge). */
export function ringWithText(options, { text, caption, valueSize = 15, textColor }) {
  const wrap = ring(options);
  const center = document.createElement('div');
  center.className = 'ring-center';
  if (text != null) {
    const value = document.createElement('div');
    value.className = 'ring-value';
    value.style.fontSize = `${valueSize}px`;
    if (textColor) value.style.color = textColor;
    value.textContent = text;
    center.appendChild(value);
  }
  if (caption) {
    const label = document.createElement('div');
    label.className = 'ring-caption';
    label.textContent = caption;
    center.appendChild(label);
  }
  wrap.appendChild(center);
  return wrap;
}

function downsample(values, perBar) {
  if (!values.length || perBar <= 1) return values;
  const groups = Math.ceil(values.length / perBar);
  const result = new Array(groups).fill(0);
  let end = values.length;
  for (let g = groups - 1; g >= 0; g -= 1) {
    const start = Math.max(0, end - perBar);
    let peak = 0;
    for (let i = start; i < end; i += 1) {
      const v = Number(values[i]);
      if (Number.isFinite(v) && v > peak) peak = v;
    }
    result[g] = peak;
    end = start;
  }
  return result;
}

/**
 * History bars. `secondary` turns it into the mirrored network/disk chart.
 */
export function barGraph({
  values = [],
  secondary = null,
  maximum = 0,
  capacity = 0,
  height = 64,
  primary = 'var(--accent)',
  secondaryColor = 'var(--secondary)',
  className = '',
}) {
  const mirrored = Array.isArray(secondary);
  let max = Number(maximum) || 0;
  if (max <= 0) {
    for (const v of values) max = Math.max(max, Number(v) || 0);
    if (mirrored) for (const v of secondary) max = Math.max(max, Number(v) || 0);
    if (max <= 0) max = 1;
  }

  const wrap = document.createElement('div');
  wrap.className = `bar-graph-wrap ${className}`.trim();
  wrap.style.width = '100%';
  wrap.style.height = `${height}px`;

  const render = (width) => {
    const w = Math.max(8, Math.round(width || 240));
    const svg = svgEl('svg', {
      class: 'bar-graph',
      viewBox: `0 0 ${w} ${height}`,
      preserveAspectRatio: 'none',
      width: '100%',
      height,
      'aria-hidden': 'true',
    });

    const gap = 1.5;
    const baseBar = 2.5;
    let step = baseBar + gap;
    let slots = Math.max(1, Math.floor((w + gap) / step));
    let barWidth = baseBar;
    let primaryValues = values;
    let secondaryValues = secondary;

    if (capacity > 0) {
      const minStep = 3.5;
      const maxSlots = Math.max(1, Math.floor(w / minStep));
      const perBar = Math.max(1, Math.ceil(capacity / maxSlots));
      slots = Math.max(1, Math.ceil(capacity / perBar));
      step = w / slots;
      barWidth = Math.max(1, step * 0.6);
      if (perBar > 1) {
        primaryValues = downsample(primaryValues, perBar);
        if (mirrored) secondaryValues = downsample(secondaryValues, perBar);
      }
    }

    const baseline = mirrored ? height / 2 : height;
    const span = mirrored ? height / 2 - 1 : height - 1;

    const draw = (series, brush, upward) => {
      const list = Array.isArray(series) ? series : [];
      const count = Math.min(list.length, slots);
      const first = list.length - count;
      for (let i = 0; i < count; i += 1) {
        let v = Number(list[first + i]);
        if (!Number.isFinite(v) || v < 0) v = 0;
        const barHeight = Math.max(1, Math.min(1, v / max) * span);
        const x = w - (count - i) * step + (step - barWidth);
        const y = upward ? baseline - barHeight : baseline;
        svg.appendChild(svgEl('rect', { x, y, width: barWidth, height: barHeight, fill: brush }));
      }
    };

    draw(primaryValues, primary, true);
    if (mirrored) draw(secondaryValues, secondaryColor, false);
    wrap.replaceChildren(svg);
  };

  render(240);
  if (typeof ResizeObserver !== 'undefined') {
    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect?.width;
      if (width) render(width);
    });
    observer.observe(wrap);
  }
  return wrap;
}
