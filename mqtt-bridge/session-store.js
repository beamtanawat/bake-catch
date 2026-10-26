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
  return data;
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
    await save({ id: record.id, endedAtClient: record.endedAtClient, result: sessionData(record.result) });
    // Preserve any concurrent replacement, and never clear unrelated browser data.
    if (storage.getItem(key) === raw) storage.removeItem(key);
  }
}
