'use strict';

/**
 * Theme tokens and card-appearance math.
 *
 * The WPF build stores colours as #AARRGGBB and paints widget cards through four
 * overrideable brushes (CardAppearance.cs). Every widget renderer gets the same
 * computed values as CSS custom properties, so opacity fades backgrounds only —
 * text, rings and graphs stay solid, exactly like the original.
 */

const THEMES = {
  dark: {
    windowBackdrop: '#C90F0F13',
    panel: '#F21C1C20',
    border: '#2EFFFFFF',
    borderSoft: '#14FFFFFF',
    section: '#0FFFFFFF',
    sectionBorder: '#0AFFFFFF',
    textPrimary: '#F5F5F7',
    textSecondary: '#C7C7CC',
    textTertiary: '#8E8E93',
    accent: '#3D8BFF',
    accentHover: '#5298FF',
    accentPressed: '#347EEB',
    accentSoft: '#333D8BFF',
    accentFill: '#263D8BFF',
    secondarySeries: '#9A9AA2',
    pink: '#FF4F8B',
    purple: '#A066FF',
    danger: '#FF453A',
    success: '#32D74B',
    warning: '#FFD60A',
    track: '#2BFFFFFF',
    onAccent: '#101014',
    scrollThumb: '#38FFFFFF',
    scrollThumbHover: '#8E8E93',
    sidebarBackground: 'rgba(15, 15, 19, 0.78)',
    shadowColor: 'rgba(0, 0, 0, 0.35)',
    shadowBlur: 16,
    shadowY: 2,
    scheme: 'dark',
  },
  light: {
    windowBackdrop: '#EBE9EEF5',
    panel: '#FFFFFFFF',
    border: '#1F1E293B',
    borderSoft: '#0F1E293B',
    section: '#FFF4F7FB',
    sectionBorder: '#0D1E293B',
    textPrimary: '#0F172A',
    textSecondary: '#475569',
    textTertiary: '#8391A7',
    accent: '#2563EB',
    accentHover: '#1D4ED8',
    accentPressed: '#1E40AF',
    accentSoft: '#2E2563EB',
    accentFill: '#1A2563EB',
    secondarySeries: '#94A3B8',
    pink: '#E11D74',
    purple: '#7C3AED',
    danger: '#DC2626',
    success: '#16A34A',
    warning: '#EA8A0C',
    track: '#FFE5EAF1',
    onAccent: '#FFFFFF',
    scrollThumb: '#98A3B4',
    scrollThumbHover: '#64748B',
    sidebarBackground: 'rgba(236, 240, 246, 0.82)',
    shadowColor: 'rgba(30, 41, 59, 0.14)',
    shadowBlur: 22,
    shadowY: 3,
    scheme: 'light',
  },
};

function parseColor(value, fallback = '#00000000') {
  let hex = String(value || fallback).trim().replace(/^#/, '');
  if (hex.length === 3) {
    hex = hex.split('').map((c) => c + c).join('');
  }
  if (hex.length === 6) hex = `FF${hex}`;
  if (hex.length !== 8) hex = `00000000`;
  return {
    a: parseInt(hex.slice(0, 2), 16),
    r: parseInt(hex.slice(2, 4), 16),
    g: parseInt(hex.slice(4, 6), 16),
    b: parseInt(hex.slice(6, 8), 16),
  };
}

function alpha(color, factor, minAlpha = 0) {
  const a = Math.max(minAlpha, Math.min(255, Math.round(color.a * Math.max(0, Math.min(1, factor)))));
  return { ...color, a };
}

function over(top, bottom) {
  const ta = top.a / 255;
  const ba = bottom.a / 255;
  const outA = ta + ba * (1 - ta);
  if (outA <= 0) return { a: 0, r: 0, g: 0, b: 0 };
  const mix = (t, b) => Math.round((t * ta + b * ba * (1 - ta)) / outA);
  return {
    a: Math.round(outA * 255),
    r: mix(top.r, bottom.r),
    g: mix(top.g, bottom.g),
    b: mix(top.b, bottom.b),
  };
}

function css(color) {
  const a = Math.round((color.a / 255) * 10000) / 10000;
  return `rgba(${color.r}, ${color.g}, ${color.b}, ${a})`;
}

function themeName(value) {
  return String(value || '').toLowerCase() === 'light' ? 'light' : 'dark';
}

function tokensFor(name) {
  const theme = THEMES[themeName(name)];
  const out = {};
  for (const [key, value] of Object.entries(theme)) {
    if (key === 'shadowBlur' || key === 'shadowY' || key === 'scheme') {
      out[key] = value;
    } else if (key === 'sidebarBackground') {
      out[key] = value;
    } else if (key === 'shadowColor') {
      out[key] = value;
    } else {
      out[key] = css(parseColor(value));
    }
  }
  out.name = themeName(name);
  out.shadow = `0 ${theme.shadowY}px ${theme.shadowBlur}px ${theme.shadowColor}`;
  out.shadowSmall = `0 1px 6px ${theme.shadowColor}`;
  return out;
}

/**
 * Port of CardAppearance.Apply. Returns CSS colours for the four card brushes
 * plus the soft external shadow (only drawn while the outer card frame is on).
 */
function cardAppearance(theme, opacity = 1, showFrame = false) {
  const palette = THEMES[themeName(theme)];
  const panel = parseColor(palette.panel);
  const border = parseColor(palette.border);
  const section = parseColor(palette.section);
  const sectionBorder = parseColor(palette.sectionBorder);
  const value = Math.max(0, Math.min(1, Number.isFinite(opacity) ? opacity : 1));

  let cardBg;
  let cardBorder;
  let sectionBg;
  let sectionBorderColor;

  if (showFrame) {
    cardBg = alpha(panel, value, 1);
    cardBorder = alpha(border, value, 0);
    sectionBg = alpha(section, value, 0);
    sectionBorderColor = alpha(sectionBorder, value, 0);
  } else {
    // Sections take the composited card colour and float on their own.
    cardBg = { a: 1, r: 0, g: 0, b: 0 };
    cardBorder = { a: 0, r: 0, g: 0, b: 0 };
    sectionBg = alpha(over(section, panel), value, 1);
    sectionBorderColor = alpha(border, value * 0.7, 0);
  }

  return {
    cardBg: css(cardBg),
    cardBorder: css(cardBorder),
    sectionBg: css(sectionBg),
    sectionBorder: css(sectionBorderColor),
    shadow: showFrame ? `0 ${palette.shadowY}px ${palette.shadowBlur}px ${palette.shadowColor}` : 'none',
    theme: themeName(theme),
    showFrame: Boolean(showFrame),
  };
}

module.exports = {
  THEMES,
  themeName,
  tokensFor,
  cardAppearance,
  parseColor,
  css,
};
