import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as profiles from '../player-profile.js';

test('dashboard lists only valid profiles owned by this browser identity', () => {
  const records = [
    { id: 'owner_B', data: { ownerUid: 'owner', playerCode: 'B', displayName: 'Beam' } },
    { id: 'other_A', data: { ownerUid: 'other', playerCode: 'A', displayName: 'Other' } },
    { id: 'owner_A', data: { ownerUid: 'owner', playerCode: 'A', displayName: 'Ann' } },
    { id: 'wrong', data: { ownerUid: 'owner', playerCode: 'C', displayName: 'Wrong path' } },
    { id: 'P001', data: { ownerUid: 'owner', displayName: 'Unassigned legacy history' } },
    { id: 'owner_D', data: { ownerUid: 'owner', playerCode: 'D', displayName: '<script>' } }
  ];
  assert.deepEqual(profiles.dashboardPlayers('owner', records), [
    { id: 'owner_A', code: 'A', displayName: 'Ann' },
    { id: 'owner_B', code: 'B', displayName: 'Beam' }
  ]);
  assert.deepEqual(profiles.dashboardPlayers('newOwner', records), []);
});
