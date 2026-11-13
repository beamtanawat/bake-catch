// Confirmed by user: AO left -> GPIO34, AO right -> GPIO35; VCC 3V3, common GND.
// deltaMv is baseline minus measured millivolts, NOT newtons or a grip percentage.
#include <WiFi.h>
#include <PubSubClient.h>
#if __has_include("secrets.h")
#include "secrets.h"
#else
const char* WIFI_SSID = "";
const char* WIFI_PASSWORD = "";
const char* MQTT_HOST = "";
const char* MQTT_TOKEN = "";
#endif

const int PIN_LEFT = 34, PIN_RIGHT = 35;
const int SAMPLES = 9, DISCARD = 3, SETTLE_US = 600;
const int BASE_ROUNDS = 40, STABLE_FRAMES = 3;
const unsigned long FRAME_MS = 20;
int thLight = 500, thHard = 2000, hyst = 600;
int baseL = 0, baseR = 0, buf[SAMPLES];
bool verbose = false;
unsigned long lastFrame = 0, lastNetwork = 0;
uint32_t sequence = 0;
char boot[17];
String command;
WiFiClient network;
PubSubClient mqtt(network);
struct Channel { int level, pendingLevel, pendingCount; };
Channel chL = {0, 0, 0}, chR = {0, 0, 0};

int readMedian(int pin) {
  for (int i = 0; i < DISCARD; i++) { analogReadMilliVolts(pin); delayMicroseconds(SETTLE_US); }
  for (int i = 0; i < SAMPLES; i++) { buf[i] = analogReadMilliVolts(pin); delayMicroseconds(SETTLE_US); }
  for (int i = 1; i < SAMPLES; i++) {
    int key = buf[i], j = i - 1;
    while (j >= 0 && buf[j] > key) { buf[j + 1] = buf[j]; j--; }
    buf[j + 1] = key;
  }
  return buf[SAMPLES / 2];
}

int captureBaseline(int pin) {
  long sum = 0;
  for (int i = 0; i < BASE_ROUNDS; i++) { sum += readMedian(pin); delay(10); }
  return sum / BASE_ROUNDS;
}

void printSettings() {
  Serial.printf("# light=%d hard=%d hyst=%d baseL=%d baseR=%d\n", thLight, thHard, hyst, baseL, baseR);
}

void doBaseline() {
  Serial.println("# capturing baseline, hands off");
  delay(1000);
  baseL = captureBaseline(PIN_LEFT); baseR = captureBaseline(PIN_RIGHT);
  chL = {0, 0, 0}; chR = {0, 0, 0};
  Serial.printf("# baseline L=%d R=%d\n", baseL, baseR);
  printSettings();
}

int rawLevel(int force, int current) {
  int offLight = max(50, thLight - hyst);
  int offHard = max(offLight + 50, thHard - hyst);
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
  if (target == ch.level) { ch.pendingCount = 0; return; }
  if (target == ch.pendingLevel) ch.pendingCount++;
  else { ch.pendingLevel = target; ch.pendingCount = 1; }
  if (ch.pendingCount >= STABLE_FRAMES) { ch.level = target; ch.pendingCount = 0; }
}

void executeCommand(String line) {
  line.trim(); if (!line.length()) return;
  char op = tolower(line[0]);
  if (op == 'r') { doBaseline(); return; }
  if (op == '?') { printSettings(); return; }
  if (op == 'd') { verbose = !verbose; return; }
  String value = line.substring(1); value.trim();
  if (!value.length()) return;
  for (unsigned int i = 0; i < value.length(); i++) if (!isDigit(value[i])) return;
  int n = value.toInt();
  if (op == 'l' && n >= 50 && n < thHard) thLight = n;
  if (op == 'h' && n > thLight && n <= 3300) thHard = n;
  if (op == 'y' && n >= 0 && n <= 3300) hyst = n;
  printSettings();
}

void handleCommand() {
  while (Serial.available()) {
    char c = Serial.read();
    if (c == '\n' || c == '\r') { executeCommand(command); command = ""; }
    else if (c == '?' || c == 'r' || c == 'R' || c == 'd' || c == 'D') {
      executeCommand(String(c)); command = "";
    } else if (command.length() < 32) command += c;
    else command = "";
  }
}

void serviceNetwork() {
  if (!WIFI_SSID[0] || !MQTT_HOST[0] || !MQTT_TOKEN[0]) return;
  if (mqtt.connected()) { mqtt.loop(); return; }
  if (millis() - lastNetwork < 5000) return;
  lastNetwork = millis();
  if (WiFi.status() != WL_CONNECTED) { WiFi.reconnect(); return; }
  char clientId[40]; snprintf(clientId, sizeof(clientId), "esp32-%s", boot);
  if (mqtt.connect(clientId, "grip-device", MQTT_TOKEN)) Serial.println("# MQTT connected");
}

void setup() {
  Serial.begin(115200); delay(300);
  analogReadResolution(12); analogSetAttenuation(ADC_11db);
  pinMode(PIN_LEFT, INPUT); pinMode(PIN_RIGHT, INPUT);
  snprintf(boot, sizeof(boot), "%08lx", (unsigned long)esp_random());
  doBaseline();
  network.setConnectionTimeout(200); mqtt.setSocketTimeout(1); mqtt.setBufferSize(384);
  mqtt.setServer(MQTT_HOST, 1883);
  if (WIFI_SSID[0] && MQTT_HOST[0] && MQTT_TOKEN[0]) {
    WiFi.mode(WIFI_STA); WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  } else Serial.println("# WiFi disabled: fill ignored secrets.h before network testing");
}

void loop() {
  handleCommand(); serviceNetwork();
  unsigned long now = millis();
  if (now - lastFrame < FRAME_MS) return;
  lastFrame = now;
  int fL = max(0, baseL - readMedian(PIN_LEFT));
  int fR = max(0, baseR - readMedian(PIN_RIGHT));
  updateChannel(chL, fL); updateChannel(chR, fR);
  if (verbose) Serial.printf("L lvl=%d f=%d   R lvl=%d f=%d\n", chL.level, fL, chR.level, fR);
  else Serial.printf("%d,%d,%d,%d\n", chL.level, fL, chR.level, fR);
  if (mqtt.connected()) {
    char payload[256];
    snprintf(payload, sizeof(payload), "{\"version\":1,\"device\":\"grip-1\",\"boot\":\"%s\",\"seq\":%lu,\"hardMv\":%d,\"levelL\":%d,\"deltaMvL\":%d,\"levelR\":%d,\"deltaMvR\":%d}",
      boot, (unsigned long)sequence++, thHard, chL.level, fL, chR.level, fR);
    mqtt.publish("bakecatch/grip-1/input", payload, false);
  }
}
