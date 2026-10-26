(function (root) {
  'use strict';
  function validate(p) {
    if (!p || p.version !== 1 || p.device !== 'grip-1' || typeof p.boot !== 'string' || !/^[a-zA-Z0-9-]{1,40}$/.test(p.boot) || !Number.isSafeInteger(p.seq) || p.seq < 0 ||
        !Number.isInteger(p.hardMv) || p.hardMv < 50 || p.hardMv > 3300) return null;
    for (const side of ['L', 'R']) {
      if (![0, 1, 2].includes(p['level' + side]) || !Number.isInteger(p['deltaMv' + side]) ||
          p['deltaMv' + side] < 0 || p['deltaMv' + side] > 3300) return null;
    }
    return p;
  }
  function parseCsv(line, seq, hardMv) {
    if (!/^\d+,\d+,\d+,\d+$/.test(line)) return null;
    const [levelL, deltaMvL, levelR, deltaMvR] = line.split(',').map(Number);
    return validate({ version: 1, device: 'grip-1', boot: 'serial', seq, hardMv, levelL, deltaMvL, levelR, deltaMvR });
  }
  function intent(p) {
    if (!validate(p)) return null;
    // Relative control scale, not a measured force percentage or newton value.
    return { left: p.levelL === 0 ? 0 : Math.min(1, p.deltaMvL / p.hardMv),
      right: p.levelR === 0 ? 0 : Math.min(1, p.deltaMvR / p.hardMv) };
  }
  root.BakeCatchGripProtocol = { validate, parseCsv, intent };
})(globalThis);
