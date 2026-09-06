import { test } from 'node:test';
import assert from 'node:assert/strict';
import { codexReading, renderCodex } from './codex-display.mjs';
const now = Date.now();
const archive = { Readings: { codex: { Source: 'Codex app server · live', Status: 'OK', RecordedAt: new Date(now).toISOString(), Windows: [{ Percent: 0, Label: '7d limit' }] } } };
test('zero is a real reading; closed notch becomes stale', () => {
  assert.equal(codexReading(archive, {}, now).windows[0].Percent, 0);
  assert.equal(codexReading(archive, {}, now + 360001).stale, true);
});
test('disconnected, demo and missing usage never create invented caps', () => {
  assert.equal(codexReading(archive, { Disabled: ['codex'] }, now).windows.length, 0);
  assert.equal(codexReading({ Readings: { codex: { ...archive.Readings.codex, Source: 'Demo' } } }, {}, now).windows.length, 0);
  assert.equal(codexReading({}, {}, now).windows.length, 0);
});
test('render clamps the ring, escapes labels, and identifies window and stale data', () => {
  const image = renderCodex({ windows: [{ Percent: 120, Label: '<7d>' }], stale: true });
  const svg = Buffer.from(image.split(',')[1], 'base64').toString();
  assert.match(svg, /120%/); assert.match(svg, /&lt;7d&gt;/); assert.match(svg, /STALE/);
});
