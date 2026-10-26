import { test } from 'node:test';
import assert from 'node:assert/strict';
const display = await import('../../web-dashboard/grip-display.js');
test('missing grip measurements show no-data, never a fabricated zero percent', () => {
  assert.equal(typeof display.formatGrip, 'function');
  for (const value of [undefined, null, NaN, '70']) assert.equal(display.formatGrip(value), '—');
  assert.equal(display.formatGrip(0), '0%');
  assert.equal(display.formatGrip(72), '72%');
});
