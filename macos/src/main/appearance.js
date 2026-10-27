'use strict';

const { tokensFor, cardAppearance, themeName } = require('./theme');

/** Theme token name → CSS custom property used by every renderer. */
const VAR_MAP = {
  windowBackdrop: '--bg',
  panel: '--panel-bg',
  border: '--panel-border',
  borderSoft: '--panel-border-soft',
  section: '--section-bg',
  sectionBorder: '--section-border',
  textPrimary: '--text-primary',
  textSecondary: '--text-secondary',
  textTertiary: '--text-tertiary',
  accent: '--accent',
  accentHover: '--accent-hover',
  accentPressed: '--accent-pressed',
  accentSoft: '--accent-soft',
  accentFill: '--accent-fill',
  secondarySeries: '--secondary',
  pink: '--pink',
  purple: '--purple',
  danger: '--danger',
  success: '--success',
  warning: '--warning',
  track: '--track',
  onAccent: '--on-accent',
  scrollThumb: '--scroll-thumb',
  scrollThumbHover: '--scroll-thumb-hover',
  sidebarBackground: '--sidebar-tint',
};

/**
 * CSS variables for one widget/card. Mirrors the WPF CardAppearance.Apply math:
 * opacity fades only card/section backgrounds and borders, never text.
 *
 * @param {string} theme
 * @param {{opacity?:number, theme?:string|null, largeSize?:boolean}} placement
 * @param {{cardFrameV2?:boolean}} settings
 * @param {{scale?:number}} [options]
 */
function appearanceFor(theme, placement = {}, settings = {}, options = {}) {
  const actualTheme = themeName(placement.theme || theme);
  const tokens = tokensFor(actualTheme);
  const vars = {};

  for (const [key, cssVar] of Object.entries(VAR_MAP)) {
    if (tokens[key] != null) vars[cssVar] = tokens[key];
  }

  const card = cardAppearance(actualTheme, Number(placement.opacity ?? 1), Boolean(settings.cardFrameV2));
  vars['--card-bg'] = card.cardBg;
  vars['--card-border'] = card.cardBorder;
  vars['--section-bg'] = card.sectionBg;
  vars['--section-border'] = card.sectionBorder;
  vars['--card-shadow'] = card.shadow;
  vars['--scheme'] = card.theme;

  return {
    vars,
    scale: options.scale ?? 1,
    showFrame: card.showFrame,
    theme: actualTheme,
  };
}

module.exports = { appearanceFor, VAR_MAP };
