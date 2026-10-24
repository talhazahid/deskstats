'use strict';

const zlib = require('node:zlib');
const fs = require('node:fs');
const { nativeImage } = require('electron');
const { resourcePath } = require('./macmon');

/**
 * Menu-bar template icon, generated at runtime so packaging does not depend on
 * a binary asset. macOS treats template images as masks: opaque pixels become
 * black/white automatically in light/dark menu bars.
 */

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let i = 0; i < 8; i += 1) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length, 0);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body), 0);
  return Buffer.concat([length, body, crc]);
}

function png(width, height, pixelAt) {
  const raw = Buffer.alloc((width * 4 + 1) * height);
  let offset = 0;
  for (let y = 0; y < height; y += 1) {
    raw[offset] = 0; // no filter
    offset += 1;
    for (let x = 0; x < width; x += 1) {
      const [r, g, b, a] = pixelAt(x, y);
      raw[offset] = r;
      raw[offset + 1] = g;
      raw[offset + 2] = b;
      raw[offset + 3] = a;
      offset += 4;
    }
  }

  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // RGBA
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

function createTrayIcon() {
  // Preferred: the DeskStats logo (ring + bars) as a menu-bar template image.
  // Electron picks up trayTemplate@2x.png automatically on Retina displays and
  // the "Template" suffix makes macOS tint it for light/dark menu bars.
  const logo = resourcePath('trayTemplate.png');
  if (fs.existsSync(logo)) {
    const image = nativeImage.createFromPath(logo);
    if (!image.isEmpty()) {
      image.setTemplateImage(true);
      return image;
    }
  }
  return fallbackIcon();
}

function fallbackIcon() {
  const size = 32;
  // Three rising bars and a baseline — readable at 16–18 px.
  const bars = [
    { x: 5, y: 18, w: 4, h: 9 },
    { x: 13, y: 11, w: 4, h: 16 },
    { x: 21, y: 5, w: 4, h: 22 },
  ];
  const buffer = png(size, size, (x, y) => {
    const inBar = bars.some((bar) => x >= bar.x && x < bar.x + bar.w && y >= bar.y && y < bar.y + bar.h);
    const baseline = y >= 27 && y <= 29 && x >= 3 && x <= 29;
    if (inBar || baseline) return [0, 0, 0, 255];
    return [0, 0, 0, 0];
  });

  const image = nativeImage.createFromBuffer(buffer, { scaleFactor: 2 });
  const resized = image.resize({ width: 18, height: 18, quality: 'best' });
  resized.setTemplateImage(true);
  return resized;
}

module.exports = { createTrayIcon };
