export function measuredGrip(value) {
  return typeof value === 'number' && Number.isFinite(value) ? Math.min(100, Math.max(0, Math.round(value))) : null;
}
export function formatGrip(value) {
  const grip = measuredGrip(value);
  return grip === null ? '—' : `${grip}%`;
}
