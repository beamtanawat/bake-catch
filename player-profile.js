export function normalizeProfile(profile) {
  if (profile == null) return null; // Old queued P001 sessions are never reassigned.
  const code = typeof profile.code === 'string' ? profile.code.trim().toUpperCase() : '';
  const displayName = typeof profile.displayName === 'string' ? profile.displayName.trim() : '';
  if (!/^[A-Z0-9][A-Z0-9_-]{0,23}$/.test(code) || !displayName || displayName.length > 40 ||
      /[<>\x00-\x1f\x7f]/.test(displayName)) throw new Error('Invalid player profile');
  return { code, displayName };
}
export function playerDocumentId(ownerUid, profile) {
  if (!/^[a-zA-Z0-9_-]{1,128}$/.test(ownerUid)) throw new Error('Invalid browser identity');
  const player = normalizeProfile(profile);
  return player ? ownerUid + '_' + player.code : 'P001';
}
export function selectedProfile(storage) {
  try { return normalizeProfile(JSON.parse(storage.getItem('bakecatch.selectedProfile'))); }
  catch { return null; }
}
export function dashboardPlayers(ownerUid, records) {
  const players = [];
  for (const { id, data } of records) {
    if (data.ownerUid !== ownerUid) continue;
    try {
      const profile = normalizeProfile({ code: data.playerCode, displayName: data.displayName });
      if (id === playerDocumentId(ownerUid, profile)) players.push({ id, ...profile });
    } catch { /* Ignore malformed or legacy records, not other players' history. */ }
  }
  return players.sort((a, b) => a.code.localeCompare(b.code));
}
export function resolvePlayerByName(ownerUid, candidate, records) {
  const profile = normalizeProfile(candidate);
  if (!profile) throw new Error('Enter a player name');
  const matches = dashboardPlayers(ownerUid, records).filter(player => player.displayName === profile.displayName);
  const saved = matches.find(player => player.code === profile.code) ?? matches[0];
  return saved ? { code: saved.code, displayName: saved.displayName } : profile;
}
export async function flushProfiles(storage, save) {
  const prefix = 'bakecatch.profile.';
  const keys = Array.from({ length: storage.length }, (_, i) => storage.key(i)).filter(key => key?.startsWith(prefix));
  for (const key of keys) {
    const raw = storage.getItem(key);
    if (raw === null) continue;
    const record = JSON.parse(raw), profile = normalizeProfile(record.profile);
    if (!profile || key !== prefix + profile.code) throw new Error('Invalid pending player');
    await save(profile, record.ownerUid);
    if (storage.getItem(key) === raw) storage.removeItem(key);
  }
}
