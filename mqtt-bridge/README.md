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
