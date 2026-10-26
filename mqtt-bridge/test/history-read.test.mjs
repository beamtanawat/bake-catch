import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { playerDocumentId } from '../player-profile.js';
import { playerHistoryData } from '../session-store.js';
const source=readFileSync(new URL('../firebase-client.js',import.meta.url),'utf8');
const lookup=source.slice(source.indexOf('export async function getPlayerHistory'),source.indexOf("window.addEventListener('bakecatch-session-queued'")).replace('export ','');
test('history reads only the current owner/profile path and refuses mismatched parent ownership',async()=>{
  const paths=[];let mismatch=false;
  const context=vm.createContext({identity:async()=> 'owner',flush:async()=>{},db:{},playerDocumentId,playerHistoryData,
    doc:(_,...path)=>path.join('/'),collection:(_,...path)=>{paths.push(path.join('/'));return path.join('/');},
    getDocFromServer:async path=>{assert.equal(path,'players/owner_A');return {exists:()=>true,data:()=>({ownerUid:mismatch?'other':'owner'})};},
    getDocsFromServer:async()=>({docs:[]})});
  vm.runInContext(lookup,context);
  assert.equal((await context.getPlayerHistory({code:'A',displayName:'Beam'})).totalPlayCount,0);
  assert.deepEqual(paths,['players/owner_A/sessions']);
  mismatch=true;
  await assert.rejects(context.getPlayerHistory({code:'A',displayName:'Beam'}),/owner mismatch/);
  assert.equal(paths.length,1);
});
test('offline history errors propagate instead of being displayed as an empty history',async()=>{
  const context=vm.createContext({identity:async()=> 'owner',flush:async()=>{},db:{},playerDocumentId,playerHistoryData,
    doc:()=>'',getDocFromServer:async()=>{throw Error('offline');}});
  vm.runInContext(lookup,context);
  await assert.rejects(context.getPlayerHistory({code:'A',displayName:'Beam'}),/offline/);
});
