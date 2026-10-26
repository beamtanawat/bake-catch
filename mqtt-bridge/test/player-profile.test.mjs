import { test } from 'node:test';
import assert from 'node:assert/strict';
const api = await import('../player-profile.js');
test('profile codes normalize consistently and names cannot contain UI markup or control characters', () => {
  assert.equal(typeof api.normalizeProfile, 'function');
  assert.deepEqual(api.normalizeProfile({ code: ' p002 ', displayName: ' Beam ' }), { code: 'P002', displayName: 'Beam' });
  for (const value of [{ code: '../P001', displayName: 'Beam' }, { code: 'A', displayName: '<b>name</b>' },
    { code: 'A', displayName: '' }, { code: 'A', displayName: 'x\ny' }]) assert.throws(() => api.normalizeProfile(value));
});
test('same browser identity separates profile codes; legacy records stay P001', () => {
  assert.equal(typeof api.playerDocumentId, 'function');
  assert.equal(api.playerDocumentId('ownerA', { code: 'A', displayName: 'One' }), 'ownerA_A');
  assert.equal(api.playerDocumentId('ownerA', { code: 'B', displayName: 'Two' }), 'ownerA_B');
  assert.equal(api.playerDocumentId('ownerB', { code: 'A', displayName: 'One' }), 'ownerB_A');
  assert.equal(api.playerDocumentId('ownerA', null), 'P001');
});
test('profile save failure retains metadata and never removes pending session data', async () => {
  const data = new Map(), storage = { get length() { return data.size; }, key: i => [...data.keys()][i],
    getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) };
  const raw = JSON.stringify({ profile: { code: 'A', displayName: 'ผู้เล่น A' }, ownerUid: 'ownerA' });
  storage.setItem('bakecatch.profile.A', raw);
  storage.setItem('bakecatch.session.keep', 'pending result');
  await assert.rejects(api.flushProfiles(storage, async () => { throw new Error('offline'); }));
  assert.equal(storage.getItem('bakecatch.profile.A'), raw);
  const saved = [];
  await api.flushProfiles(storage, async (profile, owner) => saved.push({ profile, owner }));
  assert.deepEqual(saved, [{ profile: { code: 'A', displayName: 'ผู้เล่น A' }, owner: 'ownerA' }]);
  assert.equal(storage.getItem('bakecatch.profile.A'), null);
  assert.equal(storage.getItem('bakecatch.session.keep'), 'pending result');
});
