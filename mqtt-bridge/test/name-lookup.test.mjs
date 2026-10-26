import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { playerDocumentId, resolvePlayerByName } from '../player-profile.js';

test('a concurrent queue flush cannot hide a same-name profile while the server query is pending', async () => {
  const source = readFileSync(new URL('../firebase-client.js', import.meta.url), 'utf8');
  const lookup = source.slice(source.indexOf('export async function findPlayerByName'), source.indexOf('\nasync function save(record)')).replace('export ', '');
  const localStorage = { 'bakecatch.profile.OLD': JSON.stringify({ownerUid:'owner',profile:{code:'OLD',displayName:'Beam'}}) };
  Object.defineProperty(localStorage, 'getItem', { value: key => localStorage[key] ?? null });
  const context = vm.createContext({ localStorage, identity: async () => 'owner', db: {},
    collection: () => 'players', query: (...args) => args, where: (...args) => args,
    playerDocumentId, resolvePlayerByName,
    getDocsFromServer: async () => {
      delete localStorage['bakecatch.profile.OLD']; // Flush commits after the server snapshot was taken.
      return { docs: [] };
    }
  });
  vm.runInContext(lookup, context);
  const result = await context.findPlayerByName({code:'NEW',displayName:'Beam'});
  assert.equal(result.code, 'OLD');
});
