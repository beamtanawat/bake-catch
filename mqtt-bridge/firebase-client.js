import { initializeApp, getApps } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-app.js';
import { getAuth, signInAnonymously } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-auth.js';
import { getFirestore, doc, runTransaction, serverTimestamp } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-firestore.js';
import { flushSessions } from './session-store.js';

// Reuse the dashboard's public web-app configuration, never Admin/service-account credentials.
const response = await fetch(new URL('firebase-config.json', import.meta.url));
if (!response.ok) throw new Error('Firebase configuration unavailable');
const config = await response.json();
const app = getApps().find(app => app.name === 'bakecatch-game') || initializeApp(config, 'bakecatch-game');
const auth = getAuth(app), db = getFirestore(app);
const playerRef = doc(db, 'players', 'P001'); // Same single-player prototype as the existing dashboard.
let running;
export const status = { state: 'ready', lastSessionId: null, error: null };

async function save(record) {
  if (!auth.currentUser) await signInAnonymously(auth);
  const sessionRef = doc(playerRef, 'sessions', record.id);
  await runTransaction(db, async transaction => {
    const existing = await transaction.get(sessionRef);
    if (existing.exists()) {
      if (existing.data().ownerUid !== auth.currentUser.uid) throw new Error('Session owner mismatch');
      return; // Retry after a lost response must not increase playCount again.
    }
    const player = await transaction.get(playerRef);
    const count = player.exists() ? (player.data().playCount ?? 0) : 0;
    if (!Number.isSafeInteger(count) || count < 0) throw new Error('Invalid existing playCount');
    transaction.set(sessionRef, { ...record.result, ownerUid: auth.currentUser.uid,
      endedAtClient: record.endedAtClient, playedAt: serverTimestamp() });
    transaction.set(playerRef, { playCount: count + 1, lastPlayedAt: serverTimestamp() }, { merge: true });
  });
  status.lastSessionId = record.id;
}

export function flush() {
  if (running) return running;
  status.state = 'saving';
  running = flushSessions(localStorage, save).then(() => {
    status.state = 'saved'; status.error = null;
  }).catch(error => {
    status.state = 'pending'; status.error = error.code || error.message;
    console.warn('Bake Catch: session remains queued for Firebase.', status.error);
  }).finally(() => { running = null; });
  return running;
}
window.addEventListener('bakecatch-session-queued', flush);
window.addEventListener('online', flush);
setInterval(flush, 10000);
flush();
