/*
 * FSR402 x2 + vibration switch - ESP32 DevKit V1
 *
 * GPIO34 = left grip   (module AO)
 * GPIO35 = right grip  (module AO)
 * GPIO32 = vibration switch
 *
 * The vibration switch is a bouncing contact, not an intensity sensor.
 * Shake strength is estimated by counting edges inside a moving window:
 * more edges per window means more vigorous shaking.
 * Both edges are counted, so the idle polarity of the switch does not matter.
 *
 * Wiring, bare 2-leg switch: one leg to GPIO32, other leg to GND.
 *   Leave SHAKE_PULLUP true.
 * Wiring, 3-pin module: VCC to 3V3, GND to GND, DO to GPIO32.
 *   Set SHAKE_PULLUP to false.
 *
 * Output: levelL,forceL,levelR,forceR,shakeLevel,shakeCount
 * grip level: 0 = released, 1 = light, 2 = hard
 * shakeLevel: 0 = still, 1 = moving, 2 = strong shake
 *
 * Commands:
 *   r        re-capture grip baseline (hands off)
 *   ?        print current settings
 *   d        toggle human readable output
 *   l <n>    grip light threshold
 *   h <n>    grip hard threshold
 *   y <n>    grip hysteresis
 *   m <n>    shake "moving" threshold (edges per window)
 *   s <n>    shake "strong" threshold (edges per window)
 */

const int PIN_LEFT  = 34;
const int PIN_RIGHT = 35;
const int PIN_SHAKE = 32;

const bool SHAKE_PULLUP = true;

const int SAMPLES   = 9;
const int DISCARD   = 3;
const int SETTLE_US = 600;

const int BASE_ROUNDS = 40;
const int STABLE_FRAMES = 3;
const unsigned long FRAME_MS = 20;

const int WINDOW_FRAMES = 25;
const unsigned long EDGE_MIN_US = 500;

int thLight = 500;
int thHard  = 2000;
int hyst    = 600;

int thMove   = 4;
int thStrong = 20;

int baseL = 0;
int baseR = 0;
bool verbose = false;
unsigned long lastFrame = 0;
int buf[SAMPLES];

volatile uint32_t edgeTotal = 0;
volatile unsigned long lastEdgeUs = 0;
uint32_t edgeMark = 0;

int window[WINDOW_FRAMES];
int windowPos = 0;
long windowSum = 0;
bool windowReady = false;

int shakeLevel = 0;
int shakePending = 0;
int shakePendingCount = 0;

struct Channel {
  int level;
  int pendingLevel;
  int pendingCount;
};

Channel chL = {0, 0, 0};
Channel chR = {0, 0, 0};

void IRAM_ATTR onShakeEdge() {
  unsigned long now = micros();
  if (now - lastEdgeUs < EDGE_MIN_US) return;
  lastEdgeUs = now;
  edgeTotal++;
}

void sortBuf(int n) {
  for (int i = 1; i < n; i++) {
    int key = buf[i];
    int j = i - 1;
    while (j >= 0 && buf[j] > key) {
      buf[j + 1] = buf[j];
      j--;
    }
    buf[j + 1] = key;
  }
}

int readMedian(int pin) {
  for (int i = 0; i < DISCARD; i++) {
    analogReadMilliVolts(pin);
    delayMicroseconds(SETTLE_US);
  }
  for (int i = 0; i < SAMPLES; i++) {
    buf[i] = analogReadMilliVolts(pin);
    delayMicroseconds(SETTLE_US);
  }
  sortBuf(SAMPLES);
  return buf[SAMPLES / 2];
}

int captureBaseline(int pin) {
  long sum = 0;
  for (int i = 0; i < BASE_ROUNDS; i++) {
    sum += readMedian(pin);
    delay(10);
  }
  return (int)(sum / BASE_ROUNDS);
}

void doBaseline() {
  Serial.println("# capturing baseline, hands off");
  delay(1000);
  baseL = captureBaseline(PIN_LEFT);
  baseR = captureBaseline(PIN_RIGHT);
  Serial.print("# baseline L=");
  Serial.print(baseL);
  Serial.print(" R=");
  Serial.println(baseR);
}

void printSettings() {
  Serial.print("# light=");
  Serial.print(thLight);
  Serial.print(" hard=");
  Serial.print(thHard);
  Serial.print(" hyst=");
  Serial.print(hyst);
  Serial.print(" move=");
  Serial.print(thMove);
  Serial.print(" strong=");
  Serial.print(thStrong);
  Serial.print(" baseL=");
  Serial.print(baseL);
  Serial.print(" baseR=");
  Serial.println(baseR);
}

int rawLevel(int force, int current) {
  int offLight = thLight - hyst;
  int offHard  = thHard - hyst;
  if (offLight < 50) offLight = 50;
  if (offHard < offLight + 50) offHard = offLight + 50;

  if (current >= 2) {
    if (force >= offHard) return 2;
    if (force >= offLight) return 1;
    return 0;
  }
  if (current == 1) {
    if (force >= thHard) return 2;
    if (force >= offLight) return 1;
    return 0;
  }
  if (force >= thHard) return 2;
  if (force >= thLight) return 1;
  return 0;
}

