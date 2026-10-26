import { normalizeProfile } from './player-profile.js';
// Browser outbox and Firebase share this strict whitelist. No placeholder grip statistics.
export function sessionData(result) {
  if (!result || !['Easy', 'Medium', 'Hard'].includes(result.level)) throw new Error('Invalid session level');
  const data = { level: result.level };
  for (const key of ['score', 'catchCount', 'missCount', 'maxCombo', 'recipeComplete']) {
    if (!Number.isSafeInteger(result[key]) || result[key] < 0) throw new Error('Invalid ' + key);
    data[key] = result[key];
  }
  if (!Number.isFinite(result.durationSec) || result.durationSec < 0 ||
      !Number.isFinite(result.accuracy) || result.accuracy < 0 || result.accuracy > 100)
    throw new Error('Invalid session duration or accuracy');
  data.durationSec = result.durationSec; data.accuracy = result.accuracy;
  if (result.hasGripSamples === true) {
    if (!Number.isSafeInteger(result.gripSampleCount) || result.gripSampleCount <= 0) throw new Error('Invalid grip sample count');
    for (const key of ['leftAvgGrip', 'rightAvgGrip']) {
      if (!Number.isFinite(result[key]) || result[key] < 0 || result[key] > 100) throw new Error('Invalid ' + key);
      data[key] = result[key];
    }
    data.gripSampleCount = result.gripSampleCount;
    data.gripMetric = 'controller-input-percent';
  }
  return data;
}

export function playerHistoryData(profile, ownerUid, records) {
  const player = normalizeProfile(profile);
  if (!player) throw new Error('Select a player first');
  const items = [];
  for (const { id, data } of records) {
    if (data.ownerUid !== ownerUid || (data.playerCode && data.playerCode !== player.code)) continue;
    try {
      const measured = data.gripMetric === 'controller-input-percent' && data.gripSampleCount > 0;
      const result = { ...sessionData({ ...data, hasGripSamples: measured }), hasGripSamples: measured };
      const timestamp = Number.isFinite(data.playedAt?.seconds) ? data.playedAt.seconds * 1000 : data.endedAtClient;
      items.push({ id, timestamp: Number.isFinite(timestamp) ? timestamp : 0,
        date: Number.isFinite(timestamp) ? new Date(timestamp).toLocaleString() : 'Date unavailable', result });
    } catch { /* Corrupt records are not presented as real game results. */ }
  }
  items.sort((a, b) => b.timestamp - a.timestamp || a.id.localeCompare(b.id));
  return { code: player.code, displayName: player.displayName, totalPlayCount: items.length,
    bestScore: items.reduce((best, item) => Math.max(best, item.result.score), 0), items };
}

export async function flushSessions(storage, save) {
  const prefix = 'bakecatch.session.';
  const keys = Array.from({ length: storage.length }, (_, i) => storage.key(i)).filter(key => key?.startsWith(prefix));
  for (const key of keys) {
    const raw = storage.getItem(key);
    if (raw === null) continue;
    const record = JSON.parse(raw);
    if (!/^[a-f0-9]{32}$/.test(record.id) || key !== prefix + record.id ||
        !Number.isSafeInteger(record.endedAtClient) || record.endedAtClient <= 0) throw new Error('Invalid pending session');
    await save({ id: record.id, endedAtClient: record.endedAtClient, result: sessionData(record.result),
      profile: normalizeProfile(record.profile), ownerUid: record.ownerUid || null });
    // Preserve any concurrent replacement, and never clear unrelated browser data.
    if (storage.getItem(key) === raw) storage.removeItem(key);
  }
}
