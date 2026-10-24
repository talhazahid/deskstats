'use strict';

/**
 * Builds an unsigned/ad-hoc-signed Apple Silicon .app bundle in dist/.
 * Usage: npm run pack
 *
 * No Xcode or Apple developer account is required: the bundle is assembled by
 * @electron/packager and then ad-hoc signed with the system `codesign`.
 */

const path = require('node:path');
const fs = require('node:fs');
const { execFileSync } = require('node:child_process');
const packager = require('@electron/packager');

const ROOT = path.join(__dirname, '..');
const DIST = path.join(ROOT, 'dist');
const pkg = require(path.join(ROOT, 'package.json'));

async function main() {
  fs.mkdirSync(DIST, { recursive: true });

  const paths = await packager({
    dir: ROOT,
    out: DIST,
    name: 'DeskStats',
    appBundleId: 'com.deskstats.macos',
    appCategoryType: 'public.app-category.utilities',
    appVersion: pkg.version,
    buildVersion: pkg.version,
    platform: 'darwin',
    arch: 'arm64',
    icon: path.join(ROOT, 'build', 'DeskStats.icns'),
    overwrite: true,
    prune: true,
    asar: false,
    ignore: [
      /^\/dist($|\/)/,
      /^\/upstream($|\/)/,
      /^\/build\/icon\.iconset($|\/)/,
      /^\/src_logo\.png$/,
      /^\/\.git($|\/)/,
      /^\/scripts($|\/)/,
      /^\/README\.md$/,
    ],
    extendInfo: {
      LSUIElement: true,
      NSHighResolutionCapable: true,
      LSMinimumSystemVersion: '11.0',
      NSHumanReadableCopyright: 'Copyright (C) 2026 DeskStats contributors — GPLv3',
      CFBundleDisplayName: 'DeskStats',
    },
  });

  // @electron/packager returns the app bundle path in most versions; guard
  // against versions that return the output directory instead.
  let appPath = paths[0];
  if (!appPath.endsWith('.app')) {
    const nested = path.join(appPath, 'DeskStats.app');
    if (fs.existsSync(nested)) appPath = nested;
  }
  console.log(`Packed: ${appPath}`);

  // Ad-hoc signature keeps Gatekeeper's "damaged" dialog away on the machine
  // that built it; users still need to allow it in Privacy & Security.
  try {
    execFileSync('codesign', ['--force', '--deep', '--sign', '-', appPath], { stdio: 'inherit' });
    console.log('Ad-hoc signed.');
  } catch (error) {
    console.warn('Ad-hoc signing skipped:', error.message);
  }

  const zipPath = path.join(DIST, `DeskStats-${pkg.version}-arm64.zip`);
  try {
    fs.rmSync(zipPath, { force: true });
    execFileSync('ditto', ['-c', '-k', '--sequesterRsrc', '--keepParent', appPath, zipPath], {
      stdio: 'inherit',
    });
    console.log(`Zipped: ${zipPath}`);
  } catch (error) {
    console.warn('Zip skipped:', error.message);
  }

  console.log('\nDone. Run with:');
  console.log(`  open "${appPath}"`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