void updateChannel(Channel &ch, int force) {
  int target = rawLevel(force, ch.level);
  if (target == ch.level) {
    ch.pendingCount = 0;
    return;
  }
  if (target == ch.pendingLevel) {
    ch.pendingCount++;
  } else {
    ch.pendingLevel = target;
    ch.pendingCount = 1;
  }
  if (ch.pendingCount >= STABLE_FRAMES) {
    ch.level = target;
    ch.pendingCount = 0;
  }
}

int shakeLevelFrom(int count, int current) {
  int offMove   = thMove - 1;
  int offStrong = thStrong - (thStrong / 3);
  if (offMove < 1) offMove = 1;
  if (offStrong <= offMove) offStrong = offMove + 1;

  if (current >= 2) {
    if (count >= offStrong) return 2;
    if (count >= offMove) return 1;
    return 0;
  }
  if (current == 1) {
    if (count >= thStrong) return 2;
    if (count >= offMove) return 1;
    return 0;
  }
  if (count >= thStrong) return 2;
  if (count >= thMove) return 1;
  return 0;
}

void updateShake(int count) {
  int target = shakeLevelFrom(count, shakeLevel);
  if (target == shakeLevel) {
    shakePendingCount = 0;
    return;
  }
  if (target == shakePending) {
    shakePendingCount++;
  } else {
    shakePending = target;
    shakePendingCount = 1;
  }
  if (shakePendingCount >= STABLE_FRAMES) {
    shakeLevel = target;
    shakePendingCount = 0;
  }
}

void handleCommand() {
  while (Serial.available()) {
    char c = Serial.read();
    if (c == 'r' || c == 'R') {
      doBaseline();
    } else if (c == '?') {
      printSettings();
    } else if (c == 'd' || c == 'D') {
      verbose = !verbose;
    } else if (c == 'l' || c == 'L') {
      int v = Serial.parseInt();
      if (v > 0) thLight = v;
      printSettings();
    } else if (c == 'h' || c == 'H') {
      int v = Serial.parseInt();
      if (v > 0) thHard = v;
      printSettings();
    } else if (c == 'y' || c == 'Y') {
      int v = Serial.parseInt();
      if (v >= 0) hyst = v;
      printSettings();
    } else if (c == 'm' || c == 'M') {
      int v = Serial.parseInt();
      if (v > 0) thMove = v;
      printSettings();
    } else if (c == 's' || c == 'S') {
      int v = Serial.parseInt();
      if (v > 0) thStrong = v;
      printSettings();
    }
  }
}

void setup() {
  Serial.begin(115200);
  delay(300);

  analogReadResolution(12);
  analogSetAttenuation(ADC_11db);

  pinMode(PIN_LEFT, INPUT);
  pinMode(PIN_RIGHT, INPUT);
  pinMode(PIN_SHAKE, SHAKE_PULLUP ? INPUT_PULLUP : INPUT);

  for (int i = 0; i < WINDOW_FRAMES; i++) window[i] = 0;

  attachInterrupt(digitalPinToInterrupt(PIN_SHAKE), onShakeEdge, CHANGE);

  doBaseline();
  printSettings();
}

void loop() {
  handleCommand();

  unsigned long now = millis();
  if (now - lastFrame < FRAME_MS) return;
  lastFrame = now;

  noInterrupts();
  uint32_t total = edgeTotal;
  interrupts();
  int frameEdges = (int)(total - edgeMark);
  edgeMark = total;

  windowSum -= window[windowPos];
  window[windowPos] = frameEdges;
  windowSum += frameEdges;
  windowPos++;
  if (windowPos >= WINDOW_FRAMES) {
    windowPos = 0;
    windowReady = true;
  }

  int shakeCount = windowReady ? (int)windowSum : 0;
  updateShake(shakeCount);

  int mvL = readMedian(PIN_LEFT);
  int mvR = readMedian(PIN_RIGHT);

  int fL = baseL - mvL;
  int fR = baseR - mvR;
  if (fL < 0) fL = 0;
  if (fR < 0) fR = 0;

  updateChannel(chL, fL);
  updateChannel(chR, fR);

  if (verbose) {
    Serial.print("L lvl=");
    Serial.print(chL.level);
    Serial.print(" f=");
    Serial.print(fL);
    Serial.print("   R lvl=");
    Serial.print(chR.level);
    Serial.print(" f=");
    Serial.print(fR);
    Serial.print("   shake lvl=");
    Serial.print(shakeLevel);
    Serial.print(" n=");
    Serial.println(shakeCount);
  } else {
    Serial.print(chL.level);
    Serial.print(',');
    Serial.print(fL);
    Serial.print(',');
    Serial.print(chR.level);
    Serial.print(',');
    Serial.print(fR);
    Serial.print(',');
    Serial.print(shakeLevel);
    Serial.print(',');
    Serial.println(shakeCount);
  }
}
