# Bake Catch local USB grip bridge

This bring-up mode uses the **existing serial-only ESP32 sketch**. It does not
flash firmware or use Wi-Fi. Only real sensor CSV is published; no sample grip
values are generated.

## Start

Install Node.js and npm. On macOS, `/usr/bin/python3` is also needed to configure
the receive latency on the already-open serial descriptor.

Close Arduino Serial Monitor/Plotter so this process owns the port, then:

```sh
cd /Users/tanawat/Desktop/Projects/BakeCatch/mqtt-bridge
npm ci --ignore-scripts
SERIAL_PORT=/dev/cu.usbserial-0001 npm start
```

Keep the terminal running. Wait for `Real sensor settings`, then reload the
Unity WebGL page served over local HTTP (currently `http://localhost:49329/`).
The game must have been built with `BakeCatchGrip.jslib` and the updated
`PlayerInputProvider`. Do not open the HTML directly with `file://`.

The bridge listens on loopback only: MQTT TCP 1883 and HTTP/WebSocket 8080,
topic `bakecatch/grip-1/input`. Do not expose these development listeners to
the Internet. HTTPS/WSS deployment and ESP32 Wi-Fi are separate, unverified steps.

## Input and safety

- Confirmed wiring: module VCC = 3.3 V, common ground, left AO = GPIO34,
  right AO = GPIO35. Module outputs must not exceed 3.3 V.
- The flashed sketch outputs `levelL,deltaMvL,levelR,deltaMvR` at 115200 baud.
  The bridge queries `?` for its real hard threshold; it does not recalibrate.
- `deltaMv` is baseline minus measured voltage, **not** newtons or a measured
  grip percentage. It is scaled to control speed relative to the hard threshold.
- Left/right control magnitudes are subtracted. Equal grips cancel.
  A/D and arrow keys retain priority; opposite keyboard directions cancel.
- Released readings stop movement. Data older than 500 ms is not used;
  loss of connection or hidden browser tab clears hardware input.
- On macOS, DTR/RTS are released and per-port `IOSSDATALAT` is set when opening.
  This prevents observed ~1-second receive batches from tripping the watchdog.
  No global driver setting is changed. An unsupported ioctl or missing Python
  prints an error; do not treat that as a successful low-latency setup.

## Check

```sh
npm test
```

Parser tests use isolated fixtures, never published as sensor readings. For the
physical check, release both sensors, hold left only, release, hold right only,
then unplug USB while holding: movement must stop within the existing timeout.
Also check keyboard controls, Pause/Resume, and the unchanged 45-second session.

Observed during development: before the latency setting, real USB receive gaps
were about 1000 ms. After it, real browser receive gaps were at most 23 ms over
a 40-second idle capture, with no sequence gaps. These figures measure arrival
spacing, not physical-squeeze-to-display latency or calibrated force accuracy.

## Firebase session results (WebGL)

Rebuild WebGL after adding `BakeCatchFirebase.jslib` and the `GameSession` hooks.
Use output folder `unity-game/Builds/FirebaseWebGL` (a verified build is already
present locally). With this bridge running, open:

- Game: `http://127.0.0.1:8080/game/`
- Dashboard: `http://127.0.0.1:8080/dashboard/`

Run this bridge before opening the game. It serves the Firebase modules; their
public configuration is read from the existing `web-dashboard/index.html`.
No service-account key is used, and Firebase Security Rules are not changed.

After a session ends, the game queues the **actual** result in this browser's
localStorage, then uses the dashboard's existing Anonymous Auth and Firestore
project. It writes `players/P001/sessions/<session UUID>` and updates the
player's `playCount` and `lastPlayedAt` atomically. Retrying an already-saved
UUID does not increase the count again. Only completed sessions are saved;
returning to Main Menu before the end does not create a session.

Saved fields: score, level, durationSec, catchCount, missCount, accuracy,
maxCombo, recipeComplete, ownerUid, endedAtClient, playedAt. `endedAtClient`
records the browser's end time; `playedAt` is the server save time, which can
be later after an offline retry.

When fresh hardware frames were received during Playing, the session also saves
`leftAvgGrip`, `rightAvgGrip`, `gripSampleCount`, and
`gripMetric: "controller-input-percent"`. Each side is the arithmetic mean of
its valid controller-input samples, multiplied by 100. Input is normalized to
the configured hard threshold by the existing protocol (not clinical force,
newtons, or percent muscle strength). A real released sample contributes zero;
Pause, Main Menu, Ended, invalid packets, and disconnected/missing frames add
no samples. Statistics reset on every replay. Keyboard keys never generate
grip samples, although real sensor packets still count if keyboard movement
temporarily takes priority. This is a sample mean, not a coverage/time estimate.

No-sensor/keyboard-only sessions omit the grip fields, rather than inventing
zero. The dashboard shows these as `—`/chart gaps. Old sessions are not
backfilled. Play a new complete 45-second round and refresh the dashboard to
see the new session's measured input averages.

Failed saves stay queued and retry every 10 seconds and when the browser
returns online. If the Firebase module itself could not load, reload once
online. Keep the same origin/port to access the same queue; clearing browser
data or closing a private browsing session can lose pending records. Do not
share a browser profile containing another player's pending data.

Inspect `window.BakeCatchFirebase.status` in the browser console for the save
state and error code. A `permission-denied` error requires reviewing the
project's existing Firebase rules, not making the database public.

This deliberately preserves the dashboard's **single-player P001 prototype**.
Anonymous Auth and an `ownerUid` field alone are not a verified multi-user
privacy policy. Before collecting identifiable/patient data or deploying
publicly, implement player sign-in/ownership rules and review data consent,
retention, and access. The old `index.html` manual test-data button is not used
by the game; do not use it as evidence of a real session.
