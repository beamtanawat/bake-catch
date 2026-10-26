import { initializeApp, getApps } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-app.js';
import { getAuth, signInAnonymously } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-auth.js';
import { getFirestore, doc, runTransaction, serverTimestamp, collection, query, where, getDocsFromServer, getDocFromServer } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-firestore.js';
import { flushSessions, playerHistoryData } from './session-store.js';
import { flushProfiles, playerDocumentId, resolvePlayerByName } from './player-profile.js';

// Reuse the dashboard's public web-app configuration, never Admin/service-account credentials.
const response = await fetch(new URL('firebase-config.json', import.meta.url));
if (!response.ok) throw new Error('Firebase configuration unavailable');
const config = await response.json();
const app = getApps().find(app => app.name === 'bakecatch-game') || initializeApp(config, 'bakecatch-game');
const auth = getAuth(app), db = getFirestore(app);
let running;
export const status = { state: 'ready', lastSessionId: null, error: null };

async function identity(expected) {
  await auth.authStateReady();
  if (!auth.currentUser) await signInAnonymously(auth);
  const uid = auth.currentUser.uid;
  if (expected && expected !== uid) throw new Error('Pending data belongs to another browser identity');
  localStorage.setItem('bakecatch.firebaseOwner', uid);
  return uid;
}

async function saveProfile(profile, expectedOwner) {
  const uid = await identity(expectedOwner);
  const ref = doc(db, 'players', playerDocumentId(uid, profile));
  await runTransaction(db, async transaction => {
    const existing = await transaction.get(ref);
    if (existing.exists() && existing.data().ownerUid !== uid) throw new Error('Profile owner mismatch');
    transaction.set(ref, { displayName: profile.displayName, playerCode: profile.code, ownerUid: uid,
      ...(!existing.exists() ? { playCount: 0, createdAt: serverTimestamp() } : {}) }, { merge: true });
  });
}

export async function findPlayerByName(candidate) {
  const uid = await identity();
  // Snapshot pending profiles before the query; a concurrent flush can remove their queue entries.
  const records = [];
  for (const key of Object.keys(localStorage).filter(key => key.startsWith('bakecatch.profile.'))) {
    try {
      const pending = JSON.parse(localStorage.getItem(key));
      if (pending.ownerUid !== uid) continue;
      records.push({ id: playerDocumentId(uid, pending.profile), data: {
        ownerUid: uid, playerCode: pending.profile.code, displayName: pending.profile.displayName } });
    } catch { /* Malformed pending records are left untouched for the existing queue handler. */ }
  }
  // Require a server answer: an empty offline cache must not create a duplicate identity.
  const snapshot = await getDocsFromServer(query(collection(db, 'players'), where('ownerUid', '==', uid)));
  records.push(...snapshot.docs.map(item => ({ id: item.id, data: item.data() })));
  return resolvePlayerByName(uid, candidate, records);
}

async function save(record) {
  const uid = await identity(record.ownerUid);
  const playerRef = doc(db, 'players', playerDocumentId(uid, record.profile));
  const sessionRef = doc(playerRef, 'sessions', record.id);
  await runTransaction(db, async transaction => {
    const existing = await transaction.get(sessionRef);
    if (existing.exists()) {
      if (existing.data().ownerUid !== uid) throw new Error('Session owner mismatch');
      return; // Retry after a lost response must not increase playCount again.
    }
    const player = await transaction.get(playerRef);
    if (record.profile && player.exists() && player.data().ownerUid !== uid) throw new Error('Profile owner mismatch');
    const count = player.exists() ? (player.data().playCount ?? 0) : 0;
    if (!Number.isSafeInteger(count) || count < 0) throw new Error('Invalid existing playCount');
    transaction.set(sessionRef, { ...record.result, ownerUid: uid,
      ...(record.profile ? { playerCode: record.profile.code, playerDisplayName: record.profile.displayName } : {}),
      endedAtClient: record.endedAtClient, playedAt: serverTimestamp() });
    transaction.set(playerRef, { playCount: count + 1, lastPlayedAt: serverTimestamp(),
      ...(record.profile && !player.exists() ? { displayName: record.profile.displayName,
        playerCode: record.profile.code, ownerUid: uid, createdAt: serverTimestamp() } : {}) }, { merge: true });
  });
  status.lastSessionId = record.id;
}

export function flush() {
  if (running) return running;
  status.state = 'saving';
  running = (async () => {
    await identity();
    await flushProfiles(localStorage, saveProfile);
    await flushSessions(localStorage, save);
  })().then(() => {
    status.state = 'saved'; status.error = null;
  }).catch(error => {
    status.state = 'pending'; status.error = error.code || error.message;
    console.warn('Bake Catch: session remains queued for Firebase.', status.error);
  }).finally(() => { running = null; });
  return running;
}

export async function getPlayerHistory(profile) {
  const uid = await identity();
  if (!profile) throw new Error('Select a player first');
  const playerId = playerDocumentId(uid, profile);
  await flush();
  const own = await getDocFromServer(doc(db, 'players', playerId));
  if (own.exists() && own.data().ownerUid !== uid) throw new Error('Profile owner mismatch');
  // Every browser that saved under this exact name; needs Firestore rules that let signed-in users read players/sessions.
  const players = await getDocsFromServer(query(collection(db, 'players'), where('displayName', '==', profile.displayName)));
  const ids = new Set(players.docs.map(item => item.id));
  if (own.exists()) ids.add(playerId);
  const records = [];
  for (const id of ids) {
    // shortcut: like the existing dashboard, load rounds once; use server paging for large histories.
    const snapshot = await getDocsFromServer(collection(db, 'players', id, 'sessions'));
    records.push(...snapshot.docs.map(item => ({ id: id + '_' + item.id, data: item.data() })));
  }
  return playerHistoryData(profile, uid, records, true);
}
window.addEventListener('bakecatch-session-queued', flush);
window.addEventListener('online', flush);
setInterval(flush, 10000);
flush();
