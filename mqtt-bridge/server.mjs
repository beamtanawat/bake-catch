import net from 'node:net';
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { randomBytes, timingSafeEqual } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { Aedes } from 'aedes';
import { WebSocketServer, createWebSocketStream } from 'ws';
import { SerialPort } from 'serialport';
import './protocol.js';

const topic = 'bakecatch/grip-1/input';
const host = process.env.MQTT_HOST || '127.0.0.1';
const token = process.env.GRIP_TOKEN || randomBytes(32).toString('hex');
if (host !== '127.0.0.1' && !process.env.GRIP_TOKEN) throw new Error('LAN mode requires GRIP_TOKEN.');
const broker = await Aedes.createBroker();
broker.authenticate = (client, user, password, done) => {
  if (client.conn.bakeCatchBrowser && client.id.startsWith('browser-') && !user) return done(null, true);
  const supplied = password || Buffer.alloc(0), expected = Buffer.from(token);
  done(null, user === 'grip-device' && supplied.length === expected.length && timingSafeEqual(supplied, expected));
};
broker.authorizeSubscribe = (client, sub, done) => done(null,
  client.id.startsWith('browser-') && sub.topic === topic ? sub : null);
broker.authorizePublish = (client, packet, done) => {
  try {
    if (client.id.startsWith('browser-') || packet.topic !== topic || packet.retain) throw new Error('Publish denied');
    const p = BakeCatchGripProtocol.validate(JSON.parse(packet.payload));
    if (!p) throw new Error('Invalid grip frame');
    packet.payload = Buffer.from(JSON.stringify({ ...p, serverTime: Date.now() }));
    done(null);
  } catch (error) { done(error); }
};
const tcp = net.createServer(broker.handle);
tcp.listen(1883, host);

const files = { '/protocol.js': 'protocol.js', '/grip-client.js': 'grip-client.js',
  '/mqtt.min.js': 'node_modules/mqtt/dist/mqtt.min.js' };
const server = http.createServer(async (request, response) => {
  const file = files[new URL(request.url, 'http://localhost').pathname];
  if (!file) { response.writeHead(404).end(); return; }
  try {
    const content = await readFile(new URL(file, import.meta.url));
    response.writeHead(200, { 'Content-Type': 'text/javascript', 'Cache-Control': 'no-store' });
    response.end(content);
  } catch { response.writeHead(500).end(); }
});
const sockets = new WebSocketServer({ server, path: '/mqtt', maxPayload: 4096,
  verifyClient: ({ origin }, done) => {
    try { done(['127.0.0.1', 'localhost'].includes(new URL(origin).hostname)); }
    catch { done(false); }
  }
});
sockets.on('connection', (socket, req) => {
  const stream = createWebSocketStream(socket);
  stream.bakeCatchBrowser = true;
  broker.handle(stream, req);
});
server.listen(8080, '127.0.0.1', () => console.log('Local grip broker: MQTT :1883; WebSocket ws://127.0.0.1:8080/mqtt'));
broker.on('clientError', (_, error) => console.error('MQTT client:', error.message));

// Optional bring-up path: preserve the currently flashed USB-only sensor sketch.
let serial;
if (process.env.SERIAL_PORT) {
  serial = new SerialPort({ path: process.env.SERIAL_PORT, baudRate: 115200 });
  let buffer = '', seq = 0, hardMv = null;
  let settingsQuery;
  serial.on('open', () => serial.set({ dtr: false, rts: false }, error => {
    if (error) { console.error('Serial control lines:', error.message); return; }
    if (process.platform === 'darwin') {
      // macOS IOSSDATALAT: avoid ~1-second USB receive batches. Only this open port is changed.
      // Pass its existing descriptor to Python, rather than reopening/resetting the ESP32.
      const latency = spawnSync('/usr/bin/python3', ['-c',
        'import fcntl, struct; fcntl.ioctl(3, 0x80085400, struct.pack("L", 1))'],
        { stdio: ['ignore', 'ignore', 'pipe', serial.port.fd], timeout: 2000 });
      if (latency.status !== 0) console.error('Serial low-latency setup failed:',
        latency.error?.message || latency.stderr?.toString().trim());
    }
    // ESP32 auto-reset lines must be released; startup may delay command handling.
    serial.write('?\n');
    settingsQuery = setInterval(() => {
      if (serial.isOpen && hardMv === null) serial.write('?\n');
    }, 1000);
  }));
  serial.on('data', data => {
    buffer += data.toString();
    if (buffer.length > 8192) { buffer = ''; return; }
    while (buffer.includes('\n')) {
      const end = buffer.indexOf('\n'), line = buffer.slice(0, end).trim();
      buffer = buffer.slice(end + 1);
      if (line.startsWith('# capturing')) hardMv = null;
      if (line.startsWith('# light=')) {
        const setting = /hard=(\d+) hyst=\d+ baseL=(\d+) baseR=(\d+)$/.exec(line);
        if (setting) {
          hardMv = Number(setting[1]);
          console.log('Real sensor settings:', line);
        }
      }
      if (hardMv === null) continue;
      const p = BakeCatchGripProtocol.parseCsv(line, seq++, hardMv);
      if (p) broker.publish({ topic, payload: Buffer.from(JSON.stringify({ ...p, serverTime: Date.now() })), qos: 0, retain: false }, () => {});
    }
  });
  serial.on('error', error => console.error('Serial:', error.message));
  serial.on('close', () => {
    clearInterval(settingsQuery);
    console.log('Serial disconnected; browser and Unity input will expire.');
  });
}
function stop() {
  if (serial?.isOpen) serial.close();
  sockets.close(); server.close(); tcp.close(); broker.close(() => process.exit(0));
}
process.on('SIGINT', stop); process.on('SIGTERM', stop);
