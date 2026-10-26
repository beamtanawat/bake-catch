import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../unity-game/Assets/Plugins/WebGL/BakeCatchFirebase.jslib', import.meta.url), 'utf8');
async function resolve(client) {
  const calls = [], library = {};
  const window = { BakeCatchFirebase: client, BakeCatchFirebaseLoading: Promise.resolve() };
  vm.runInNewContext(source, { window, LibraryManager: { library }, mergeInto: Object.assign,
    UTF8ToString: value => value, _BakeCatchFirebaseInit() {}, setTimeout, clearTimeout,
    SendMessage: (...args) => calls.push(args) });
  library.BakeCatchFirebaseFindPlayer(JSON.stringify({ code: 'NEW', displayName: 'Beam' }), 'Bake Catch UI');
  await new Promise(resolve => setImmediate(resolve));
  return calls;
}
test('name bridge returns the existing internal identity to Unity without directly saving a profile', async () => {
  const profile = { code: 'OLD', displayName: 'Beam' };
  const calls = await resolve({ findPlayerByName: async () => profile });
  assert.equal(calls.length, 1);
  assert.equal(calls[0][0], 'Bake Catch UI');
  assert.equal(calls[0][1], 'ReceivePlayerProfile');
  assert.deepEqual(JSON.parse(calls[0][2]), profile);
});
test('name bridge reports failed lookup and never substitutes a new identity', async () => {
  for (const client of [null, { findPlayerByName: async () => { throw new Error('offline'); } }]) {
    const calls = await resolve(client);
    assert.equal(calls.length, 1);
    assert.equal(calls[0][1], 'ReceivePlayerError');
  }
});
