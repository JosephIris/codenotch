// Package the original artwork into a multi-resolution Windows ICO without redrawing it.
import fs from 'node:fs';
const assets = new URL('Codenotch/Assets/', import.meta.url);
const original = new URL('../Sources/Assets.xcassets/AppIcon.appiconset/', import.meta.url);
const frames = [
  [16, 'icon_16x16.png'], [32, 'icon_32x32.png'], [64, 'icon_32x32@2x.png'],
  [128, 'icon_128x128.png'], [256, 'icon_256x256.png']
].map(([size, file]) => ({ size, data: fs.readFileSync(new URL(file, original)) }));
const directory = Buffer.alloc(6 + frames.length * 16);
directory.writeUInt16LE(1, 2);
directory.writeUInt16LE(frames.length, 4);
let offset = directory.length;
for (const [index, frame] of frames.entries()) {
  const entry = 6 + index * 16;
  directory[entry] = directory[entry + 1] = frame.size === 256 ? 0 : frame.size;
  directory.writeUInt16LE(1, entry + 4);
  directory.writeUInt16LE(32, entry + 6);
  directory.writeUInt32LE(frame.data.length, entry + 8);
  directory.writeUInt32LE(offset, entry + 12);
  offset += frame.data.length;
}
fs.mkdirSync(assets, { recursive: true });
fs.writeFileSync(new URL('Codenotch.ico', assets), Buffer.concat([directory, ...frames.map(f => f.data)]));
fs.copyFileSync(new URL('icon_256x256.png', original), new URL('AppIcon.png', assets));
