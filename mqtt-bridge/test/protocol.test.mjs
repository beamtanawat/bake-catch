import { test } from 'node:test';
import assert from 'node:assert/strict';
import '../protocol.js';
const { parseCsv, intent, validate } = globalThis.BakeCatchGripProtocol;
// Isolated parser fixtures only; never published to a broker or used as hardware evidence.
test('valid sensor CSV drives signed proportional input without treating mV as force units', () => {
  assert.deepEqual(intent(parseCsv('1,500,2,2000', 1, 2000)), { left: .25, right: 1 });
  assert.deepEqual(intent(parseCsv('0,40,0,45', 2, 2000)), { left: 0, right: 0 });
});
test('rejects debug lines, malformed, non-finite and out-of-range sensor data', () => {
  for (const line of ['# baseline', '0,0,0', '-1,0,0,0', '3,1,0,0', '1,3301,0,0', '0,NaN,0,0', '0,0,0,0 trailing'])
    assert.equal(parseCsv(line, 0, 2000), null);
  assert.equal(validate({}), null);
  assert.equal(parseCsv('0,0,0,0', 0, 0), null);
});
test('GripShake six-field CSV carries the debounced right-hand shake level only', () => {
  assert.equal(intent(parseCsv('0,0,0,0,2,31', 3, 2000)).shake, 2);
  assert.equal(intent(parseCsv('0,0,0,0,0,0', 4, 2000)).shake, 0);
  for (const line of ['0,0,0,0,3,0', '0,0,0,0,1', '0,0,0,0,1,2,3', '0,0,0,0,-1,0'])
    assert.equal(parseCsv(line, 5, 2000), null);
});
