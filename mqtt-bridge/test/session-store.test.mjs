import { test } from 'node:test';
import assert from 'node:assert/strict';

// Fixtures stay local: these tests never contact Firebase or publish grip data.
const implementation = await import('../session-store.js');
const result = { score: 120, catchCount: 8, missCount: 2, maxCombo: 4,
  recipeComplete: 1, durationSec: 45, accuracy: 80, level: 'Easy', leftAvgGrip: 0, rightAvgGrip: 0 };
const id = 'a'.repeat(32);
function storage() {
  const data = new Map();
  return { get length() { return data.size; }, key: i => [...data.keys()][i],
    getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) };
}
test('session payload preserves real results but omits unmeasured grip placeholders', () => {
  assert.equal(typeof implementation.sessionData, 'function');
  assert.deepEqual(implementation.sessionData(result), { score: 120, catchCount: 8, missCount: 2,
    maxCombo: 4, recipeComplete: 1, durationSec: 45, accuracy: 80, level: 'Easy' });
});
test('invalid results never become Firebase session data', () => {
  assert.equal(typeof implementation.sessionData, 'function');
  for (const invalid of [{ ...result, score: -1 }, { ...result, durationSec: NaN },
    { ...result, accuracy: 101 }, { ...result, catchCount: 1.5 }, { ...result, level: 'Unknown' }])
    assert.throws(() => implementation.sessionData(invalid));
});
test('measured grip averages including true released zero survive the save whitelist', () => {
  const data = implementation.sessionData({ ...result, hasGripSamples: true, gripSampleCount: 200,
    leftAvgGrip: 37.5, rightAvgGrip: 0 });
  assert.equal(data.leftAvgGrip, 37.5);
  assert.equal(data.rightAvgGrip, 0);
  assert.equal(data.gripSampleCount, 200);
  assert.equal(data.gripMetric, 'controller-input-percent');
});
test('grip fields require measured samples and finite percentages', () => {
  for (const fields of [{ gripSampleCount: 0 }, { gripSampleCount: 1.5 },
    { leftAvgGrip: NaN }, { rightAvgGrip: 101 }, { leftAvgGrip: -1 }])
    assert.throws(() => implementation.sessionData({ ...result, hasGripSamples: true, gripSampleCount: 2, ...fields }));
  assert.equal(implementation.sessionData({ ...result, hasGripSamples: false, gripSampleCount: 0 }).leftAvgGrip, undefined);
});
test('failed cloud save retains the exact record for retry; success removes only that record', async () => {
  assert.equal(typeof implementation.flushSessions, 'function');
  const local = storage(), key = 'bakecatch.session.' + id;
  local.setItem('unrelated', 'keep');
  const raw = JSON.stringify({ id, result, endedAtClient: 1792980000000 });
  local.setItem(key, raw);
  await assert.rejects(implementation.flushSessions(local, async () => { throw new Error('offline'); }));
  assert.equal(local.getItem(key), raw);
  const saved = [];
  await implementation.flushSessions(local, async record => saved.push(record));
  assert.equal(saved[0].id, id);
  assert.equal(saved[0].result.leftAvgGrip, undefined);
  assert.equal(local.getItem(key), null);
  assert.equal(local.getItem('unrelated'), 'keep');
  await implementation.flushSessions(local, async record => saved.push(record));
  assert.equal(saved.length, 1);
});
test('one malformed pending record cannot delete or upload another player record', async () => {
  assert.equal(typeof implementation.flushSessions, 'function');
  const local = storage();
  local.setItem('bakecatch.session.' + id, JSON.stringify({ id: '../other', result, endedAtClient: 1792980000000 }));
  let writes = 0;
  await assert.rejects(implementation.flushSessions(local, async () => { writes++; }));
  assert.equal(writes, 0);
  assert.equal(local.length, 1);
});
test('queued results retain their original profile after the active player changes', async () => {
  const local = storage(), saved = [];
  local.setItem('bakecatch.session.' + id, JSON.stringify({ id, result, endedAtClient: 1792980000000,
    profile: { code: 'A', displayName: 'Player A' }, ownerUid: 'ownerA' }));
  local.setItem('bakecatch.selectedProfile', JSON.stringify({ code: 'B', displayName: 'Player B' }));
  await implementation.flushSessions(local, async record => saved.push(record));
  assert.deepEqual(saved[0].profile, { code: 'A', displayName: 'Player A' });
  assert.equal(saved[0].ownerUid, 'ownerA');
  assert.ok(local.getItem('bakecatch.selectedProfile'));
});
