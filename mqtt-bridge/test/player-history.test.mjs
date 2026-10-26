import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as store from '../session-store.js';

const result = {score:120,catchCount:5,missCount:1,maxCombo:3,recipeComplete:1,accuracy:83.3,durationSec:45,level:'Easy'};
test('history isolates its owner, orders newest first, and summarizes only actual valid rounds', () => {
  const records = [
    {id:'old',data:{...result,ownerUid:'owner',playedAt:{seconds:1}}},
    {id:'new',data:{...result,score:220,ownerUid:'owner',playedAt:{seconds:2},gripMetric:'controller-input-percent',gripSampleCount:8,leftAvgGrip:0,rightAvgGrip:25}},
    {id:'other',data:{...result,ownerUid:'other',score:9999}},
    {id:'differentProfile',data:{...result,ownerUid:'owner',playerCode:'B',score:9999}},
    {id:'invalid',data:{...result,ownerUid:'owner',score:NaN}}
  ];
  const history=store.playerHistoryData({code:'A',displayName:'Beam'},'owner',records);
  assert.equal(history.code,'A');assert.equal(history.totalPlayCount,2);assert.equal(history.bestScore,220);
  assert.deepEqual(history.items.map(item=>item.id),['new','old']);
  assert.equal(history.items[0].result.hasGripSamples,true);
  assert.equal(history.items[0].result.leftAvgGrip,0);
  assert.equal(history.items[1].result.hasGripSamples,false);
  assert.equal(history.items[1].result.leftAvgGrip,undefined);
});
test('empty history has zero rounds, not a fabricated session', () => {
  const history=store.playerHistoryData({code:'A',displayName:'Beam'},'owner',[]);
  assert.equal(history.totalPlayCount,0);assert.equal(history.bestScore,0);assert.deepEqual(history.items,[]);
});
