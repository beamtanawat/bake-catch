(function () {
  'use strict';
  const base = new URL('.', document.currentScript.src).href;
  let client, receiver, last = 0, lastSeq = -1, boot = '', timer;
  function send(value) { if (receiver) receiver(value); }
  function unavailable() { last = 0; send(null); }
  function load(name) {
    return new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = base + name; script.onload = resolve; script.onerror = reject;
      document.head.appendChild(script);
    });
  }
  window.BakeCatchGrip = {
    async start(onGrip) {
      this.stop(); receiver = onGrip;
      try {
        if (!window.BakeCatchGripProtocol) await load('protocol.js');
        if (!window.mqtt) await load('mqtt.min.js');
        client = mqtt.connect(base.replace(/^http/, 'ws') + 'mqtt', {
          clientId: 'browser-' + crypto.randomUUID(), protocolVersion: 4,
          clean: true, reconnectPeriod: 1000, queueQoSZero: false
        });
        client.on('connect', () => { unavailable(); lastSeq = -1; boot = ''; client.subscribe('bakecatch/grip-1/input', { qos: 0 }); });
        client.on('close', unavailable); client.on('offline', unavailable); client.on('error', unavailable);
        client.on('message', (_, payload, packet) => {
          try {
            const p = BakeCatchGripProtocol.validate(JSON.parse(payload.toString()));
            if (!p || packet.retain || !Number.isFinite(p.serverTime) || Math.abs(Date.now() - p.serverTime) > 500 ||
                document.hidden) { unavailable(); return; }
            if (p.boot !== boot) { lastSeq = -1; boot = p.boot; }
            if (p.seq <= lastSeq) { unavailable(); return; }
            lastSeq = p.seq; last = performance.now(); send(BakeCatchGripProtocol.intent(p));
          } catch { unavailable(); }
        });
        timer = setInterval(() => { if (document.hidden || !last || performance.now() - last > 500) unavailable(); }, 100);
      } catch (error) { console.warn('Grip unavailable; keyboard remains available.', error); unavailable(); }
    },
    stop() { if (timer) clearInterval(timer); if (client) client.end(true); client = null; unavailable(); receiver = null; }
  };
})();
