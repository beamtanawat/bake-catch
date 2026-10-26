import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { playerDocumentId } from '../player-profile.js';
import { playerHistoryData } from '../session-store.js';
const source=readFileSync(new URL('../firebase-client.js',import.meta.url),'utf8');
const lookup=source.slice(source.indexOf('export async function getPlayerHistory'),source.indexOf("window.addEventListener('bakecatch-session-queued'")).replace('export ','');
test('history merges every browser saved under the same name and still refuses a mismatched own profile',async()=>{
  const played={level:'Easy',score:10,catchCount:1,missCount:0,maxCombo:1,recipeComplete:0,accuracy:100,durationSec:45,playedAt:{seconds:1}};
  const sessions={'players/owner_A/sessions':[['a',{...played,ownerUid:'owner'}]],'players/other_B/sessions':[['b',{...played,score:40,ownerUid:'other',playerCode:'B'}]]};
  let mismatch=false;
  const context=vm.createContext({identity:async()=> 'owner',flush:async()=>{},db:{},playerDocumentId,playerHistoryData,
    doc:(_,...path)=>path.join('/'),collection:(_,...path)=>path.join('/'),where:(...args)=>args,query:(path)=>'players-by-name',
    getDocFromServer:async path=>{assert.equal(path,'players/owner_A');return {exists:()=>true,data:()=>({ownerUid:mismatch?'other':'owner'})};},
    getDocsFromServer:async ref=>({docs:(ref==='players-by-name'?[['owner_A'],['other_B']].map(([id])=>({id})):(sessions[ref]||[]).map(([id,data])=>({id,data:()=>data})))})});
  vm.runInContext(lookup,context);
  const history=await context.getPlayerHistory({code:'A',displayName:'Beam'});
  assert.equal(history.totalPlayCount,2);assert.equal(history.bestScore,40);
  mismatch=true;
  await assert.rejects(context.getPlayerHistory({code:'A',displayName:'Beam'}),/owner mismatch/);
});
test('offline history errors propagate instead of being displayed as an empty history',async()=>{
  const context=vm.createContext({identity:async()=> 'owner',flush:async()=>{},db:{},playerDocumentId,playerHistoryData,
    doc:()=>'',getDocFromServer:async()=>{throw Error('offline');}});
  vm.runInContext(lookup,context);
  await assert.rejects(context.getPlayerHistory({code:'A',displayName:'Beam'}),/offline/);
});
