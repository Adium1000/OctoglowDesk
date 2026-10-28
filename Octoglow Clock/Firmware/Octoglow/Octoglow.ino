//    ____       __              __             
//   / __ \_____/ /_____  ____ _/ /___ _      __
//  / / / / ___/ __/ __ \/ __ `/ / __ \ | /| / /
// / /_/ / /__/ /_/ /_/ / /_/ / / /_/ / |/ |/ / 
// \____/\___/\__/\____/\__, /_/\____/|__/|__/  
//                     /____/                   

                                                                                                   
#include <WiFi.h>
#include <ESPmDNS.h>
#include <esp_mac.h>
#include <MD_MAX72xx.h>
#include <SPI.h>
#include <Wire.h>
#include <Adafruit_BMP280.h>
#include <WebServer.h>
#include <Preferences.h>
#include <ArduinoJson.h>
#include <HTTPClient.h>
#include <Update.h>
#include <StreamString.h>
#include <esp_sntp.h>
#include <esp_wifi.h>
#include <esp_sleep.h>
#include <esp_system.h>
#include <esp_ota_ops.h>
#include <atomic>
#include "runtime_support.h"
#include "mbedtls/sha256.h"
#include "mbedtls/md.h"

// FIRMWARE VERSION
#define FW_VERSION "0.3"

// FORWARD DECLARATIONS
extern bool provisionMode;
void stopProvisionMode();
bool connectToWiFi(const char* ssid, const char* pass);
void startProvisionMode();
static void registerRoutes();
static void startupFactoryResetArm();
void handleStartAp();
void handleApState();
void handleApSett();
void handleTimeSett();
void handleApPass();
void handleStopAp();
String getApSsid();
String getApPass();
String jsonEscape(const String& in);
void saveSettings();
void saveSettingsDeferred();
void applyBrightness();
static void displayLoopTick();
static void displayTaskMain(void* parameter);
// AUTH
void handleAuthState();
void handleAuthSetup();
void handleLogin();
void handleLogout();
void handleUpdateAccount();
void handleDashboard();
bool checkAuth();
// SOFTWARE UPDATE (OTA)
void handleSwUpdateUpload();
void handleSwUpdateUploadDone();
void handleBackupRestore();
void handleBackupExport();
void handleFactoryReset();
void loadSettings();
void advanceSlot();
void retreatSlot();
int  prevActiveSlot(int from);
void enterSlot(bool playTransition = true);
void drawCircuitTileFrame();
static bool ensureCircuitSlot();
void resumeCircuitTileFromPriority();
void beginC2PCapture();
void finishC2PTransition(uint8_t tileTrans = 0, uint8_t tileSpd = 0);
void beginP2CCapture();
void finishP2CTransition();
void registerTouchTap();
void checkPendingTap();
void executeTouchAction(uint8_t action);
void toggleScreenPower();
bool ipTick();
bool vmPriorityTick();
bool browserDownloadsPriorityTick();
bool obsPriorityTick();
bool beamngPriorityTick();
bool assettoPriorityTick();
bool nfsu2PriorityTick();
bool nfsmwPriorityTick();
bool nfscPriorityTick();
bool qbitPriorityTick();
bool bambuPriorityTick();
bool gameFpsPriorityTick();
void startIpShowFromTouch();
void npInit();
void rebuildNowPlayingBuf();
int  nextActiveSlot(int from);
void sortItems();
bool higherPriorityTileActive(uint8_t id);
void rebuildPriorityOrderFromEts2Order();
String priorityOrderToString();
bool npPriorityTick();
void weatherInit();
void weatherFetch();
void weatherDrawAtPos(int pos);
void currencyInit();
void currencyFetch();
void currencyDrawAtPos(int pos);
void drawCanvas();
String canvasBitmapToHex();
bool   canvasBitmapFromHex(const String& hex);
void   ssInit();
void   ssTick();
void   handleScreensaverSett();
String urlEncode(const String& s);

// STOPWATCH
void swInit();
bool swPriorityTick();
void handleSwStart();
void handleSwStop();
void handleSwState();

// TIMER
void timerInit();
bool timerPriorityTick();
void timerCheckExpiry();
void handleTimerSett();
void handleTimerStart();
void handleTimerPause();
void handleTimerState();

// POWER
void handleWifiInfo();
void handlePower();
void noteActivity();
void sleepWake();
void sleepEnter();
bool powerOffNow();

// ALARM
void alarmInit();
bool alarmPriorityTick();
void alarmStopRinging();
void handleAlarmSett();
void handleAlarmStop();
void handleAlarmState();

// DISPLAY
#define CLK_PIN      3
#define DATA_PIN     1
#define CS_PIN       2
#define MAX_DEVICES  4
#define HARDWARE_TYPE MD_MAX72XX::FC16_HW
#define BRIGHTNESS   4

// Brightness / Dim

uint8_t  curBrightness = BRIGHTNESS;
bool     dimAutoOn     = false;
uint8_t  dimFromH = 22, dimFromM = 0;
uint8_t  dimToH   =  7, dimToM   = 0;
uint8_t  dimLevel  = 1;

// BMP280
#define BMP_SDA 5
#define BMP_SCL 4

// TOUCH
#define TOUCH_PIN     6
#define TOUCH_HOLD_MS 2000
#define DOUBLE_TAP_WINDOW_MS 500

// BUZZER
#define BUZZER_PIN 12
// Which volume a sound answers to. NONE is the main volume, heard only in the
// main slider's own preview; the other four can be set apart.
#define BZ_CAT_NONE  0
#define BZ_CAT_NOTIF 1
#define BZ_CAT_AUTO  2
#define BZ_CAT_ALARM 3
#define BZ_CAT_TOUCH 4

// AP CONFIG
#define AP_PASS ""

// TIME

// TIMEZONE TABLE

struct TzEntry { const char* iana; const char* posix; };
static const TzEntry TZ_TABLE[] PROGMEM = {
  {"Africa/Abidjan", "GMT0"},
  {"Africa/Accra", "GMT0"},
  {"Africa/Addis_Ababa", "EAT-3"},
  {"Africa/Algiers", "CET-1"},
  {"Africa/Cairo", "EET-2"},
  {"Africa/Casablanca", "WET0"},
  {"Africa/Johannesburg", "SAST-2"},
  {"Africa/Lagos", "WAT-1"},
  {"Africa/Nairobi", "EAT-3"},
  {"Africa/Tripoli", "EET-2"},
  {"Africa/Tunis", "CET-1"},
  {"America/Anchorage", "AKST9AKDT,M3.2.0,M11.1.0"},
  {"America/Argentina/Buenos_Aires", "ART3"},
  {"America/Bogota", "COT5"},
  {"America/Caracas", "VET4"},
  {"America/Chicago", "CST6CDT,M3.2.0,M11.1.0"},
  {"America/Denver", "MST7MDT,M3.2.0,M11.1.0"},
  {"America/Halifax", "AST4ADT,M3.2.0,M11.1.0"},
  {"America/Lima", "PET5"},
  {"America/Los_Angeles", "PST8PDT,M3.2.0,M11.1.0"},
  {"America/Mexico_City", "CST6CDT,M4.1.0,M10.5.0"},
  {"America/New_York", "EST5EDT,M3.2.0,M11.1.0"},
  {"America/Phoenix", "MST7"},
  {"America/Santiago", "CLT4CLST,M10.2.6/24,M3.2.6/24"},
  {"America/Sao_Paulo", "BRT3BRST,M10.3.0/0,M2.3.0/0"},
  {"America/St_Johns", "NST3:30NDT,M3.2.0,M11.1.0"},
  {"America/Toronto", "EST5EDT,M3.2.0,M11.1.0"},
  {"America/Vancouver", "PST8PDT,M3.2.0,M11.1.0"},
  {"America/Winnipeg", "CST6CDT,M3.2.0,M11.1.0"},
  {"Asia/Almaty", "ALMT-6"},
  {"Asia/Amman", "EET-2EEST,M3.5.4/0,M10.5.5/1"},
  {"Asia/Baghdad", "AST-3"},
  {"Asia/Baku", "AZT-4"},
  {"Asia/Bangkok", "ICT-7"},
  {"Asia/Beirut", "EET-2EEST,M3.5.0/0,M10.5.0/0"},
  {"Asia/Colombo", "IST-5:30"},
  {"Asia/Dhaka", "BST-6"},
  {"Asia/Dubai", "GST-4"},
  {"Asia/Ho_Chi_Minh", "ICT-7"},
  {"Asia/Hong_Kong", "HKT-8"},
  {"Asia/Irkutsk", "IRKT-8"},
  {"Asia/Jakarta", "WIB-7"},
  {"Asia/Jerusalem", "IST-2IDT,M3.4.4/26,M10.5.0"},
  {"Asia/Kabul", "AFT-4:30"},
  {"Asia/Karachi", "PKT-5"},
  {"Asia/Kathmandu", "NPT-5:45"},
  {"Asia/Kolkata", "IST-5:30"},
  {"Asia/Krasnoyarsk", "KRAT-7"},
  {"Asia/Kuala_Lumpur", "MYT-8"},
  {"Asia/Kuwait", "AST-3"},
  {"Asia/Magadan", "MAGT-11"},
  {"Asia/Manila", "PHT-8"},
  {"Asia/Muscat", "GST-4"},
  {"Asia/Nicosia", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Asia/Novosibirsk", "NOVT-7"},
  {"Asia/Omsk", "OMST-6"},
  {"Asia/Qatar", "AST-3"},
  {"Asia/Riyadh", "AST-3"},
  {"Asia/Seoul", "KST-9"},
  {"Asia/Shanghai", "CST-8"},
  {"Asia/Singapore", "SGT-8"},
  {"Asia/Taipei", "CST-8"},
  {"Asia/Tashkent", "UZT-5"},
  {"Asia/Tbilisi", "GET-4"},
  {"Asia/Tehran", "IRST-3:30IRDT,80/0,264/0"},
  {"Asia/Tokyo", "JST-9"},
  {"Asia/Ulaanbaatar", "ULAT-8"},
  {"Asia/Vladivostok", "VLAT-10"},
  {"Asia/Yakutsk", "YAKT-9"},
  {"Asia/Yekaterinburg", "YEKT-5"},
  {"Asia/Yerevan", "AMT-4"},
  {"Atlantic/Azores", "AZOT1AZOST,M3.5.0/0,M10.5.0/1"},
  {"Atlantic/Cape_Verde", "CVT1"},
  {"Australia/Adelaide", "ACST-9:30ACDT,M10.1.0,M4.1.0/3"},
  {"Australia/Brisbane", "AEST-10"},
  {"Australia/Darwin", "ACST-9:30"},
  {"Australia/Hobart", "AEST-10AEDT,M10.1.0,M4.1.0/3"},
  {"Australia/Melbourne", "AEST-10AEDT,M10.1.0,M4.1.0/3"},
  {"Australia/Perth", "AWST-8"},
  {"Australia/Sydney", "AEST-10AEDT,M10.1.0,M4.1.0/3"},
  {"Europe/Amsterdam", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Athens", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Belgrade", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Berlin", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Brussels", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Bucharest", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Budapest", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Copenhagen", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Dublin", "GMT0IST,M3.5.0/1,M10.5.0"},
  {"Europe/Helsinki", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Istanbul", "TRT-3"},
  {"Europe/Kiev", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Kyiv", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Lisbon", "WET0WEST,M3.5.0/1,M10.5.0"},
  {"Europe/London", "GMT0BST,M3.5.0/1,M10.5.0"},
  {"Europe/Luxembourg", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Madrid", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Minsk", "FET-3"},
  {"Europe/Moscow", "MSK-3"},
  {"Europe/Oslo", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Paris", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Prague", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Riga", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Rome", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Samara", "SAMT-4"},
  {"Europe/Sofia", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Stockholm", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Tallinn", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Vienna", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Vilnius", "EET-2EEST,M3.5.0/3,M10.5.0/4"},
  {"Europe/Warsaw", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Europe/Zurich", "CET-1CEST,M3.5.0,M10.5.0/3"},
  {"Pacific/Auckland", "NZST-12NZDT,M9.5.0,M4.1.0/3"},
  {"Pacific/Fiji", "FJT-12"},
  {"Pacific/Guam", "ChST-10"},
  {"Pacific/Honolulu", "HST10"},
  {"Pacific/Noumea", "NCT-11"},
  {"Pacific/Port_Moresby", "PGT-10"},
  {"UTC", "UTC0"},
  {nullptr, nullptr}
};
static const int TZ_TABLE_SIZE = 119;

const char* ianaToPostfix(const char* iana) {
  for (int i = 0; i < TZ_TABLE_SIZE; i++) {
    if (strcmp(iana, TZ_TABLE[i].iana) == 0)
      return TZ_TABLE[i].posix;
  }
  return nullptr;
}

void applyUtcOffsetFallback(int offset_sec) {
  int absOff = abs(offset_sec);
  int h = absOff / 3600;
  int m = (absOff % 3600) / 60;
  char posix[24];

  if (m == 0) {
    snprintf(posix, sizeof(posix), "UTC%s%d", offset_sec >= 0 ? "-" : "+", h);
  } else {
    snprintf(posix, sizeof(posix), "UTC%s%d:%02d", offset_sec >= 0 ? "-" : "+", h, m);
  }
  setenv("TZ", posix, 1);
  tzset();
  Serial.printf("[TZ] Fallback POSIX: %s\n", posix);
}

void autoDetectTimezone() {
  if (WiFi.status() != WL_CONNECTED) return;
  HTTPClient http;

  http.begin("http://ip-api.com/json/?fields=timezone,offset");
  http.setTimeout(5000);
  int code = http.GET();
  if (code == 200) {
    String body = http.getString();
    StaticJsonDocument<128> doc;
    if (!deserializeJson(doc, body)) {
      const char* iana = doc["timezone"] | "";
      int offset_sec   = doc["offset"]   | 0;
      Serial.printf("[TZ] IP timezone: %s (offset %d)\n", iana, offset_sec);
      const char* posix = ianaToPostfix(iana);
      if (posix) {
        setenv("TZ", posix, 1);
        tzset();
        Serial.printf("[TZ] POSIX: %s\n", posix);
      } else {

        applyUtcOffsetFallback(offset_sec);
      }
    }
  } else {
    Serial.printf("[TZ] ip-api.com eroare %d - ramane TZ anterior\n", code);
  }
  http.end();
}

// NOW PLAYING

#define NOW_PLAYING_TIMEOUT_MS 20000

// CYCLE ITEMS
#define ITEM_HOUR        0
#define ITEM_DATE        1
#define ITEM_TEMP        2
#define ITEM_NOW_PLAYING 3
#define ITEM_WEATHER     4
#define ITEM_MEMENTO     5
#define ITEM_CANVAS      6
#define ITEM_NOTIF       7
#define ITEM_PRESSURE    8
#define ITEM_SCREENSAVER 9
#define ITEM_HOURWEEK    10
#define ITEM_CURRENCY    11
#define ITEM_YOUTUBE     12
#define ITEM_BATTERY     13
#define ITEM_AFTERBURNER 14
#define ITEM_COUNTDOWN   15
#define NUM_ITEMS        15
// The countdown tile's events (see COUNTDOWN TILE).
#define CD_MAX_EVENTS 5
#define CD_NAME_MAX   24          // bytes, with the terminator
#define CD_USED       0x01
#define CD_YEARLY     0x02
struct CountdownEvent {
  uint16_t year;
  uint8_t  month, day, flags;
  char     name[CD_NAME_MAX];
  // The event's own icon from the catalogue, 0 for the calendar. It took the
  // byte the struct ended in as padding, which was always stored as 0: the
  // "cdEvents" blob keeps its size, and events saved before read as "calendar".
  uint8_t  icon;
};
// The blob is read only when its length is this struct's times five.
static_assert(sizeof(CountdownEvent) == 30, "the cdEvents blob changed size: events saved before would be lost");
#define CANVAS_COLS      32

// SCREEN SAVER 
// The numbers are what "ssAnim" keeps, so they stay as they are: 4 was Meteor
// Shower and 6 Game of Life, both taken out - a clock that still has one of
// them saved plays Random.
#define SS_ANIM_RANDOM      0
#define SS_ANIM_PONG        1
#define SS_ANIM_FIREWORKS   2
#define SS_ANIM_EQ          3
#define SS_ANIM_RETIRED_4   4
#define SS_ANIM_RAIN        5
#define SS_ANIM_RETIRED_6   6
#define SS_ANIM_SNAKE       7
#define SS_ANIM_HELIX       8
#define SS_ANIM_BRICKS      9
#define SS_ANIM_RUNNER      10
#define SS_ANIM_MAX         10
static inline bool ssAnimValid(int a) {
  return a >= 0 && a <= SS_ANIM_MAX && a != SS_ANIM_RETIRED_4 && a != SS_ANIM_RETIRED_6;
}

struct CycleItem {
  uint8_t  id;
  bool     enabled;
  uint16_t durationSec;
  uint8_t  order;
};

// GLOBALS
// Hardware SPI uses the existing wiring. Define OCTOGLOW_SOFTWARE_SPI=1 to
// compare with the original driver on unusual matrix modules.
#ifndef OCTOGLOW_SOFTWARE_SPI
#define OCTOGLOW_SOFTWARE_SPI 0
#endif
#if OCTOGLOW_SOFTWARE_SPI
MD_MAX72XX mx(HARDWARE_TYPE, DATA_PIN, CLK_PIN, CS_PIN, MAX_DEVICES);
#else
MD_MAX72XX mx(HARDWARE_TYPE, SPI, CS_PIN, MAX_DEVICES);
#endif
static GzipPageTransfers pageTransfers;
void servicePageTransfers();
bool gSuppressHwFlash = false;
void screenStreamTick();
// The Arduino loop task serves the network.  Matrix timing runs in a slightly
// higher-priority task pinned to the same core, so a slow HTTP request can be
// pre-empted without allowing two cores to touch the display driver at once.
static TaskHandle_t displayTaskHandle = nullptr;
static volatile bool backgroundServiceActive = false;
static inline void mxCommit() {
  mx.update(gSuppressHwFlash ? MD_MAX72XX::OFF : MD_MAX72XX::ON);
}

// The level the panel is running at right now, 0 meaning shut down. /getscreen
// reads it: a panel in shutdown keeps its frame in the buffer but lights none of it.
uint8_t mxPanelLevel = 0;
// When /getscreen was last asked (0: never). The WiFi stays awake for a few seconds
// after, so a page polling it sees a smooth picture - see screenStreamTick().
unsigned long screenPolledMs = 0;

void applyMaxBrightness(uint8_t uiLevel) {
  mxPanelLevel = uiLevel;
  if (uiLevel == 0) {
    mx.control(MD_MAX72XX::SHUTDOWN, true);
  } else {
    mx.control(MD_MAX72XX::SHUTDOWN, false);
    uint8_t hwIntensity = uiLevel - 1;
    mx.control(MD_MAX72XX::INTENSITY, hwIntensity);
  }
}
Adafruit_BMP280   bmp;
MeasuredWebServer server(80);
Preferences       prefs;

// AUTH GLOBALS
static char authUser[33]       = "";
static char authPassHash[65]   = "";
static bool authConfigured     = false;

// Cat timp ramane valabil cookie-ul de sesiune (secunde). 30 zile.
#define SESSION_TTL_SECONDS (30UL * 24UL * 3600UL)
// A session this close to its end gets a new cookie from /whoami, which the
// web interface asks every 15 s and Octoglow Connect when it starts: whatever
// stays signed in never has to give the password again.
#define SESSION_REFRESH_SECONDS (15UL * 24UL * 3600UL)

// LOGIN LOCK
//
// Wrong passwords since the clock started. The fifth closes the login until the
// next restart - every login, the right password included, or guessing would
// simply go on. Only this count is kept, in RAM: nothing about who tried, no
// list of addresses, nothing written to NVS. Sessions already signed in (a
// browser, Octoglow Connect) carry a signed cookie that never goes through
// /login, so they keep working, and a restart from one of them opens it again.
#define LOGIN_MAX_FAILS 5
static uint8_t gLoginFails  = 0;
static bool    gLoginLocked = false;
// Prag folosit ca sa stim daca ceasul (NTP) e deja sincronizat la bord.
// (orice data reala de dupa 2023 e mai mare decat asta; time(nullptr) inainte
// de sincronizare NTP intoarce valori foarte mici, aproape de epoch 0)
#define TIME_LOOKS_VALID(t) ((t) > 1700000000UL)

static void sha256hex(const char* input, char outHex[65]) {
  uint8_t digest[32];
  mbedtls_sha256_context ctx;
  mbedtls_sha256_init(&ctx);
  mbedtls_sha256_starts(&ctx, 0);
  mbedtls_sha256_update(&ctx, (const unsigned char*)input, strlen(input));
  mbedtls_sha256_finish(&ctx, digest);
  mbedtls_sha256_free(&ctx);
  for (int i = 0; i < 32; i++) sprintf(outHex + i * 2, "%02x", digest[i]);
  outHex[64] = '\0';
}

// HMAC-SHA256(payload) folosind authPassHash-ul curent ca si cheie.
// Nu se stocheaza nimic in plus pe ESP - cheia e deja cea salvata la login.
static void hmacHex(const char* payload, char outHex[65]) {
  uint8_t digest[32];
  const mbedtls_md_info_t* info = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  mbedtls_md_context_t ctx;
  mbedtls_md_init(&ctx);
  mbedtls_md_setup(&ctx, info, 1 /* HMAC */);
  mbedtls_md_hmac_starts(&ctx, (const unsigned char*)authPassHash, strlen(authPassHash));
  mbedtls_md_hmac_update(&ctx, (const unsigned char*)payload, strlen(payload));
  mbedtls_md_hmac_finish(&ctx, digest);
  mbedtls_md_free(&ctx);
  for (int i = 0; i < 32; i++) sprintf(outHex + i * 2, "%02x", digest[i]);
  outHex[64] = '\0';
}

// Comparatie in timp constant, ca sa nu scurgem informatie prin timing.
static bool constTimeEq(const char* a, const char* b) {
  size_t la = strlen(a), lb = strlen(b);
  if (la != lb) return false;
  uint8_t diff = 0;
  for (size_t i = 0; i < la; i++) diff |= (uint8_t)(a[i] ^ b[i]);
  return diff == 0;
}

static void userToHex(const char* user, char* out) {
  size_t n = strlen(user);
  for (size_t i = 0; i < n; i++) sprintf(out + i * 2, "%02x", (uint8_t)user[i]);
  out[n * 2] = '\0';
}

// Construieste un token de sesiune STATELESS: userHex.expiry.hmac
// ESP-ul nu retine nimic despre sesiuni active - orice token se poate
// re-verifica oricand (inclusiv dupa un reboot) doar cu authPassHash-ul
// curent, care e deja incarcat din NVS la boot in loadAuth().
static String makeSessionToken(const char* user) {
  char userHex[70];
  userToHex(user, userHex);
  time_t now = time(nullptr);
  unsigned long expiry = (unsigned long)now + SESSION_TTL_SECONDS;
  char payload[140];
  snprintf(payload, sizeof(payload), "%s.%lu", userHex, expiry);
  char sig[65];
  hmacHex(payload, sig);
  return String(payload) + "." + String(sig);
}

// Verifica un token din cookie fara nicio stare pe ESP.
// Intoarce numele userului daca e valid, altfel sir gol.
static String verifySessionToken(const String& tok) {
  if (!authConfigured || tok.length() == 0) return "";

  int d1 = tok.indexOf('.');
  int d2 = (d1 < 0) ? -1 : tok.indexOf('.', d1 + 1);
  if (d1 < 0 || d2 < 0) return "";

  String userHex   = tok.substring(0, d1);
  String expiryStr = tok.substring(d1 + 1, d2);
  String sig        = tok.substring(d2 + 1);
  if (userHex.length() == 0 || expiryStr.length() == 0 || sig.length() != 64) return "";

  // Fara ceas real (NTP nesincronizat) nu putem verifica expirarea in siguranta.
  time_t now = time(nullptr);
  if (!TIME_LOOKS_VALID(now)) return "";

  unsigned long expiry = strtoul(expiryStr.c_str(), nullptr, 10);
  if ((unsigned long)now > expiry) return "";

  char payload[140];
  snprintf(payload, sizeof(payload), "%s.%s", userHex.c_str(), expiryStr.c_str());
  char expectedSig[65];
  hmacHex(payload, expectedSig);
  if (!constTimeEq(sig.c_str(), expectedSig)) return "";

  // Decodifica userHex -> text si confirma ca e chiar contul configurat acum
  // (daca userul/parola s-au schimbat intre timp, cheia HMAC s-a schimbat si
  // deja am fi picat mai sus la verificarea semnaturii - asta e doar un dublu-check).
  String user;
  for (int i = 0; i + 1 < (int)userHex.length(); i += 2) {
    char byteStr[3] = { userHex[i], userHex[i + 1], 0 };
    user += (char)strtol(byteStr, nullptr, 16);
  }
  if (user != String(authUser)) return "";
  return user;
}

// The expiry written in a token (checked or not), 0 when there is none.
static unsigned long sessionTokenExpiry(const String& tok) {
  int d1 = tok.indexOf('.');
  int d2 = (d1 < 0) ? -1 : tok.indexOf('.', d1 + 1);
  if (d1 < 0 || d2 < 0) return 0;
  return strtoul(tok.substring(d1 + 1, d2).c_str(), nullptr, 10);
}

// NAME ON THE NETWORK
//
// octoglow-XXXX.local, XXXX the end of this clock's MAC, so two clocks on one
// network never share a name; renamed from the web interface or Octoglow
// Connect (POST /hostname). Kept in the "wifi" namespace with the network it
// belongs to, so a backup restored on another clock with only Personalizare
// ticked (the default) does not hand it this one's name. It is also the name
// the router lists, and the one Connect finds the clock by when the router
// gives it another address: the clock announces _octoglow._tcp with its id,
// the whole MAC, which /authstate reports as well.
static char gHostName[33] = "";
static char gDeviceId[13] = "";

// Lowercase letters, digits and hyphens, not at either end: a DNS label.
static bool hostNameValid(const char* n) {
  size_t len = strlen(n);
  if (len < 1 || len > 32 || n[0] == '-' || n[len - 1] == '-') return false;
  for (size_t i = 0; i < len; i++)
    if (!((n[i] >= 'a' && n[i] <= 'z') || (n[i] >= '0' && n[i] <= '9') || n[i] == '-')) return false;
  return true;
}

static void netIdentityInit() {
  uint8_t mac[6] = {0};
  esp_read_mac(mac, ESP_MAC_WIFI_STA);
  snprintf(gDeviceId, sizeof(gDeviceId), "%02x%02x%02x%02x%02x%02x", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  prefs.begin("wifi", true);
  String saved = prefs.isKey("host") ? prefs.getString("host", "") : String("");
  prefs.end();
  if (hostNameValid(saved.c_str())) strncpy(gHostName, saved.c_str(), sizeof(gHostName) - 1);
  else snprintf(gHostName, sizeof(gHostName), "octoglow-%02x%02x", mac[4], mac[5]);
}

// (Re)announces the name; mDNS itself follows the station through reconnects.
static void mdnsStart() {
  MDNS.end();
  if (!MDNS.begin(gHostName)) {
    Serial.println("[mdns] could not start");
    return;
  }
  MDNS.addService("http", "tcp", 80);
  MDNS.addService("octoglow", "tcp", 80);
  MDNS.addServiceTxt("octoglow", "tcp", "id", (const char*)gDeviceId);
  MDNS.addServiceTxt("octoglow", "tcp", "fw", FW_VERSION);
}

// AP CONFIG 

// The access point name, kept in RAM the way gWifiSsidCached is further down:
// /state reports it on every request, and opening the NVS namespace for it each
// time was the most expensive thing that endpoint still did.
static String gApSsidCached;
static bool   gApSsidLoaded = false;

String getApSsid() {
  if (!gApSsidLoaded) {
    prefs.begin("apcfg", true);
    gApSsidCached = prefs.getString("ssid", "");
    prefs.end();
    // Builds before Sept 2026 had a personal built-in name, and saving the AP
    // page copied it here. It counts as never set, so the clock falls back to
    // its own; compared by hash so the name itself stays out of the firmware.
    if (cfgKeyHash(gApSsidCached.c_str()) == 0xd3749e5au) gApSsidCached = "";
    gApSsidLoaded = true;
  }
  // Match the unique out-of-box network name. A user-supplied AP name still
  // wins and is stored independently under "apcfg".
  return gApSsidCached.length() ? gApSsidCached : String("octoglow-") + String(gDeviceId).substring(8);
}

String getApPass() {
  prefs.begin("apcfg", true);
  bool hasKey = prefs.isKey("pass");
  String p = hasKey ? prefs.getString("pass", "") : String(AP_PASS);
  prefs.end();
  return p;
}

static void loadAuth() {
  prefs.begin("auth", true);
  String u = prefs.getString("user", "");
  String h = prefs.getString("hash", "");
  prefs.end();
  if (u.length() > 0 && h.length() == 64) {
    strncpy(authUser,     u.c_str(), 32); authUser[32]   = '\0';
    strncpy(authPassHash, h.c_str(), 64); authPassHash[64] = '\0';
    authConfigured = true;
  }
}

// Existing clocks already have an account and must not be sent through first-run
// setup after a firmware update. New clocks keep this flag until the last step.
static bool oobeComplete = false;

static void loadOobeState() {
  prefs.begin("oobe", false);
  if (prefs.isKey("done")) oobeComplete = prefs.getBool("done", false);
  else {
    oobeComplete = authConfigured;
    prefs.putBool("done", oobeComplete);
  }
  prefs.end();
}

static bool setOobeComplete(bool complete) {
  prefs.begin("oobe", false);
  bool saved = prefs.putBool("done", complete) > 0;
  prefs.end();
  if (saved) oobeComplete = complete;
  return saved;
}

static void saveAuth(const char* user, const char* passHash) {
  prefs.begin("auth", false);
  prefs.putString("user", user);
  prefs.putString("hash", passHash);
  prefs.end();
  strncpy(authUser,     user,     32); authUser[32]   = '\0';
  strncpy(authPassHash, passHash, 64); authPassHash[64] = '\0';
  authConfigured = true;
}

static String extractCookieToken() {
  String cookieHdr = server.header("Cookie");
  int idx = cookieHdr.indexOf("sc_tok=");
  if (idx < 0) return "";
  String tok = cookieHdr.substring(idx + 7);
  int end = tok.indexOf(';');
  if (end >= 0) tok = tok.substring(0, end);
  tok.trim();
  return tok;
}

static String getTokenUser() {
  String tok = extractCookieToken();
  if (tok.length() == 0) return "";
  return verifySessionToken(tok);
}

bool checkAuth() {
  server.client().setNoDelay(true);
  screenPolledMs = millis() | 1;
  if (provisionMode) return true;
  if (getTokenUser().length() == 0) {
    server.send(401, "text/plain", "Neautentificat");
    return false;
  }
  return true;
}

CycleItem items[NUM_ITEMS] = {
  { ITEM_HOUR,        true,  5,  0 },
  { ITEM_DATE,        true,  4,  1 },
  { ITEM_TEMP,        true,  4,  2 },
  { ITEM_NOW_PLAYING, false, 8,  3 },
  { ITEM_WEATHER,     false, 8,  4 },
  { ITEM_MEMENTO,     false, 8,  5 },
  { ITEM_CANVAS,      false, 8,  6 },
  { ITEM_PRESSURE,    false, 8,  7 },
  { ITEM_SCREENSAVER, false, 15,  8 },
  { ITEM_CURRENCY,    false, 8,  9 },
  { ITEM_YOUTUBE,     false, 8, 10 },
  { ITEM_HOURWEEK,    false, 5, 11 },
  { ITEM_BATTERY,     false, 8, 12 },
  { ITEM_AFTERBURNER, false, 8, 13 },
  { ITEM_COUNTDOWN,   false, 8, 14 },
};

// TILES REMOVED FROM THE MANAGER
//
// One bit per tile: set means the row is not in the Tile Manager, and the tile
// can only come back through the "add" picker. tileHiddenMask is indexed by
// circuit tile id (ITEM_*), prioHiddenMask by PRIORITY_ID_*.
//
// PRIORITY_ID_NOWPLAYING's bit is deliberately never used: Now Playing is one
// tile that lives in either list, so its presence is the ITEM_NOW_PLAYING bit
// in tileHiddenMask no matter which list is showing it, and npIsPriority only
// says which.
uint16_t tileHiddenMask = 0;
uint32_t prioHiddenMask = 0;

// Whether a circuit tile is in the Tile Manager at all - the same thing as "not
// offered by the add picker". A tile that is not there has nothing to draw on,
// so nothing needs fetching for it.
static inline bool tileInManager(uint8_t id) {
  return !(tileHiddenMask & (1u << id));
}

// A removed tile has no switch left to turn it off with, so removing one has to
// turn it off too. Kept here rather than only in the browser so a restored
// backup, or a client that predates this, cannot leave a tile cycling on the
// panel with no row to stop it from.
static void applyTileHiddenMask() {
  bool anyVisible = false;
  for (int i = 0; i < NUM_ITEMS; i++) {
    if (!(tileHiddenMask & (1u << items[i].id))) { anyVisible = true; break; }
  }
  // Emptying the circuit list entirely would leave the panel with nothing to
  // show and the interface with nothing to put back, so that mask is refused.
  if (!anyVisible) tileHiddenMask = 0;
  for (int i = 0; i < NUM_ITEMS; i++) {
    if (tileHiddenMask & (1u << items[i].id)) items[i].enabled = false;
  }
}

// CANVAS TILE

uint8_t canvasBitmap[CANVAS_COLS] = {0};

uint8_t       currentSlot      = 0;
unsigned long slotStartMs      = 0;

// What the panel is showing at this instant, for the Tile Manager's live
// marker. gLiveTileId is a circuit tile id (ITEM_*) or -1; gLivePrio is the
// wire name of the priority tile that took the frame, or "" when none did.
// Both are set from loop(), where the decision is actually made - and cleared
// the moment the panel starts changing hands (enterSlot, beginC2PCapture,
// beginP2CCapture): the tile preview takes "live" to mean "copy the panel",
// and between a transition and the next pass that set them, the panel already
// showed the next tile under the old one's name.
int8_t        gLiveTileId      = -1;
const char*   gLivePrio        = "";
bool          inScrollAnim     = false;
bool          inFadeOut        = false;
bool          inFadeIn         = false;
// Sentinel for "the BMP280 has never given us a usable reading".
#define TEMP_NO_READING (-999)
int           lastTemp         = TEMP_NO_READING;
unsigned long lastTempSampleMs = 0;
#define TEMP_SAMPLE_INTERVAL_MS 5000UL

// Value currently rendered on the temperature tile, so loop() can tell the
// "--" placeholder apart from a real number and swap it out in place.
int           gTempShownValue  = TEMP_NO_READING;

// BMP280 HEALTH
//
// The sensor used to be initialised exactly once in setup() and never checked
// again: if bmp.begin() lost the race at power-up, or the I2C bus later got
// wedged by a slave holding SDA low, every read failed forever and only a
// reboot brought the temperature back. These track the failure state so the
// sensor can be recovered while the clock keeps running.
bool          bmpOk              = false;
uint8_t       bmpFailStreak      = 0;
unsigned long bmpNextRetryMs     = 0;
unsigned long bmpRetryBackoffMs  = 3000UL;
unsigned long lastPressureReadMs = 0;
#define BMP_FAIL_LIMIT       3        // consecutive bad reads before re-init
#define BMP_RETRY_MIN_MS     3000UL
#define BMP_RETRY_MAX_MS     60000UL
#define BMP_READ_INTERVAL_MS 5000UL

unsigned long gLastStaticDrawMs = 0;
bool          gStaticDrawDone   = false;

// HARDWARE MONITOR - main loop instrumentation
//
// The ESP32 exposes no readable CPU-usage counter without enabling the FreeRTOS
// run-time-stats sdkconfig options (off by default in the Arduino core), so no
// usage percentage is reported - inventing one from the loop rate would only
// look precise. What is measured instead is genuinely useful on its own:
// loop() here is a non-blocking state machine, so its iteration rate and, more
// importantly, its worst iteration period tell you directly when something is
// blocking the main loop - which is exactly what makes the web UI sluggish.
unsigned long hwLastLoopUs   = 0;   // timestamp of the previous loop() entry
unsigned long hwLoopCount    = 0;   // iterations in the window being measured
unsigned long hwWindowStart  = 0;
unsigned long hwLoopMaxUs    = 0;   // worst iteration period in the current window
uint32_t      hwLoopsPerSec  = 0;   // last completed window
uint32_t      hwLoopMaxUsLast = 0;

// HTTP TIMING (diagnostic)
//
// server.handleClient() runs the whole request inline, so however long a
// response takes to push is time loop() is not animating the matrix. These
// record the worst offender since boot so it can be read back from /hwstate.
uint32_t      hwHttpMaxUs     = 0;    // longest single handleClient() call
char          hwHttpMaxUri[28] = "";  // which request that was
uint32_t      hwLoopMaxUsEver = 0;    // worst loop iteration since boot
uint32_t      hwPageFull      = 0;    // dashboard sent in full
uint32_t      hwPage304       = 0;    // dashboard answered with a 304
uint32_t      hwPageUs        = 0;    // longest page work slice (not transfer duration)
uint32_t      hwPageTransferUs = 0;   // complete transfer, including network waits
uint32_t      hwStateBuildUs  = 0;    // /state: time spent building + flushing

// These values cannot change until the next boot. getSketchSize() verifies
// the entire image; never call it from the hardware monitor's polling path.
static uint32_t hwSketchSize = 0, hwSketchFree = 0;
static bool backgroundFetchInFlight = false; // owned by loop(), released by polls

void servicePageTransfers() {
  uint32_t started = micros();
  pageTransfers.tick(hwPageTransferUs);
  uint32_t elapsed = micros() - started;
  if (elapsed > hwPageUs) hwPageUs = elapsed;
}

uint32_t      hwStateUs       = 0;    // longest /state build+send
bool          hwPeaksCleared  = false;

// The configured network name, kept in RAM. /state used to re-open the NVS
// "wifi" namespace on every request just to read this back, which is by far the
// most expensive thing that endpoint did.
String        gWifiSsidCached;

void refreshWifiSsidCache() {
  prefs.begin("wifi", true);
  gWifiSsidCached = prefs.getString("ssid", "");
  prefs.end();
}

// Boot is legitimately busy - WiFi association, NTP, the first weather and
// currency fetches - and those spikes swamped the numbers we actually care
// about. Drop everything recorded in the first 30 seconds, once.
static void hwClearPeaksAfterBoot() {
  if (hwPeaksCleared || millis() < 30000UL) return;
  hwPeaksCleared  = true;
  hwHttpMaxUs     = 0;
  hwLoopMaxUsEver = 0;
  hwPageUs        = 0;
  hwPageTransferUs = 0;
  hwStateUs       = 0;
  hwStateBuildUs  = 0;
  hwHttpMaxUri[0] = '\0';
}

// The peaks above are worst-since-boot, which is the wrong window for chasing
// a stall that happens now and then: one bad moment hours ago hides every later
// one. This lets the interface start a fresh window right before reproducing it.
static void hwResetPeaks() {
  hwHttpMaxUs     = 0;
  hwLoopMaxUsEver = 0;
  hwPageUs        = 0;
  hwPageTransferUs = 0;
  hwStateUs       = 0;
  hwStateBuildUs  = 0;
  hwHttpMaxUri[0] = '\0';
}

// Wraps every handleClient() call in the low-priority network loop so the cost
// is attributed. Calls left in the display state machines intentionally become
// no-ops after the dedicated display task has started.
static void serviceHttp() {
  if (displayTaskHandle && xTaskGetCurrentTaskHandle() == displayTaskHandle) return;
  servicePageTransfers();
  server.handledUri[0] = '\0';
  uint32_t t0 = micros();
  server.handleClient();
  uint32_t dt = micros() - t0;
  if (dt > hwHttpMaxUs) {
    hwHttpMaxUs = dt;
    // Left empty when there is no URI to name: the interface words that itself,
    // in the language it is showing, where "(fara cerere)" went out as-is.
    strncpy(hwHttpMaxUri, server.handledUri, sizeof(hwHttpMaxUri) - 1);
    hwHttpMaxUri[sizeof(hwHttpMaxUri) - 1] = '\0';
  }
}

// Called at the top of every loop() iteration; a few microseconds of work.
inline void hwMonitorTick() {
  unsigned long nowUs = micros();
  if (hwLastLoopUs) {
    unsigned long dt = nowUs - hwLastLoopUs;
    if (dt > hwLoopMaxUs) hwLoopMaxUs = dt;
    if (dt > hwLoopMaxUsEver) hwLoopMaxUsEver = (uint32_t)dt;
  }
  hwLastLoopUs = nowUs;
  hwLoopCount++;

  unsigned long nowMs = millis();
  if (nowMs - hwWindowStart >= 1000) {
    hwLoopsPerSec   = (uint32_t)((uint64_t)hwLoopCount * 1000 / (nowMs - hwWindowStart));
    hwLoopMaxUsLast = hwLoopMaxUs;
    hwLoopCount   = 0;
    hwLoopMaxUs   = 0;
    hwWindowStart = nowMs;
  }
}



char     nowPlayingBuf[300]    = "";  // "artist - title", and both can be long
char     nowArtist[128]        = "";
char     nowTitle[160]         = "";
unsigned long lastNowPlayingMs = 0;
bool     nowPlayingActive      = false;
// Octoglow Connect saying playback has stopped, waiting for the display task to
// act on it. Set from the web handler and cleared where the timeout is checked,
// so the tile goes away through exactly the same path either way.
volatile bool npStopRequested  = false;

uint8_t  npDisplayMode         = 0;

// Adaptive icon: shows a "video" glyph instead of the music note when the
// detected playback source is a video player (set by Octoglow Connect).
bool     npAdaptiveIcon        = true;
bool     npIsVideoSource       = false;
// Playback held rather than stopped: Octoglow Connect says so while it keeps
// the tile up on pause, and the tile shows the pause icon for as long as it does.
bool     npIsPaused            = false;

// NOTIFICATIONS 
char     notifBuf[204]         = "";
bool     notifActive           = false;
// When set, this replaces the notification tile's usual glyph for the current
// message only, and is cleared as soon as that message ends.
const uint8_t* notifIconOverride = nullptr;
// True only while the "web interface accessed" alert is the message on screen.
bool     webAccessAlertActive  = false;
bool     hideTileIcons         = false;
bool     notifEnabled          = true;

bool     hideIconDate          = false;
bool     hideIconTemp          = false;
bool     hideIconReminder      = false;
bool     hideIconWeather       = false;
bool     hideIconNotif         = false;
bool     hideIconNowPlaying    = false;
bool     hideIconPressure      = false;
bool     hideIconCurrency      = false;
bool     hideIconYoutube       = false;
bool     hideIconWebAccess     = false;
bool     hideIconIp            = false;
bool     hideIconVoicemeeter   = false;
bool     hideIconBrowserDownloads = false;
bool     hideIconQbit          = false;
bool     hideIconBambu         = false;
bool     hideIconObs           = false;
bool     hideIconAfterburner   = false;
bool     hideIconCountdown   = false;
bool     hideIconGameFps       = false;
bool     hideIconVolume       = false;

// TILE ICON OVERRIDES (0 = Auto/Default -> tile's own built-in icon)
// Values 1..N select an entry from ICON_CATALOG (see below).
uint8_t  iconSelDate           = 0;
uint8_t  iconSelTemp           = 0;
uint8_t  iconSelReminder       = 0;
uint8_t  iconSelNotif          = 0;
uint8_t  iconSelNpMusic        = 0;
uint8_t  iconSelNpVideo        = 0;
uint8_t  iconSelNpPaused       = 0;
uint8_t  iconSelPressure       = 0;
uint8_t  iconSelCurrency       = 0;
uint8_t  iconSelIp             = 0;
uint8_t  iconSelVoicemeeter    = 0;
uint8_t  iconSelBrowserDownloads = 0;
uint8_t  iconSelQbit            = 0; // 0 = Download Arrow, Browser Downloads' icon
uint8_t  iconSelBambu           = 0; // 0 = context-sensitive Focus / Warning / Info
// How each progress tile draws its bar (BAR_STYLE_*), POST /barstyle. The
// defaults are the bars they had before there was a choice.
#define BAR_STYLE_PROGRESS       0   // "Progress": two lit rows that grow
#define BAR_STYLE_UNDERLINE      1   // "Underline Progress": the same in a frame
#define BAR_STYLE_UNDERLINE_CUT  2   // "Underline Progress (Cutted Edges)": the frame's corners off
#define BAR_STYLE_COUNT          3
uint8_t  barStyleBrowserDownloads = BAR_STYLE_PROGRESS;
uint8_t  barStyleQbit           = BAR_STYLE_UNDERLINE;
uint8_t  barStyleBambu          = BAR_STYLE_UNDERLINE;
uint8_t  iconSelObs            = 0;
uint8_t  iconSelBattery        = 0; // 0 = icon follows the battery level
uint8_t  iconSelLowBattery     = 0; // the low battery alert's; 0 = the same
uint8_t  iconSelAfterburner    = 0; // 0 = Info for FPS, Chip for the temperatures
uint8_t  iconSelCountdown    = 0; // 0 = the calendar
uint8_t  iconSelGameFps        = 0; // 0 = Info, like the Afterburner tile's FPS
uint8_t  iconSelVolume        = 0; // 0 = source icon
// Weather has several built-in icons (one per condition); each can be
// overridden independently since the tile switches icon automatically.
uint8_t  iconWxSunny           = 0;
uint8_t  iconWxCloud           = 0;
uint8_t  iconWxRain            = 0;
uint8_t  iconWxStorm           = 0;
uint8_t  iconWxSnow            = 0;
uint8_t  iconWxWind            = 0;
uint8_t  iconWxNight           = 0;

uint8_t  scrollType            = 0;

uint8_t  scrollTypeDate         = 0;
uint8_t  scrollTypeTemp         = 0;
uint8_t  scrollTypeReminder     = 0;
uint8_t  scrollTypeWeather      = 0;
uint8_t  scrollTypeNotif        = 0;
uint8_t  scrollTypeNowPlaying   = 0;
uint8_t  scrollTypePressure     = 0;
uint8_t  scrollTypeCurrency     = 0;
uint8_t  scrollTypeYoutube      = 0;
uint8_t  scrollTypeWebAccess    = 0;
uint8_t  scrollTypeStopwatch    = 0;
uint8_t  scrollTypeTimer        = 0;
uint8_t  scrollTypeIp           = 0;
uint8_t  scrollTypeObs          = 0;
uint8_t  scrollTypeAfterburner  = 0;
uint8_t  scrollTypeCountdown  = 0;
uint8_t  scrollTypeGameFps      = 0;
uint8_t  scrollTypeVolume      = 0;

// FONT TYPE (0 = Marymba/default, 1 = Tiko 3x7, 2 = Mako 4x7,
//            3 = Segmente 4x7, 4 = Segmente Pline 4x7, 5 = Bold 5x7,
//            6 = Italic 5x7, 7 = Topait 4x8, 8 = Umbra 5x8)
uint8_t  fontType               = 0;

uint8_t  fontTypeDate           = 0;
uint8_t  fontTypeTemp           = 0;
uint8_t  fontTypeReminder       = 0;
uint8_t  fontTypeWeather        = 0;
uint8_t  fontTypeNotif          = 0;
uint8_t  fontTypeNowPlaying     = 0;
uint8_t  fontTypePressure       = 0;
uint8_t  fontTypeCurrency       = 0;
uint8_t  fontTypeYoutube        = 0;
uint8_t  fontTypeWebAccess      = 0;
uint8_t  fontTypeStopwatch      = 0;
uint8_t  fontTypeTimer          = 0;
uint8_t  fontTypeIp             = 0;
uint8_t  fontTypeObs            = 0;
uint8_t  fontTypeAfterburner    = 0;
uint8_t  fontTypeCountdown    = 0;
uint8_t  fontTypeGameFps        = 0;
uint8_t  fontTypeVolume        = 0;
// The speed tiles, the alarm and the two battery screens, each with a font of
// its own like every other tile.
uint8_t  fontTypeEts2           = 0;
uint8_t  fontTypeBeamng         = 0;
uint8_t  fontTypeAssetto        = 0;
uint8_t  fontTypeNfsu2          = 0;
uint8_t  fontTypeNfsmw          = 0;
uint8_t  fontTypeNfsc           = 0;
uint8_t  fontTypeAlarm          = 0;
uint8_t  fontTypeBattery        = 0;   // the circuit tile
uint8_t  fontTypeLowBattery     = 0;   // the priority alert

// TILE TRANSITION 
uint8_t  tileTransGlobal       = 0;
uint8_t  tileTransHour         = 0;
uint8_t  tileTransDate         = 0;
uint8_t  tileTransTemp         = 0;
uint8_t  tileTransNp           = 0;
uint8_t  tileTransWx           = 0;
uint8_t  tileTransRem          = 0;
uint8_t  tileTransCanvas       = 0;
uint8_t  tileTransPress        = 0;
uint8_t  tileTransSs           = 0;
uint8_t  tileTransCurr         = 0;
uint8_t  tileTransYt           = 0;
uint8_t  tileTransHw           = 0;
uint8_t  tileTransWeb          = 0;
uint8_t  tileTransNotif        = 0;
uint8_t  tileTransEts2         = 0;
uint8_t  tileTransVm           = 0;
uint8_t  tileTransBrowserDownloads = 0;
uint8_t  tileTransQbit          = 0;
uint8_t  tileTransBambu         = 0;
uint8_t  tileTransAfterburner   = 0;
uint8_t  tileTransCountdown   = 0;
uint8_t  tileTransGameFps       = 0;
uint8_t  tileTransVolume       = 0;
uint8_t  tileTransObs          = 0;
uint8_t  tileTransBeamng       = 0;
uint8_t  tileTransAssetto      = 0;
uint8_t  tileTransNfsu2         = 0;
uint8_t  tileTransNfsmw         = 0;
uint8_t  tileTransNfsc          = 0;
uint8_t  tileTransSw           = 0;
uint8_t  tileTransTmr          = 0;
uint8_t  tileTransAlarm        = 0;
uint8_t  tileTransIp           = 0;
uint8_t  tileTransC2P          = 0; // Circuit Tile -> Priority Tile
uint8_t  tileTransP2C          = 0; // Priority Tile -> Circuit Tile

// TILE TRANSITION SPEED
//
// How fast a transition plays, in percent of the pace its effect was drawn
// at: 100 is unchanged, 200 twice as fast, 50 half as fast. Every tileTrans*
// setting above has its own, so whichever setting picked an effect also sets
// its pace; the global slider re-applies one value to all of them. Kept in one
// NVS blob ("trSpeeds") rather than 21 more keys.
#define TRSPD_DEFAULT  100
#define TRSPD_MIN       50
#define TRSPD_MAX      250
#define TRSPD_HOUR      0
#define TRSPD_DATE      1
#define TRSPD_TEMP      2
#define TRSPD_NP        3
#define TRSPD_WX        4
#define TRSPD_REM       5
#define TRSPD_CANVAS    6
#define TRSPD_PRESS     7
#define TRSPD_SS        8
#define TRSPD_CURR      9
#define TRSPD_YT       10
#define TRSPD_HW       11
#define TRSPD_WEB      12
#define TRSPD_NOTIF    13
#define TRSPD_ETS2     14
#define TRSPD_SW       15
#define TRSPD_TMR      16
#define TRSPD_ALARM    17
#define TRSPD_IP       18
#define TRSPD_C2P      19
#define TRSPD_P2C      20
#define TRSPD_VM       21
#define TRSPD_BROWSER_DOWNLOADS 22
#define TRSPD_OBS      23
#define TRSPD_BEAMNG   24
#define TRSPD_ASSETTO  25
#define TRSPD_QBIT     26
#define TRSPD_BAMBU    27
#define TRSPD_AFTERBURNER 28
#define TRSPD_GAMEFPS  29
#define TRSPD_NFSU2    30
#define TRSPD_NFSMW    31
#define TRSPD_NFSC     32
#define TRSPD_COUNTDOWN 33
#define TRSPD_VOLUME   34
#define TRSPD_COUNT    35

// Suffix of the /state field and the POST argument, "tileTransSpd" + key - the
// same names the effects carry after "tileTrans".
static const char* const TRSPD_KEYS[TRSPD_COUNT] = {
  "Hour", "Date", "Temp", "Np", "Wx", "Rem", "Canvas", "Press", "Ss", "Curr",
  "Yt", "Hw", "Web", "Notif", "Ets2", "Sw", "Tmr", "Alarm", "Ip", "C2P", "P2C", "Vm", "BrowserDownloads", "Obs",
  "Beamng", "Assetto", "Qbit", "Bambu", "Afterburner", "GameFps", "Nfsu2", "Nfsmw", "Nfsc", "Countdown", "Volume"
};

uint8_t  tileTransSpeed        = TRSPD_DEFAULT;  // the global slider
uint8_t  tileTransSpd[TRSPD_COUNT];              // one per setting, filled by loadSettings()

// Bounce, Wrap, Bounce + Icon, Wrap + Icon. Pulse Bounce (4) and Flow + Icon
// (5) were removed in Sept 2026; loadSettings() moves a tile still set to one
// of them to the layout it was built on.
#define SCROLL_TYPE_MAX 3
static inline bool scrollIconInBuffer(uint8_t st) { return st == 2 || st == 3; }
static inline bool scrollIsWrap(uint8_t st)       { return st == 1 || st == 3; }

// Key, drawn for the "web interface accessed" alert.
// SCROLL SPEED
//
// Milliseconds between one column step and the next - lower is faster. Every
// tile that can scroll has its own, defaulting to the constant above so nothing
// changes until the user moves a slider.
#define SCROLL_SPEED_DEFAULT 40
#define SCROLL_SPEED_MIN     10
#define SCROLL_SPEED_MAX     120
#define SPD_DATE        0
#define SPD_TEMP        1
#define SPD_REMINDER    2
#define SPD_WEATHER     3
#define SPD_NOTIF       4
#define SPD_NOWPLAYING  5
#define SPD_PRESSURE    6
#define SPD_CURRENCY    7
#define SPD_YOUTUBE     8
#define SPD_WEBACCESS   9
#define SPD_STOPWATCH  10
#define SPD_IP         11
#define SPD_TIMER      12
#define SPD_OBS        13
#define SPD_AFTERBURNER 14
#define SPD_GAMEFPS    15
#define SPD_COUNTDOWN  16
#define SPD_VOLUME     17
#define SCROLL_SPEED_COUNT 18

// Suffix used for the NVS key, the /state field and the POST argument, so the
// three stay in step by construction instead of by hand.
static const char* const SPD_KEYS[SCROLL_SPEED_COUNT] = {
  "Date", "Temp", "Reminder", "Weather", "Notif", "NowPlaying", "Pressure",
  "Currency", "Youtube", "WebAccess", "Stopwatch", "Ip", "Timer", "Obs", "Afterburner", "GameFps", "Countdown", "Volume"
};

uint8_t scrollSpeed = SCROLL_SPEED_DEFAULT;                 // the global slider
uint8_t scrollSpeedTile[SCROLL_SPEED_COUNT] = {
  SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT,
  SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT,
  SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT,
  SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT, SCROLL_SPEED_DEFAULT,
  SCROLL_SPEED_DEFAULT
};

static inline unsigned long spd(uint8_t i) {
  return scrollSpeedTile[i];
}

// The alert borrows the notification tile's renderer but has its own look, so
// these pick which set of settings applies to the message currently on screen.
static inline uint8_t notifScrollType() {
  return webAccessAlertActive ? scrollTypeWebAccess : scrollTypeNotif;
}
static inline uint8_t notifFontType() {
  return webAccessAlertActive ? fontTypeWebAccess : fontTypeNotif;
}
static inline bool notifHideIcon() {
  return webAccessAlertActive ? hideIconWebAccess : hideIconNotif;
}
static inline unsigned long notifScrollSpeed() {
  return spd(webAccessAlertActive ? SPD_WEBACCESS : SPD_NOTIF);
}

const uint8_t keyIcon[8] = {
  0b00000000,
  0b01100000,
  0b10010000,
  0b10011111,
  0b10010011,
  0b01100010,
  0b00000000,
  0b00000000
};

const uint8_t notifIcon[8] = {
  0b00000000,
  0b01111110,
  0b11000011,
  0b10100101,
  0b10011001,
  0b10000001,
  0b01111110,
  0b00000000
};

// SHOW IP ADDRESS (Touch Sensor Action)
const uint8_t ipIcon[8] = {
  0b00000001,
  0b00000011,
  0b00001011,
  0b00011011,
  0b01011011,
  0b11011011,
  0b11011011,
  0b00000000
};

// EURO TRUCK SIMULATOR 2

bool     ets2Active            = false;
bool     ets2Enabled           = true;
bool     ets2OrderFirst        = false;

// VOICEMEETER AUDIO VISUALIZER
// Frames arrive from Octoglow Connect as authenticated UDP datagrams. The
// display task only copies the most recent complete frame, never waits for audio.
bool              voicemeeterEnabled = false;
uint8_t           vmMode = 0;          // Spectrum, VU, Stereo, Waveform, horizontal L/R
uint8_t           vmSensitivity = 60;  // 10..100, interpreted by Octoglow Connect
volatile bool     vmActive = false;
volatile bool     vmFrameDirty = false;
volatile unsigned long vmLastPacketMs = 0;
uint8_t           vmFrame[32] = {0};
uint16_t          vmLastSequence = 0;
bool              vmHaveSequence = false;
bool              vmWasActive = false;
portMUX_TYPE      vmMux = portMUX_INITIALIZER_UNLOCKED;

// CHROMIUM BROWSER DOWNLOADS
// Octoglow Connect sends only aggregate progress; filenames and URLs never
// reach the clock. A short timeout guarantees this priority tile disappears if
// Connect or the browser extension stops unexpectedly.
bool              browserDownloadsEnabled = false;
volatile bool     browserDownloadActive = false;
volatile bool     browserDownloadDirty = false;
volatile unsigned long browserDownloadLastMs = 0;
volatile uint8_t  browserDownloadProgress = 0;
volatile uint16_t browserDownloadCount = 0;
bool              browserDownloadWasActive = false;
portMUX_TYPE      browserDownloadMux = portMUX_INITIALIZER_UNLOCKED;

// QBITTORRENT
//
// The progress of one torrent, from Octoglow Connect, which reads qBittorrent's
// WebUI API on the computer: the clock never talks to qBittorrent. Connect
// chooses the torrent and sends its percent alone - no name, no hash, no speed.
// Silence for QBIT_TIMEOUT_MS takes the tile down, as with Browser Downloads.
#define QBIT_TIMEOUT_MS 6000UL
bool              qbitEnabled = false;       // the tile's switch in the Tile Manager
volatile bool     qbitActive = false;
volatile bool     qbitDirty = false;         // a new percent to draw
volatile unsigned long qbitLastMs = 0;
volatile uint8_t  qbitProgress = 0;          // 0-100
bool              qbitWasActive = false;
portMUX_TYPE      qbitMux = portMUX_INITIALIZER_UNLOCKED;

// BAMBU LAN print progress. Connect supplies only progress and an icon state.
#define BAMBU_TIMEOUT_MS 6000UL
bool              bambuEnabled = false;
volatile bool     bambuActive = false;
volatile bool     bambuDirty = false;
volatile unsigned long bambuLastMs = 0;
volatile uint8_t  bambuProgress = 0;
volatile uint8_t  bambuKind = 0; // 0 focus, 1 warning, 2 info
bool              bambuWasActive = false;
portMUX_TYPE      bambuMux = portMUX_INITIALIZER_UNLOCKED;

// GAME FPS - the frame rate MSI Afterburner's tile reads from RTSS, as a
// priority tile that is up only while a game is played. It has no data of its
// own: the frame rate comes in with the Afterburner tile's values through
// /abstatus, and Connect marks it game=1 when it belongs to a game in front.
bool              gameFpsEnabled = false;    // the tile's switch in the Tile Manager
bool              gameFpsWasActive = false;
bool volumeEnabled = false;
bool volumeWasActive = false;
bool volumeAdaptiveIcon = true;
// Network task only publishes a small snapshot. The matrix task owns rendering.
portMUX_TYPE volumeMux = portMUX_INITIALIZER_UNLOCKED;
uint32_t volumeLastMs = 0;
bool volumeReceived = false;
int volumePacked = 0; // percent (0..100), mute bit 7, source bits 8..9


// OBS STUDIO
// Octoglow Connect reads OBS through its own WebSocket server and sends only
// whether it streams and/or records and for how long - never a scene, a source
// or a file name. It repeats that about every two seconds while OBS is busy, so
// a tile whose sender vanished (Connect or OBS closed) times out on its own.
#define OBS_TIMEOUT_MS 6000UL
bool              obsEnabled = false;        // the tile's switch in the Tile Manager
bool              obsShowElapsed = true;     // "Show recording/stream duration"
volatile bool     obsActive = false;
volatile bool     obsStreaming = false;
volatile bool     obsRecording = false;
// Milliseconds the output on show has been running, as OBS reports it
// (outputDuration): the stream's while streaming, otherwise the recording's.
volatile uint32_t obsElapsedMs = 0;
volatile unsigned long obsLastMs = 0;
volatile bool     obsDirty = false;
bool              obsWasActive = false;
portMUX_TYPE      obsMux = portMUX_INITIALIZER_UNLOCKED;

// BEAMNG.DRIVE
// Octoglow Connect listens for the game's OutGauge UDP packets on the computer
// and sends only the speed, in whole km/h: a few times a second while it
// changes, about every two seconds while it doesn't, and active=0 when the
// packets stop (or the vehicle stands still, if Connect is set so). Silence for
// BEAMNG_TIMEOUT_MS takes the tile down too.
#define BEAMNG_TIMEOUT_MS 5000UL
bool              beamngEnabled = false;     // the tile's switch in the Tile Manager
volatile bool     beamngActive = false;
volatile int      beamngSpeedKmh = 0;
volatile unsigned long beamngLastMs = 0;
bool              beamngWasActive = false;
portMUX_TYPE      beamngMux = portMUX_INITIALIZER_UNLOCKED;

// ASSETTO CORSA
// The original game, not Competizione. Octoglow Connect reads its shared memory
// on the computer and sends only the speed, in whole km/h, the way it sends
// BeamNG.drive's: a few times a second while it changes, about every two
// seconds while it doesn't, and active=0 the moment the game pauses, leaves the
// session or closes. Silence for ASSETTO_TIMEOUT_MS takes the tile down too.
#define ASSETTO_TIMEOUT_MS 5000UL
bool              assettoEnabled = false;    // the tile's switch in the Tile Manager
volatile bool     assettoActive = false;
volatile int      assettoSpeedKmh = 0;
volatile unsigned long assettoLastMs = 0;
bool              assettoWasActive = false;
portMUX_TYPE      assettoMux = portMUX_INITIALIZER_UNLOCKED;

// NEED FOR SPEED UNDERGROUND 2
// Octoglow Connect reads the player's car speed out of SPEED2.EXE's memory on
// the computer, read-only, and sends it like Assetto Corsa's: a few times a
// second while it changes, about every two seconds while it doesn't, active=0
// the moment the race pauses, ends or the game closes. It sends the speed in
// both units, each worked out from the game's own metres per second, so the
// tile shows in mph what the game's speedometer shows too. Silence for
// NFSU2_TIMEOUT_MS takes the tile down.
#define NFSU2_TIMEOUT_MS 5000UL
bool              nfsu2Enabled = false;      // the tile's switch in the Tile Manager
volatile bool     nfsu2Active = false;
volatile int      nfsu2SpeedKmh = 0;
volatile int      nfsu2SpeedMphValue = -1;   // -1: not sent, worked out from km/h
volatile unsigned long nfsu2LastMs = 0;
bool              nfsu2WasActive = false;
portMUX_TYPE      nfsu2Mux = portMUX_INITIALIZER_UNLOCKED;

// NEED FOR SPEED MOST WANTED (2005)
// The same as NFS Underground 2's, out of speed.exe (v1.3): Octoglow Connect
// reads the player's car speed from the game's memory, read-only, and sends it
// in both units, each worked out from the game's own metres per second.
// Silence for NFSMW_TIMEOUT_MS takes the tile down.
#define NFSMW_TIMEOUT_MS 5000UL
bool              nfsmwEnabled = false;      // the tile's switch in the Tile Manager
volatile bool     nfsmwActive = false;
volatile int      nfsmwSpeedKmh = 0;
volatile int      nfsmwSpeedMphValue = -1;   // -1: not sent, worked out from km/h
volatile unsigned long nfsmwLastMs = 0;
bool              nfsmwWasActive = false;
portMUX_TYPE      nfsmwMux = portMUX_INITIALIZER_UNLOCKED;

// NEED FOR SPEED CARBON
// The same as NFS Underground 2's, out of NFSC.exe (v1.4): Octoglow Connect
// reads the player's car speed from the game's memory, read-only, and sends it
// in both units, each worked out from the game's own metres per second.
// Silence for NFSC_TIMEOUT_MS takes the tile down.
#define NFSC_TIMEOUT_MS 5000UL
bool              nfscEnabled = false;      // the tile's switch in the Tile Manager
volatile bool     nfscActive = false;
volatile int      nfscSpeedKmh = 0;
volatile int      nfscSpeedMphValue = -1;   // -1: not sent, worked out from km/h
volatile unsigned long nfscLastMs = 0;
bool              nfscWasActive = false;
portMUX_TYPE      nfscMux = portMUX_INITIALIZER_UNLOCKED;

// The unit each speed tile is drawn in. The speeds stay in km/h - as they are
// sent, and as the ETS2 alert judges them - so a change of unit shows at once
// and moves no threshold.
bool              ets2SpeedMph = false;
bool              beamngSpeedMph = false;
bool              assettoSpeedMph = false;
bool              nfsu2SpeedMph = false;
bool              nfsmwSpeedMph = false;
bool              nfscSpeedMph = false;

// How each speed tile looks - the plain number or one of the gauges
// (SPEED_GAUGE_*) - and the km/h at the end of its gauge's scale: a truck
// rarely passes 130, a car in BeamNG.drive goes well past it, and a race car
// in Assetto Corsa past 250.
#define ETS2_GAUGE_FULL_DEFAULT    130
#define BEAMNG_GAUGE_FULL_DEFAULT  250
#define ASSETTO_GAUGE_FULL_DEFAULT 300
#define NFSU2_GAUGE_FULL_DEFAULT   300
#define NFSMW_GAUGE_FULL_DEFAULT   300
#define NFSC_GAUGE_FULL_DEFAULT    300
uint8_t           ets2Gauge = 0;
uint8_t           beamngGauge = 0;
uint8_t           assettoGauge = 0;
uint8_t           nfsu2Gauge = 0;
uint8_t           nfsmwGauge = 0;
uint8_t           nfscGauge = 0;
uint16_t          ets2GaugeFull = ETS2_GAUGE_FULL_DEFAULT;
uint16_t          beamngGaugeFull = BEAMNG_GAUGE_FULL_DEFAULT;
uint16_t          assettoGaugeFull = ASSETTO_GAUGE_FULL_DEFAULT;
uint16_t          nfsu2GaugeFull = NFSU2_GAUGE_FULL_DEFAULT;
uint16_t          nfsmwGaugeFull = NFSMW_GAUGE_FULL_DEFAULT;
uint16_t          nfscGaugeFull = NFSC_GAUGE_FULL_DEFAULT;

// PRIORITY TILES generic precedence order

#define PRIORITY_ID_NOTIF      0
#define PRIORITY_ID_ETS2       1
#define PRIORITY_ID_NOWPLAYING 2
#define PRIORITY_ID_STOPWATCH  3
#define PRIORITY_ID_TIMER      4
#define PRIORITY_ID_WEB        5
#define PRIORITY_ID_ALARM      6
#define PRIORITY_ID_VOICEMEETER 7
#define PRIORITY_ID_BROWSER_DOWNLOADS 8
#define PRIORITY_ID_OBS        9
#define PRIORITY_ID_LOW_BATTERY 10
#define PRIORITY_ID_BEAMNG     11
#define PRIORITY_ID_ASSETTO    12
#define PRIORITY_ID_QBIT       13
#define PRIORITY_ID_BAMBU      14
#define PRIORITY_ID_GAMEFPS    15
#define PRIORITY_ID_NFSU2      16
#define PRIORITY_ID_NFSMW      17
#define PRIORITY_ID_NFSC       18
#define PRIORITY_ID_VOLUME     19
#define NUM_PRIORITY_IDS       20
// The alarm starts out at the top: it is the one tile that goes off on its own,
// while you are somewhere else, and an alarm ringing behind another tile is an
// alarm you can hear but cannot read. The order stays the user's to change.
uint8_t  priorityOrder[NUM_PRIORITY_IDS] = { PRIORITY_ID_ALARM, PRIORITY_ID_LOW_BATTERY, PRIORITY_ID_VOLUME, PRIORITY_ID_NOTIF, PRIORITY_ID_BROWSER_DOWNLOADS, PRIORITY_ID_QBIT, PRIORITY_ID_BAMBU, PRIORITY_ID_OBS, PRIORITY_ID_VOICEMEETER, PRIORITY_ID_ETS2, PRIORITY_ID_BEAMNG, PRIORITY_ID_ASSETTO, PRIORITY_ID_NFSU2, PRIORITY_ID_NFSMW, PRIORITY_ID_NFSC, PRIORITY_ID_GAMEFPS, PRIORITY_ID_NOWPLAYING, PRIORITY_ID_STOPWATCH, PRIORITY_ID_TIMER, PRIORITY_ID_WEB };

// The host PC sends this via /batterystatus. Data expires if Connect stops.
portMUX_TYPE batteryMux = portMUX_INITIALIZER_UNLOCKED;
int8_t batteryPercent = -1;
bool batteryPresent = false;
bool batteryAc = false;
bool batteryCharging = false;
unsigned long batteryLastMs = 0;
unsigned long batteryAlertUntilMs = 0;
bool batteryAlertArmed = true;
bool batteryAlertWasActive = false;
bool lowBatteryEnabled = true;          // the alert's switch in the Tile Manager
#define BATTERY_LOW_THRESHOLD 20
// Host updates arrive every 15 s. Allow brief Wi-Fi/auth interruptions without
// replacing a recent, genuine reading with "--"; still expire a lost host.
#define BATTERY_STATUS_TIMEOUT_MS 120000UL

// ALARM
//
// Three independent alarms. Each has a time, the weekdays it repeats on, a tone
// and a switch. One rings when the clock enters its minute on one of its days,
// and goes on ringing until the touch sensor or the interface stops it.
#define ALARM_COUNT 3

// Days are one bit each, bit 0 = Monday through bit 6 = Sunday. struct tm counts
// from Sunday, so the two are bridged in exactly one place: ALARM_WDAY_BIT.
#define ALARM_WDAY_BIT(tm_wday) ((uint8_t)(1 << (((tm_wday) + 6) % 7)))
#define ALARM_ALL_DAYS 0x7F

// Ringing forever would hold the panel and the buzzer for the rest of the day
// when nobody is there to stop it. After this it gives up on its own; the alarm
// still rings at its next slot.
#define ALARM_MAX_RING_MS 900000UL
// Silence between one pass of the tone and the next.
#define ALARM_TONE_GAP_MS 700UL

struct AlarmCfg {
  uint8_t hour;
  uint8_t minute;
  uint8_t days;
  bool    enabled;
  // Whether this slot holds an alarm at all. Absent is not the same as switched
  // off: an alarm that is off is one you keep and will want again, an absent
  // one is a card that never appears.
  bool    present;
  char    tone[16];
};

AlarmCfg alarms[ALARM_COUNT] = {
  { 7, 30, 0x1F,           false, false, "classic" },
  { 8,  0, 0x60,           false, false, "chimes"  },
  { 9,  0, ALARM_ALL_DAYS, false, false, "morning" },
};

bool          alarmRinging     = false;
int8_t        alarmRingingIdx  = -1;
unsigned long alarmRingStartMs = 0;
bool          alarmWasActive   = false;
unsigned long alarmToneNextMs  = 0;

// One latch per alarm, held for as long as the clock is inside that alarm's
// minute. loop() runs thousands of times a second, so without it an alarm would
// re-fire on every pass; clearing it the moment the minute passes is also what
// lets the same alarm ring again tomorrow, and again next week.
bool          alarmFired[ALARM_COUNT] = { false, false, false };
bool     webAccessEnabled = true;
// ALERTS: what the Alerts tile (the "webaccess" one) says on the panel, each
// with a switch of its own in the tile's settings. The tile's switch
// (webAccessEnabled) turns them all off together.
bool     webAlertAccess   = true;   // the dashboard was opened in a browser
bool     webAlertLocked   = true;   // too many wrong passwords closed the sign-in
bool     webAlertLogin    = true;   // someone signed in from a browser
bool     webAlertNet      = true;   // the Wi-Fi connection was lost
bool     nowPlayingIsPriority  = false;

bool     npPriorityWasActive   = false;
bool     timerWasActive        = false;

int      ets2Speed             = 0;
unsigned long lastEts2Ms       = 0;
#define ETS2_TIMEOUT_MS 5000

static uint8_t  ets2ColBuf[64];
static int      ets2ColCount  = 0;
static int      ets2LastSpeed = -1;

// STOPWATCH  variables
bool          swRunning       = false;
bool          swWasActive     = false;
unsigned long swStartMs       = 0;
unsigned long swElapsedMs     = 0;

// TIMER variables
char          eventSoundTimer[16] = "calm";

uint32_t      timerDurationSec      = 300;
uint32_t      timerRemainingSnapMs  = 300000UL;
bool          timerRunning          = false;
bool          timerFinished         = false;
bool          timerBlinkOn          = true;
unsigned long timerBlinkLastMs      = 0;
unsigned long timerEndMs            = 0;
uint8_t       timerPreset           = 0;

// POMODORO - the Timer's second mode. Work, a short break, work... and after
// every pomoRounds of work a long break, each part starting by itself with its
// own sound, until it is paused. The panel shows the time as the Timer does,
// and for a moment when a part starts (and then every minute) which part it
// is: "M2" - the second round of work - "P" or "PL" (a long break); "W2", "B"
// or "LB" in English, following the web interface's language.
#define TIMER_MODE_TIMER    0
#define TIMER_MODE_POMODORO 1
uint8_t  timerMode    = TIMER_MODE_TIMER;
uint8_t  pomoWorkMin  = 25;
uint8_t  pomoShortMin = 5;
uint8_t  pomoLongMin  = 15;
uint8_t  pomoRounds   = 4;       // rounds of work before a long break
uint8_t  pomoPhase    = 0;       // 0 work, 1 short break, 2 long break
uint8_t  pomoRound    = 1;       // the round of work, 1..pomoRounds
bool     pomoInCycle  = false;   // started, and not reset since
unsigned long pomoShownMs = 0;   // when the part's label was last put up
char     eventSoundPomoBreak[16] = "chime";   // a round of work is over
char     eventSoundPomoWork[16]  = "bell";    // a break is over
extern char webUiLang[3];                      // the labels' language, set further down

static uint32_t pomoPhaseSec(uint8_t phase) {
  return (uint32_t)(phase == 0 ? pomoWorkMin : phase == 1 ? pomoShortMin : pomoLongMin) * 60UL;
}

// Back to the start of a cycle: round 1, work, not running.
static void pomoReset() {
  pomoInCycle = false;
  pomoPhase = 0;
  pomoRound = 1;
  timerRemainingSnapMs = pomoPhaseSec(0) * 1000UL;
}

// The part that follows the one just over, already running.
static void pomoNextPhase() {
  if (pomoPhase == 0) {
    pomoPhase = (pomoRound % pomoRounds == 0) ? 2 : 1;
    nbPlayPreset(eventSoundPomoBreak, BZ_CAT_AUTO);
  } else {
    pomoPhase = 0;
    pomoRound = pomoRound % pomoRounds + 1;   // after the long break, round 1
    nbPlayPreset(eventSoundPomoWork, BZ_CAT_AUTO);
  }
  timerEndMs = millis() + pomoPhaseSec(pomoPhase) * 1000UL;
  pomoShownMs = millis();
}

void timerStart() {
  if (timerRunning || timerFinished) return;
  if (timerMode == TIMER_MODE_POMODORO && !pomoInCycle) {
    pomoPhase = 0;
    pomoRound = 1;
    timerRemainingSnapMs = pomoPhaseSec(0) * 1000UL;
    pomoInCycle = true;
  }
  if (timerRemainingSnapMs == 0) timerRemainingSnapMs = (unsigned long)timerDurationSec * 1000UL;
  timerEndMs  = millis() + timerRemainingSnapMs;
  timerRunning = true;
  // Resumed or started: say which part it is.
  pomoShownMs = millis();
}

void timerPause() {
  if (!timerRunning) return;
  long rem = (long)(timerEndMs - millis());
  timerRemainingSnapMs = (rem > 0) ? (unsigned long)rem : 0;
  timerRunning = false;
}

void timerDismiss() {
  timerFinished = false;
  timerRemainingSnapMs = (unsigned long)timerDurationSec * 1000UL;
}

uint32_t timerGetRemainingSec() {
  if (timerRunning) {
    long rem = (long)(timerEndMs - millis());
    if (rem < 0) rem = 0;
    return (uint32_t)(rem / 1000);
  }
  return (uint32_t)(timerRemainingSnapMs / 1000);
}

void timerCheckExpiry() {
  if (!timerRunning) return;
  if (timerGetRemainingSec() == 0) {
    if (timerMode == TIMER_MODE_POMODORO) {
      pomoNextPhase();
      return;
    }
    timerRunning = false;
    timerRemainingSnapMs = 0;
    timerFinished = true;
    timerBlinkOn = true;
    timerBlinkLastMs = millis();
    nbPlayPreset(eventSoundTimer, BZ_CAT_AUTO);
  }
}

// The part's label is up for its first 3 seconds, and for 2 every minute.
static bool pomoLabelShowing() {
  if (timerMode != TIMER_MODE_POMODORO || !pomoInCycle || !timerRunning) return false;
  unsigned long e = millis() - pomoShownMs;
  return e < 3000UL || (e >= 60000UL && e % 60000UL < 2000UL);
}

void timerFormatText(char* out, size_t outSize) {
  if (pomoLabelShowing()) {
    bool ro = strcmp(webUiLang, "en") != 0;
    if (pomoPhase == 0) snprintf(out, outSize, "%c%u", ro ? 'M' : 'W', (unsigned)pomoRound);
    else snprintf(out, outSize, "%s", pomoPhase == 1 ? (ro ? "P" : "B") : (ro ? "PL" : "LB"));
    return;
  }
  uint32_t rem = timerGetRemainingSec();
  uint32_t h = rem / 3600, m = (rem % 3600) / 60, s = rem % 60;
  if (h > 0) snprintf(out, outSize, "%02u:%02u:%02u", (unsigned)h, (unsigned)m, (unsigned)s);
  else       snprintf(out, outSize, "%02u:%02u", (unsigned)m, (unsigned)s);
}

unsigned long swGetElapsedMs() {
  return swRunning ? (swElapsedMs + (millis() - swStartMs)) : swElapsedMs;
}

void swFormatFull(char* out, size_t outSize, unsigned long ms) {
  unsigned long h  = ms / 3600000UL;
  unsigned long m  = (ms / 60000UL) % 60UL;
  unsigned long s  = (ms / 1000UL) % 60UL;
  unsigned long cs = (ms / 10UL) % 100UL;
  snprintf(out, outSize, "%02lu:%02lu:%02lu:%02lu", h, m, s, cs);
}

void swFormatAdaptive(char* out, size_t outSize, unsigned long ms) {
  unsigned long h = ms / 3600000UL;
  unsigned long m = (ms / 60000UL) % 60UL;
  unsigned long s = (ms / 1000UL) % 60UL;
  if (h > 0) {
    snprintf(out, outSize, "%02lu:%02lu:%02lu", h, m, s);
  } else {
    snprintf(out, outSize, "%02lu:%02lu", m, s);
  }
}

// WEATHER
char     weatherCity[64]       = "";
char     weatherApiKey[64]     = "";

// OpenWeather keys are 32 hex characters. Rather than pin it to that and break
// the day they change format, this only insists on something a key could be:
// printable ASCII, no spaces, and short enough to store whole. That is already
// enough to reject the bullet characters a masked field used to post back.
static bool wxKeyLooksValid(const String& k) {
  if (k.length() < 8 || k.length() >= sizeof(weatherApiKey)) return false;
  for (unsigned int i = 0; i < k.length(); i++) {
    char c = k[i];
    if (c <= ' ' || c > '~') return false;
  }
  return true;
}
char     weatherLang[8]        = "en";
float    weatherLat            = 0.0f;
float    weatherLon            = 0.0f;
char     weatherBuf[128]       = "";
// Which parts of the reading the tile draws. Stored as flags rather than an
// index so the seven combinations need no lookup table, and so the default (all
// three) is the same value the tile behaved as before this existed.
#define WX_SHOW_TEMP 0x01
#define WX_SHOW_HUM  0x02
#define WX_SHOW_DESC 0x04
#define WX_SHOW_ALL  (WX_SHOW_TEMP | WX_SHOW_HUM | WX_SHOW_DESC)
uint8_t  wxPreset              = WX_SHOW_ALL;

float    weatherTempC          = -999;
int      weatherHumidity       = -1;
char     weatherDesc[48]       = "";
unsigned long lastWeatherFetch = 0;
bool     weatherValid          = false;
#define WEATHER_FETCH_INTERVAL_MS  600000UL

// WHY AN ONLINE TILE HAS NOTHING TO SHOW
//
// The weather, the YouTube counter and the exchange rate sit their turn out
// until their service answers (slotShowable()), so the clock itself never says
// what is wrong. The Tile Manager does, from one of these: on the web page and
// in Octoglow Connect the tile's line reads the error and its icon carries an
// exclamation mark. 0 also covers "the first answer is still on its way".
#define API_ERR_NONE      0
#define API_ERR_NO_KEY    1   // no API key stored
#define API_ERR_NO_TARGET 2   // no place (weather) / no channel (YouTube)
#define API_ERR_REFUSED   3   // the service refused the key
#define API_ERR_NOT_FOUND 4   // the place / the channel does not exist
#define API_ERR_NO_ANSWER 5   // no answer: network, timeout, an unreadable reply
// After an unanswered request the next try comes in a minute, not after the
// service's whole refresh interval (ten minutes; six hours for the rates).
#define API_RETRY_MS      60000UL
uint8_t  weatherErr            = API_ERR_NONE;
static uint8_t weatherFails    = 0;

// What a failed request leaves behind. A refusal is reported at once; a lost
// answer only when there is no reading to show, or twice in a row - one
// dropped request should not put a warning on a tile that still shows data.
static void apiNoteFailure(uint8_t code, bool haveData, uint8_t& err, uint8_t& fails) {
  if (code == API_ERR_NONE) return;
  if (code != API_ERR_NO_ANSWER || !haveData || ++fails >= 2) err = code;
}

// The time of the last request, moved so that the next one is due in a minute.
static unsigned long apiRetryStamp(unsigned long intervalMs) {
  unsigned long t = millis() - intervalMs + API_RETRY_MS;
  return t ? t : 1;
}

const uint8_t sunnyIcon[8] = {
  0b00011000,
  0b01000010,
  0b00011000,
  0b10111101,
  0b10111101,
  0b00011000,
  0b01000010,
  0b00011000
};

const uint8_t cloudIcon[8] = {
  0b00000000,
  0b00111000,
  0b01000110,
  0b10000001,
  0b10000001,
  0b01111110,
  0b00000000,
  0b00000000
};

const uint8_t cloudRainIcon[8] = {
  0b00000000,
  0b00111000,
  0b01000110,
  0b10000001,
  0b10000001,
  0b01111110,
  0b01010100,
  0b00101010
};

const uint8_t cloudLightningIcon[8] = {
  0b00111000,
  0b01000110,
  0b10000001,
  0b10000001,
  0b01111110,
  0b00100100,
  0b01101100,
  0b01001000
};

const uint8_t cloudSnowIcon[8] = {
  0b00111000,
  0b01000110,
  0b10000001,
  0b10010001,
  0b00111000,
  0b01010010,
  0b11100111,
  0b01000010
};

const uint8_t windIcon[8] = {
  0b00001100,
  0b00010010,
  0b00000010,
  0b11111100,
  0b00000000,
  0b11111000,
  0b00000100,
  0b00001000
};

char     weatherCondition[32]  = "";

const uint8_t moonIcon[8] = {
  0b00111100,
  0b01110000,
  0b11100000,
  0b11100000,
  0b11100000,
  0b11100000,
  0b01110000,
  0b00111100
};

// THE CLOCK, READ SAFELY
//
// getLocalTime(info, 0) is not a reliable way to ask what time it is. The core
// writes it as
//
//     uint32_t start = millis();
//     while ((millis() - start) <= ms) { ... return true; }
//     return false;
//
// so with ms == 0 the body runs only if the millisecond counter has not ticked
// between those two millis() calls. That gap is a couple of microseconds out of
// every thousand, which makes roughly one call in several hundred report failure
// on a clock that is perfectly set - and callers that bail on failure then skip
// a draw for no reason at all.
//
// Giving it a non-zero timeout would trade that for a different problem: on a
// clock that has never been set it spends a delay(10) per attempt, and these run
// inside loop(), where the display advances one column per iteration.
//
// This is the same test with neither behaviour: read the clock once, and decide
// from the year whether it has been set.
static bool readLocalTime(struct tm& out) {
  time_t t = time(nullptr);
  localtime_r(&t, &out);
  return out.tm_year > (2016 - 1900);
}

static bool isNight() {
  struct tm ti;
  if (!readLocalTime(ti)) return false;
  int h = ti.tm_hour;
  return (h >= 21 || h < 6);
}

// Set by the Preview while it draws a weather of its own (a clock with no key,
// no place or no answer yet has none to show): 22 degrees, 54% and a clear
// sky, put the way the tile's settings have it - the unit, what it shows, the
// icon chosen for a clear day or night, the description in the weather's
// language.
static bool gWxSample = false;

// "clear sky" as OpenWeatherMap words it in the language asked for.
static const char* wxSampleDesc() {
  static const char* const WORDS[][2] = {
    { "ro", "cer senin" },     { "de", "Klarer Himmel" }, { "fr", "ciel dégagé" },
    { "es", "cielo claro" },   { "it", "cielo sereno" },  { "hu", "tiszta égbolt" },
    { "pt", "céu limpo" },     { "nl", "onbewolkt" },     { "pl", "bezchmurnie" },
  };
  for (const auto& w : WORDS)
    if (strncasecmp(weatherLang, w[0], 2) == 0) return w[1];
  return "clear sky";
}

const uint8_t* getWeatherIcon() {
  const char* cond = gWxSample ? "Clear" : weatherCondition;
  if (strcasecmp(cond, "Clear") == 0)
    return isNight() ? resolveTileIcon(iconWxNight, moonIcon) : resolveTileIcon(iconWxSunny, sunnyIcon);
  if (strcasecmp(cond, "Clouds") == 0)
    return resolveTileIcon(iconWxCloud, cloudIcon);
  if (strcasecmp(cond, "Rain") == 0 ||
      strcasecmp(cond, "Drizzle") == 0)
    return resolveTileIcon(iconWxRain, cloudRainIcon);
  if (strcasecmp(cond, "Thunderstorm") == 0)
    return resolveTileIcon(iconWxStorm, cloudLightningIcon);
  if (strcasecmp(cond, "Snow") == 0)
    return resolveTileIcon(iconWxSnow, cloudSnowIcon);
  if (strcasecmp(cond, "Wind") == 0   ||
      strcasecmp(cond, "Mist") == 0   ||
      strcasecmp(cond, "Fog") == 0    ||
      strcasecmp(cond, "Haze") == 0   ||
      strcasecmp(cond, "Smoke") == 0  ||
      strcasecmp(cond, "Dust") == 0   ||
      strcasecmp(cond, "Sand") == 0   ||
      strcasecmp(cond, "Ash") == 0    ||
      strcasecmp(cond, "Squall") == 0 ||
      strcasecmp(cond, "Tornado") == 0)
    return resolveTileIcon(iconWxWind, windIcon);

  return resolveTileIcon(iconWxCloud, cloudIcon);
}

uint8_t  tempUnit              = 0;

uint8_t  hourFormat            = 0;
bool     hourLeadingZero       = false;   // 2:30 -> 02:30

// WATCHFACE TILE (the legacy ITEM_HOURWEEK id stays stable in saved tile lists)
//
// Its own copy of the hour options, so it can run 12h next to a 24h Hour tile,
// plus the one-row bar that goes with the weekday.
#define HW_BAR_STATIC    0   // a fixed underline
#define HW_BAR_SECONDS   1   // fills up across the minute
#define HW_BAR_BELOW     0
#define HW_BAR_ABOVE     1
#define HW_SKIN_COMPACT  0
#define HW_SKIN_SECONDS  1
#define HW_SKIN_TEMP     2   // legacy saved skin; migrated to Compact + sensor
#define HW_CONTENT_DAY      0
#define HW_CONTENT_SENSOR   1
#define HW_CONTENT_WEATHER  2
uint8_t  hwSkin                = HW_SKIN_COMPACT;
uint8_t  hwContent             = HW_CONTENT_DAY;
uint8_t  hwFormat              = 0;       // 0 = 24h, 1 = 12h
bool     hwLeadZero            = false;
uint8_t  hwBarMode             = HW_BAR_SECONDS;
uint8_t  hwBarPos              = HW_BAR_BELOW;
bool     hwSwap                = false;   // day on the left, time on the right

static void normalizeWatchfaceSettings() {
  if (hwSkin == HW_SKIN_TEMP) {
    hwSkin = HW_SKIN_COMPACT;
    hwContent = HW_CONTENT_SENSOR;
  }
  if (hwSkin > HW_SKIN_SECONDS) hwSkin = HW_SKIN_COMPACT;
  if (hwContent > HW_CONTENT_WEATHER) hwContent = HW_CONTENT_DAY;
}

// DEFAULT START MODE
//
// Which radio the clock comes up on after a power cut. Choosing AP also keeps
// the tile loop running there: the access point stops being a repair hatch and
// becomes how the clock normally lives, for anyone with no router to put it on.
// Purely an interface preference: with it off, the web interface leaves out the
// rows it would otherwise show dimmed - a tile that needs the internet while the
// access point is up, an event sound whose tile is switched off, the update
// check in AP mode. Kept on the device so it travels with the user rather than
// with the browser.
bool     showGrayedContent     = true;

// Marks the row the panel is showing right now, in the Tile Manager.
bool     liveTileHighlight     = true;

// Dark mode and the interface language. Neither changes anything the device
// does - they sit next to accentColor purely so the interface looks the same
// from whichever browser the user opens it in. The browser keeps its own
// localStorage copy as well, to paint the right theme before /state arrives.
bool     webUiDark             = true;
char     webUiLang[3]          = "en";
// Forma ramelor de iconita din interfata: 0 cerc, 1 patrat rotunjit, 2 dala,
// 3 squircle.
uint8_t  webUiShape            = 0;
// Which palette of the Themes page the colours came from: its number, 100 for
// the user's own, 255 on a clock that has never said (the page then works it
// out from the accent). The page's business entirely - kept here so "restore"
// goes back to the same palette in every browser.
uint8_t  webUiPalette          = 255;
// Custom interface colours as "bgDark,cardDark,bgLight,cardLight": each slot is
// empty for the theme's own colour, or #rrggbb. Stored as sent - the browser
// works the rest of the surfaces out from them.
char     webUiColors[32]       = "";

#define START_MODE_WIFI 0
#define START_MODE_AP   1
uint8_t  defaultStartMode      = START_MODE_WIFI;

// True while the panel belongs to the AP badge rather than to the tiles. Every
// early return in loop() goes through this, so the one exception - AP picked as
// the start mode - is stated once instead of thirty-four times.
static inline bool tileLoopSuspended() {
  return provisionMode && defaultStartMode != START_MODE_AP;
}

// NETWORK TIME
//
// On by default: NTP keeps the clock right on its own. Switched off, nothing
// writes the RTC but the user - and since there is no battery behind it, the
// time is genuinely lost at every restart and has to be typed in again.
bool     netTimeSync           = true;

// With no valid clock verifySessionToken() refuses to judge a cookie's expiry,
// so every session is rejected and the page that sets the time is the one page
// nobody can reach. Boot manual mode on a plausible date instead of 1970.
#define MANUAL_TIME_FALLBACK 1767225600UL   // 2026-01-01 00:00:00

// SNTP would otherwise walk over a hand-set clock at its next poll.
static void stopNtp() {
  if (esp_sntp_enabled()) esp_sntp_stop();
}

static void seedManualClock() {
  if (TIME_LOOKS_VALID(time(nullptr))) return;
  struct timeval tv;
  tv.tv_sec  = (time_t)MANUAL_TIME_FALLBACK;
  tv.tv_usec = 0;
  settimeofday(&tv, nullptr);
}

// mktime() reads the fields as local time in whatever TZ is active and
// localtime_r() turns them back into the same fields, so what the user types is
// exactly what the tiles show - no offset arithmetic on either side.
static bool applyManualTime(int y, int mo, int d, int h, int mi, int s) {
  if (y < 2024 || y > 2099 || mo < 1 || mo > 12 || d < 1 || d > 31 ||
      h < 0 || h > 23 || mi < 0 || mi > 59 || s < 0 || s > 59) return false;
  struct tm t;
  memset(&t, 0, sizeof(t));
  t.tm_year  = y - 1900;
  t.tm_mon   = mo - 1;
  t.tm_mday  = d;
  t.tm_hour  = h;
  t.tm_min   = mi;
  t.tm_sec   = s;
  t.tm_isdst = -1;
  time_t e = mktime(&t);
  if (e <= 0) return false;
  // mktime normalises out-of-range days (32 Jan becomes 1 Feb); reject that
  // rather than silently storing a date the user did not ask for.
  if (t.tm_mday != d || t.tm_mon != mo - 1) return false;
  struct timeval tv;
  tv.tv_sec  = e;
  tv.tv_usec = 0;
  settimeofday(&tv, nullptr);
  return true;
}

uint8_t  dateFormat            = 2;
uint8_t  dateLang              = 0;  // 0 = English, 1 = Romanian (used only by dateFormat 4 "Mon 11")
char     customDateFmt[24]     = "DD/MM/YYYY";  // used only by dateFormat 5 (custom)

enum NpState {
  NP_SHOW_START,
  NP_PAUSE_BEFORE_RIGHT,
  NP_SCROLL_RIGHT,
  NP_PAUSE_AFTER_RIGHT,
  NP_SCROLL_LEFT,
  NP_PAUSE_BEFORE_NEXT,
  NP_PAUSE_SHORT,
  NP_SCROLL_WRAP
};
NpState       npState         = NP_SHOW_START;
unsigned long npPauseStartMs  = 0;
#define NP_PAUSE_MS 1000

#define SCROLL_WRAP_PASSES 2
static int npWrapPass = 0;

NpState       wxState         = NP_SHOW_START;
unsigned long wxPauseStartMs  = 0;
static int    wxWrapPass      = 0;

NpState       notifState       = NP_SHOW_START;
unsigned long notifPauseMs     = 0;
// Room for the whole text in the widest font (6 columns a letter with the
// gap) plus the icon. At 512 anything past ~85 characters was cut off.
#define NOTIF_COL_MAX 1250
static uint8_t  notifColBuf[NOTIF_COL_MAX];
static int      notifColCount  = 0;
static int      notifScrollPos = 0;
static unsigned long notifLastScrollMs = 0;
static int      notifWrapPass  = 0;
static uint8_t  wxColBuf[512];
static int      wxColCount  = 0;
static int      wxScrollPos = 0;
static unsigned long wxLastScrollMs = 0;

// SHOW IP ADDRESS (Touch Sensor Action)
char          ipBuf[24]          = "";
bool          ipShowActive       = false;
NpState       ipState            = NP_SHOW_START;
unsigned long ipPauseMs          = 0;
static uint8_t  ipColBuf[128];
static int      ipColCount       = 0;
static int      ipScrollPos      = 0;
static unsigned long ipLastScrollMs = 0;
static int      ipWrapPass       = 0;

bool          provisionMode    = false;

// POWER
//
// sleepActive is the panel being dark while everything else carries on: the
// web server answers, Octoglow Connect still gets through, and anything that
// arrives counts as a reason to light back up. Power Off is a different thing
// entirely and does not live in a flag - it is a deep sleep the device only
// comes out of by being held.
bool          sleepActive      = false;
bool          autoSleepOn      = false;
uint32_t      autoSleepSec     = 1800;
unsigned long lastActivityMs   = 0;
// Long enough that a hand passing the pad is not a power button, short enough
// that nobody thinks the clock is broken.
#define POWER_ON_HOLD_MS 1500

bool          buzzerOn         = true;
uint8_t       buzzerVolume     = 80;
// A level per category. The main volume sets all four at once, the way the
// global transition speed sets every tile; they can then be moved apart.
uint8_t       buzzVolNotif     = 80;
uint8_t       buzzVolAuto      = 80;
uint8_t       buzzVolAlarm     = 80;
uint8_t       buzzVolTouch     = 80;
char          buzzerPreset[16] = "calm";
char          eventSoundTile[16]  = "calm";
char          eventSoundWifi[16]  = "urgent";
char          eventSoundNotif[16] = "soft";
char          eventSoundWeb[16]   = "soft";
char          eventSoundEts2[16]  = "urgent";
char          eventSoundTouch[16] = "soft";
char          accentColor[8]      = "#d0bcff";
#define ETS2_SPEED_LIMIT 130
bool          ets2SpeedAlertFired = false;
bool          wifiWasConnected    = true;
bool          touchActive      = false;
unsigned long touchStartMs     = 0;
bool          touchShortFired  = false;
uint8_t       touchTapAction       = 8; // default: Show IP Address
uint8_t       touchDoubleTapAction = 0;
bool          tapPending           = false;
unsigned long lastTapReleaseMs     = 0;
bool          touchScreenOff       = false;

char displayBuf[32];

// BUZZER OUTPUT
//
// The notes are played by the RMT peripheral, from a task of their own.
//
// Timing: a note has to end on time, and the next one start on time, even when
// loop() is busy. A tile transition, a slow request or a WiFi reconnect holds
// loop() for hundreds of milliseconds; a note that only loop() could stop
// droned on through all of it, and a tune that only loop() could advance fell
// apart. buzzTask() walks the queue on its own clock, and each note is a
// counted run of periods that the hardware stops by itself.
//
// Volume: a passive buzzer has no volume line, and narrowing the pulse to make
// it quieter pushes the sound into its upper harmonics, so everything played
// quietly came out at a different pitch. Instead the note keeps its
// half-and-half square shape and its high half is chopped by a 40 kHz carrier.
// The buzzer cannot follow the carrier, only its average, so the level scales
// with the carrier duty and the pitch stays where it was.
//
// Shape: rewriting that duty while a note sounds lets it die away like a
// struck bell or a plucked string instead of stopping dead, which is most of
// the difference between a chime and a beep.
#include "soc/rmt_struct.h"
#include "soc/gpio_struct.h"
#include "soc/gpio_sig_map.h"

#define BUZZ_TICK_HZ       1000000UL  // symbol durations in microseconds
#define BUZZ_CARRIER_HZ    40000UL
#define BUZZ_CARRIER_TICKS 2000       // one carrier period at the 80 MHz RMT group clock
// Never 0 ticks high or low: the register reads 0 as 65536, and the HAL asserts.
#define BUZZ_TICKS_MIN     4
#define BUZZ_TICKS_MAX     1960

// How a note's level moves while it sounds.
#define BZ_ENV_FLAT   0  // on, then off: the original beeps
#define BZ_ENV_PLUCK  1  // struck and quickly gone
#define BZ_ENV_BELL   2  // struck and left to ring
#define BZ_ENV_SWELL  3  // rises, then fades

static portMUX_TYPE nbMux = portMUX_INITIALIZER_UNLOCKED;
static bool    buzzReady        = false;
static int     buzzCarrierTicks = -2;  // -1 carrier off, -2 not known
static int8_t  buzzRmtCh        = -1;  // the RMT channel on the pin, -1 unknown

static uint8_t buzzCatVolume(uint8_t cat) {
  uint8_t v;
  switch (cat) {
    case BZ_CAT_NOTIF: v = buzzVolNotif; break;
    case BZ_CAT_AUTO:  v = buzzVolAuto;  break;
    case BZ_CAT_ALARM: v = buzzVolAlarm; break;
    case BZ_CAT_TOUCH: v = buzzVolTouch; break;
    default:           v = buzzerVolume; break;
  }
  return v > 100 ? 100 : v;
}

static bool buzzBegin() {
  if (buzzReady) return true;
  if (!rmtInit(BUZZER_PIN, RMT_TX_MODE, RMT_MEM_NUM_BLOCKS_1, BUZZ_TICK_HZ)) return false;
  buzzReady        = true;
  buzzCarrierTicks = -2;
  // The Arduino layer does not say which channel it took; the GPIO matrix does.
  uint32_t sel = GPIO.func_out_sel_cfg[BUZZER_PIN].func_sel;
  buzzRmtCh = (sel >= RMT_SIG_OUT0_IDX && sel <= RMT_SIG_OUT3_IDX) ? (int8_t)(sel - RMT_SIG_OUT0_IDX) : -1;
  return true;
}

// Between notes, through the driver. It reaches the pin when the next note starts.
static void buzzCarrierSet(int ticks) {
  if (ticks == buzzCarrierTicks) return;
  bool ok;
  if (ticks < 0) {
    ok = rmtSetCarrier(BUZZER_PIN, false, false, 0, 0.5f);
  } else {
    // The third argument is polarity_active_low: false puts the carrier on the
    // high half of the note. The half tick keeps the driver's truncation from
    // landing one below.
    ok = rmtSetCarrier(BUZZER_PIN, true, false, BUZZ_CARRIER_HZ, (ticks + 0.5f) / BUZZ_CARRIER_TICKS);
  }
  buzzCarrierTicks = ok ? ticks : -2;
}

// While a note sounds. The driver has no call for this, so the duty goes
// straight into the register, followed by the channel's synchronisation bit
// that carries configuration across to the RMT clock domain - the same bit the
// driver sets to start or stop a transmission. The critical section keeps the
// RMT interrupt, which lives on this core, off CONF0 while it is written.
static void buzzCarrierLive(int ticks) {
  if (ticks == buzzCarrierTicks) return;
  if (buzzRmtCh < 0 || buzzCarrierTicks < 0) { buzzCarrierSet(ticks); return; }
  portENTER_CRITICAL(&nbMux);
  RMT.chncarrier_duty[buzzRmtCh].val = ((uint32_t)ticks << 16) | (uint32_t)(BUZZ_CARRIER_TICKS - ticks);
  RMT.chnconf0[buzzRmtCh].conf_update_chn = 1;
  portEXIT_CRITICAL(&nbMux);
  buzzCarrierTicks = ticks;
}

static int buzzTicksFor(float amp) {
  int t = (int)(amp * BUZZ_CARRIER_TICKS + 0.5f);
  if (t < BUZZ_TICKS_MIN) t = BUZZ_TICKS_MIN;
  if (t > BUZZ_TICKS_MAX) t = BUZZ_TICKS_MAX;
  return t;
}

// The level t ms into a note dur ms long, 1 being the note's full volume.
static float buzzEnvelope(uint8_t env, uint32_t t, uint16_t dur) {
  float x = dur ? (float)t / dur : 1.0f;
  if (x > 1.0f) x = 1.0f;
  switch (env) {
    case BZ_ENV_PLUCK: return expf(-3.5f * x);  // ends near 3 %
    case BZ_ENV_BELL:  return expf(-1.8f * x);  // still ringing at 16 %
    case BZ_ENV_SWELL: return x < 0.35f ? 0.4f + 0.6f * x / 0.35f : 1.0f - 0.85f * (x - 0.35f) / 0.65f;
    default:           return 1.0f;
  }
}

// An empty looping write cuts off whatever is sounding; the pin drops to its
// idle level, low.
static void buzzSilence() {
  if (buzzReady) rmtWriteLooping(BUZZER_PIN, NULL, 0);
}

// Starts a note. Returns its full-volume carrier amplitude, or a negative
// number when nothing is sounding: a rest, or a category turned down to 0.
static float buzzStartNote(uint16_t freq, uint16_t durMs, uint8_t env, uint8_t cat) {
  uint8_t vol = buzzCatVolume(cat);
  if (freq == 0 || durMs == 0 || vol == 0) { buzzSilence(); return -1.0f; }
  if (!buzzBegin()) return -1.0f;
  // Loudness falls away far more slowly than the number does, so the setting
  // is squared: 50 drives the buzzer at a quarter, about -12 dB.
  float peak = (vol / 100.0f) * (vol / 100.0f);
  // A flat note at full volume needs no carrier: that is the plain square wave
  // tone() used to play. A shaped note keeps it on so its level can move.
  if (env == BZ_ENV_FLAT && vol >= 100) buzzCarrierSet(-1);
  else                                  buzzCarrierSet(buzzTicksFor(peak * buzzEnvelope(env, 0, durMs)));
  if (freq < 20)    freq = 20;     // half a period has to fit in 15 bits of µs
  if (freq > 20000) freq = 20000;
  uint32_t period = (BUZZ_TICK_HZ + freq / 2) / freq;
  static rmt_data_t sym;
  sym.level0 = 1; sym.duration0 = period / 2;
  sym.level1 = 0; sym.duration1 = period - period / 2;
  uint32_t cycles = ((uint32_t)durMs * 1000UL + period / 2) / period;
  // One cycle would go out as a one-shot write, and the next note could not
  // cut that short. Starting a repeated write cancels the one before it.
  if (cycles < 2) cycles = 2;
  rmtWriteRepeated(BUZZER_PIN, &sym, 1, cycles);
  return peak;
}

// BUZZER QUEUE
//
// loop() and the web handlers queue notes, buzzTask() plays them, and nbMux
// covers everything the two share. Each note carries the category whose volume
// it plays at; nbEnqCat is the one being queued, and only loop() touches it.
#define NB_QUEUE_MAX 64
struct NbNote { uint16_t freq; uint16_t dur; uint8_t env; uint8_t cat; };
static NbNote  nbQueue[NB_QUEUE_MAX];
static uint8_t nbHead     = 0;
static uint8_t nbTail     = 0;
static bool    nbStopReq  = false;
static bool    nbNoteOn   = false;  // a note, or the gap after it, is under way
static bool    nbShutReq  = false;
static volatile bool nbShutDone = false;
static uint8_t nbEnqCat   = BZ_CAT_NONE;
static TaskHandle_t buzzTaskH = NULL;

static void buzzWake() {
  if (buzzTaskH) xTaskNotifyGive(buzzTaskH);
}

static void nbEnqueueEnv(uint16_t freq, uint16_t dur, uint8_t env) {
  portENTER_CRITICAL(&nbMux);
  uint8_t next = (nbTail + 1) % NB_QUEUE_MAX;
  if (next != nbHead) {
    nbQueue[nbTail] = { freq, dur, env, nbEnqCat };
    nbTail = next;
  }
  portEXIT_CRITICAL(&nbMux);
  buzzWake();
}

static void nbEnqueue(uint16_t freq, uint16_t dur) {
  nbEnqueueEnv(freq, dur, BZ_ENV_FLAT);
}

// Empties the queue and cuts off whatever is sounding.
static void nbStop() {
  portENTER_CRITICAL(&nbMux);
  nbHead = nbTail = 0;
  nbStopReq = true;
  portEXIT_CRITICAL(&nbMux);
  buzzWake();
}

// Anything queued, sounding, or about to be cut off.
static bool nbBusy() {
  portENTER_CRITICAL(&nbMux);
  bool busy = nbHead != nbTail || nbNoteOn || nbStopReq;
  portEXIT_CRITICAL(&nbMux);
  return busy;
}

// A note holds the queue for its length plus a 5 ms gap. A shaped note is
// revisited every 3 ms to move its level; otherwise the task sleeps until the
// note is over or something new is queued.
static void buzzTask(void*) {
  unsigned long start = 0;
  uint16_t dur = 0, span = 0;
  uint8_t  env = BZ_ENV_FLAT;
  float    peak = -1.0f;
  for (;;) {
    unsigned long now = millis();
    NbNote n = { 0, 0, 0, 0 };
    bool shut, stop = false, have = false;
    portENTER_CRITICAL(&nbMux);
    shut = nbShutReq;
    if (nbStopReq) { nbStopReq = false; nbNoteOn = false; stop = true; }
    if (nbNoteOn && now - start >= span) nbNoteOn = false;
    if (!shut && !nbNoteOn && nbHead != nbTail) {
      n = nbQueue[nbHead];
      nbHead = (nbHead + 1) % NB_QUEUE_MAX;
      nbNoteOn = have = true;
    }
    bool idle = !nbNoteOn;
    portEXIT_CRITICAL(&nbMux);

    if (shut) {
      buzzSilence();
      if (buzzReady) { rmtDeinit(BUZZER_PIN); buzzReady = false; }
      nbShutDone = true;
      vTaskSuspend(NULL);
    }
    if (have) {
      start = now; dur = n.dur; span = n.dur + 5; env = n.env;
      peak = buzzStartNote(n.freq, n.dur, n.env, n.cat);
    } else if (stop) {
      peak = -1.0f;
      buzzSilence();
    }
    bool shaping = peak >= 0 && env != BZ_ENV_FLAT && now - start < dur;
    if (shaping) buzzCarrierLive(buzzTicksFor(peak * buzzEnvelope(env, now - start, dur)));

    TickType_t wait = portMAX_DELAY;
    if (shaping) {
      wait = pdMS_TO_TICKS(3);
    } else if (!idle) {
      unsigned long el = millis() - start;
      wait = el < span ? pdMS_TO_TICKS(span - el) : 1;
      if (wait == 0) wait = 1;
    }
    ulTaskNotifyTake(pdTRUE, wait);
  }
}

// Silent and released: for boot and for sleep, where the pin must sit low.
static void buzzOff() {
  if (buzzTaskH) {
    portENTER_CRITICAL(&nbMux);
    nbHead = nbTail = 0;
    nbShutReq = true;
    portEXIT_CRITICAL(&nbMux);
    buzzWake();
    for (int i = 0; i < 50 && !nbShutDone; i++) delay(2);
  }
  pinMode(BUZZER_PIN, OUTPUT);
  digitalWrite(BUZZER_PIN, LOW);
}

// Above loop() in priority and on loop()'s core: a busy pass cannot hold a
// note back, and the RMT interrupt the task installs lands on the core that
// buzzCarrierLive() shuts it out of.
static void buzzStartTask() {
  if (buzzTaskH) return;
  xTaskCreatePinnedToCore(buzzTask, "buzzer", 5120, NULL, 2, &buzzTaskH, xPortGetCoreID());
}

// cat is whose volume the preset plays at: BZ_CAT_NOTIF, BZ_CAT_AUTO or
// BZ_CAT_TOUCH.
static void nbPlayPreset(const char* id, uint8_t cat) {
  if (!buzzerOn) return;
  if (strcmp(id, "none") == 0) return;
  nbStop();
  nbEnqCat = cat;
  if (strcmp(id, "calm") == 0) {
    nbEnqueue(1000, 80);
  } else if (strcmp(id, "loud") == 0) {
    nbEnqueue(1500, 130);
  } else if (strcmp(id, "urgent") == 0) {
    for (int i = 0; i < 3; i++) { nbEnqueue(1800, 60); nbEnqueue(0, 15); }
  } else if (strcmp(id, "soft") == 0) {
    nbEnqueue(650, 60);
  } else if (strcmp(id, "double") == 0) {
    nbEnqueue(1200, 70); nbEnqueue(0, 20); nbEnqueue(1200, 70);
  } else if (strcmp(id, "triple") == 0) {
    for (int i = 0; i < 3; i++) { nbEnqueue(1200, 60); nbEnqueue(0, 20); }
  } else if (strcmp(id, "chime") == 0) {
    nbEnqueue(523, 90); nbEnqueue(0, 5); nbEnqueue(659, 90); nbEnqueue(0, 5); nbEnqueue(784, 130);
  } else if (strcmp(id, "bell") == 0) {
    nbEnqueue(1568, 60); nbEnqueue(0, 10); nbEnqueue(1318, 70); nbEnqueue(0, 10); nbEnqueue(1046, 160);
  } else if (strcmp(id, "doorbell") == 0) {
    nbEnqueue(784, 160); nbEnqueue(0, 10); nbEnqueue(659, 220);
  } else if (strcmp(id, "xylophone") == 0) {
    int notes[5] = {659, 784, 880, 988, 1175};
    for (int i = 0; i < 5; i++) { nbEnqueue(notes[i], 55); nbEnqueue(0, 5); }
  } else if (strcmp(id, "harp") == 0) {
    for (int f = 1400; f >= 500; f -= 90) { nbEnqueue(f, 22); nbEnqueue(0, 5); }
  } else if (strcmp(id, "marimba") == 0) {
    nbEnqueue(440, 110); nbEnqueue(0, 5); nbEnqueue(220, 160);
  } else if (strcmp(id, "crystal") == 0) {
    nbEnqueue(1568, 45); nbEnqueue(0, 8); nbEnqueue(1976, 45); nbEnqueue(0, 8); nbEnqueue(2349, 90);
  } else if (strcmp(id, "wave") == 0) {
    for (int f = 400; f <= 1000; f += 60) { nbEnqueue(f, 15); }
    for (int f = 1000; f >= 400; f -= 60) { nbEnqueue(f, 15); }
  } else if (strcmp(id, "lullaby") == 0) {
    int notes[4] = {784, 659, 587, 523};
    for (int i = 0; i < 4; i++) { nbEnqueue(notes[i], 140); nbEnqueue(0, 10); }
  } else if (strcmp(id, "pingpong") == 0) {
    for (int i = 0; i < 4; i++) { nbEnqueue(i % 2 == 0 ? 1200 : 800, 55); nbEnqueue(0, 10); }
  } else if (strcmp(id, "sos") == 0) {
    for (int i = 0; i < 3; i++) { nbEnqueue(1500, 60); nbEnqueue(0, 60); }
    nbEnqueue(0, 80);
    for (int i = 0; i < 3; i++) { nbEnqueue(1500, 180); nbEnqueue(0, 60); }
    nbEnqueue(0, 80);
    for (int i = 0; i < 3; i++) { nbEnqueue(1500, 60); nbEnqueue(0, 60); }
  } else if (strcmp(id, "siren") == 0) {
    for (int f = 400; f <= 1800; f += 70) nbEnqueue(f, 18);
    for (int f = 1800; f >= 400; f -= 70) nbEnqueue(f, 18);
  } else if (strcmp(id, "klaxon") == 0) {
    for (int i = 0; i < 6; i++) { nbEnqueue(i % 2 == 0 ? 1000 : 1400, 45); nbEnqueue(0, 10); }
  } else if (strcmp(id, "laser") == 0) {
    for (int f = 2500; f >= 300; f -= 100) nbEnqueue(f, 10);
  } else if (strcmp(id, "robot") == 0) {
    int notes[5] = {900, 1300, 700, 1600, 1000};
    for (int i = 0; i < 5; i++) { nbEnqueue(notes[i], 40); nbEnqueue(0, 15); }
  } else if (strcmp(id, "fanfare") == 0) {
    nbEnqueue(523, 90);  nbEnqueue(0, 10);
    nbEnqueue(659, 90);  nbEnqueue(0, 10);
    nbEnqueue(784, 90);  nbEnqueue(0, 10);
    nbEnqueue(1046, 220);
  } else if (strcmp(id, "powerdown") == 0) {
    for (int f = 1200; f >= 200; f -= 50) nbEnqueue(f, 14);
  } else if (strcmp(id, "powerup") == 0) {
    for (int f = 200; f <= 1200; f += 50) nbEnqueue(f, 14);
  } else if (strcmp(id, "heartbeat") == 0) {
    nbEnqueue(150, 80); nbEnqueue(0, 40); nbEnqueue(150, 60); nbEnqueue(0, 220);
    nbEnqueue(150, 80); nbEnqueue(0, 40); nbEnqueue(150, 60);
  } else if (strcmp(id, "scifi") == 0) {
    for (int i = 0; i < 2; i++) {
      for (int f = 600; f <= 1600; f += 80) nbEnqueue(f, 12);
      for (int f = 1600; f >= 600; f -= 80) nbEnqueue(f, 12);
    }
  } else if (strcmp(id, "arcade") == 0) {
    int notes[4] = {800, 1000, 1200, 1600};
    for (int i = 0; i < 4; i++) { nbEnqueue(notes[i], 45); nbEnqueue(0, 8); }
  } else if (strcmp(id, "zen") == 0) {
    nbEnqueue(220, 400);
  } else if (strcmp(id, "bubble") == 0) {
    int notes[5] = {600, 900, 1200, 1500, 1800};
    for (int i = 0; i < 5; i++) { nbEnqueue(notes[i], 30); nbEnqueue(0, 10); }
  } else if (strcmp(id, "whistle") == 0) {
    for (int f = 800; f <= 2000; f += 40) nbEnqueue(f, 12);
  // The shaped ones. Each note is struck and left to fade (PLUCK, BELL) or
  // swells and falls away (SWELL), and they keep to the pentatonic and major
  // intervals, in the octave the buzzer sings best in.
  } else if (strcmp(id, "glass") == 0) {
    nbEnqueueEnv(2637, 120, BZ_ENV_BELL); nbEnqueueEnv(1976, 650, BZ_ENV_BELL);
  } else if (strcmp(id, "musicbox") == 0) {
    nbEnqueueEnv(1047, 110, BZ_ENV_PLUCK); nbEnqueueEnv(1319, 110, BZ_ENV_PLUCK);
    nbEnqueueEnv(1568, 110, BZ_ENV_PLUCK); nbEnqueueEnv(2093, 480, BZ_ENV_BELL);
  } else if (strcmp(id, "kalimba") == 0) {
    nbEnqueueEnv(1568, 150, BZ_ENV_PLUCK); nbEnqueueEnv(1319, 150, BZ_ENV_PLUCK);
    nbEnqueueEnv(1047, 380, BZ_ENV_PLUCK);
  } else if (strcmp(id, "dingdong") == 0) {
    nbEnqueueEnv(1319, 420, BZ_ENV_BELL); nbEnqueueEnv(1047, 750, BZ_ENV_BELL);
  } else if (strcmp(id, "droplet") == 0) {
    nbEnqueueEnv(1568, 50, BZ_ENV_PLUCK); nbEnqueueEnv(2349, 300, BZ_ENV_PLUCK);
  } else if (strcmp(id, "success") == 0) {
    nbEnqueueEnv(784, 90, BZ_ENV_PLUCK);   nbEnqueueEnv(1047, 90, BZ_ENV_PLUCK);
    nbEnqueueEnv(1319, 90, BZ_ENV_PLUCK);  nbEnqueueEnv(1568, 450, BZ_ENV_BELL);
  } else if (strcmp(id, "softping") == 0) {
    nbEnqueueEnv(1760, 800, BZ_ENV_BELL);
  } else if (strcmp(id, "aurora") == 0) {
    nbEnqueueEnv(1047, 280, BZ_ENV_SWELL); nbEnqueueEnv(1568, 560, BZ_ENV_BELL);
  } else if (strcmp(id, "harmony") == 0) {
    nbEnqueueEnv(880, 80, BZ_ENV_PLUCK);   nbEnqueueEnv(1109, 80, BZ_ENV_PLUCK);
    nbEnqueueEnv(1319, 80, BZ_ENV_PLUCK);  nbEnqueueEnv(1760, 550, BZ_ENV_BELL);
  } else if (strcmp(id, "twinkle") == 0) {
    nbEnqueueEnv(2637, 70, BZ_ENV_PLUCK);  nbEnqueueEnv(2093, 70, BZ_ENV_PLUCK);
    nbEnqueueEnv(1568, 70, BZ_ENV_PLUCK);  nbEnqueueEnv(2637, 380, BZ_ENV_BELL);
  } else if (strcmp(id, "boldalert") == 0) {
    for (int i = 0; i < 4; i++) { nbEnqueue(300, 70); nbEnqueue(0, 10); nbEnqueue(1200, 70); nbEnqueue(0, 10); }
  } else {
    nbEnqueue(1000, 80);
  }
}

// ALARM TONES
//
// The presets above are single notifications - a note or two and done. An alarm
// has to carry on until somebody stops it, so each tone here is a phrase that
// alarmToneTick() re-queues for as long as the alarm rings. They go through the
// same nbEnqueue()/buzzTask() pair as the presets; only the repeat is new.
// NB_QUEUE_MAX leaves room for 63 entries, rests included, and no phrase below
// goes past 30.
static void alarmEnqueueTone(const char* id) {
  nbEnqCat = BZ_CAT_ALARM;
  if (strcmp(id, "chimes") == 0) {
    // A four-note chime figure, twice over.
    static const uint16_t n[4] = { 659, 784, 587, 392 };
    for (int r = 0; r < 2; r++)
      for (int i = 0; i < 4; i++) { nbEnqueue(n[i], 260); nbEnqueue(0, 40); }
  } else if (strcmp(id, "morning") == 0) {
    static const uint16_t n[6] = { 523, 659, 784, 659, 784, 1046 };
    for (int r = 0; r < 2; r++)
      for (int i = 0; i < 6; i++) { nbEnqueue(n[i], 190); nbEnqueue(0, 30); }
  } else if (strcmp(id, "radar") == 0) {
    for (int r = 0; r < 4; r++) {
      nbEnqueue(880, 90);   nbEnqueue(0, 30);
      nbEnqueue(1046, 90);  nbEnqueue(0, 30);
      nbEnqueue(1318, 150); nbEnqueue(0, 200);
    }
  } else if (strcmp(id, "insistent") == 0) {
    for (int r = 0; r < 6; r++) { nbEnqueue(2000, 90); nbEnqueue(0, 60); nbEnqueue(1600, 90); nbEnqueue(0, 160); }
  } else if (strcmp(id, "siren") == 0) {
    for (int f = 500; f <= 1500; f += 125) nbEnqueue(f, 60);
    for (int f = 1500; f >= 500; f -= 125) nbEnqueue(f, 60);
  } else if (strcmp(id, "melody") == 0) {
    static const uint16_t n[8] = { 523, 587, 659, 523, 523, 587, 659, 523 };
    for (int i = 0; i < 8; i++) { nbEnqueue(n[i], 220); nbEnqueue(0, 30); }
  } else if (strcmp(id, "sunrise") == 0) {
    // A slow climb up the pentatonic scale, every step left to ring.
    static const uint16_t n[6] = { 1047, 1175, 1319, 1568, 1760, 2093 };
    for (int i = 0; i < 6; i++) nbEnqueueEnv(n[i], i == 5 ? 1100 : 420, BZ_ENV_BELL);
  } else if (strcmp(id, "musicbox") == 0) {
    static const uint16_t n[16] = { 1319, 1568, 2093, 1568, 1760, 1568, 1319, 1047,
                                    1175, 1319, 1568, 1319, 1175, 1047, 1175, 1047 };
    for (int i = 0; i < 15; i++) nbEnqueueEnv(n[i], 230, BZ_ENV_PLUCK);
    nbEnqueueEnv(n[15], 800, BZ_ENV_BELL);
  } else if (strcmp(id, "arpeggio") == 0) {
    static const uint16_t n[8] = { 1047, 1319, 1568, 2093, 2637, 2093, 1568, 1319 };
    for (int r = 0; r < 2; r++)
      for (int i = 0; i < 8; i++) nbEnqueueEnv(n[i], 140, BZ_ENV_PLUCK);
    nbEnqueueEnv(1047, 700, BZ_ENV_BELL);
  } else if (strcmp(id, "birds") == 0) {
    // Short plucked chirps with room between them - a trill, a call, a flutter.
    static const uint16_t a[3] = { 2093, 2349, 2637 };
    static const uint16_t b[4] = { 1976, 2349, 2794, 2349 };
    for (int r = 0; r < 2; r++) {
      for (int i = 0; i < 3; i++) nbEnqueueEnv(a[i], 20, BZ_ENV_PLUCK);
      nbEnqueue(0, 140);
      nbEnqueueEnv(2637, 24, BZ_ENV_PLUCK); nbEnqueueEnv(2349, 40, BZ_ENV_PLUCK);
      nbEnqueue(0, 320);
      for (int i = 0; i < 4; i++) nbEnqueueEnv(b[i], 18, BZ_ENV_PLUCK);
      nbEnqueue(0, 480);
    }
  } else if (strcmp(id, "zen") == 0) {
    nbEnqueueEnv(880, 1000, BZ_ENV_SWELL);  nbEnqueue(0, 120);
    nbEnqueueEnv(1319, 1000, BZ_ENV_SWELL); nbEnqueue(0, 120);
    nbEnqueueEnv(1175, 1000, BZ_ENV_SWELL); nbEnqueue(0, 120);
    nbEnqueueEnv(880, 1400, BZ_ENV_SWELL);
  } else if (strcmp(id, "windchime") == 0) {
    nbEnqueueEnv(2637, 420, BZ_ENV_BELL); nbEnqueueEnv(2093, 320, BZ_ENV_BELL); nbEnqueue(0, 120);
    nbEnqueueEnv(2349, 520, BZ_ENV_BELL); nbEnqueueEnv(1760, 360, BZ_ENV_BELL); nbEnqueue(0, 200);
    nbEnqueueEnv(1568, 620, BZ_ENV_BELL); nbEnqueueEnv(2637, 260, BZ_ENV_BELL);
    nbEnqueueEnv(2093, 800, BZ_ENV_BELL);
  } else if (strcmp(id, "gentle") == 0) {
    // Three soft four-note figures, each a step higher, then one bell.
    static const uint16_t n[3][4] = { { 1047, 1319, 1568, 1319 },
                                      { 1175, 1397, 1760, 1397 },
                                      { 1319, 1568, 2093, 1568 } };
    for (int p = 0; p < 3; p++) {
      for (int i = 0; i < 4; i++) nbEnqueueEnv(n[p][i], 260, BZ_ENV_PLUCK);
      nbEnqueue(0, 200);
    }
    nbEnqueueEnv(2093, 900, BZ_ENV_BELL);
  } else {
    // "classic" and anything unrecognised: the two-tone alarm everybody knows.
    for (int r = 0; r < 7; r++) { nbEnqueue(1200, 130); nbEnqueue(0, 60); nbEnqueue(900, 130); nbEnqueue(0, 60); }
  }
}

// Minutes from now until the next alarm that is switched on, or -1 if there is
// none. It walks forward one day at a time instead of reasoning about the
// calendar, so midnight, the end of the week and "already gone for today" are
// all the same case. Eight steps rather than seven: an alarm set for one day a
// week is exactly a week away when today is that day and the time has passed.
static int alarmMinutesUntilNext(int* outIdx) {
  if (outIdx) *outIdx = -1;
  struct tm ti;
  if (!readLocalTime(ti)) return -1;
  int nowMin = ti.tm_hour * 60 + ti.tm_min;
  int best = -1, bestIdx = -1;
  for (int i = 0; i < ALARM_COUNT; i++) {
    if (!alarms[i].present || !alarms[i].enabled || alarms[i].days == 0) continue;
    int at = alarms[i].hour * 60 + alarms[i].minute;
    for (int d = 0; d <= 7; d++) {
      if (!(alarms[i].days & ALARM_WDAY_BIT((ti.tm_wday + d) % 7))) continue;
      int delta = d * 1440 + at - nowMin;
      if (delta < 0) continue;
      // The minute it is ringing in, or has already rung in, is behind us.
      if (delta == 0 && alarmFired[i]) continue;
      if (best < 0 || delta < best) { best = delta; bestIdx = i; }
      break;
    }
  }
  if (outIdx) *outIdx = bestIdx;
  return best;
}

static void alarmStartRinging(int idx) {
  // An alarm nobody can see is half an alarm.
  sleepWake();
  alarmRinging     = true;
  alarmRingingIdx  = (int8_t)idx;
  alarmRingStartMs = millis();
  alarmToneNextMs  = 0;
  if (buzzerOn) {
    nbStop();
    alarmEnqueueTone(alarms[idx].tone);
  }
}

void alarmStopRinging() {
  if (!alarmRinging) return;
  alarmRinging    = false;
  alarmRingingIdx = -1;
  alarmToneNextMs = 0;
  nbStop();
}

// Feeds the buzzer another pass of the tone once the last one has drained. The
// gap is measured from the moment the queue empties, not from when it started,
// so a long tone and a short one both get the same pause between repeats.
static void alarmToneTick() {
  if (!alarmRinging || !buzzerOn) return;
  if (nbBusy()) { alarmToneNextMs = 0; return; }
  unsigned long now = millis();
  if (alarmToneNextMs == 0) { alarmToneNextMs = now + ALARM_TONE_GAP_MS; return; }
  if ((long)(now - alarmToneNextMs) < 0) return;
  alarmToneNextMs = 0;
  if (alarmRingingIdx >= 0) alarmEnqueueTone(alarms[alarmRingingIdx].tone);
}

// Called every pass through loop(). Everything it decides is a function of the
// wall clock, so a reboot changes nothing: the alarms come back from NVS and the
// first tick after the clock is set picks up where it left off.
static void alarmTick() {
  // Taken out of the Tile Manager means switched off, the same as it does for
  // every other priority tile - and a tile that is gone cannot be silenced.
  if (prioHiddenMask & (1u << PRIORITY_ID_ALARM)) {
    if (alarmRinging) alarmStopRinging();
    return;
  }
  if (alarmRinging && (millis() - alarmRingStartMs >= ALARM_MAX_RING_MS)) alarmStopRinging();

  // Four times a second is ample for something that acts on whole minutes, and
  // it keeps even the cheap clock read off the critical path.
  static unsigned long lastCheckMs = 0;
  unsigned long nowMs = millis();
  if (nowMs - lastCheckMs < 250) return;
  lastCheckMs = nowMs;

  struct tm ti;
  if (!readLocalTime(ti)) return;

  uint8_t todayBit = ALARM_WDAY_BIT(ti.tm_wday);
  for (int i = 0; i < ALARM_COUNT; i++) {
    bool match = alarms[i].present &&
                 alarms[i].enabled &&
                 (alarms[i].days & todayBit) &&
                 ti.tm_hour == alarms[i].hour &&
                 ti.tm_min  == alarms[i].minute;
    if (!match)        { alarmFired[i] = false; continue; }
    if (alarmFired[i]) continue;
    // Two alarms set to the same minute both latch here, but only the first in
    // index order starts ringing: the second would otherwise queue up behind it
    // and ask to be stopped a second time for the same minute.
    alarmFired[i] = true;
    if (!alarmRinging) alarmStartRinging(i);
  }
}

// BUZZER BLOCKING (for non-touch events)

void beepSwitch() {
  if (!buzzerOn) return;
  nbPlayPreset(eventSoundTile, BZ_CAT_AUTO);
}

void beepTouch() {
  if (!buzzerOn) return;
  nbPlayPreset(eventSoundTouch, BZ_CAT_TOUCH);
}

// CUSTOM FONT
const uint8_t FONT[][5] = {
  {0x3E, 0x51, 0x49, 0x45, 0x3E},
  {0x00, 0x42, 0x7F, 0x40, 0x00},
  {0x42, 0x61, 0x51, 0x49, 0x46},
  {0x21, 0x41, 0x45, 0x4B, 0x31},
  {0x18, 0x14, 0x12, 0x7F, 0x10},
  {0x27, 0x45, 0x45, 0x45, 0x39},
  {0x3C, 0x4A, 0x49, 0x49, 0x30},
  {0x01, 0x71, 0x09, 0x05, 0x03},
  {0x36, 0x49, 0x49, 0x49, 0x36},
  {0x06, 0x49, 0x49, 0x29, 0x1E},
};
const uint8_t CHAR_C[5]     = {0x3E, 0x41, 0x41, 0x41, 0x22};
const uint8_t CHAR_F[5]     = {0x7F, 0x09, 0x09, 0x01, 0x01};
const uint8_t CHAR_DEG[5]   = {0x06, 0x09, 0x09, 0x06, 0x00};
const uint8_t CHAR_SPACE[5] = {0x00, 0x00, 0x00, 0x00, 0x00};
const uint8_t CHAR_COLON[2] = {0x36, 0x36};
const uint8_t CHAR_DOT[1]   = {0x40};
// Font mode constants (moved up from below so they're visible to appendGlyphAuto)
#define FONT_MODE_DEFAULT 0
#define FONT_MODE_TIKO    1
#define FONT_MODE_MAKO    2
#define FONT_MODE_SEG     3   // seven-segment look, the gaps between segments left dark
#define FONT_MODE_SEGFILL 4   // the same with the corners lit, so the strokes join up
#define FONT_MODE_BOLD    5   // two-column uprights, the heavy one
#define FONT_MODE_ITALIC  6   // Mako leaned one column to the right
#define FONT_MODE_HOP     7   // "Topait": capitals bobbing up and down
// 8 ("Umbra") and 9 ("Terminal") were removed in Sept 2026. Their ids stay
// unused so Bloc keeps 10 and saved settings keep meaning the same font; a
// tile still set to one of them is moved to Mako when the settings load.
#define FONT_MODE_SHADOW  8
#define FONT_MODE_TERMINAL 9
#define FONT_MODE_BLOCK   10 // Mako with doubled vertical strokes
#define FONT_MODE_COUNT   11
// The widest glyph any of our fonts draws (Bold's M and W); every buffer that
// takes one glyph's columns is this big.
#define TIKO_MAX_COLS 7

// Every font but Marymba is drawn from tables of our own rather than the
// library font, so every place that asked "is it Tiko?" really means "is it
// one of ours?" - and then has to ask the right table.
uint8_t tikoGetChar(char ch, uint8_t size, uint8_t* buf);
uint8_t makoGetChar(char ch, uint8_t size, uint8_t* buf);
uint8_t segGetChar(char ch, uint8_t size, uint8_t* buf, bool filled);
uint8_t boldGetChar(char ch, uint8_t size, uint8_t* buf);
uint8_t italicGetChar(char ch, uint8_t size, uint8_t* buf);
uint8_t hopGetChar(char ch, uint8_t size, uint8_t* buf);
uint8_t blockGetChar(char ch, uint8_t size, uint8_t* buf);
// A font id the interface may set: one this build draws, not a retired one.
static inline bool fontModeOk(int m) {
  return m >= 0 && m < FONT_MODE_COUNT && m != FONT_MODE_SHADOW && m != FONT_MODE_TERMINAL;
}
static inline bool isPixelFont(uint8_t m) {
  return m != FONT_MODE_DEFAULT && m < FONT_MODE_COUNT;
}
static inline uint8_t pixGetChar(uint8_t m, char ch, uint8_t size, uint8_t* buf) {
  switch (m) {
    case FONT_MODE_MAKO:    return makoGetChar(ch, size, buf);
    case FONT_MODE_SEG:     return segGetChar(ch, size, buf, false);
    case FONT_MODE_SEGFILL: return segGetChar(ch, size, buf, true);
    case FONT_MODE_BOLD:    return boldGetChar(ch, size, buf);
    case FONT_MODE_ITALIC:  return italicGetChar(ch, size, buf);
    case FONT_MODE_HOP:     return hopGetChar(ch, size, buf);
    case FONT_MODE_BLOCK:   return blockGetChar(ch, size, buf);
    default:                return tikoGetChar(ch, size, buf);
  }
}

static void appendGlyphAuto(uint8_t* buf, int& count, int maxCount, char ch, uint8_t fontMode = FONT_MODE_DEFAULT) {
  if (isPixelFont(fontMode)) {
    uint8_t tmp[TIKO_MAX_COLS];
    uint8_t n = pixGetChar(fontMode, ch, sizeof(tmp), tmp);
    if (n == 0) return;
    for (int i = 0; i < n && count < maxCount; i++) buf[count++] = tmp[i];
    if (count < maxCount) buf[count++] = 0x00;
    return;
  }
  if (ch >= '0' && ch <= '9') {
    const uint8_t* g = FONT[ch - '0'];
    for (int i = 0; i < 5 && count < maxCount; i++) buf[count++] = g[i];
    if (count < maxCount) buf[count++] = 0x00;
  } else if (ch == ':') {
    for (int i = 0; i < 2 && count < maxCount; i++) buf[count++] = CHAR_COLON[i];
    if (count < maxCount) buf[count++] = 0x00;
  } else if (ch == '.') {
    if (count < maxCount) buf[count++] = CHAR_DOT[0];
    if (count < maxCount) buf[count++] = 0x00;
  } else {
    uint8_t tmp[8];
    uint8_t n = mx.getChar(ch, sizeof(tmp), tmp);
    if (n == 0) return;
    for (int i = 0; i < n && count < maxCount; i++) buf[count++] = tmp[i];
    if (count < maxCount) buf[count++] = 0x00;
  }
}


// ============================================================
// TIKO FONT (3x7 custom pixel font) — alternative to the
// library-default "Marymba" font used across all tiles.
// Each glyph is stored as N columns (N=3 for almost every
// character, N=5 only for 'W'); each column byte packs 7 rows,
// bit r (r=0..6, r=0 is the TOP row) -> same column-encoding
// convention as the existing digit FONT[][5] table above, so
// it can be dropped in anywhere getChar()-style output is used.
// ============================================================
struct TikoGlyph {
  char    ch;
  uint8_t cols;
  uint8_t data[TIKO_MAX_COLS];
};

const TikoGlyph TIKO_FONT[] = {
  { '1', 3, { 0x42, 0x7F, 0x40 } },
  { '0', 3, { 0x3E, 0x41, 0x3E } },
  { '2', 3, { 0x62, 0x51, 0x4E } },
  { '3', 3, { 0x41, 0x49, 0x3E } },
  { '4', 3, { 0x0F, 0x08, 0x7F } },
  { '5', 3, { 0x4F, 0x49, 0x31 } },
  { '6', 3, { 0x3E, 0x49, 0x30 } },
  { '7', 3, { 0x01, 0x71, 0x0F } },
  { '8', 3, { 0x3E, 0x49, 0x3E } },
  { '9', 3, { 0x06, 0x49, 0x3E } },
  { 'A', 3, { 0x7E, 0x09, 0x7E } },
  { 'B', 3, { 0x7F, 0x49, 0x3E } },
  { 'C', 3, { 0x3E, 0x41, 0x41 } },
  { 'D', 3, { 0x7F, 0x41, 0x3E } },
  { 'E', 3, { 0x7F, 0x49, 0x41 } },
  { 'F', 3, { 0x7F, 0x09, 0x01 } },
  { 'H', 3, { 0x7F, 0x08, 0x7F } },
  { 'I', 3, { 0x00, 0x7F, 0x00 } },
  { 'J', 3, { 0x40, 0x3F, 0x00 } },
  { 'K', 3, { 0x7F, 0x10, 0x6C } },
  { 'L', 3, { 0x3F, 0x40, 0x40 } },
  { 'G', 3, { 0x3E, 0x41, 0x70 } },
  { 'M', 3, { 0x7F, 0x02, 0x7F } },
  { 'N', 3, { 0x7F, 0x01, 0x7E } },
  { 'O', 3, { 0x3E, 0x41, 0x3E } },
  { 'P', 3, { 0x7F, 0x09, 0x06 } },
  { 'Q', 3, { 0x1E, 0x21, 0x5E } },
  { 'R', 3, { 0x7F, 0x09, 0x76 } },
  { 'S', 3, { 0x4E, 0x49, 0x31 } },
  { 'T', 3, { 0x01, 0x7F, 0x01 } },
  { 'U', 3, { 0x3F, 0x40, 0x3F } },
  { 'V', 3, { 0x3F, 0x60, 0x3F } },
  { 'W', 5, { 0x3F, 0x40, 0x38, 0x40, 0x3F } },
  { 'X', 3, { 0x67, 0x18, 0x67 } },
  { 'Y', 3, { 0x07, 0x78, 0x07 } },
  { ':', 3, { 0x00, 0x36, 0x00 } },
  { ',', 3, { 0x00, 0x60, 0x00 } },
  { '.', 3, { 0x00, 0x20, 0x00 } },
  { '?', 3, { 0x02, 0x51, 0x0E } },
  { '!', 3, { 0x00, 0x5F, 0x00 } },
  { '%', 3, { 0x71, 0x08, 0x47 } },
  { '^', 3, { 0x04, 0x03, 0x04 } },
  { '(', 3, { 0x3E, 0x41, 0x00 } },
  { ')', 3, { 0x00, 0x41, 0x3E } },
  { '[', 3, { 0x7F, 0x41, 0x00 } },
  { ']', 3, { 0x00, 0x41, 0x7F } },
  { '<', 3, { 0x08, 0x14, 0x00 } },
  { '>', 3, { 0x00, 0x14, 0x08 } },
  { '\'', 3, { 0x00, 0x03, 0x00 } },
  { ';', 3, { 0x40, 0x26, 0x00 } },
  { '/', 3, { 0x70, 0x1C, 0x07 } },
  { '\\', 3, { 0x07, 0x1C, 0x70 } },
  { '|', 3, { 0x00, 0x7F, 0x00 } },
  { '"', 3, { 0x03, 0x00, 0x03 } },
  { '`', 3, { 0x01, 0x02, 0x00 } },
  { '~', 3, { 0x0C, 0x08, 0x18 } },
  { 'Z', 3, { 0x47, 0x4D, 0x71 } },
};
const uint8_t TIKO_FONT_COUNT = sizeof(TIKO_FONT) / sizeof(TIKO_FONT[0]);

// Characters that exist in the pixel-art source but are not part
// of the printable glyph table above (used by the temp/pressure
// builders instead of going through tikoGetChar):
const uint8_t TIKO_DEG[3]   = { 0x02, 0x05, 0x02 };  // °
const uint8_t TIKO_MINUS[3] = { 0x08, 0x08, 0x08 };  // -
const uint8_t TIKO_SPACE[2] = { 0x00, 0x00 };        // ' ' (no glyph in source font)

// Drop-in replacement for mx.getChar(ch, size, buf): looks the
// character up in TIKO_FONT (case-insensitive — the source font
// only defines uppercase letters), falls back to a blank space
// glyph for anything unmapped (accented letters, '@', '-', '+',
// '=', '&', '*', '#', '_', '$', etc. are not present in the
// supplied pixel art and are not guessed at here).
uint8_t tikoGetChar(char ch, uint8_t size, uint8_t* buf) {
  if (ch == ' ') {
    uint8_t n = (2 <= size) ? 2 : size;
    for (uint8_t i = 0; i < n; i++) buf[i] = TIKO_SPACE[i];
    return n;
  }
  if (ch == '-') {
    uint8_t n = (3 <= size) ? 3 : size;
    for (uint8_t i = 0; i < n; i++) buf[i] = TIKO_MINUS[i];
    return n;
  }
  char up = (ch >= 'a' && ch <= 'z') ? (ch - 32) : ch;
  for (uint8_t gi = 0; gi < TIKO_FONT_COUNT; gi++) {
    if (TIKO_FONT[gi].ch == up) {
      uint8_t n = (TIKO_FONT[gi].cols <= size) ? TIKO_FONT[gi].cols : size;
      for (uint8_t i = 0; i < n; i++) buf[i] = TIKO_FONT[gi].data[i];
      return n;
    }
  }
  // Unmapped character: fall back to a blank 3-col space so text
  // keeps flowing instead of stalling the buffer.
  uint8_t n = (3 <= size) ? 3 : size;
  for (uint8_t i = 0; i < n; i++) buf[i] = 0x00;
  return n;
}

// ============================================================
// MICRO FONT (3x5) for the Watchface's weekday, seconds and temperature.
// Same column layout as TIKO_FONT (bit 0 = top row).
// ============================================================
const TikoGlyph MICRO_FONT[] = {
  { '0', 3, { 0x1F, 0x11, 0x1F } },
  { '1', 3, { 0x12, 0x1F, 0x10 } },
  { '2', 3, { 0x1D, 0x15, 0x17 } },
  { '3', 3, { 0x15, 0x15, 0x1F } },
  { '4', 3, { 0x07, 0x04, 0x1F } },
  { '5', 3, { 0x17, 0x15, 0x1D } },
  { '6', 3, { 0x1F, 0x15, 0x1D } },
  { '7', 3, { 0x01, 0x01, 0x1F } },
  { '8', 3, { 0x1F, 0x15, 0x1F } },
  { '9', 3, { 0x17, 0x15, 0x1F } },
  { '-', 2, { 0x04, 0x04 } },
  { 'A', 3, { 0x1E, 0x05, 0x1E } },
  { 'C', 3, { 0x1F, 0x11, 0x11 } },
  { 'D', 3, { 0x1F, 0x11, 0x0E } },
  { 'E', 3, { 0x1F, 0x15, 0x11 } },
  { 'F', 3, { 0x1F, 0x05, 0x01 } },
  { 'H', 3, { 0x1F, 0x04, 0x1F } },
  { 'I', 3, { 0x11, 0x1F, 0x11 } },
  { 'J', 3, { 0x08, 0x10, 0x0F } },
  { 'L', 3, { 0x1F, 0x10, 0x10 } },
  { 'M', 3, { 0x1F, 0x02, 0x1F } },
  { 'N', 3, { 0x1F, 0x01, 0x1E } },
  { 'O', 3, { 0x0E, 0x11, 0x0E } },
  { 'R', 3, { 0x1F, 0x05, 0x1A } },
  { 'S', 3, { 0x12, 0x15, 0x09 } },
  { 'T', 3, { 0x01, 0x1F, 0x01 } },
  { 'U', 3, { 0x1F, 0x10, 0x1F } },
  { 'V', 3, { 0x0F, 0x10, 0x0F } },
  { 'W', 5, { 0x1F, 0x08, 0x04, 0x08, 0x1F } },
};
const uint8_t MICRO_FONT_COUNT = sizeof(MICRO_FONT) / sizeof(MICRO_FONT[0]);

// Case-insensitive like tikoGetChar(); returns 0 for a letter it lacks.
// buf must hold TIKO_MAX_COLS bytes.
static uint8_t microGetChar(char ch, uint8_t* buf) {
  char up = (ch >= 'a' && ch <= 'z') ? (ch - 32) : ch;
  for (uint8_t gi = 0; gi < MICRO_FONT_COUNT; gi++) {
    if (MICRO_FONT[gi].ch == up) {
      memcpy(buf, MICRO_FONT[gi].data, MICRO_FONT[gi].cols);
      return MICRO_FONT[gi].cols;
    }
  }
  return 0;
}

// ============================================================
// MAKO FONT (4x7 custom pixel font) - the third option, between
// Tiko's 3-wide capitals and Marymba's 5-wide library look.
// Rounded bowls and cut corners; real lowercase with a 5-row
// x-height and descenders on row 7. Digits are all exactly 4
// columns so the clock centres the same whatever the time.
// Same column encoding as Tiko: bit r is row r, r=0 is the top.
// Generated from the ASCII drawings in mako_font.py.
// ============================================================
const TikoGlyph MAKO_FONT[] = {
  { '0', 4, { 0x3E, 0x41, 0x41, 0x3E } },
  { '1', 4, { 0x00, 0x42, 0x7F, 0x40 } },
  { '2', 4, { 0x62, 0x51, 0x49, 0x46 } },
  { '3', 4, { 0x41, 0x49, 0x49, 0x36 } },
  { '4', 4, { 0x0C, 0x0A, 0x09, 0x7F } },
  { '5', 4, { 0x27, 0x45, 0x45, 0x39 } },
  { '6', 4, { 0x3E, 0x49, 0x49, 0x30 } },
  { '7', 4, { 0x01, 0x71, 0x09, 0x07 } },
  { '8', 4, { 0x36, 0x49, 0x49, 0x36 } },
  { '9', 4, { 0x06, 0x49, 0x49, 0x3E } },
  { 'A', 4, { 0x7E, 0x09, 0x09, 0x7E } },
  { 'B', 4, { 0x7F, 0x49, 0x49, 0x36 } },
  { 'C', 4, { 0x3E, 0x41, 0x41, 0x22 } },
  { 'D', 4, { 0x7F, 0x41, 0x41, 0x3E } },
  { 'E', 4, { 0x7F, 0x49, 0x49, 0x41 } },
  { 'F', 4, { 0x7F, 0x09, 0x09, 0x01 } },
  { 'G', 4, { 0x3E, 0x41, 0x49, 0x7A } },
  { 'H', 4, { 0x7F, 0x08, 0x08, 0x7F } },
  { 'I', 3, { 0x41, 0x7F, 0x41 } },
  { 'J', 4, { 0x30, 0x40, 0x41, 0x3F } },
  { 'K', 4, { 0x7F, 0x08, 0x14, 0x63 } },
  { 'L', 4, { 0x7F, 0x40, 0x40, 0x40 } },
  { 'M', 5, { 0x7F, 0x02, 0x0C, 0x02, 0x7F } },
  { 'N', 4, { 0x7F, 0x06, 0x18, 0x7F } },
  { 'O', 4, { 0x3E, 0x41, 0x41, 0x3E } },
  { 'P', 4, { 0x7F, 0x09, 0x09, 0x06 } },
  { 'Q', 4, { 0x3E, 0x41, 0x21, 0x5E } },
  { 'R', 4, { 0x7F, 0x09, 0x19, 0x66 } },
  { 'S', 4, { 0x46, 0x49, 0x49, 0x31 } },
  { 'T', 5, { 0x01, 0x01, 0x7F, 0x01, 0x01 } },
  { 'U', 4, { 0x3F, 0x40, 0x40, 0x3F } },
  { 'V', 4, { 0x1F, 0x60, 0x60, 0x1F } },
  { 'W', 5, { 0x7F, 0x20, 0x18, 0x20, 0x7F } },
  { 'X', 4, { 0x63, 0x1C, 0x1C, 0x63 } },
  { 'Y', 4, { 0x07, 0x78, 0x78, 0x07 } },
  { 'Z', 4, { 0x61, 0x51, 0x4D, 0x43 } },
  { 'a', 4, { 0x20, 0x54, 0x54, 0x78 } },
  { 'b', 4, { 0x7F, 0x44, 0x44, 0x38 } },
  { 'c', 4, { 0x38, 0x44, 0x44, 0x44 } },
  { 'd', 4, { 0x38, 0x44, 0x44, 0x7F } },
  { 'e', 4, { 0x38, 0x54, 0x54, 0x58 } },
  { 'f', 3, { 0x7E, 0x05, 0x05 } },
  { 'g', 4, { 0x98, 0xA4, 0xA4, 0x7C } },
  { 'h', 4, { 0x7F, 0x04, 0x04, 0x78 } },
  { 'i', 1, { 0x7D } },
  { 'j', 2, { 0x80, 0x7D } },
  { 'k', 4, { 0x7F, 0x10, 0x28, 0x44 } },
  { 'l', 2, { 0x3F, 0x40 } },
  { 'm', 5, { 0x7C, 0x04, 0x78, 0x04, 0x78 } },
  { 'n', 4, { 0x7C, 0x04, 0x04, 0x78 } },
  { 'o', 4, { 0x38, 0x44, 0x44, 0x38 } },
  { 'p', 4, { 0xFC, 0x24, 0x24, 0x18 } },
  { 'q', 4, { 0x18, 0x24, 0x24, 0xFC } },
  { 'r', 3, { 0x7C, 0x08, 0x04 } },
  { 's', 4, { 0x48, 0x54, 0x54, 0x24 } },
  { 't', 3, { 0x04, 0x3F, 0x44 } },
  { 'u', 4, { 0x3C, 0x40, 0x40, 0x7C } },
  { 'v', 4, { 0x1C, 0x60, 0x60, 0x1C } },
  { 'w', 5, { 0x3C, 0x40, 0x30, 0x40, 0x3C } },
  { 'x', 4, { 0x6C, 0x10, 0x10, 0x6C } },
  { 'y', 4, { 0x1C, 0xA0, 0xA0, 0x7C } },
  { 'z', 4, { 0x64, 0x54, 0x4C, 0x44 } },
  { '.', 1, { 0x40 } },
  { ',', 2, { 0x80, 0x60 } },
  { ':', 1, { 0x36 } },
  { ';', 2, { 0x40, 0x36 } },
  { '!', 1, { 0x5F } },
  { '?', 4, { 0x02, 0x51, 0x09, 0x06 } },
  { '-', 3, { 0x08, 0x08, 0x08 } },
  { '+', 3, { 0x08, 0x1C, 0x08 } },
  { '=', 3, { 0x14, 0x14, 0x14 } },
  { '/', 3, { 0x60, 0x1C, 0x03 } },
  { '\\', 3, { 0x03, 0x1C, 0x60 } },
  { '(', 2, { 0x3E, 0x41 } },
  { ')', 2, { 0x41, 0x3E } },
  { '[', 2, { 0x7F, 0x41 } },
  { ']', 2, { 0x41, 0x7F } },
  { '\'', 1, { 0x03 } },
  { '"', 3, { 0x03, 0x00, 0x03 } },
  { '%', 4, { 0x13, 0x0B, 0x34, 0x32 } },
  { '*', 3, { 0x0A, 0x04, 0x0A } },
  { '#', 5, { 0x12, 0x3F, 0x12, 0x3F, 0x12 } },
  { '_', 4, { 0x80, 0x80, 0x80, 0x80 } },
  { '<', 3, { 0x08, 0x14, 0x22 } },
  { '>', 3, { 0x22, 0x14, 0x08 } },
  { '^', 3, { 0x02, 0x01, 0x02 } },
  { '~', 4, { 0x10, 0x08, 0x10, 0x08 } },
  { '`', 2, { 0x01, 0x02 } },
  { '|', 1, { 0x7F } },
};
const uint8_t MAKO_FONT_COUNT = sizeof(MAKO_FONT) / sizeof(MAKO_FONT[0]);
const uint8_t MAKO_DEG[3] = { 0x02, 0x05, 0x02 };  // 3-wide ring, the same shape Tiko draws

// Like tikoGetChar, but case-sensitive - Mako has its own lowercase - and
// anything it does not draw (@, &, $...) falls back to the Marymba glyph
// instead of a gap, so a notification never silently loses a character.
uint8_t makoGetChar(char ch, uint8_t size, uint8_t* buf) {
  if (ch == ' ') {
    uint8_t n = (2 <= size) ? 2 : size;
    for (uint8_t i = 0; i < n; i++) buf[i] = 0x00;
    return n;
  }
  for (uint8_t gi = 0; gi < MAKO_FONT_COUNT; gi++) {
    if (MAKO_FONT[gi].ch == ch) {
      uint8_t n = (MAKO_FONT[gi].cols <= size) ? MAKO_FONT[gi].cols : size;
      for (uint8_t i = 0; i < n; i++) buf[i] = MAKO_FONT[gi].data[i];
      return n;
    }
  }
  return mx.getChar(ch, size, buf);
}

// ============================================================
// SEGMENT FONTS (4x7) - the look of a seven-segment display.
// A character is stored as the segments it lights rather than
// as pixels, and drawn when asked for: a and d are the top and
// bottom bars, g the middle one, f and e the left side (upper,
// lower), b and c the right side. Segmente lights the segments
// alone, so their ends stay apart the way they do on the real
// thing; Segmente Pline also lights every corner a lit segment
// runs into, and the strokes join up. Letters take the usual
// seven-segment spellings (b, d, n, o, r, t, u...), M and W two
// cells' worth of columns, and anything with no segment
// spelling at all comes from Mako.
// ============================================================
#define SG_A 0x01
#define SG_B 0x02
#define SG_C 0x04
#define SG_D 0x08
#define SG_E 0x10
#define SG_F 0x20
#define SG_G 0x40

struct SegGlyph {
  char    ch;
  uint8_t segs;
};

const SegGlyph SEG_FONT[] = {
  { '0', SG_A | SG_B | SG_C | SG_D | SG_E | SG_F },
  { '1', SG_B | SG_C },
  { '2', SG_A | SG_B | SG_D | SG_E | SG_G },
  { '3', SG_A | SG_B | SG_C | SG_D | SG_G },
  { '4', SG_B | SG_C | SG_F | SG_G },
  { '5', SG_A | SG_C | SG_D | SG_F | SG_G },
  { '6', SG_A | SG_C | SG_D | SG_E | SG_F | SG_G },
  { '7', SG_A | SG_B | SG_C },
  { '8', SG_A | SG_B | SG_C | SG_D | SG_E | SG_F | SG_G },
  { '9', SG_A | SG_B | SG_C | SG_D | SG_F | SG_G },
  { 'A', SG_A | SG_B | SG_C | SG_E | SG_F | SG_G },
  { 'B', SG_C | SG_D | SG_E | SG_F | SG_G },
  { 'C', SG_A | SG_D | SG_E | SG_F },
  { 'D', SG_B | SG_C | SG_D | SG_E | SG_G },
  { 'E', SG_A | SG_D | SG_E | SG_F | SG_G },
  { 'F', SG_A | SG_E | SG_F | SG_G },
  { 'G', SG_A | SG_C | SG_D | SG_E | SG_F },
  { 'H', SG_B | SG_C | SG_E | SG_F | SG_G },
  { 'I', SG_E | SG_F },
  { 'J', SG_B | SG_C | SG_D | SG_E },
  { 'K', SG_C | SG_E | SG_F | SG_G },
  { 'L', SG_D | SG_E | SG_F },
  { 'N', SG_A | SG_B | SG_C | SG_E | SG_F },
  { 'O', SG_C | SG_D | SG_E | SG_G },
  { 'P', SG_A | SG_B | SG_E | SG_F | SG_G },
  { 'Q', SG_A | SG_B | SG_C | SG_F | SG_G },
  { 'R', SG_E | SG_G },
  { 'S', SG_A | SG_C | SG_D | SG_F | SG_G },
  { 'T', SG_D | SG_E | SG_F | SG_G },
  { 'U', SG_B | SG_C | SG_D | SG_E | SG_F },
  { 'V', SG_C | SG_D | SG_E },
  { 'X', SG_B | SG_C | SG_E | SG_F | SG_G },
  { 'Y', SG_B | SG_C | SG_D | SG_F | SG_G },
  { 'Z', SG_A | SG_B | SG_D | SG_E | SG_G },
  { '-', SG_G },
  { '_', SG_D },
  { '=', SG_D | SG_G },
};
const uint8_t SEG_FONT_COUNT = sizeof(SEG_FONT) / sizeof(SEG_FONT[0]);
const uint8_t SEG_DEG[3]     = { 0x06, 0x09, 0x06 };  // a small ring over the top rows
const uint8_t SEGFILL_DEG[3] = { 0x0F, 0x09, 0x0F };  // the same ring, corners lit

// Case-insensitive like tikoGetChar(); the columns come out in the same layout
// as every other table here (bit r is row r, r=0 the top).
uint8_t segGetChar(char ch, uint8_t size, uint8_t* buf, bool filled) {
  uint8_t tmp[TIKO_MAX_COLS];
  uint8_t n = 0;
  char up = (ch >= 'a' && ch <= 'z') ? (ch - 32) : ch;
  if (ch == ' ') {
    tmp[0] = tmp[1] = 0x00; n = 2;
  } else if (ch == ':') {
    tmp[0] = 0x14; n = 1;                      // one dot either side of the middle bar
  } else if (ch == '.') {
    tmp[0] = 0x40; n = 1;
  } else if (up == 'M' || up == 'W') {
    // Three uprights under a bar (M) or over one (W), the way two cells of a
    // segment display spell them.
    uint8_t side = filled ? 0x7F : 0x36;
    uint8_t bar  = (up == 'M') ? 0x01 : 0x40;
    tmp[0] = side; tmp[1] = bar; tmp[2] = side; tmp[3] = bar; tmp[4] = side;
    n = 5;
  } else {
    for (uint8_t gi = 0; gi < SEG_FONT_COUNT; gi++) {
      if (SEG_FONT[gi].ch != up) continue;
      uint8_t s = SEG_FONT[gi].segs, left = 0, mid = 0, right = 0;
      if (s & SG_F) left  |= 0x06;           // rows 1-2
      if (s & SG_E) left  |= 0x30;           // rows 4-5
      if (s & SG_B) right |= 0x06;
      if (s & SG_C) right |= 0x30;
      if (s & SG_A) mid   |= 0x01;           // row 0
      if (s & SG_G) mid   |= 0x08;           // row 3
      if (s & SG_D) mid   |= 0x40;           // row 6
      if (filled) {
        if (s & (SG_A | SG_F))        left  |= 0x01;
        if (s & (SG_F | SG_E | SG_G)) left  |= 0x08;
        if (s & (SG_E | SG_D))        left  |= 0x40;
        if (s & (SG_A | SG_B))        right |= 0x01;
        if (s & (SG_B | SG_C | SG_G)) right |= 0x08;
        if (s & (SG_C | SG_D))        right |= 0x40;
      }
      tmp[0] = left; tmp[1] = mid; tmp[2] = mid; tmp[3] = right;
      n = 4;
      break;
    }
    if (n == 0) return makoGetChar(ch, size, buf);
  }
  if (n > size) n = size;
  memcpy(buf, tmp, n);
  return n;
}

// ============================================================
// BOLD FONT (5x7) - the heavy one. Every upright is two columns
// wide and every bar one row tall, which is as much weight as
// seven rows carry and still keep three bars apart. Digits are
// all 5 columns, like Marymba's, so the clock sits where it
// always has; N, T, X and Y take 6 and M and W 7 to keep their
// strokes thick. Capitals only (case-insensitive like Tiko), and
// anything not drawn here comes from Mako.
// Same column encoding as Tiko: bit r is row r, r=0 is the top.
// Generated from the ASCII drawings in newfonts.py.
// ============================================================
const TikoGlyph BOLD_FONT[] = {
  { '0', 5, { 0x3E, 0x7F, 0x41, 0x7F, 0x3E } },
  { '1', 5, { 0x00, 0x42, 0x7F, 0x7F, 0x40 } },
  { '2', 5, { 0x62, 0x73, 0x59, 0x4F, 0x46 } },
  { '3', 5, { 0x22, 0x63, 0x49, 0x7F, 0x36 } },
  { '4', 5, { 0x0F, 0x0F, 0x08, 0x7F, 0x7F } },
  { '5', 5, { 0x27, 0x67, 0x45, 0x7D, 0x39 } },
  { '6', 5, { 0x3E, 0x7F, 0x49, 0x79, 0x30 } },
  { '7', 5, { 0x01, 0x61, 0x79, 0x1F, 0x07 } },
  { '8', 5, { 0x36, 0x7F, 0x49, 0x7F, 0x36 } },
  { '9', 5, { 0x06, 0x4F, 0x49, 0x7F, 0x3E } },
  { 'A', 5, { 0x7E, 0x7F, 0x09, 0x7F, 0x7E } },
  { 'B', 5, { 0x7F, 0x7F, 0x49, 0x7F, 0x36 } },
  { 'C', 5, { 0x3E, 0x7F, 0x41, 0x41, 0x41 } },
  { 'D', 5, { 0x7F, 0x7F, 0x41, 0x7F, 0x3E } },
  { 'E', 5, { 0x7F, 0x7F, 0x49, 0x49, 0x41 } },
  { 'F', 5, { 0x7F, 0x7F, 0x09, 0x09, 0x01 } },
  { 'G', 5, { 0x3E, 0x7F, 0x41, 0x79, 0x79 } },
  { 'H', 5, { 0x7F, 0x7F, 0x08, 0x7F, 0x7F } },
  { 'I', 4, { 0x41, 0x7F, 0x7F, 0x41 } },
  { 'J', 5, { 0x30, 0x70, 0x40, 0x7F, 0x3F } },
  { 'K', 5, { 0x7F, 0x7F, 0x1C, 0x77, 0x63 } },
  { 'L', 5, { 0x7F, 0x7F, 0x40, 0x40, 0x40 } },
  { 'M', 7, { 0x7F, 0x7F, 0x06, 0x0C, 0x06, 0x7F, 0x7F } },
  { 'N', 6, { 0x7F, 0x7F, 0x06, 0x0C, 0x7F, 0x7F } },
  { 'O', 5, { 0x3E, 0x7F, 0x41, 0x7F, 0x3E } },
  { 'P', 5, { 0x7F, 0x7F, 0x09, 0x0F, 0x06 } },
  { 'Q', 5, { 0x1E, 0x3F, 0x21, 0x7F, 0x5E } },
  { 'R', 5, { 0x7F, 0x7F, 0x09, 0x7F, 0x76 } },
  { 'S', 5, { 0x46, 0x4F, 0x49, 0x79, 0x31 } },
  { 'T', 6, { 0x01, 0x01, 0x7F, 0x7F, 0x01, 0x01 } },
  { 'U', 5, { 0x3F, 0x7F, 0x40, 0x7F, 0x3F } },
  { 'V', 5, { 0x1F, 0x3F, 0x60, 0x3F, 0x1F } },
  { 'W', 7, { 0x7F, 0x7F, 0x30, 0x18, 0x30, 0x7F, 0x7F } },
  { 'X', 6, { 0x63, 0x77, 0x1C, 0x1C, 0x77, 0x63 } },
  { 'Y', 6, { 0x03, 0x07, 0x7C, 0x7C, 0x07, 0x03 } },
  { 'Z', 5, { 0x71, 0x79, 0x5D, 0x4F, 0x47 } },
  { ':', 2, { 0x36, 0x36 } },
  { '.', 2, { 0x60, 0x60 } },
  { ',', 2, { 0xE0, 0x60 } },
  { '!', 2, { 0x5F, 0x5F } },
  { '?', 5, { 0x02, 0x03, 0x59, 0x5F, 0x06 } },
  { '-', 3, { 0x08, 0x08, 0x08 } },
};
const uint8_t BOLD_FONT_COUNT = sizeof(BOLD_FONT) / sizeof(BOLD_FONT[0]);
const uint8_t BOLD_DEG[3] = { 0x07, 0x05, 0x07 };  // a square ring, as heavy as the strokes

uint8_t boldGetChar(char ch, uint8_t size, uint8_t* buf) {
  if (ch == ' ') {
    uint8_t n = (2 <= size) ? 2 : size;
    memset(buf, 0, n);
    return n;
  }
  char up = (ch >= 'a' && ch <= 'z') ? (ch - 32) : ch;
  for (uint8_t gi = 0; gi < BOLD_FONT_COUNT; gi++) {
    if (BOLD_FONT[gi].ch == up) {
      uint8_t n = (BOLD_FONT[gi].cols <= size) ? BOLD_FONT[gi].cols : size;
      memcpy(buf, BOLD_FONT[gi].data, n);
      return n;
    }
  }
  return makoGetChar(ch, size, buf);
}

// ============================================================
// ITALIC FONT (5x7) - Mako leaned to the right: the top four
// rows of a glyph sit one column further over than the rest, so
// every upright steps once on its way down. Worked out from Mako
// when asked for rather than stored, which gives it Mako's
// lowercase and punctuation as well. Digits keep the column the
// lean adds (all 5 wide, for the clock); anything else drops the
// columns the lean left empty, so '.' and '!' stay narrow.
// ============================================================
uint8_t italicGetChar(char ch, uint8_t size, uint8_t* buf) {
  if (ch == ' ') {
    uint8_t n = (2 <= size) ? 2 : size;
    memset(buf, 0, n);
    return n;
  }
  uint8_t src[TIKO_MAX_COLS];
  uint8_t n = makoGetChar(ch, sizeof(src) - 1, src);
  if (n == 0) return 0;
  uint8_t tmp[TIKO_MAX_COLS] = { 0 };
  for (uint8_t c = 0; c < n; c++) {
    tmp[c + 1] |= src[c] & 0x0F;   // rows 0-3, one column over
    tmp[c]     |= src[c] & 0xF0;   // rows 4-7, where Mako has them
  }
  uint8_t first = 0, cnt = n + 1;
  if (ch < '0' || ch > '9') {
    while (cnt > 1 && tmp[first + cnt - 1] == 0) cnt--;
    while (cnt > 1 && tmp[first] == 0) { first++; cnt--; }
  }
  if (cnt > size) cnt = size;
  memcpy(buf, tmp + first, cnt);
  return cnt;
}

// ============================================================
// TOPAIT FONT (4x8) - Mako's capitals hopping: a character whose
// code is odd sits one row lower than one whose code is even, so
// a line of text bobs up and down ("12:45" is 1 low, 2 high,
// 4 high, 5 low). It uses the eighth row the other fonts leave
// dark, and a glyph that already reaches that row (',' '_' and
// the like) stays where it is. Lowercase comes out as capitals,
// since Mako's own lowercase hangs its descenders on that row.
// ============================================================
uint8_t hopGetChar(char ch, uint8_t size, uint8_t* buf) {
  char up = (ch >= 'a' && ch <= 'z') ? (ch - 32) : ch;
  uint8_t n = makoGetChar(up, size, buf);
  if (!(up & 1)) return n;
  for (uint8_t i = 0; i < n; i++)
    if (buf[i] & 0x80) return n;
  for (uint8_t i = 0; i < n; i++) buf[i] <<= 1;
  return n;
}

uint8_t blockGetChar(char ch, uint8_t size, uint8_t* buf) {
  uint8_t n = makoGetChar(ch, size, buf);
  for (uint8_t i = 0; i < n; i++) buf[i] |= (uint8_t)(buf[i] << 1);
  return n;
}

// The degree sign our own fonts draw next to a temperature (3 columns).
static inline const uint8_t* pixDeg(uint8_t m) {
  switch (m) {
    case FONT_MODE_MAKO:    return MAKO_DEG;
    case FONT_MODE_SEG:     return SEG_DEG;
    case FONT_MODE_SEGFILL: return SEGFILL_DEG;
    case FONT_MODE_BOLD:    return BOLD_DEG;
    case FONT_MODE_BLOCK:   return MAKO_DEG;
    default:                return TIKO_DEG;   // Tiko, and Italic (Mako's ring is the same)
  }
}

// FONT_MODE_DEFAULT = 0 ("Marymba", the existing library/FONT[] look)
// FONT_MODE_TIKO    = 1 (3x7 custom font)
// FONT_MODE_MAKO    = 2 (4x7 custom font with lowercase)
// FONT_MODE_SEG     = 3 (4x7 seven-segment look, gaps between segments)
// FONT_MODE_SEGFILL = 4 (4x7 seven-segment look, strokes joined)
// FONT_MODE_BOLD    = 5 (5x7 heavy, two-column uprights)
// FONT_MODE_ITALIC  = 6 (5x7 Mako leaned to the right)
// FONT_MODE_HOP     = 7 (4x8 Topait, capitals bobbing up and down)
// 8 and 9 (Umbra, Terminal) retired
// FONT_MODE_BLOCK   =10 (Mako with doubled vertical strokes)
// (defined earlier, near appendGlyphAuto, so they're visible where first used)

const uint8_t wifiLogo[8] = {
  0b00000010, 0b00001001, 0b00000101, 0b00110101,
  0b00110101, 0b00000101, 0b00001001, 0b00000010
};

const uint8_t apIcon[8] = {
  0b00011000,
  0b01000010,
  0b00011000,
  0b10111101,
  0b10111101,
  0b00011000,
  0b01000010,
  0b00011000
};

void drawApScreen() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);

  for (int col = 0; col < 8; col++) {
    uint8_t colVal = 0;
    for (int row = 0; row < 8; row++) {
      if (ipIcon[row] & (0x80 >> col)) colVal |= (1 << row);
    }
    mx.setColumn(31 - col, colVal);
  }

  mx.setColumn(31 - 8, 0x00);

  // In the font picked under Tile Manager -> Font Type, like the tiles.
  uint8_t lbl[23]; int lblN = 0;
  appendGlyphAuto(lbl, lblN, sizeof(lbl), 'A', fontType);
  appendGlyphAuto(lbl, lblN, sizeof(lbl), 'P', fontType);
  for (int i = 0; i < lblN; i++) mx.setColumn(31 - (9 + i), lbl[i]);

  mxCommit();
}

// LOADING ANIMATION
//
// A gap of LOAD_GAP pixels running clockwise around the 24-pixel perimeter of an
// 8x8 rounded box. 24 perimeter positions means 24 frames and exactly one full
// turn, with every position visited once - so the loop is seamless and needs no
// stored frame table: 24 frames x 8 rows would be 192 bytes of flash holding
// data that is fully derivable from the frame index.
#define LOAD_FRAMES   24
#define LOAD_GAP       3
#define LOAD_FRAME_MS 55

// Wi-Fi connect window, and how much of it the Wi-Fi logo keeps to itself
// before the spinner takes over - cutting to the spinner immediately made the
// logo look like it had been yanked off the panel.
#define WIFI_CONNECT_ATTEMPTS 20   // x500ms -> 10s ceiling on the wait
#define LOAD_LOGO_HOLD_PCT    20   // logo holds for the first fifth of it

// The AP badge gets the same treatment on the way in: long enough to read that
// the mode changed, then the spinner while the access point comes up.
#define AP_SCREEN_HOLD_MS 2000

// Maps a perimeter position (0..23, clockwise starting at the top-left) to the
// pixel it lights.
static inline void loadPerimeterPixel(uint8_t idx, uint8_t& row, uint8_t& col) {
  idx %= LOAD_FRAMES;
  if (idx < 6)       { row = 0;        col = idx + 1;  }   // top,    left  -> right
  else if (idx < 12) { row = idx - 5;  col = 7;        }   // right,  top   -> bottom
  else if (idx < 18) { row = 7;        col = 18 - idx; }   // bottom, right -> left
  else               { row = 24 - idx; col = 0;        }   // left,   bottom-> top
}

// Builds one frame as 8 row bytes (MSB = leftmost column), the same layout the
// static icons above use.
static void buildLoadFrame(uint8_t frame, uint8_t rows[8]) {
  for (uint8_t i = 0; i < 8; i++) rows[i] = 0;
  uint8_t gapStart = (uint8_t)((frame + 3) % LOAD_FRAMES);
  for (uint8_t k = 0; k < LOAD_FRAMES; k++) {
    if ((uint8_t)((k + LOAD_FRAMES - gapStart) % LOAD_FRAMES) < LOAD_GAP) continue;
    uint8_t r, c;
    loadPerimeterPixel(k, r, c);
    rows[r] |= (uint8_t)(0x80 >> c);
  }
}

// Spinner on the left, "Load" next to it.
void drawLoadingScreen(uint8_t frame) {
  uint8_t rows[8];
  buildLoadFrame(frame, rows);

  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);

  for (int col = 0; col < 8; col++) {
    uint8_t colVal = 0;
    for (int row = 0; row < 8; row++) {
      if (rows[row] & (0x80 >> col)) colVal |= (1 << row);
    }
    mx.setColumn(31 - col, colVal);
  }

  // Build the text into a column buffer first so its width is known and it can
  // be centred in the space left of the icon instead of risking a clipped tail.
  // In the font picked under Tile Manager -> Font Type, like the Wifi and AP
  // screens around it.
  uint8_t txtCols[24];
  int nTxt = 0;
  for (const char* p = "Load"; *p; p++) appendGlyphAuto(txtCols, nTxt, sizeof(txtCols), *p, fontType);
  if (nTxt > 0 && txtCols[nTxt - 1] == 0x00) nTxt--;  // no spacing after the last letter

  const int textStart = 9;                     // one blank column after the icon
  int avail = 32 - textStart;
  int at = textStart + (nTxt < avail ? (avail - nTxt) / 2 : 0);
  for (int i = 0; i < nTxt && at < 32; i++) mx.setColumn(31 - at++, txtCols[i]);

  mxCommit();
}

// Keeps the spinner turning for `ms` milliseconds instead of blocking on a bare
// delay(). The frame counter is static so a wait split across several calls
// carries on from where the previous one stopped - the animation never restarts
// mid-spin, however many times it is called.
static uint8_t loadAnimFrame = 0;

static void loadAnimWait(uint16_t ms) {
  uint32_t start = millis();
  do {
    drawLoadingScreen(loadAnimFrame);
    loadAnimFrame = (uint8_t)((loadAnimFrame + 1) % LOAD_FRAMES);
    delay(LOAD_FRAME_MS);
  } while ((uint32_t)(millis() - start) < ms);
}

// autoDetectTimezone() blocks on an HTTP request for up to 5s, which would
// otherwise freeze the spinner mid-turn. Give it the same treatment the
// weather and currency fetches already get - run it on core 0 and keep
// animating on this one until it reports back. The guard is well past the 5s
// HTTP timeout, so a wedged request can never hold up boot indefinitely.
static volatile bool tzDetectBusy = false;

static void tzDetectTask(void* pv) {
  autoDetectTimezone();
  tzDetectBusy = false;
  vTaskDelete(nullptr);
}

// Same task, but nobody waits for it - safe to call from an HTTP handler,
// where blocking for the 5s lookup would stall the whole loop.
static void tzDetectAsync() {
  if (tzDetectBusy) return;
  tzDetectBusy = true;
  if (xTaskCreatePinnedToCore(tzDetectTask, "tzDetect", 8192, nullptr, 1, nullptr, 0) != pdPASS)
    tzDetectBusy = false;
}

static void autoDetectTimezoneAnimated() {
  tzDetectBusy = true;
  if (xTaskCreatePinnedToCore(tzDetectTask, "tzDetect", 8192, nullptr, 1, nullptr, 0) != pdPASS) {
    tzDetectBusy = false;
    autoDetectTimezone();          // could not spawn the task - run it inline
    return;
  }
  uint32_t start = millis();
  while (tzDetectBusy && (uint32_t)(millis() - start) < 12000) loadAnimWait(LOAD_FRAME_MS);
}

const uint8_t musicIcon[8] = {
  0b11100000,
  0b10011100,
  0b10000100,
  0b10000100,
  0b11000100,
  0b11100110,
  0b11000111,
  0b00000110
};

const uint8_t videoIcon[8] = {
  0b00000000,
  0b01111110,
  0b11101111,
  0b11100111,
  0b11100111,
  0b11101111,
  0b01111110,
  0b00000000
};

const uint8_t mementoIcon[8] = {
  0b00100100,
  0b01011010,
  0b00111100,
  0b00111100,
  0b00111100,
  0b01111110,
  0b00011000,
  0b00000000
};

// MEMENTO
char     mementoBuf[128]        = "";
NpState  mementoState           = NP_SHOW_START;
unsigned long mementoPauseMs    = 0;
// Room for the whole text in the widest font (6 columns a letter with the
// gap) plus the icon. At 512 anything past ~85 characters was cut off.
#define MEMENTO_COL_MAX 800
static uint8_t  mementoColBuf[MEMENTO_COL_MAX];
static int      mementoColCount = 0;
static int      mementoScrollPos = 0;
static unsigned long mementoLastScrollMs = 0;
static int      mementoWrapPass = 0;
#define NP_ICON_COLS   8
#define NP_SEP_COLS    1
#define NP_TEXT_COLS   (NP_DISPLAY_COLS - NP_ICON_COLS - NP_SEP_COLS)

const uint8_t tempIcon[8] = {
  0b00011000,
  0b00011000,
  0b00011000,
  0b00011000,
  0b00011000,
  0b00101100,
  0b00100100,
  0b00011000
};
#define TEMP_ICON_COLS  8
#define TEMP_SEP_COLS   1
#define TEMP_TEXT_COLS  (32 - TEMP_ICON_COLS - TEMP_SEP_COLS)

const uint8_t dateIcon[8] = {
  0b01111110,
  0b11111111,
  0b11111111,
  0b10000001,
  0b10000001,
  0b10000001,
  0b10000001,
  0b01111110
};
#define DATE_ICON_COLS  8
#define DATE_SEP_COLS   1
#define DATE_TEXT_COLS  (32 - DATE_ICON_COLS - DATE_SEP_COLS)

const uint8_t pressureIconUp[8] = {
  0b00011000,
  0b00111100,
  0b01111110,
  0b11011011,
  0b10011001,
  0b00011000,
  0b00011000,
  0b00011000
};
const uint8_t pressureIconDown[8] = {
  0b00011000,
  0b00011000,
  0b00011000,
  0b10011001,
  0b11011011,
  0b01111110,
  0b00111100,
  0b00011000
};
const uint8_t pressureIconFlat[8] = {
  0b00000000,
  0b00000000,
  0b00000000,
  0b01111110,
  0b01111110,
  0b00000000,
  0b00000000,
  0b00000000
};
#define PRESSURE_ICON_COLS  8
#define PRESSURE_SEP_COLS   1
#define PRESSURE_TEXT_COLS  (32 - PRESSURE_ICON_COLS - PRESSURE_SEP_COLS)

// ============================================================
// CUSTOM TILE ICONS (extra icons offered in the web UI picker,
// in addition to every icon the tiles already use natively)
// ============================================================
const uint8_t exclamationIcon[8] = {
  0b00011000,
  0b00011000,
  0b00011000,
  0b00011000,
  0b00011000,
  0b00000000,
  0b00011000,
  0b00011000
};

const uint8_t pauseIcon[8] = {
  0b00000000,
  0b01100110,
  0b01100110,
  0b01100110,
  0b01100110,
  0b01100110,
  0b01100110,
  0b00000000
};

const uint8_t euroIcon[8] = {
  0b00011000,
  0b00100100,
  0b01110000,
  0b00100000,
  0b01110000,
  0b00100100,
  0b00011000,
  0b00000000
};

const uint8_t bluetoothIcon[8] = {
  0b00001000,
  0b00101100,
  0b00011010,
  0b00001100,
  0b00001100,
  0b00011010,
  0b00101100,
  0b00001000
};

const uint8_t blockedIcon[8] = {
  0b00000000,
  0b00111100,
  0b01000110,
  0b01001010,
  0b01010010,
  0b01100010,
  0b00111100,
  0b00000000
};

// Speaker supplied for the Voicemeeter tile. Rows are stored MSB-first, like
// the rest of the 8x8 icon catalog.
const uint8_t speakerIcon[8] = {
  0b00010000,
  0b00110010,
  0b11110001,
  0b11110101,
  0b11110101,
  0b11110001,
  0b00110010,
  0b00010000
};

// Download Arrow supplied for Browser Downloads.
const uint8_t downloadArrowIcon[8] = {
  0b00011000,
  0b00011000,
  0b00011000,
  0b00011000,
  0b01111110,
  0b00111100,
  0b10011001,
  0b11111111
};

// Camera Record supplied for OBS Studio.
const uint8_t cameraRecordIcon[8] = {
  0b00000000,
  0b10011111,
  0b11011111,
  0b11111111,
  0b11111111,
  0b11011111,
  0b10011111,
  0b00000000
};

// Extra user-selectable icons. Keep this order in sync with both pickers.
const uint8_t laptopIcon[8]       = {0x00, 0x7E, 0x42, 0x42, 0x42, 0x7E, 0xFF, 0x00};
const uint8_t phoneIcon[8]        = {0x3C, 0x42, 0x42, 0x42, 0x42, 0x42, 0x5A, 0x3C};
const uint8_t musicNoteBIcon[8]   = {0x10, 0x30, 0x50, 0x10, 0x18, 0x1C, 0x08, 0x00};
const uint8_t batteryEmptyIcon[8] = {0x00, 0xFE, 0x82, 0x83, 0x83, 0x82, 0xFE, 0x00};
const uint8_t battery1Icon[8]     = {0x00, 0xFE, 0xC2, 0xC3, 0xC3, 0xC2, 0xFE, 0x00};
const uint8_t battery2Icon[8]     = {0x00, 0xFE, 0xE2, 0xE3, 0xE3, 0xE2, 0xFE, 0x00};
const uint8_t battery3Icon[8]     = {0x00, 0xFE, 0xF2, 0xF3, 0xF3, 0xF2, 0xFE, 0x00};
const uint8_t battery4Icon[8]     = {0x00, 0xFE, 0xFA, 0xFB, 0xFB, 0xFA, 0xFE, 0x00};
const uint8_t battery5Icon[8]     = {0x00, 0xFE, 0xFE, 0xFF, 0xFF, 0xFE, 0xFE, 0x00};
const uint8_t cordIcon[8]         = {0x24, 0x24, 0x24, 0x7E, 0x3C, 0x18, 0x18, 0x18};
const uint8_t speakerBoxIcon[8]   = {0x7E, 0x5A, 0x42, 0x5A, 0x66, 0x66, 0x5A, 0x7E};
const uint8_t headsetMicIcon[8]   = {0x7E, 0xC3, 0x81, 0x81, 0x81, 0xE7, 0xE7, 0xEF};
const uint8_t headphonesIcon[8]   = {0x7E, 0xC3, 0x81, 0x81, 0x81, 0xE7, 0xE7, 0xE7};
const uint8_t warningIcon[8]      = {0x18, 0x18, 0x24, 0x24, 0x66, 0x7E, 0xE7, 0xFF};
const uint8_t objectFocusIcon[8]  = {0xC3, 0x81, 0x18, 0x18, 0x00, 0x3C, 0x81, 0xC3};
const uint8_t infoIcon[8]         = {0x00, 0x18, 0x18, 0x00, 0x18, 0x18, 0x18, 0x00};
const uint8_t chipIcon[8]         = {0x24, 0x7E, 0xC3, 0x5A, 0x5A, 0xC3, 0x7E, 0x24};

// Master catalog of every icon selectable in the "Icon Settings" picker
// (all pre-existing tile icons + the new ones above). The index used by
// iconSel*/iconWx* settings is (position in this array + 1); 0 means
// "Auto / Default" i.e. keep the tile's own built-in icon logic.
struct IconCatalogEntry { const char* id; const uint8_t* bmp; };
const IconCatalogEntry ICON_CATALOG[] = {
  { "date",      dateIcon },
  { "temp",      tempIcon },
  { "reminder",  mementoIcon },
  { "music",     musicIcon },
  { "notif",     notifIcon },
  { "pressup",   pressureIconUp },
  { "pressdown", pressureIconDown },
  { "pressflat", pressureIconFlat },
  { "sunny",     sunnyIcon },
  { "cloud",     cloudIcon },
  { "rain",      cloudRainIcon },
  { "storm",     cloudLightningIcon },
  { "snow",      cloudSnowIcon },
  { "wind",      windIcon },
  { "moon",      moonIcon },
  { "wifi",      ipIcon },
  { "ap",        apIcon },
  { "exclaim",   exclamationIcon },
  { "pause",     pauseIcon },
  { "euro",      euroIcon },
  { "bluetooth", bluetoothIcon },
  { "blocked",   blockedIcon },
  { "wifilogo",  wifiLogo },
  { "video",     videoIcon },
  { "speaker",   speakerIcon },
  { "download",  downloadArrowIcon },
  { "camrec",    cameraRecordIcon },
  { "laptop",    laptopIcon },
  { "phone",     phoneIcon },
  { "musicb",    musicNoteBIcon },
  { "batempty",  batteryEmptyIcon },
  { "bat1",      battery1Icon },
  { "bat2",      battery2Icon },
  { "bat3",      battery3Icon },
  { "bat4",      battery4Icon },
  { "bat5",      battery5Icon },
  { "cord",      cordIcon },
  { "speakerbox",speakerBoxIcon },
  { "headsetmic",headsetMicIcon },
  { "headphones",headphonesIcon },
  { "warning",   warningIcon },
  { "focus",     objectFocusIcon },
  { "info",      infoIcon },
  { "chip",      chipIcon }
};
#define ICON_CATALOG_COUNT (sizeof(ICON_CATALOG) / sizeof(ICON_CATALOG[0]))

static inline const uint8_t* resolveTileIcon(uint8_t sel, const uint8_t* fallback) {
  if (sel > 0 && sel <= ICON_CATALOG_COUNT) return ICON_CATALOG[sel - 1].bmp;
  return fallback;
}

static const uint8_t* batteryLevelIcon(int pct) {
  if (pct <= 0) return batteryEmptyIcon;
  if (pct >= 100) return battery5Icon;
  // One segment per fifth; a nonzero charge always shows at least one.
  const uint8_t* levels[] = {battery1Icon, battery2Icon, battery3Icon, battery4Icon, battery5Icon};
  return levels[constrain((pct - 1) / 20, 0, 4)];
}

static const uint8_t* digitGlyph(uint8_t fontMode, int d, uint8_t* tmp, int& width);

static void batteryDraw(bool alert) {
  int pct;
  bool present;
  unsigned long last;
  portENTER_CRITICAL(&batteryMux);
  pct = batteryPercent;
  present = batteryPresent;
  last = batteryLastMs;
  portEXIT_CRITICAL(&batteryMux);
  bool valid = present && pct >= 0 && last != 0 && millis() - last < BATTERY_STATUS_TIMEOUT_MS;
  const uint8_t* icon = resolveTileIcon(alert ? iconSelLowBattery : iconSelBattery,
                                        valid ? batteryLevelIcon(pct) : batteryEmptyIcon);
  uint8_t output[32] = {0};
  for (int x = 0; x < 8; x++) {
    for (int y = 0; y < 8; y++) {
      if (icon[y] & (0x80u >> x)) output[x] |= (1u << y);
    }
  }
  if (valid) {
    char number[4];
    snprintf(number, sizeof(number), "%d", constrain(pct, 0, 100));
    uint8_t fontMode = alert ? fontTypeLowBattery : fontTypeBattery;
    // Keep the value next to the 8-column icon instead of centering it in the
    // remaining space. "100%" ends inside the 32 columns in every font.
    int at = 9;
    for (const char* p = number; *p; p++) {
      uint8_t tmp[TIKO_MAX_COLS];
      int w;
      const uint8_t* g = digitGlyph(fontMode, *p - '0', tmp, w);
      for (int x = 0; x < w && at + x < 32; x++) output[at + x] = g[x];
      at += w + 1;
    }
    static const uint8_t percentGlyph[5] = {0x23, 0x13, 0x08, 0x64, 0x62};
    uint8_t ownPercent[TIKO_MAX_COLS];
    const uint8_t* percent = percentGlyph;
    int percentW = 5;
    if (isPixelFont(fontMode)) {
      percentW = pixGetChar(fontMode, '%', TIKO_MAX_COLS, ownPercent);
      percent = ownPercent;
    }
    if (at + percentW <= 32)
      for (int x = 0; x < percentW; x++) output[at + x] = percent[x];
  } else {
    output[9] = output[10] = output[15] = output[16] = 0x08; // --
  }
  if (alert) output[8] = 0x7E;
  mx.update(MD_MAX72XX::OFF);
  for (int x = 0; x < 32; x++) mx.setColumn(31 - x, output[x]);
  mxCommit();
}

// The alert's switch and its place in the Tile Manager, asked the same way by
// the tick, higherPriorityTileActive(), initVisibleDisplay() and the wake-up.
static inline bool lowBatteryAllowed() {
  return lowBatteryEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_LOW_BATTERY));
}

static bool batteryAlertTick() {
  unsigned long until;
  unsigned long last;
  portENTER_CRITICAL(&batteryMux);
  until = batteryAlertUntilMs;
  last = batteryLastMs;
  portEXIT_CRITICAL(&batteryMux);
  bool active = lowBatteryAllowed() &&
                last != 0 && millis() - last < BATTERY_STATUS_TIMEOUT_MS &&
                (int32_t)(until - millis()) > 0;
  if (!active) {
    if (batteryAlertWasActive) {
      batteryAlertWasActive = false;
      resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!batteryAlertWasActive || strcmp(gLivePrio, "lowbattery") != 0) {
    batteryAlertWasActive = true;
    beginC2PCapture();
    batteryDraw(true);
    finishC2PTransition(tileTransNotif, tileTransSpd[TRSPD_NOTIF]);
  }
  return true;
}

// Now Playing's default (un-overridden) icon: the music note, unless
// Adaptive Icon is on and the source app was reported as a video player.
static inline const uint8_t* npFallbackIcon() {
  // Paused comes first: what is on hold matters more than what kind of thing it is.
  if (npIsPaused) return pauseIcon;
  return (npAdaptiveIcon && npIsVideoSource) ? videoIcon : musicIcon;
}

// Resolves the icon actually shown on the Now Playing tile: uses the
// Music override or the Video override depending on the currently
// detected source, falling back to npFallbackIcon() (which respects
// Adaptive Icon) when the relevant override is left on Auto.
static inline const uint8_t* npResolvedIcon() {
  uint8_t sel = npIsPaused ? iconSelNpPaused : npIsVideoSource ? iconSelNpVideo : iconSelNpMusic;
  return resolveTileIcon(sel, npFallbackIcon());
}

// DISPLAY HELPERS

void writeCharN(const uint8_t* ch, int n, int logicalCol) {
  for (int i = 0; i < n; i++) {
    int physCol = 31 - (logicalCol + i);
    if (physCol >= 0 && physCol < 32) mx.setColumn(physCol, ch[i]);
  }
}

void writeChar(const uint8_t* ch, int logicalCol) {
  writeCharN(ch, 5, logicalCol);
}

void writeGap(int logicalCol) {
  int physCol = 31 - logicalCol;
  if (physCol >= 0 && physCol < 32) mx.setColumn(physCol, 0x00);
}
static uint8_t  tempColBuf[128];
static int      tempColCount = 0;
static int      tempScrollPos = 0;
static unsigned long tempLastScrollMs = 0;
static NpState  tempState    = NP_SHOW_START;
static unsigned long tempPauseMs = 0;
static bool     tempNeedsScroll = false;
static int      tempWrapPass = 0;

static uint8_t  dateColBuf[128];
static int      dateColCount = 0;
static int      dateScrollPos = 0;
static unsigned long dateLastScrollMs = 0;
static NpState  dateState    = NP_SHOW_START;
static unsigned long datePauseMs = 0;
static bool     dateNeedsScroll = false;
static int      dateWrapPass = 0;

static void tempAddGlyphTo(uint8_t* buf, int& count, const uint8_t* glyph, int width = 5) {
  for (int i = 0; i < width && count < 62; i++)
    buf[count++] = glyph[i];
  if (count < 63) buf[count++] = 0x00;
}

static void tempAddGlyph(const uint8_t* glyph, int width = 5) {
  for (int i = 0; i < width && tempColCount < 126; i++)
    tempColBuf[tempColCount++] = glyph[i];
  if (tempColCount < 127) tempColBuf[tempColCount++] = 0x00;
}

// Shared helper: returns a pointer-friendly digit glyph (0-9) for
// the classic 5-wide FONT[] table or whichever font of ours is set,
// and reports its column width via 'width'. 'tmp' must be
// at least TIKO_MAX_COLS bytes and stays valid until reused.
static const uint8_t* digitGlyph(uint8_t fontMode, int d, uint8_t* tmp, int& width) {
  if (isPixelFont(fontMode)) {
    width = pixGetChar(fontMode, (char)('0' + d), TIKO_MAX_COLS, tmp);
    return tmp;
  }
  width = 5;
  return FONT[d];
}

static void dateAddDot() {
  if (isPixelFont(fontTypeDate)) {
    uint8_t tmp[TIKO_MAX_COLS];
    uint8_t n = pixGetChar(fontTypeDate, '.', TIKO_MAX_COLS, tmp);
    for (int i = 0; i < n && dateColCount < 126; i++) dateColBuf[dateColCount++] = tmp[i];
    if (dateColCount < 127) dateColBuf[dateColCount++] = 0x00;
    return;
  }
  if (dateColCount < 126) dateColBuf[dateColCount++] = CHAR_DOT[0];
  if (dateColCount < 127) dateColBuf[dateColCount++] = 0x00;
}

// SCROLL TYPE

static void scrollBufPrependIcon(uint8_t* buf, int& count, int maxCount, const uint8_t icon[8], int iconCols) {
  for (int col = 0; col < iconCols && count < maxCount; col++) {
    uint8_t colVal = 0;
    for (int row = 0; row < 8; row++) {
      if (icon[row] & (0x80 >> col)) colVal |= (1 << row);
    }
    buf[count++] = colVal;
  }
  if (count < maxCount) buf[count++] = 0x00;
}

void tempBuildBuffer(int tempC) {
  int temp = (tempC == TEMP_NO_READING) ? 0
           : (tempUnit == 1) ? (tempC * 9 / 5 + 32) : tempC;
  tempColCount = 0;
  if (scrollIconInBuffer(scrollTypeTemp) && !hideIconTemp) {
    scrollBufPrependIcon(tempColBuf, tempColCount, 127, resolveTileIcon(iconSelTemp, tempIcon), TEMP_ICON_COLS);
  }

  bool tiko = isPixelFont(fontTypeTemp);   // Tiko or Mako

  if (tempC == TEMP_NO_READING) {
    // No usable reading yet. Draw "--" followed by the unit rather than
    // leaving the buffer empty: an empty buffer meant tempDrawAtPos() wrote 32
    // blank columns and the matrix stayed black for the tile's whole slot,
    // which reads as a broken clock instead of a quiet sensor. This also
    // matches the "--" the web dashboard already shows for the same state.
    for (int rep = 0; rep < 2; rep++) {
      for (int i = 0; i < 3 && tempColCount < 126; i++)
        tempColBuf[tempColCount++] = tiko ? TIKO_MINUS[i] : 0x08;
      if (tempColCount < 127) tempColBuf[tempColCount++] = 0x00;
    }
  } else {
    bool negative = (temp < 0);
    int  absTemp  = abs(temp);

    if (negative) {
      const uint8_t* minusGlyph = tiko ? TIKO_MINUS : nullptr;
      int minusW = tiko ? 3 : 3;
      for (int i = 0; i < minusW && tempColCount < 126; i++)
        tempColBuf[tempColCount++] = tiko ? minusGlyph[i] : 0x08;
      if (tempColCount < 127) tempColBuf[tempColCount++] = 0x00;
    }

    uint8_t dtmp[TIKO_MAX_COLS]; int dw;
    if (absTemp >= 100) {
      tempAddGlyph(digitGlyph(fontTypeTemp, absTemp / 100, dtmp, dw), dw);
      tempAddGlyph(digitGlyph(fontTypeTemp, (absTemp / 10) % 10, dtmp, dw), dw);
      tempAddGlyph(digitGlyph(fontTypeTemp, absTemp % 10, dtmp, dw), dw);
    } else if (absTemp >= 10) {
      tempAddGlyph(digitGlyph(fontTypeTemp, absTemp / 10, dtmp, dw), dw);
      tempAddGlyph(digitGlyph(fontTypeTemp, absTemp % 10, dtmp, dw), dw);
    } else {
      tempAddGlyph(digitGlyph(fontTypeTemp, absTemp, dtmp, dw), dw);
    }
  }

  if (tiko) {
    tempAddGlyph(pixDeg(fontTypeTemp), 3);
    uint8_t utmp[TIKO_MAX_COLS];
    uint8_t uw = pixGetChar(fontTypeTemp, (tempUnit == 1) ? 'F' : 'C', TIKO_MAX_COLS, utmp);
    tempAddGlyph(utmp, uw);
  } else {
    tempAddGlyph(CHAR_DEG);
    tempAddGlyph((tempUnit == 1) ? CHAR_F : CHAR_C);
  }

  if (tempColCount > 0) tempColCount--;
}

void tempDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);

  if (!hideIconTemp && !scrollIconInBuffer(scrollTypeTemp)) {

    // Resolved once rather than 64 times inside the loop below.
    const uint8_t* tempIconBmp = resolveTileIcon(iconSelTemp, tempIcon);
    for (int col = 0; col < TEMP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (tempIconBmp[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - TEMP_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconTemp || scrollIconInBuffer(scrollTypeTemp);
  int textOffset = fullWidth ? 0 : (TEMP_ICON_COLS + TEMP_SEP_COLS);
  int textCols   = fullWidth ? 32 : TEMP_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < tempColCount) ? tempColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }

  mxCommit();
}

void tempInit(int tempC) {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();

  tempBuildBuffer(tempC);
  int tempEffTextCols = (hideIconTemp || scrollIconInBuffer(scrollTypeTemp)) ? 32 : TEMP_TEXT_COLS;
  tempNeedsScroll = (tempColCount > tempEffTextCols);

  if (tempNeedsScroll) {
    tempScrollPos = 0;
    tempState     = NP_PAUSE_BEFORE_RIGHT;
    tempPauseMs   = millis();
    tempDrawAtPos(0);
  } else {

    int offset = (tempEffTextCols - tempColCount) / 2;
    if (offset < 0) offset = 0;
    tempDrawAtPos(-offset);
  }
}

// MSI AFTERBURNER
//
// Octoglow Connect reads what MSI Afterburner and RivaTuner Statistics Server
// publish in their shared memory on the PC and posts it here (POST /abstatus):
// the frame rate RTSS measures for the 3D application in the foreground, and
// Afterburner's CPU and GPU temperatures. The clock never makes a value up: one
// left out of a post, or not refreshed within AB_TIMEOUT_MS, is unavailable, and
// the tile then leaves the circuit instead of showing an old number.
#define AB_TIMEOUT_MS  6000UL
#define AB_MODE_FPS    0
#define AB_MODE_CPU    1
#define AB_MODE_GPU    2
#define AB_NONE        INT16_MIN
#define AB_UNDRAWN     (-100000)
#define AB_TEXT_COLS   (32 - TEMP_ICON_COLS - TEMP_SEP_COLS)

uint8_t abMode = AB_MODE_FPS;   // which of the three values the tile shows

static portMUX_TYPE abMux = portMUX_INITIALIZER_UNLOCKED;
static int16_t abFps     = AB_NONE;   // whole frames per second
static int16_t abCpuDeci = AB_NONE;   // tenths of a degree Celsius
static int16_t abGpuDeci = AB_NONE;
static unsigned long abLastMs = 0;
// Connect's word that the frame rate belongs to a game in front (game=1).
static bool abFpsGame = false;

// n / d to the nearest whole number, halves away from zero.
static inline int abDivRound(int32_t n, int32_t d) {
  return n >= 0 ? (int)((n + d / 2) / d) : -(int)((-n + d / 2) / d);
}

// The chosen value as the panel shows it - whole frames per second, or whole
// degrees in the unit the Temperature tile uses - or false while there is none.
static bool abShownValue(int& value) {
  int16_t fps, cpu, gpu;
  unsigned long last;
  portENTER_CRITICAL(&abMux);
  fps = abFps;
  cpu = abCpuDeci;
  gpu = abGpuDeci;
  last = abLastMs;
  portEXIT_CRITICAL(&abMux);
  if (last == 0 || millis() - last >= AB_TIMEOUT_MS) return false;
  if (abMode == AB_MODE_FPS) {
    if (fps == AB_NONE) return false;
    value = fps;
    return true;
  }
  int16_t deci = (abMode == AB_MODE_GPU) ? gpu : cpu;
  if (deci == AB_NONE) return false;
  // F = C x 9/5 + 32, worked from the tenths so it is rounded once: 58.4 C is
  // 137.1 F, where converting the rounded 58 would have given 136.
  value = (tempUnit == 1) ? abDivRound((int32_t)deci * 9 + 1600, 50) : abDivRound(deci, 10);
  return true;
}

// The one question the circuit, the redraws and /abstate all ask.
static inline bool abLive() {
  int v;
  return abShownValue(v);
}

// The frame rate while a game is played, for the Game FPS priority tile:
// fresh, there, and marked by Connect as a game's. Any other 3D application
// RTSS measures - a slicer, a browser - leaves the tile down.
static bool abGameFps(int& fps) {
  int16_t f;
  bool game;
  unsigned long last;
  portENTER_CRITICAL(&abMux);
  f = abFps;
  game = abFpsGame;
  last = abLastMs;
  portEXIT_CRITICAL(&abMux);
  if (last == 0 || millis() - last >= AB_TIMEOUT_MS || f == AB_NONE || !game) return false;
  fps = f;
  return true;
}

// The Game FPS tile is switched on and in the Tile Manager.
static inline bool gameFpsWanted() {
  return gameFpsEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_GAMEFPS));
}

// The one question the priority loop, the redraws and /abstate all ask.
static bool gameFpsLive() {
  int fps;
  return gameFpsWanted() && abGameFps(fps);
}

// Info for the frame rate, Chip for the two temperatures, unless one was picked.
static inline const uint8_t* abIcon() {
  return resolveTileIcon(iconSelAfterburner, abMode == AB_MODE_FPS ? infoIcon : chipIcon);
}

static uint8_t  abColBuf[96];
static int      abColCount = 0;
static int      abScrollPos = 0;
static unsigned long abLastScrollMs = 0;
static NpState  abState = NP_SHOW_START;
static unsigned long abPauseMs = 0;
static bool     abNeedsScroll = false;
static int      abWrapPass = 0;
// What is on the panel, so a new reading redraws only when the text changes.
static int      abDrawnValue = AB_UNDRAWN;
static uint8_t  abDrawnMode = 0xFF;
static uint8_t  abDrawnUnit = 0xFF;

static inline int abTextCols() {
  return (hideIconAfterburner || scrollIconInBuffer(scrollTypeAfterburner)) ? 32 : AB_TEXT_COLS;
}

static inline void abPut(uint8_t* buf, int& count, int cap, uint8_t col) {
  if (count < cap) buf[count++] = col;
}

// "FPS 144", "CPU 58°C", "GPU 66°F" in the given font, into buf; the icon goes
// first when it scrolls along with the text (iconInBuf, otherwise nullptr).
// Both the circuit tile and the Game FPS priority tile draw through this, so
// the frame rate looks the same on either. Returns the columns written.
static int abBuildText(uint8_t* buf, int cap, uint8_t mode, int value, uint8_t font, const uint8_t* iconInBuf) {
  int count = 0;
  if (iconInBuf) scrollBufPrependIcon(buf, count, cap - 1, iconInBuf, TEMP_ICON_COLS);
  const char* label = mode == AB_MODE_FPS ? "FPS" : mode == AB_MODE_CPU ? "CPU" : "GPU";
  for (const char* p = label; *p; p++) appendGlyphAuto(buf, count, cap, *p, font);
  // A word space: two dark columns more than between letters.
  abPut(buf, count, cap, 0x00);
  abPut(buf, count, cap, 0x00);
  char digits[8];
  snprintf(digits, sizeof(digits), "%d", value);
  for (const char* p = digits; *p; p++) {
    if (*p == '-') {
      bool pix = isPixelFont(font);
      for (int i = 0; i < 3; i++) abPut(buf, count, cap, pix ? TIKO_MINUS[i] : 0x08);
      abPut(buf, count, cap, 0x00);
    } else {
      appendGlyphAuto(buf, count, cap, *p, font);
    }
  }
  if (mode != AB_MODE_FPS) {
    // The degree sign and the unit exactly as the Temperature tile draws them.
    if (isPixelFont(font)) {
      const uint8_t* deg = pixDeg(font);
      for (int i = 0; i < 3; i++) abPut(buf, count, cap, deg[i]);
      abPut(buf, count, cap, 0x00);
      uint8_t u[TIKO_MAX_COLS];
      uint8_t uw = pixGetChar(font, tempUnit == 1 ? 'F' : 'C', TIKO_MAX_COLS, u);
      for (int i = 0; i < uw; i++) abPut(buf, count, cap, u[i]);
      abPut(buf, count, cap, 0x00);
    } else {
      for (int i = 0; i < 5; i++) abPut(buf, count, cap, CHAR_DEG[i]);
      abPut(buf, count, cap, 0x00);
      const uint8_t* u = tempUnit == 1 ? CHAR_F : CHAR_C;
      for (int i = 0; i < 5; i++) abPut(buf, count, cap, u[i]);
      abPut(buf, count, cap, 0x00);
    }
  }
  if (count > 0 && buf[count - 1] == 0x00) count--;
  return count;
}

// In the tile's font, with its icon and scroll.
static void abBuildBuffer(int value) {
  abColCount = abBuildText(abColBuf, (int)sizeof(abColBuf), abMode, value, fontTypeAfterburner,
                           (!hideIconAfterburner && scrollIconInBuffer(scrollTypeAfterburner)) ? abIcon() : nullptr);
}

// The icon in columns 0-7 and one dark column after it, then textCols columns
// of buf from pos; with no icon (nullptr) the text has the whole width.
static void abDrawCols(const uint8_t* buf, int count, int pos, const uint8_t* icon, int textCols) {
  mx.update(MD_MAX72XX::OFF);
  int textOffset = 0;
  if (icon) {
    for (int col = 0; col < TEMP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (icon[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - TEMP_ICON_COLS, 0x00);
    textOffset = TEMP_ICON_COLS + TEMP_SEP_COLS;
  }
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    mx.setColumn(31 - (textOffset + col), (src >= 0 && src < count) ? buf[src] : 0x00);
  }
  mxCommit();
}

static void abDrawAtPos(int pos) {
  abDrawCols(abColBuf, abColCount, pos,
             (!hideIconAfterburner && !scrollIconInBuffer(scrollTypeAfterburner)) ? abIcon() : nullptr,
             abTextCols());
}

static void abDrawCentred() {
  int offset = (abTextCols() - abColCount) / 2;
  abDrawAtPos(-(offset < 0 ? 0 : offset));
}

// Puts the tile up from the start: still and centred when the text fits,
// otherwise held for half the slot and then scrolled, like the Temperature tile.
static void abInit() {
  int value;
  abWrapPass = 0;
  if (!abShownValue(value)) {
    // The circuit lets the tile up only with a value; this is the instant one
    // ran out in between. The loop moves on at its next pass.
    mx.update(MD_MAX72XX::OFF);
    for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
    mxCommit();
    abDrawnValue = AB_UNDRAWN;
    abDrawnMode = 0xFF;
    abNeedsScroll = false;
    abState = NP_SHOW_START;
    return;
  }
  abDrawnValue = value;
  abDrawnMode = abMode;
  abDrawnUnit = tempUnit;
  abBuildBuffer(value);
  abNeedsScroll = abColCount > abTextCols();
  if (abNeedsScroll) {
    abScrollPos = 0;
    abState = NP_PAUSE_BEFORE_RIGHT;
    abPauseMs = millis();
    abDrawAtPos(0);
  } else {
    abState = NP_SHOW_START;
    abDrawCentred();
  }
}

// A new reading while the tile is up is drawn in place. A scroll keeps going
// from where it is, so a frame rate that changes every second never restarts
// it; text that stops fitting starts one.
static void abRefresh() {
  int value;
  if (!abShownValue(value)) return;
  if (abMode != abDrawnMode || tempUnit != abDrawnUnit) {
    abInit();
    return;
  }
  if (value == abDrawnValue) return;
  abDrawnValue = value;
  abBuildBuffer(value);
  bool fits = abColCount <= abTextCols();
  if (!abNeedsScroll) {
    if (fits) abDrawCentred();
    else abInit();
    return;
  }
  int maxPos = abColCount - abTextCols();
  if (maxPos < 0) maxPos = 0;
  if (abState != NP_SCROLL_WRAP && abScrollPos > maxPos) abScrollPos = maxPos;
  abDrawAtPos(abScrollPos);
}

// ATMOSPHERIC PRESSURE 

#define PRESSURE_HISTORY_LEN      6
#define PRESSURE_SAMPLE_MS        (30UL * 60UL * 1000UL)
#define PRESSURE_TREND_THRESH_HPA 1.0f
float         pressureHistory[PRESSURE_HISTORY_LEN] = {NAN, NAN, NAN, NAN, NAN, NAN};
uint8_t       pressureHistCount     = 0;
unsigned long lastPressureSampleMs  = 0;
float         lastPressureHpa       = 1013.25f;
int8_t        pressureTrend         = 0;

static inline const uint8_t* pressureIconForTrend() {
  const uint8_t* base = pressureIconFlat;
  if (pressureTrend > 0) base = pressureIconUp;
  else if (pressureTrend < 0) base = pressureIconDown;
  return resolveTileIcon(iconSelPressure, base);
}

// Brings up the BMP280 at either of its two possible addresses. Also used as
// the recovery path, so it must be safe to call repeatedly.
static bool bmpInitSensor() {
  if (!(bmp.begin(0x76) || bmp.begin(0x77))) {
    bmpOk = false;
    return false;
  }
  bmp.setSampling(Adafruit_BMP280::MODE_NORMAL,
                  Adafruit_BMP280::SAMPLING_X2,
                  Adafruit_BMP280::SAMPLING_X16,
                  Adafruit_BMP280::FILTER_X16,
                  Adafruit_BMP280::STANDBY_MS_500);
  bmpOk = true;
  return true;
}

// A slave interrupted mid-byte (brown-out, ESD, a knocked jumper) keeps SDA
// pulled low and the bus is then dead for everyone - Wire just times out on
// every transaction. Nine manual clock pulses walk it out of that byte, then a
// manual STOP releases the bus.
static void i2cBusRecover() {
  Wire.end();
  pinMode(BMP_SCL, OUTPUT_OPEN_DRAIN);
  pinMode(BMP_SDA, INPUT_PULLUP);
  digitalWrite(BMP_SCL, HIGH);
  for (int i = 0; i < 9 && digitalRead(BMP_SDA) == LOW; i++) {
    digitalWrite(BMP_SCL, LOW);  delayMicroseconds(5);
    digitalWrite(BMP_SCL, HIGH); delayMicroseconds(5);
  }
  pinMode(BMP_SDA, OUTPUT_OPEN_DRAIN);
  digitalWrite(BMP_SDA, LOW);  delayMicroseconds(5);
  digitalWrite(BMP_SCL, HIGH); delayMicroseconds(5);
  digitalWrite(BMP_SDA, HIGH); delayMicroseconds(5);
  Wire.begin(BMP_SDA, BMP_SCL);
}

// Single guarded read. The range test is written positively so a NaN - what a
// missing sensor produces - is rejected too.
static bool bmpReadTempC(float& out) {
  float t = bmp.readTemperature();
  if (!(t >= -40.0f && t <= 85.0f)) {
    if (bmpFailStreak < 255) bmpFailStreak++;
    return false;
  }
  bmpFailStreak = 0;
  bmpOk         = true;
  out           = t;
  return true;
}

// Called from loop(). Cheap while the sensor is healthy; when it is not, it
// retries with backoff so a permanently absent sensor costs one begin() attempt
// per minute instead of one per iteration.
static void bmpHealthTick() {
  if (bmpOk && bmpFailStreak < BMP_FAIL_LIMIT) return;

  unsigned long now = millis();
  if (bmpNextRetryMs != 0 && (long)(now - bmpNextRetryMs) < 0) return;

  bmpOk = false;
  i2cBusRecover();
  if (bmpInitSensor()) {
    Serial.println("[bmp] senzor reinitializat");
    bmpFailStreak      = 0;
    bmpRetryBackoffMs  = BMP_RETRY_MIN_MS;
    lastTempSampleMs   = 0;   // resample on the very next tick
    lastPressureReadMs = 0;
  } else {
    bmpRetryBackoffMs *= 2;
    if (bmpRetryBackoffMs > BMP_RETRY_MAX_MS) bmpRetryBackoffMs = BMP_RETRY_MAX_MS;
  }
  bmpNextRetryMs = millis() + bmpRetryBackoffMs;
}

void tempSampleTick() {
  unsigned long now = millis();
  if (lastTempSampleMs != 0 && (now - lastTempSampleMs) < TEMP_SAMPLE_INTERVAL_MS) return;
  lastTempSampleMs = now;
  float tf;
  if (bmpReadTempC(tf)) lastTemp = (int)round(tf);
}

void pressureSampleTick() {
  // This used to hit the sensor on every single loop() iteration - thousands of
  // I2C transactions a second (readPressure() reads the temperature registers
  // too), which is both wasted time and the most likely way to wedge the bus in
  // the first place. Atmospheric pressure does not move that fast.
  unsigned long readNow = millis();
  if (lastPressureReadMs != 0 && (readNow - lastPressureReadMs) < BMP_READ_INTERVAL_MS) return;
  lastPressureReadMs = readNow;

  float hpa = bmp.readPressure() / 100.0F;
  // Written as a positive range test so a NaN reading (failed/absent sensor) is
  // rejected too. `hpa < 850 || hpa > 1100` is false for NaN, which let NaN
  // through to lastPressureHpa - and /state then serialised (int)round(NaN),
  // which is undefined behaviour and can emit a garbage pressureHpa value.
  if (!(hpa >= 850 && hpa <= 1100)) {
    // Counts towards the same failure streak as a bad temperature read, so
    // bmpHealthTick() can recover a sensor that only fails on this register.
    if (bmpFailStreak < 255) bmpFailStreak++;
    return;
  }
  lastPressureHpa = hpa;

  unsigned long now = millis();
  if (lastPressureSampleMs != 0 && (now - lastPressureSampleMs) < PRESSURE_SAMPLE_MS) {
    return;
  }
  lastPressureSampleMs = now;

  for (int i = 0; i < PRESSURE_HISTORY_LEN - 1; i++) pressureHistory[i] = pressureHistory[i + 1];
  pressureHistory[PRESSURE_HISTORY_LEN - 1] = hpa;
  if (pressureHistCount < PRESSURE_HISTORY_LEN) pressureHistCount++;
  float oldest = NAN;
  for (int i = 0; i < PRESSURE_HISTORY_LEN; i++) {
    if (!isnan(pressureHistory[i])) { oldest = pressureHistory[i]; break; }
  }
  if (!isnan(oldest)) {
    float diff = hpa - oldest;
    if (diff >= PRESSURE_TREND_THRESH_HPA)       pressureTrend = 1;
    else if (diff <= -PRESSURE_TREND_THRESH_HPA) pressureTrend = -1;
    else                                          pressureTrend = 0;
  }
}

static uint8_t  pressureColBuf[128];
static int      pressureColCount = 0;
static int      pressureScrollPos = 0;
static unsigned long pressureLastScrollMs = 0;
static NpState  pressureState    = NP_SHOW_START;
static unsigned long pressurePauseMs = 0;
static bool     pressureNeedsScroll = false;
static int      pressureWrapPass = 0;

void pressureBuildBuffer(int hpaInt) {
  pressureColCount = 0;
  if (scrollIconInBuffer(scrollTypePressure) && !hideIconPressure) {
    scrollBufPrependIcon(pressureColBuf, pressureColCount, 127, pressureIconForTrend(), PRESSURE_ICON_COLS);
  }

  int absVal = abs(hpaInt);
  uint8_t dtmp[TIKO_MAX_COLS]; int dw;
  if (absVal >= 1000) {
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, (absVal / 1000) % 10, dtmp, dw), dw);
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, (absVal / 100) % 10, dtmp, dw), dw);
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, (absVal / 10) % 10, dtmp, dw), dw);
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, absVal % 10, dtmp, dw), dw);
  } else if (absVal >= 100) {
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, absVal / 100, dtmp, dw), dw);
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, (absVal / 10) % 10, dtmp, dw), dw);
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, absVal % 10, dtmp, dw), dw);
  } else if (absVal >= 10) {
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, absVal / 10, dtmp, dw), dw);
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, absVal % 10, dtmp, dw), dw);
  } else {
    tempAddGlyphTo(pressureColBuf, pressureColCount, digitGlyph(fontTypePressure, absVal, dtmp, dw), dw);
  }

  if (pressureColCount > 0) pressureColCount--;
}

void pressureDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);

  if (!hideIconPressure && !scrollIconInBuffer(scrollTypePressure)) {

    const uint8_t* icon = pressureIconForTrend();
    for (int col = 0; col < PRESSURE_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (icon[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - PRESSURE_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconPressure || scrollIconInBuffer(scrollTypePressure);
  int textOffset = fullWidth ? 0 : (PRESSURE_ICON_COLS + PRESSURE_SEP_COLS);
  int textCols   = fullWidth ? 32 : PRESSURE_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < pressureColCount) ? pressureColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }

  mxCommit();
}

void pressureInit(int hpaInt) {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();

  pressureBuildBuffer(hpaInt);
  int pressureEffTextCols = (hideIconPressure || scrollIconInBuffer(scrollTypePressure)) ? 32 : PRESSURE_TEXT_COLS;
  pressureNeedsScroll = (pressureColCount > pressureEffTextCols);

  if (pressureNeedsScroll) {
    pressureScrollPos = 0;
    pressureState     = NP_PAUSE_BEFORE_RIGHT;
    pressurePauseMs   = millis();
    pressureDrawAtPos(0);
  } else {

    pressureDrawAtPos(0);
  }
}

// CURRENCY STANDARDS 

#define CURRENCY_FETCH_INTERVAL_MS   (6UL * 60UL * 60UL * 1000UL)
#define CURRENCY_TREND_THRESH_PCT    0.1f

char          currencyBase[4]     = "EUR";
char          currencyQuote[4]    = "RON";
bool          currencyCompareEnabled = false;
float         currencyRateNow     = NAN;
float         currencyRateMonthAgo = NAN;
bool          currencyValid       = false;
int8_t        currencyTrend       = 0;
unsigned long lastCurrencyFetch   = 0;

static inline const uint8_t* currencyIconForTrend() {
  const uint8_t* base = pressureIconFlat;
  if (currencyTrend > 0) base = pressureIconUp;
  else if (currencyTrend < 0) base = pressureIconDown;
  return resolveTileIcon(iconSelCurrency, base);
}

static uint8_t  currColBuf[128];
static int      currColCount = 0;
static int      currScrollPos = 0;
static unsigned long currLastScrollMs = 0;
static NpState  currState    = NP_SHOW_START;
static unsigned long currPauseMs = 0;
static bool     currNeedsScroll = false;
static int      currWrapPass = 0;

static void currBufAppendMD(char ch) {
  appendGlyphAuto(currColBuf, currColCount, 127, ch, fontTypeCurrency);
}

void currencyBuildBuffer() {
  currColCount = 0;
  if (scrollIconInBuffer(scrollTypeCurrency) && !hideIconCurrency) {
    scrollBufPrependIcon(currColBuf, currColCount, 127, currencyIconForTrend(), PRESSURE_ICON_COLS);
  }
  if (currencyCompareEnabled) {
    currBufAppendMD('1');
    currBufAppendMD(' ');
  }
  for (int ci = 0; currencyBase[ci] != '\0'; ci++) currBufAppendMD(currencyBase[ci]);
  if (currencyCompareEnabled) {
    currBufAppendMD(' ');
    currBufAppendMD('=');
    currBufAppendMD(' ');
    char rateStr[16];
    if (currencyValid && !isnan(currencyRateNow)) {

      if (fabsf(currencyRateNow) >= 1.0f) snprintf(rateStr, sizeof(rateStr), "%.2f", currencyRateNow);
      else                                snprintf(rateStr, sizeof(rateStr), "%.4f", currencyRateNow);
    } else {
      strcpy(rateStr, "...");
    }
    for (int ci = 0; rateStr[ci] != '\0'; ci++) currBufAppendMD(rateStr[ci]);
    currBufAppendMD(' ');
    for (int ci = 0; currencyQuote[ci] != '\0'; ci++) currBufAppendMD(currencyQuote[ci]);
  }

  if (currColCount > 0) currColCount--;
}

void currencyDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);

  if (!hideIconCurrency && !scrollIconInBuffer(scrollTypeCurrency)) {
    const uint8_t* icon = currencyIconForTrend();
    for (int col = 0; col < PRESSURE_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (icon[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - PRESSURE_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconCurrency || scrollIconInBuffer(scrollTypeCurrency);
  int textOffset = fullWidth ? 0 : (PRESSURE_ICON_COLS + PRESSURE_SEP_COLS);
  int textCols   = fullWidth ? 32 : PRESSURE_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < currColCount) ? currColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }

  mxCommit();
}

void currencyInit() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();

  currencyBuildBuffer();
  int currEffTextCols = (hideIconCurrency || scrollIconInBuffer(scrollTypeCurrency)) ? 32 : PRESSURE_TEXT_COLS;
  currNeedsScroll = (currColCount > currEffTextCols);

  currScrollPos = 0;
  currWrapPass  = 0;
  currencyDrawAtPos(0);
  currState   = NP_PAUSE_BEFORE_RIGHT;
  currPauseMs = millis();
}

static bool currencyFetchOne(const char* base, const char* quote, const char* dateStr, float& outRate) {
  if (WiFi.status() != WL_CONNECTED) return false;
  String url = "https://api.frankfurter.dev/v1/" + String(dateStr) +
               "?base=" + String(base) + "&symbols=" + String(quote);
  HTTPClient http;
  http.begin(url);
  http.setTimeout(8000);
  int code = http.GET();
  bool ok = false;
  if (code == 200) {
    String body = http.getString();
    DynamicJsonDocument doc(1024);
    DeserializationError err = deserializeJson(doc, body);
    if (!err) {
      float r = doc["rates"][quote] | NAN;
      if (!isnan(r)) { outRate = r; ok = true; }
    }
  }
  http.end();
  return ok;
}

// --- Non-blocking currency fetch ---------------------------------------
// Same problem as weatherFetch(), only worse: this ran up to TWO blocking
// HTTP requests back to back (currencyFetchOne has an 8s timeout each,
// see above), which could freeze the whole display for up to ~16 seconds
// right in the middle of loop(). Moved to a background task for the same
// reasons as weatherFetch().
static TaskHandle_t  currencyFetchTaskHandle = nullptr;
static std::atomic<bool> currencyFetchBusy       = false;
static std::atomic<bool> currencyFetchReady      = false;
static volatile bool currFetchOk             = false;
static volatile uint8_t currFetchErr         = API_ERR_NONE;
uint8_t              currencyErr             = API_ERR_NONE;
static uint8_t       currencyFails           = 0;
static float         currFetchRateNow;
static float         currFetchRateMonthAgo;
static bool          currFetchHaveMonthAgo;
static int8_t        currFetchTrend;

static void currencyFetchTask(void* pv) {
  bool ok = false;
  // Asked before the clock knows the date, there is nothing to report yet.
  uint8_t err = API_ERR_NONE;
  currFetchHaveMonthAgo = false;

  const char* refQuote = currencyCompareEnabled ? currencyQuote
                        : (strcasecmp(currencyBase, "EUR") == 0 ? "USD" : "EUR");

  struct tm ti;
  if (readLocalTime(ti)) {
    time_t nowEpoch = mktime(&ti);
    time_t monthAgoEpoch = nowEpoch - (30L * 24L * 60L * 60L);
    struct tm* tiMonthAgo = gmtime(&monthAgoEpoch);
    char monthAgoStr[11];
    snprintf(monthAgoStr, sizeof(monthAgoStr), "%04d-%02d-%02d",
             tiMonthAgo->tm_year + 1900, tiMonthAgo->tm_mon + 1, tiMonthAgo->tm_mday);

    float rNow = NAN, rOld = NAN;
    bool okNow = currencyFetchOne(currencyBase, refQuote, "latest", rNow);
    bool okOld = okNow && currencyFetchOne(currencyBase, refQuote, monthAgoStr, rOld);
    // Not while Wi-Fi is still coming up: that is no fault of the service.
    if (!okNow && WiFi.status() == WL_CONNECTED) err = API_ERR_NO_ANSWER;

    if (okNow) {
      currFetchRateNow = rNow;
      ok = true;
      if (okOld && rOld > 0) {
        currFetchRateMonthAgo = rOld;
        currFetchHaveMonthAgo = true;
        float pctDiff = (rNow - rOld) / rOld * 100.0f;
        if (pctDiff >= CURRENCY_TREND_THRESH_PCT)       currFetchTrend = 1;
        else if (pctDiff <= -CURRENCY_TREND_THRESH_PCT) currFetchTrend = -1;
        else                                            currFetchTrend = 0;
      }
    }
  }

  currFetchErr         = err;
  currFetchOk          = ok;
  currencyFetchReady    = true;
  currencyFetchBusy     = false;
  vTaskDelete(nullptr);
}

void currencyFetch() {
  if (currencyFetchBusy || currencyFetchReady || backgroundFetchInFlight) return;

  lastCurrencyFetch  = millis(); // stamp now so loop() doesn't re-trigger every iteration
  currencyFetchBusy  = true;
  currencyFetchReady = false;
  backgroundFetchInFlight = true;
  if (xTaskCreatePinnedToCore(currencyFetchTask, "currFetch", 8192, nullptr, 1, &currencyFetchTaskHandle, 0) != pdPASS) {
    currencyFetchBusy = false;
    backgroundFetchInFlight = false;
  }
}

// Call from loop(); cheap check, only does work once a fetch has completed.
static void currencyFetchPoll() {
  if (!currencyFetchReady || currencyFetchBusy) return;
  currencyFetchReady = false;
  backgroundFetchInFlight = false;
  if (currFetchOk) {
    currencyRateNow = currFetchRateNow;
    currencyValid   = true;
    if (currFetchHaveMonthAgo) {
      currencyRateMonthAgo = currFetchRateMonthAgo;
      currencyTrend        = currFetchTrend;
    }
    currencyErr   = API_ERR_NONE;
    currencyFails = 0;
  } else {
    apiNoteFailure(currFetchErr, currencyValid, currencyErr, currencyFails);
    lastCurrencyFetch = apiRetryStamp(CURRENCY_FETCH_INTERVAL_MS);
  }
}

// SUBSCRIBER COUNTER TILE (YouTube)
//
// Shows the subscriber count as "icon + number", scrolling when the number does
// not fit - same behaviour as the currency tile.
//
// Only YouTube is implemented: it is the one platform of the obvious three that
// publishes a usable public endpoint (Data API v3, with a free API key). TikTok
// and Instagram expose no follower count without either an approved OAuth app or
// a third-party scraper, so tiles for them would have had no dependable source.
// The structure below is kept indexable so another platform can be slotted in if
// a workable source ever appears.

#define SOCIAL_COUNT 1
#define SOC_YT 0
#define SOCIAL_FETCH_INTERVAL_MS (10UL * 60UL * 1000UL)

struct SocialCfg {
  char     handle[80];    // channel link, @handle or channel id
  char     apiKey[80];    // YouTube Data API key
  uint32_t count;
  bool     valid;
  bool     showName;      // false: "12400"   true: "NUME: 12400"
  uint8_t  err;           // API_ERR_*: what the last request ran into
  uint8_t  fails;         // unanswered requests in a row
};
SocialCfg     social[SOCIAL_COUNT];
unsigned long lastSocialFetch[SOCIAL_COUNT] = { 0 };

uint8_t iconSelYoutube = 0;

static inline int socialIndexForItem(uint8_t id) {
  return (id == ITEM_YOUTUBE) ? SOC_YT : -1;
}

static const uint8_t* socialIcon(int i) {
  (void)i;
  // Reuses the existing video glyph unless the user picked one in Icon Settings.
  return resolveTileIcon(iconSelYoutube, videoIcon);
}

// --- render -----------------------------------------------------------------
static uint8_t  socColBuf[128];
static int      socColCount = 0;
static int      socScrollPos = 0;
static unsigned long socLastScrollMs = 0;
static NpState  socState = NP_SHOW_START;
static unsigned long socPauseMs = 0;
static bool     socNeedsScroll = false;
static int      socWrapPass = 0;
static int      socActiveIdx = -1;

// Set by the Preview while it draws a channel of its own, "Octoglow" with 30
// subscribers: a clock with no channel or no answer yet has none to show.
static bool gSocSample = false;

static void socBufAppend(char ch) {
  appendGlyphAuto(socColBuf, socColCount, 127, ch, fontTypeYoutube);
}

// Turns whatever the user typed - a full link, an @handle or a bare name - into
// something short enough to sit in front of the count. The '@' is dropped on
// purpose: the Tiko font has no glyph for it and would render a blank gap.
static void socialDisplayName(int i, char* out, size_t n) {
  const char* h = social[i].handle;
  const char* p = strrchr(h, '/');
  p = p ? p + 1 : h;
  if (*p == '@') p++;
  size_t len = 0;
  while (p[len] != '\0' && p[len] != '?' && len < n - 1) len++;
  memcpy(out, p, len);
  out[len] = '\0';
}

void socialBuildBuffer(int i) {
  socColCount = 0;
  if (scrollIconInBuffer(scrollTypeYoutube) && !hideIconYoutube) {
    scrollBufPrependIcon(socColBuf, socColCount, 127, socialIcon(i), PRESSURE_ICON_COLS);
  }
  char txt[96];
  char num[16];
  if (gSocSample)           strcpy(num, "30");
  else if (social[i].valid) snprintf(num, sizeof(num), "%lu", (unsigned long)social[i].count);
  else                      strcpy(num, "...");

  if (social[i].showName) {
    char name[32];
    socialDisplayName(i, name, sizeof(name));
    if (gSocSample && name[0] == '\0') strcpy(name, "Octoglow");
    if (name[0] != '\0') snprintf(txt, sizeof(txt), "%s: %s", name, num);
    else                 snprintf(txt, sizeof(txt), "%s", num);
  } else {
    snprintf(txt, sizeof(txt), "%s", num);
  }

  for (int ci = 0; txt[ci] != '\0'; ci++) socBufAppend(txt[ci]);
  if (socColCount > 0) socColCount--;
}

void socialDrawAtPos(int pos) {
  int i = socActiveIdx;
  if (i < 0) return;
  mx.update(MD_MAX72XX::OFF);

  // Blank first so no pixel from the previous frame can survive in a column
  // this one does not write.
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);

  if (!hideIconYoutube && !scrollIconInBuffer(scrollTypeYoutube)) {
    const uint8_t* icon = socialIcon(i);
    for (int col = 0; col < PRESSURE_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (icon[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      int physCol = 31 - col;
      if (physCol >= 0 && physCol < 32) mx.setColumn(physCol, colVal);
    }
  }

  bool fullWidth = hideIconYoutube || scrollIconInBuffer(scrollTypeYoutube);
  int textOffset = fullWidth ? 0 : (PRESSURE_ICON_COLS + PRESSURE_SEP_COLS);
  int textCols   = fullWidth ? 32 : PRESSURE_TEXT_COLS;
  for (int c = 0; c < textCols; c++) {
    int srcCol = pos + c;
    uint8_t colVal = (srcCol >= 0 && srcCol < socColCount) ? socColBuf[srcCol] : 0x00;
    int physCol = 31 - (textOffset + c);
    if (physCol >= 0 && physCol < 32) mx.setColumn(physCol, colVal);
  }
  mxCommit();
}

void socialInit(uint8_t itemId) {
  int i = socialIndexForItem(itemId);
  if (i < 0) return;
  socActiveIdx = i;

  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();

  socialBuildBuffer(i);
  int effTextCols = (hideIconYoutube || scrollIconInBuffer(scrollTypeYoutube)) ? 32 : PRESSURE_TEXT_COLS;
  socNeedsScroll = (socColCount > effTextCols);
  socWrapPass    = 0;

  socScrollPos = 0;
  if (socNeedsScroll) {
    socState   = NP_PAUSE_BEFORE_RIGHT;
    socPauseMs = millis();
  } else {
    // Fits on the matrix: leave the scroll state machine idle. Nothing is
    // offset - the icon sits at the left edge and the number follows it.
    socState = NP_SHOW_START;
  }
  socialDrawAtPos(0);
}

// --- fetch ------------------------------------------------------------------
// The API returns subscriberCount as a *string*, so it is read as one and
// converted rather than relying on an implicit numeric cast.
static bool socialFetchYouTube(int i, uint32_t& out, uint8_t& err) {
  err = API_ERR_NONE;
  if (social[i].apiKey[0] == '\0' || social[i].handle[0] == '\0') return false;
  err = API_ERR_NO_ANSWER;

  String h = String(social[i].handle);
  h.trim();
  String param;
  int at = h.lastIndexOf('@');
  if (at >= 0) {
    String tail = h.substring(at + 1);
    int slash = tail.indexOf('/');
    if (slash >= 0) tail = tail.substring(0, slash);
    param = "forHandle=@" + urlEncode(tail);
  } else {
    int slash = h.lastIndexOf('/');
    String tail = (slash >= 0) ? h.substring(slash + 1) : h;
    int q = tail.indexOf('?');
    if (q >= 0) tail = tail.substring(0, q);
    param = tail.startsWith("UC") ? ("id=" + urlEncode(tail))
                                  : ("forUsername=" + urlEncode(tail));
  }

  String url = "https://www.googleapis.com/youtube/v3/channels?part=statistics&" +
               param + "&key=" + urlEncode(String(social[i].apiKey));
  HTTPClient http;
  http.begin(url);
  http.setTimeout(8000);
  int code = http.GET();
  bool ok = false;
  if (code == 200) {
    String body = http.getString();
    DynamicJsonDocument doc(2048);
    if (!deserializeJson(doc, body)) {
      const char* s = doc["items"][0]["statistics"]["subscriberCount"] | (const char*)nullptr;
      if (s && *s) { out = (uint32_t)strtoul(s, nullptr, 10); ok = true; err = API_ERR_NONE; }
      // An answer with no channel in it: the link, handle or id matches nothing.
      else if (doc["items"].isNull() || doc["items"].size() == 0) err = API_ERR_NOT_FOUND;
    }
  } else {
    // 400 is what an unknown key gets ("API key not valid"), 403 a key that
    // may not use the YouTube Data API or has spent its daily quota.
    if (code == 400 || code == 401 || code == 403) err = API_ERR_REFUSED;
    Serial.printf("[social] YouTube HTTP %d\n", code);
  }
  http.end();
  return ok;
}

// Runs on core 0 like the weather and currency fetches, so the matrix and the
// web server are never blocked by the request.
static std::atomic<bool> socialFetchBusy  = false;
static std::atomic<bool> socialFetchReady = false;
static volatile int  socialFetchIdx   = -1;
static volatile bool socialFetchOk    = false;
static volatile uint8_t socialFetchErr = API_ERR_NONE;
static uint32_t      socialFetchValue = 0;

static void socialFetchTask(void* pv) {
  int i = socialFetchIdx;
  uint32_t n = 0;
  bool ok = false;
  uint8_t err = API_ERR_NONE;
  if (i >= 0 && WiFi.status() == WL_CONNECTED) {
    ok = socialFetchYouTube(i, n, err);
  }
  socialFetchErr   = err;
  socialFetchValue = n;
  socialFetchOk    = ok;
  socialFetchReady = true;
  socialFetchBusy  = false;
  vTaskDelete(nullptr);
}

void socialFetch(int i) {
  if (i < 0 || i >= SOCIAL_COUNT) return;
  if (socialFetchBusy || socialFetchReady || backgroundFetchInFlight) return;
  if (WiFi.status() != WL_CONNECTED) return;
  lastSocialFetch[i] = millis();
  socialFetchIdx   = i;
  socialFetchBusy  = true;
  socialFetchReady = false;
  backgroundFetchInFlight = true;
  if (xTaskCreatePinnedToCore(socialFetchTask, "socFetch", 8192, nullptr, 1, nullptr, 0) != pdPASS) {
    socialFetchBusy = false;
    backgroundFetchInFlight = false;
  }
}

// Cheap check from loop(); only does work once a fetch has landed.
static void socialFetchPoll() {
  if (!socialFetchReady || socialFetchBusy) return;
  socialFetchReady = false;
  backgroundFetchInFlight = false;
  int i = socialFetchIdx;
  if (i < 0 || i >= SOCIAL_COUNT) return;
  if (socialFetchOk) {
    social[i].count = socialFetchValue;
    social[i].valid = true;
    social[i].err   = API_ERR_NONE;
    social[i].fails = 0;
    if (gLiveTileId == ITEM_YOUTUBE + i) socialInit(ITEM_YOUTUBE + i);
  } else {
    apiNoteFailure(socialFetchErr, social[i].valid, social[i].err, social[i].fails);
    if (socialFetchErr == API_ERR_NO_ANSWER) lastSocialFetch[i] = apiRetryStamp(SOCIAL_FETCH_INTERVAL_MS);
  }
}

// What the Tile Manager says about a counter tile (see API_ERR_*).
static uint8_t socialErrCode(int i) {
  if (social[i].apiKey[0] == '\0') return API_ERR_NO_KEY;
  if (social[i].handle[0] == '\0') return API_ERR_NO_TARGET;
  return social[i].err;
}

// Refreshes whichever tiles are enabled, one at a time.
static void socialTickFetch() {
  if (socialFetchBusy) return;
  for (int i = 0; i < SOCIAL_COUNT; i++) {
    uint8_t id = ITEM_YOUTUBE + i;
    // Cheapest question first. The items[] scan used to run on every pass
    // through loop(); now it only runs once a refresh is actually due.
    if (lastSocialFetch[i] != 0 && millis() - lastSocialFetch[i] < SOCIAL_FETCH_INTERVAL_MS) continue;
    if (!tileInManager(id)) continue;
    bool enabled = false;
    for (int k = 0; k < NUM_ITEMS; k++)
      if (items[k].id == id && items[k].enabled) { enabled = true; break; }
    if (!enabled) continue;
    socialFetch(i);
    return;
  }
}

// SPEED DISPLAY (ETS2, BeamNG.drive, Assetto Corsa)
//
// The tiles are sent whole km/h and draw them as "57km", or as "35mph" when
// set so. mph is worked out here, rounded halves up, so every screen that
// shows it - the panel, the web interface, Octoglow Connect - reads the same.

int kmhToMph(int kmh) {
  if (kmh <= 0) return 0;
  // 1 mile = 1.609344 km, in integers: 999 km/h stays well inside 32 bits.
  return (int)(((uint32_t)kmh * 1000000u + 804672u) / 1609344u);
}

// The number drawn for a speed: in the chosen unit, 0 to 999.
int speedShown(int kmh, bool mph) {
  int v = mph ? kmhToMph(kmh) : kmh;
  if (v < 0) v = 0;
  if (v > 999) v = 999;
  return v;
}

// The p and h of "mph", three columns each: with the system font's m they keep
// three digits and the unit inside 31 columns, where its own four-column
// letters would run to 33 and lose the last one.
static const uint8_t SPEED_MPH_P[3] = { 0xFC, 0x24, 0x18 };
static const uint8_t SPEED_MPH_H[3] = { 0x7F, 0x04, 0x78 };

// "km", or "mph" with the narrow p and h: the classic look, and what a font
// whose own letters would run past the panel falls back to.
static void speedAppendClassicUnit(uint8_t* buf, int& count, bool mph) {
  uint8_t m[8];
  uint8_t nm = mx.getChar('m', sizeof(m), m);
  if (mph) {
    for (int i = 0; i < nm && count < 63; i++) buf[count++] = m[i];
    if (count < 63) buf[count++] = 0x00;
    tempAddGlyphTo(buf, count, SPEED_MPH_P, 3);
    for (int i = 0; i < 3 && count < 64; i++) buf[count++] = SPEED_MPH_H[i];
    return;
  }
  uint8_t k[8];
  uint8_t nk = mx.getChar('k', sizeof(k), k);
  for (int i = 0; i < nk && count < 63; i++) buf[count++] = k[i];
  if (count < 63) buf[count++] = 0x00;
  for (int i = 0; i < nm && count < 63; i++) buf[count++] = m[i];
}

// shown >= 0 is the number to draw, already in the unit (a tile whose game
// gives both); otherwise it is worked out from kmh.
static void speedBuildBuffer(uint8_t* buf, int& count, int kmh, bool mph, uint8_t fontMode, int shown = -1) {
  count = 0;
  int speed = shown >= 0 ? (shown > 999 ? 999 : shown) : speedShown(kmh, mph);
  int digits[3];
  int nd = 0;
  if (speed >= 100) digits[nd++] = speed / 100;
  if (speed >= 10)  digits[nd++] = (speed / 10) % 10;
  digits[nd++] = speed % 10;
  for (int i = 0; i < nd; i++) {
    uint8_t tmp[TIKO_MAX_COLS];
    int w;
    const uint8_t* g = digitGlyph(fontMode, digits[i], tmp, w);
    tempAddGlyphTo(buf, count, g, w);
  }
  if (!isPixelFont(fontMode)) {
    speedAppendClassicUnit(buf, count, mph);
    return;
  }
  int unitAt = count;
  for (const char* c = mph ? "mph" : "km"; *c; c++) appendGlyphAuto(buf, count, 64, *c, fontMode);
  if (count > unitAt) count--;   // no gap after the last letter
  // Bold, Italic and Umbra draw three digits and "mph" wider than the panel:
  // the unit goes back to the narrow letters, which always fit.
  if (count > 32) {
    count = unitAt;
    speedAppendClassicUnit(buf, count, mph);
  }
}

static void speedDraw(const uint8_t* buf, int count) {
  mx.update(MD_MAX72XX::OFF);

  int offset = (32 - count) / 2;
  if (offset < 0) offset = 0;

  for (int col = 0; col < 32; col++) {
    int src = col - offset;
    uint8_t val = (src >= 0 && src < count) ? buf[src] : 0x00;
    mx.setColumn(31 - col, val);
  }

  mxCommit();
}

// SPEED GAUGES
//
// Besides the plain "57km", a speed tile can be drawn as a gauge: an arc that
// sweeps up the left side and along the top edge, a dial with a needle, or a
// segmented bar under the number. A gauge fills against the tile's full scale
// in km/h, the same whichever unit the number is in, and draws its digits in
// faces of its own that fit beside it - the tile's font is the plain look's.
// The web interface (gaugeFrame() in portal.html) and Octoglow Connect
// (SpeedGauge.cs) draw the same frames for their previews, from these same
// tables: a change here goes there too.
#define SPEED_GAUGE_PLAIN 0
#define SPEED_GAUGE_ARC   1
#define SPEED_GAUGE_DIAL  2
#define SPEED_GAUGE_BAR   3
#define SPEED_GAUGE_COUNT 4
#define SPEED_GAUGE_FULL_MIN 40
#define SPEED_GAUGE_FULL_MAX 500

static const uint8_t GAUGE_DIGITS_46[10][4] = {
  { 0x1E, 0x21, 0x21, 0x1E },
  { 0x00, 0x22, 0x3F, 0x20 },
  { 0x22, 0x31, 0x29, 0x26 },
  { 0x21, 0x25, 0x25, 0x1A },
  { 0x0F, 0x08, 0x08, 0x3F },
  { 0x27, 0x25, 0x25, 0x19 },
  { 0x1E, 0x25, 0x25, 0x18 },
  { 0x01, 0x31, 0x0D, 0x03 },
  { 0x1A, 0x25, 0x25, 0x1A },
  { 0x06, 0x29, 0x29, 0x1E }
};
static const uint8_t GAUGE_DIGITS_45[10][4] = {
  { 0x0E, 0x11, 0x11, 0x0E },
  { 0x00, 0x12, 0x1F, 0x10 },
  { 0x19, 0x15, 0x15, 0x12 },
  { 0x11, 0x15, 0x15, 0x0A },
  { 0x07, 0x04, 0x04, 0x1F },
  { 0x17, 0x15, 0x15, 0x09 },
  { 0x0E, 0x15, 0x15, 0x08 },
  { 0x01, 0x19, 0x05, 0x03 },
  { 0x0A, 0x15, 0x15, 0x0A },
  { 0x02, 0x15, 0x15, 0x0E }
};
static const uint8_t GAUGE_UNIT_K[4] = { 0x1F, 0x04, 0x0A, 0x11 };
static const uint8_t GAUGE_UNIT_M[5] = { 0x1F, 0x02, 0x04, 0x02, 0x1F };
static const uint8_t GAUGE_UNIT_P[3] = { 0x1F, 0x05, 0x07 };
static const uint8_t GAUGE_UNIT_H[3] = { 0x1F, 0x04, 0x1F };
static const int8_t GAUGE_SWEEP[][2] = { {0,7}, {0,6}, {1,5}, {1,4}, {2,3}, {3,2}, {4,1}, {5,1}, {6,0} };  // then row 0, columns 7-31
static const int8_t GAUGE_DIAL_TICKS[9][2] = { {0,7}, {1,4}, {2,2}, {4,1}, {7,0}, {10,1}, {12,2}, {13,4}, {14,7} };
static const int8_t GAUGE_NEEDLE[33][2] = { {1,7}, {1,6}, {1,6}, {1,5}, {1,5}, {2,4}, {2,4}, {2,3}, {3,3}, {3,2}, {4,2}, {4,2}, {5,1}, {5,1}, {6,1}, {6,1}, {7,1}, {8,1}, {8,1}, {9,1}, {9,1}, {10,2}, {10,2}, {11,2}, {11,3}, {12,3}, {12,4}, {12,4}, {13,5}, {13,5}, {13,6}, {13,6}, {13,7} };

// Which digits a gauge draws its number in. Plain defines rather than an enum:
// the Arduino builder declares every function at the top of the sketch, above
// any type declared here.
#define GAUGE_DIGITS_ARC  0   // 4x6
#define GAUGE_DIGITS_BAR  1   // 4x5
#define GAUGE_DIGITS_DIAL 2   // Tiko, 3x7
#define GAUGE_KM_WIDTH  10   // K, a column, M
#define GAUGE_MPH_WIDTH 13   // M, P, H with a column between each

// How many of `steps` a speed fills on a scale ending at `full` km/h, rounded half up.
static int gaugePart(int kmh, int full, int steps) {
  if (full < 1) full = 1;
  int k = kmh < 0 ? 0 : (kmh > full ? full : kmh);
  return (steps * k * 2 + full) / (2 * full);
}

static void gaugeSet(uint8_t* cols, int x, int y) {
  if (x >= 0 && x < 32 && y >= 0 && y < 8) cols[x] |= (uint8_t)(1u << y);
}

static int gaugeGlyph(uint8_t* cols, const uint8_t* g, int w, int x, int shift) {
  for (int i = 0; i < w; i++)
    if (x + i >= 0 && x + i < 32) cols[x + i] |= (uint8_t)(g[i] << shift);
  return w;
}

// The digits of `value`, a column apart, ending just before column `right`.
static void gaugeNumber(uint8_t* cols, int value, int right, int shift, uint8_t face) {
  int digits[3], nd = 0;
  if (value >= 100) digits[nd++] = value / 100;
  if (value >= 10)  digits[nd++] = (value / 10) % 10;
  digits[nd++] = value % 10;
  const uint8_t* g[3];
  int w[3];
  uint8_t tmp[3][TIKO_MAX_COLS];
  int width = nd - 1;
  for (int i = 0; i < nd; i++) {
    if (face == GAUGE_DIGITS_ARC)      { g[i] = GAUGE_DIGITS_46[digits[i]]; w[i] = 4; }
    else if (face == GAUGE_DIGITS_BAR) { g[i] = GAUGE_DIGITS_45[digits[i]]; w[i] = 4; }
    else g[i] = digitGlyph(FONT_MODE_TIKO, digits[i], tmp[i], w[i]);
    width += w[i];
  }
  int x = right - width;
  for (int i = 0; i < nd; i++) x += gaugeGlyph(cols, g[i], w[i], x, shift) + 1;
}

// "KM", or "MPH", from column x.
static void gaugeUnit(uint8_t* cols, bool mph, int x, int shift) {
  if (mph) {
    x += gaugeGlyph(cols, GAUGE_UNIT_M, 5, x, shift) + 1;
    x += gaugeGlyph(cols, GAUGE_UNIT_P, 3, x, shift) + 1;
    gaugeGlyph(cols, GAUGE_UNIT_H, 3, x, shift);
    return;
  }
  x += gaugeGlyph(cols, GAUGE_UNIT_K, 4, x, shift) + 1;
  gaugeGlyph(cols, GAUGE_UNIT_M, 5, x, shift);
}

// Draws a gauge into 32 columns (bit r = row r, row 0 at the top).
static void speedGaugeFrame(uint8_t* cols, int kmh, bool mph, uint8_t style, int full, int shown = -1) {
  memset(cols, 0, 32);
  int value = shown >= 0 ? (shown > 999 ? 999 : shown) : speedShown(kmh, mph);
  if (style == SPEED_GAUGE_ARC) {
    // Up the curve, two columns thick, then along the top row. What the
    // speed hasn't reached shows only its quarter marks and its end.
    const int curve = sizeof(GAUGE_SWEEP) / sizeof(GAUGE_SWEEP[0]);
    const int len = curve + 25;
    int lit = gaugePart(kmh, full, len);
    for (int i = 0; i < len; i++) {
      int x = i < curve ? GAUGE_SWEEP[i][0] : 7 + (i - curve);
      int y = i < curve ? GAUGE_SWEEP[i][1] : 0;
      if (i < lit) {
        gaugeSet(cols, x, y);
        if (i < curve && y > 0) gaugeSet(cols, x + 1, y);
      } else if (i == (len + 2) / 4 - 1 || i == (len * 2 + 2) / 4 - 1 ||
                 i == (len * 3 + 2) / 4 - 1 || i == len - 1) {
        gaugeSet(cols, x, y);
      }
    }
    gaugeSet(cols, 0, 7);
    // "MPH" doesn't fit beside three digits and the curve: in mph the number stands alone.
    if (mph) {
      gaugeNumber(cols, value, 31, 2, GAUGE_DIGITS_ARC);
    } else {
      gaugeNumber(cols, value, 31 - GAUGE_KM_WIDTH - 1, 2, GAUGE_DIGITS_ARC);
      gaugeUnit(cols, false, 32 - GAUGE_KM_WIDTH, 3);
    }
  } else if (style == SPEED_GAUGE_DIAL) {
    // A half dial on the bottom edge, its needle from the hub at the middle.
    for (int i = 0; i < 9; i++) gaugeSet(cols, GAUGE_DIAL_TICKS[i][0], GAUGE_DIAL_TICKS[i][1]);
    int step = gaugePart(kmh, full, 32);
    int x0 = 7, y0 = 7, x1 = GAUGE_NEEDLE[step][0], y1 = GAUGE_NEEDLE[step][1];
    int dx = abs(x1 - x0), dy = -abs(y1 - y0);
    int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
    int err = dx + dy;
    while (true) {
      gaugeSet(cols, x0, y0);
      if (x0 == x1 && y0 == y1) break;
      int e2 = 2 * err;
      if (e2 >= dy) { err += dy; x0 += sx; }
      if (e2 <= dx) { err += dx; y0 += sy; }
    }
    gaugeSet(cols, 6, 7);
    gaugeSet(cols, 8, 7);
    gaugeNumber(cols, value, 31, 0, GAUGE_DIGITS_DIAL);
  } else if (style == SPEED_GAUGE_BAR) {
    // The number and its unit centred on top, sixteen segments along the bottom.
    int unitWidth = mph ? GAUGE_MPH_WIDTH : GAUGE_KM_WIDTH;
    int x0 = (32 - (14 + 2 + unitWidth)) / 2;
    gaugeNumber(cols, value, x0 + 14, 0, GAUGE_DIGITS_BAR);
    gaugeUnit(cols, mph, x0 + 16, 0);
    int lit = gaugePart(kmh, full, 16);
    for (int i = 0; i < 16; i++) {
      gaugeSet(cols, 2 * i, 7);
      if (i < lit) gaugeSet(cols, 2 * i, 6);
    }
  }
}

// The columns a speed tile shows: its gauge across the panel, or the plain
// number, centred by speedDraw.
static void speedBuildFrame(uint8_t* buf, int& count, int kmh, bool mph, uint8_t fontMode,
                            uint8_t gauge, int full, int shown = -1) {
  if (gauge == SPEED_GAUGE_PLAIN || gauge >= SPEED_GAUGE_COUNT) {
    speedBuildBuffer(buf, count, kmh, mph, fontMode, shown);
    return;
  }
  speedGaugeFrame(buf, kmh, mph, gauge, full, shown);
  count = 32;
}

void ets2BuildBuffer(int speed) {
  speedBuildFrame(ets2ColBuf, ets2ColCount, speed, ets2SpeedMph, fontTypeEts2, ets2Gauge, ets2GaugeFull);
}

void ets2DrawAtPos() {
  speedDraw(ets2ColBuf, ets2ColCount);
}

void ets2Init() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();

  ets2BuildBuffer(ets2Speed);
  ets2DrawAtPos();
  ets2LastSpeed = ets2Speed;
}

bool ets2Tick() {
  if (!ets2Active) return false;

  if (millis() - lastEts2Ms > ETS2_TIMEOUT_MS) {
    ets2Active = false;
    beginP2CCapture();
    if (!ensureCircuitSlot()) {
      finishP2CTransition();
      return false;
    }
    CycleItem& cur = items[currentSlot];
    if (cur.id == ITEM_NOW_PLAYING) npInit();
    else if (cur.id == ITEM_WEATHER) weatherInit();
    else if (cur.id == ITEM_MEMENTO) mementoInit();
    else if (cur.id == ITEM_COUNTDOWN) countdownInit();
    else if (cur.id == ITEM_CANVAS) drawCanvas();
    else if (cur.id == ITEM_CURRENCY) currencyInit();
    else if (socialIndexForItem(cur.id) >= 0) socialInit(cur.id);
    else { gLastStaticDrawMs = 0; gStaticDrawDone = false; }
    finishP2CTransition();
    return false;
  }

  if (ets2Speed != ets2LastSpeed || strcmp(gLivePrio, "ets2") != 0) {
    ets2Init();
  }
  return true;
}

static int writeDigit(int d, int col) {
  writeChar(FONT[d], col); col += 5;
  writeGap(col);           col += 1;
  return col;
}

static int writeDigitFont(int d, int col, uint8_t fontMode) {
  uint8_t tmp[TIKO_MAX_COLS]; int w;
  const uint8_t* g = digitGlyph(fontMode, d, tmp, w);
  writeCharN(g, w, col); col += w;
  writeGap(col);         col += 1;
  return col;
}

static void dateAddGlyph(const uint8_t* glyph, int width = 5) {
  for (int i = 0; i < width && dateColCount < 126; i++)
    dateColBuf[dateColCount++] = glyph[i];
  if (dateColCount < 127) dateColBuf[dateColCount++] = 0x00;
}

static void dateAddNumber(int value, int digits) {
  int divisor = 1;
  for (int i = 1; i < digits; i++) divisor *= 10;
  for (int i = 0; i < digits; i++) {
    int d = (value / divisor) % 10;
    uint8_t dtmp[TIKO_MAX_COLS]; int dw;
    dateAddGlyph(digitGlyph(fontTypeDate, d, dtmp, dw), dw);
    divisor /= 10;
  }
}

const char* const WEEKDAY_NAMES_EN[7] = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
const char* const WEEKDAY_NAMES_RO[7] = { "Dum", "Lun", "Mar", "Mie", "Joi", "Vin", "Sam" };

static void dateAddChar(char ch) {
  if (isPixelFont(fontTypeDate)) {
    uint8_t tmp[TIKO_MAX_COLS];
    uint8_t n = pixGetChar(fontTypeDate, ch, sizeof(tmp), tmp);
    if (n == 0) return;
    for (int i = 0; i < n && dateColCount < 126; i++) dateColBuf[dateColCount++] = tmp[i];
    if (dateColCount < 127) dateColBuf[dateColCount++] = 0x00;
    return;
  }
  if (ch >= '0' && ch <= '9') {
    dateAddGlyph(FONT[ch - '0']);
  } else {
    uint8_t tmp[8];
    uint8_t n = mx.getChar(ch, sizeof(tmp), tmp);
    if (n == 0) return;
    for (int i = 0; i < n && dateColCount < 126; i++) dateColBuf[dateColCount++] = tmp[i];
    if (dateColCount < 127) dateColBuf[dateColCount++] = 0x00;
  }
}

static void dateAddText(const char* s) {
  for (int i = 0; s[i] != '\0'; i++) dateAddChar(s[i]);
}

const char* const WEEKDAY_FULL_EN[7] = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
const char* const WEEKDAY_FULL_RO[7] = { "Duminica", "Luni", "Marti", "Miercuri", "Joi", "Vineri", "Sambata" };

// Validates a custom date format pattern made of Y/M/D/W runs and literal '/' characters.
// Rules: Y run must be exactly 2 or 4 chars; M and D runs must be exactly 2 chars;
// W runs accept any length; any other character is invalid.
static bool isDateFmtLiteralChar(char c) {
  return c == '/' || c == '.' || c == '(' || c == ')' || c == ' ';
}

static bool dateValidateCustomFormat(const char* pattern, String* errOut = nullptr) {
  int len = strlen(pattern);
  if (len == 0 || len > 20) {
    if (errOut) *errOut = "Format invalid";
    return false;
  }
  bool hasContent = false;
  int i = 0;
  while (i < len) {
    char c = toupper(pattern[i]);
    if (c != 'Y' && c != 'M' && c != 'D' && c != 'W' && !isDateFmtLiteralChar(pattern[i])) {
      if (errOut) *errOut = "Caracter invalid";
      return false;
    }
    int j = i;
    while (j < len && toupper(pattern[j]) == c) j++;
    int count = j - i;
    if (c == 'Y') {
      hasContent = true;
      if (count != 2 && count != 4) {
        if (errOut) *errOut = "Y trebuie sa fie YY sau YYYY";
        return false;
      }
    } else if (c == 'M') {
      hasContent = true;
      if (count != 2) {
        if (errOut) *errOut = "M trebuie sa fie MM";
        return false;
      }
    } else if (c == 'D') {
      hasContent = true;
      if (count != 2) {
        if (errOut) *errOut = "D trebuie sa fie DD";
        return false;
      }
    } else if (c == 'W') {
      hasContent = true;
    }
    i = j;
  }
  if (!hasContent) {
    if (errOut) *errOut = "Formatul trebuie sa contina cel putin Y, M, D sau W";
    return false;
  }
  return true;
}

// Renders a validated custom pattern into the date scroll buffer.
static void dateRenderCustom(const char* pattern, int day, int month, int year, int wday) {
  int len = strlen(pattern);
  int i = 0;
  if (wday < 0 || wday > 6) wday = 0;
  while (i < len) {
    char c = toupper(pattern[i]);
    int j = i;
    while (j < len && toupper(pattern[j]) == c) j++;
    int count = j - i;
    if (c == 'Y') {
      if (count == 2) dateAddNumber(year % 100, 2);
      else dateAddNumber(year, 4);
    } else if (c == 'M') {
      dateAddNumber(month, 2);
    } else if (c == 'D') {
      dateAddNumber(day, 2);
    } else if (c == 'W') {
      const char* full = (dateLang == 1) ? WEEKDAY_FULL_RO[wday] : WEEKDAY_FULL_EN[wday];
      int flen = strlen(full);
      if (count >= flen) {
        dateAddText(full);
      } else {
        char buf[16];
        int n = (count < (int)sizeof(buf) - 1) ? count : (int)sizeof(buf) - 1;
        memcpy(buf, full, n);
        buf[n] = '\0';
        dateAddText(buf);
      }
    } else if (isDateFmtLiteralChar(pattern[i])) {
      for (int k = 0; k < count; k++) dateAddChar(pattern[i + k]);
    }
    i = j;
  }
}

void dateBuildBuffer(int day, int month, int year, int wday) {
  dateColCount = 0;
  if (scrollIconInBuffer(scrollTypeDate) && !hideIconDate) {
    scrollBufPrependIcon(dateColBuf, dateColCount, 127, resolveTileIcon(iconSelDate, dateIcon), DATE_ICON_COLS);
  }
  int yy = year % 100;

  switch (dateFormat) {
    case 0:
      dateAddNumber(year, 4);
      dateAddDot();
      dateAddNumber(month, 2);
      dateAddDot();
      dateAddNumber(day, 2);
      break;
    case 1:
      dateAddNumber(yy, 2);
      dateAddDot();
      dateAddNumber(month, 2);
      dateAddDot();
      dateAddNumber(day, 2);
      break;
    case 3:
      dateAddNumber(day, 2);
      dateAddDot();
      dateAddNumber(month, 2);
      break;
    case 4: {
      if (wday < 0 || wday > 6) wday = 0;
      const char* const* names = (dateLang == 1) ? WEEKDAY_NAMES_RO : WEEKDAY_NAMES_EN;
      dateAddText(names[wday]);
      dateAddChar(' ');
      dateAddNumber(day, 2);
      break;
    }
    case 5:
      dateRenderCustom(customDateFmt, day, month, year, wday);
      break;
    default:
      dateAddNumber(day, 2);
      dateAddDot();
      dateAddNumber(month, 2);
      dateAddDot();
      dateAddNumber(year, 4);
      break;
  }

  if (dateColCount > 0) dateColCount--;
}

void dateDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);

  if (!hideIconDate && !scrollIconInBuffer(scrollTypeDate)) {

    // Resolved once rather than 64 times inside the loop below.
    const uint8_t* dateIconBmp = resolveTileIcon(iconSelDate, dateIcon);
    for (int col = 0; col < DATE_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (dateIconBmp[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - DATE_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconDate || scrollIconInBuffer(scrollTypeDate);
  int textOffset = fullWidth ? 0 : (DATE_ICON_COLS + DATE_SEP_COLS);
  int textCols   = fullWidth ? 32 : DATE_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < dateColCount) ? dateColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }

  mxCommit();
}

// Returns whether it actually put something on the panel. The caller has
// already cleared the display by the time this runs, so "did not draw" and
// "left the panel blank" are the same thing, and the caller has to know.
bool dateInit() {
  struct tm ti;
  if (!readLocalTime(ti)) return false;
  int day   = ti.tm_mday;
  int month = ti.tm_mon + 1;
  int year  = ti.tm_year + 1900;
  int wday  = ti.tm_wday;

  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();

  dateBuildBuffer(day, month, year, wday);
  int dateEffTextCols = (hideIconDate || scrollIconInBuffer(scrollTypeDate)) ? 32 : DATE_TEXT_COLS;
  dateNeedsScroll = (dateColCount > dateEffTextCols);

  if (dateNeedsScroll) {
    dateScrollPos = 0;
    dateState     = NP_PAUSE_BEFORE_RIGHT;
    datePauseMs   = millis();
    dateDrawAtPos(0);
  } else {

    int offset = (dateEffTextCols - dateColCount) / 2;
    if (offset < 0) offset = 0;
    dateDrawAtPos(-offset);
  }
  return true;
}

void drawDate() {
  dateInit();
}

void drawHour() {
  struct tm ti;
  if (!readLocalTime(ti)) return;
  int h     = ti.tm_hour;
  int m     = ti.tm_min;
  int s     = ti.tm_sec;
  bool show = (s % 2 == 0);

  bool is12 = (hourFormat == 1);
  bool pm   = false;
  if (is12) {
    pm = (h >= 12);
    h  = h % 12;
    if (h == 0) h = 12;
  }

  mx.update(MD_MAX72XX::OFF);
  for (int i = 0; i < 32; i++) mx.setColumn(i, 0x00);

  // A leading zero only ever adds the tens digit; the centring below already
  // works off twoDigitH, so nothing else has to change.
  bool twoDigitH = (h >= 10) || hourLeadingZero;
  bool tiko = isPixelFont(fontType);   // any font of ours
  // Every font keeps its digits one width, so the width of '0' is the width
  // of all of them: Marymba, Bold and Italic 5, Mako and the segment fonts 4,
  // Tiko 3. The centring below needs it.
  uint8_t w0tmp[TIKO_MAX_COLS]; int digitW;
  digitGlyph(fontType, 0, w0tmp, digitW);

  uint8_t colonTmp[TIKO_MAX_COLS];
  const uint8_t* colonGlyph = CHAR_COLON;
  int colonW = 2;
  if (tiko) { colonW = pixGetChar(fontType, ':', TIKO_MAX_COLS, colonTmp); colonGlyph = colonTmp; }

  int totalWidth = (twoDigitH ? 2 : 1) * (digitW + 1) + (colonW + 1) + 2 * digitW + 1;
  int col = (32 - totalWidth) / 2;

  if (twoDigitH) {
    col = writeDigitFont(h / 10, col, fontType);
  }
  col = writeDigitFont(h % 10, col, fontType);
  if (show) {
    writeCharN(colonGlyph, colonW, col); col += colonW; writeGap(col); col += 1;
  } else {
    for (int i = 0; i < colonW + 1; i++) { mx.setColumn(31 - col, 0x00); col++; }
  }
  col = writeDigitFont(m / 10, col, fontType);
  // Two statements: in one call, dw could be read before digitGlyph() sets it
  // (C++ leaves the order of a call's arguments open).
  uint8_t dtmp[TIKO_MAX_COLS]; int dw;
  const uint8_t* lastDigit = digitGlyph(fontType, m % 10, dtmp, dw);
  writeCharN(lastDigit, dw, col);

  mxCommit();
}

// Watchface: Compact with one selected information source, or Squarish with
// seconds. Compact's weekday or temperature occupies 13 columns.
static int hwPutCols(uint8_t* fb, int col, const uint8_t* g, int n, int shift) {
  for (int i = 0; i < n; i++, col++)
    if (col >= 0 && col < 32) fb[col] |= (uint8_t)(g[i] << shift);
  return col;
}

// Seconds skin: 5x7 square hours/minutes and top-aligned 3x5 seconds.
// Its fixed columns fill all 32 pixels: HH at 0/6, MM at 13/19, SS at 25/29.
// The two-column hour/minute gap replaces the colon in this skin only.
static const uint8_t HW_SQUARE_DIGITS[10][5] = {
  {0x7F, 0x41, 0x41, 0x41, 0x7F},
  {0x00, 0x00, 0x7F, 0x00, 0x00},
  {0x79, 0x49, 0x49, 0x49, 0x4F},
  {0x49, 0x49, 0x49, 0x49, 0x7F},
  {0x0F, 0x08, 0x08, 0x08, 0x7F},
  {0x4F, 0x49, 0x49, 0x49, 0x79},
  {0x7F, 0x49, 0x49, 0x49, 0x79},
  {0x01, 0x01, 0x01, 0x01, 0x7F},
  {0x7F, 0x49, 0x49, 0x49, 0x7F},
  {0x4F, 0x49, 0x49, 0x49, 0x7F},
};
static const uint8_t HW_SQUARE_SECONDS[10][3] = {
  {0x1F, 0x11, 0x1F}, {0x00, 0x1F, 0x00},
  {0x1D, 0x15, 0x17}, {0x15, 0x15, 0x1F},
  {0x07, 0x04, 0x1F}, {0x17, 0x15, 0x1D},
  {0x1F, 0x15, 0x1D}, {0x01, 0x01, 0x1F},
  {0x1F, 0x15, 0x1F}, {0x17, 0x15, 0x1F},
};

void drawHourWeekday() {
  struct tm ti;
  if (!readLocalTime(ti)) return;
  int h = ti.tm_hour;
  if (hwFormat == 1) {
    h = h % 12;
    if (h == 0) h = 12;
  }
  bool twoDigitH = (h >= 10) || hwLeadZero;

  uint8_t fb[32] = { 0 };
  uint8_t g[TIKO_MAX_COLS];

  if (hwSkin == HW_SKIN_SECONDS) {
    if (twoDigitH) hwPutCols(fb, 0, HW_SQUARE_DIGITS[h / 10], 5, 0);
    hwPutCols(fb, 6, HW_SQUARE_DIGITS[h % 10], 5, 0);
    hwPutCols(fb, 13, HW_SQUARE_DIGITS[ti.tm_min / 10], 5, 0);
    hwPutCols(fb, 19, HW_SQUARE_DIGITS[ti.tm_min % 10], 5, 0);
    hwPutCols(fb, 25, HW_SQUARE_SECONDS[ti.tm_sec / 10], 3, 0);
    hwPutCols(fb, 29, HW_SQUARE_SECONDS[ti.tm_sec % 10], 3, 0);
    mx.update(MD_MAX72XX::OFF);
    for (int c = 0; c < 32; c++) mx.setColumn(31 - c, fb[c]);
    mxCommit();
    return;
  }

  // 3-column digits with a gap after each, and a one-column colon instead of
  // Tiko's three-wide one, or "12:45 WED" would not fit. It hugs the day: on
  // the left it ends at column 16, on the right it starts at 15, so the gap
  // between the two is the same both ways.
  const bool swapped = hwSwap;
  int col = swapped ? 15 : 17 - ((twoDigitH ? 7 : 3) + 3 + 7);
  if (twoDigitH) {
    tikoGetChar((char)('0' + h / 10), sizeof(g), g);
    col = hwPutCols(fb, col, g, 3, 0) + 1;
  }
  tikoGetChar((char)('0' + h % 10), sizeof(g), g);
  col = hwPutCols(fb, col, g, 3, 0) + 1;
  if (ti.tm_sec % 2 == 0) fb[col] |= 0x36;
  col += 2;
  tikoGetChar((char)('0' + ti.tm_min / 10), sizeof(g), g);
  col = hwPutCols(fb, col, g, 3, 0) + 1;
  tikoGetChar((char)('0' + ti.tm_min % 10), sizeof(g), g);
  hwPutCols(fb, col, g, 3, 0);

  const bool above = hwBarPos == HW_BAR_ABOVE;
  const int sideX = swapped ? 0 : 19;
  if (hwContent != HW_CONTENT_DAY) {
    // Full-height Tiko temperature, right-aligned in the 13-column side area.
    // A two-digit reading has its digits at 21/25 and the degree ring at 29.
    char digits[8];
    const bool fromWeather = hwContent == HW_CONTENT_WEATHER;
    const float celsius = fromWeather ? weatherTempC : (float)lastTemp;
    const bool valid = fromWeather ? (weatherValid && isfinite(celsius)) : lastTemp != TEMP_NO_READING;
    if (!valid) strcpy(digits, "--");
    else {
      const int value = (int)round(tempUnit == 1 ? celsius * 9.0f / 5.0f + 32.0f : celsius);
      snprintf(digits, sizeof(digits), "%d", value);
    }
    int count = strlen(digits);
    if (count > 3) { strcpy(digits, "--"); count = 2; }
    const int digitWidth = count * 4 - 1;
    // Keep the entire signed/three-digit value when it needs the degree's space.
    const bool showDegree = valid && count <= 2 && strcmp(digits, "--") != 0;
    const int totalWidth = digitWidth + (showDegree ? 4 : 0);
    col = sideX + 13 - totalWidth;
    for (int i = 0; i < count; i++) {
      tikoGetChar(digits[i], sizeof(g), g);
      col = hwPutCols(fb, col, g, 3, 0) + 1;
    }
    if (showDegree) hwPutCols(fb, col, TIKO_DEG, 3, 0);
  } else {
    const int shift = above ? 2 : 0;
    const char* day = dateLang == 1 ? WEEKDAY_NAMES_RO[ti.tm_wday] : WEEKDAY_NAMES_EN[ti.tm_wday];
    int dayWidth = -1;
    for (const char* p = day; *p; p++) dayWidth += microGetChar(*p, g) + 1;
    col = sideX + (dayWidth < 13 ? (13 - dayWidth) / 2 : 0);
    for (const char* p = day; *p; p++) {
      const int width = microGetChar(*p, g);
      col = hwPutCols(fb, col, g, width, shift) + 1;
    }
    const int barEnd = sideX + (hwBarMode == HW_BAR_SECONDS ? ti.tm_sec * 13 / 60 : 12);
    for (int c = sideX; c <= barEnd; c++) fb[c] |= above ? 0x01 : 0x40;
  }

  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(31 - c, fb[c]);
  mxCommit();
}

// NP MANUAL SCROLL
#define NP_SCROLL_SPEED_MS SCROLL_SPEED_DEFAULT

#define NP_DISPLAY_COLS    32

// Room for the whole text in the widest font (6 columns a letter with the
// gap) plus the icon. At 512 anything past ~85 characters was cut off.
#define NP_COL_MAX 1830
static uint8_t  npColBuf[NP_COL_MAX];
static int      npColCount  = 0;
static int      npScrollPos = 0;
static unsigned long npLastScrollMs = 0;


void npBuildBuffer() {

  char cleanBuf[sizeof(nowPlayingBuf)];
  sanitizeUtf8(nowPlayingBuf, cleanBuf, sizeof(cleanBuf));

  npColCount = 0;
  if (scrollIconInBuffer(scrollTypeNowPlaying) && !hideIconNowPlaying) {
    scrollBufPrependIcon(npColBuf, npColCount, NP_COL_MAX, npResolvedIcon(), NP_ICON_COLS);
  }
  for (int ci = 0; cleanBuf[ci] != '\0' && npColCount < NP_COL_MAX - 12; ci++) {
    appendGlyphAuto(npColBuf, npColCount, NP_COL_MAX, cleanBuf[ci], fontTypeNowPlaying);
  }

  if (npColCount > 0) npColCount--;
}

void npDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);

  if (!hideIconNowPlaying && !scrollIconInBuffer(scrollTypeNowPlaying)) {

    // Resolved once rather than 64 times inside the loop below.
    const uint8_t* npIconBmp = npResolvedIcon();
    for (int col = 0; col < NP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (npIconBmp[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }

    mx.setColumn(31 - NP_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconNowPlaying || scrollIconInBuffer(scrollTypeNowPlaying);
  int textOffset = fullWidth ? 0 : (NP_ICON_COLS + NP_SEP_COLS);
  int textCols   = fullWidth ? 32 : NP_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < npColCount) ? npColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }

  mxCommit();
}

void npInit() {

  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < NP_DISPLAY_COLS; c++) mx.setColumn(c, 0x00);
  mxCommit();
  npBuildBuffer();
  npScrollPos = 0;
  npWrapPass = 0;
  npDrawAtPos(0);
  npState = NP_PAUSE_BEFORE_RIGHT;
  npPauseStartMs = millis();
}

// STOPWATCH DISPLAY

static uint8_t  swColBuf[160];
static int      swColCount     = 0;
static int      swScrollPos    = 0;
static int      swWrapPass     = 0;
static unsigned long swLastScrollMs  = 0;
static unsigned long swLastRedrawMs  = 0;
NpState       swState          = NP_SHOW_START;
unsigned long swPauseStartMs   = 0;

void swBuildBuffer() {
  char txt[16];
  swFormatAdaptive(txt, sizeof(txt), swGetElapsedMs());
  swColCount = 0;
  for (int ci = 0; txt[ci] != '\0' && swColCount < 150; ci++) {
    appendGlyphAuto(swColBuf, swColCount, 160, txt[ci], fontTypeStopwatch);
  }
  if (swColCount > 0) swColCount--;
}

void swDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  for (int col = 0; col < 32; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < swColCount) ? swColBuf[src] : 0x00;
    mx.setColumn(31 - col, val);
  }
  mxCommit();
}

void swInit() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();
  swBuildBuffer();
  swScrollPos = 0;
  swLastRedrawMs = millis();
  if (swColCount <= 32) {

    swDrawAtPos(-((32 - swColCount) / 2));
    swState = NP_SHOW_START;
  } else {

    swDrawAtPos(0);
    swState = NP_PAUSE_BEFORE_RIGHT;
  }
  swPauseStartMs = millis();
}

bool swPriorityTick() {
  if (!swRunning) {
    if (swWasActive) {
      swWasActive = false;
      resumeCircuitTileFromPriority();
    }
    return false;
  }

  if (!swWasActive) {
    swWasActive = true;
    beginC2PCapture();
    swInit();
    finishC2PTransition(tileTransSw, tileTransSpd[TRSPD_SW]);
    return true;
  }

  unsigned long now = millis();

  if (swColCount <= 32) {
    if (now - swLastRedrawMs >= 1000) {
      swLastRedrawMs = now;
      swBuildBuffer();
      swDrawAtPos(-((32 - swColCount) / 2));
    }
    return true;
  }

  switch (swState) {
    case NP_PAUSE_BEFORE_RIGHT:
      if (now - swPauseStartMs >= NP_PAUSE_MS) {
        if (scrollIsWrap(scrollTypeStopwatch)) {
          swWrapPass = 0;
          swScrollPos = 0;
          swState = NP_SCROLL_WRAP;
        } else {
          swState = NP_SCROLL_RIGHT;
        }
        swLastScrollMs = now;
      }
      break;

    case NP_SCROLL_WRAP: {
      if (now - swLastScrollMs >= spd(SPD_STOPWATCH)) {
        swLastScrollMs = now;
        swScrollPos++;
        if (swScrollPos >= swColCount) {
          swWrapPass++;
          if (swWrapPass >= SCROLL_WRAP_PASSES) {

            swInit();
            break;
          }
          swScrollPos = -32;
        }
        swDrawAtPos(swScrollPos);
      }
      break;
    }

    case NP_SCROLL_RIGHT: {
      int maxPos = swColCount - 32;
      if (maxPos <= 0) {
        swState = NP_PAUSE_SHORT;
        swPauseStartMs = now;
        break;
      }
      if (now - swLastScrollMs >= spd(SPD_STOPWATCH)) {
        swLastScrollMs = now;
        swScrollPos++;
        swDrawAtPos(swScrollPos);
        if (swScrollPos >= maxPos) {
          swScrollPos = maxPos;
          swState = NP_PAUSE_AFTER_RIGHT;
          swPauseStartMs = now;
        }
      }
      break;
    }

    case NP_PAUSE_AFTER_RIGHT:
      if (now - swPauseStartMs >= NP_PAUSE_MS) {
        swState = NP_SCROLL_LEFT;
        swLastScrollMs = now;
      }
      break;

    case NP_SCROLL_LEFT:
      if (now - swLastScrollMs >= spd(SPD_STOPWATCH)) {
        swLastScrollMs = now;
        swScrollPos--;
        if (swScrollPos <= 0) {
          swScrollPos = 0;
          swDrawAtPos(0);
          swState = NP_PAUSE_BEFORE_NEXT;
          swPauseStartMs = now;
        } else {
          swDrawAtPos(swScrollPos);
        }
      }
      break;

    case NP_PAUSE_BEFORE_NEXT:
      if (now - swPauseStartMs >= NP_PAUSE_MS) {

        swInit();
      }
      break;

    case NP_PAUSE_SHORT:
      if (now - swPauseStartMs >= 4000UL) {
        swInit();
      }
      break;

    default: break;
  }

  return true;
}

// TIMER DISPLAY 

static uint8_t  timerColBuf[160];
static int      timerColCount     = 0;
static int      timerScrollPos    = 0;
static int      timerWrapPass     = 0;
static unsigned long timerLastScrollMs  = 0;
static unsigned long timerLastRedrawMs  = 0;
NpState       timerState          = NP_SHOW_START;
unsigned long timerPauseStartMs   = 0;

void timerBuildBuffer() {
  char txt[16];
  timerFormatText(txt, sizeof(txt));
  timerColCount = 0;
  for (int ci = 0; txt[ci] != '\0' && timerColCount < 150; ci++) {
    appendGlyphAuto(timerColBuf, timerColCount, 160, txt[ci], fontTypeTimer);
  }
  if (timerColCount > 0) timerColCount--;
}

void timerDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  for (int col = 0; col < 32; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < timerColCount) ? timerColBuf[src] : 0x00;
    mx.setColumn(31 - col, val);
  }
  mxCommit();
}

void timerBlankScreen() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();
}

void timerInit() {
  timerBlankScreen();
  timerBuildBuffer();
  timerScrollPos = 0;
  timerLastRedrawMs = millis();
  if (timerColCount <= 32) {
    timerDrawAtPos(-((32 - timerColCount) / 2));
    timerState = NP_SHOW_START;
  } else {
    timerDrawAtPos(0);
    timerState = NP_PAUSE_BEFORE_RIGHT;
  }
  timerPauseStartMs = millis();
}

void timerTickRender() {
  unsigned long now = millis();
  if (timerColCount <= 32) {
    if (now - timerLastRedrawMs >= 1000) {
      timerLastRedrawMs = now;
      timerBuildBuffer();
      timerDrawAtPos(-((32 - timerColCount) / 2));
    }
    return;
  }
  switch (timerState) {
    case NP_PAUSE_BEFORE_RIGHT:
      if (now - timerPauseStartMs >= NP_PAUSE_MS) {
        if (scrollIsWrap(scrollTypeTimer)) {
          timerWrapPass = 0;
          timerScrollPos = 0;
          timerState = NP_SCROLL_WRAP;
        } else {
          timerState = NP_SCROLL_RIGHT;
        }
        timerLastScrollMs = now;
      }
      break;
    case NP_SCROLL_WRAP:
      if (now - timerLastScrollMs >= spd(SPD_TIMER)) {
        timerLastScrollMs = now;
        timerScrollPos++;
        if (timerScrollPos >= timerColCount) {
          timerWrapPass++;
          if (timerWrapPass >= SCROLL_WRAP_PASSES) { timerInit(); break; }
          timerScrollPos = -32;
        }
        timerDrawAtPos(timerScrollPos);
      }
      break;
    case NP_SCROLL_RIGHT: {
      int maxPos = timerColCount - 32;
      if (maxPos <= 0) { timerState = NP_PAUSE_SHORT; timerPauseStartMs = now; break; }
      if (now - timerLastScrollMs >= spd(SPD_TIMER)) {
        timerLastScrollMs = now;
        timerScrollPos++;
        timerDrawAtPos(timerScrollPos);
        if (timerScrollPos >= maxPos) {
          timerScrollPos = maxPos;
          timerState = NP_PAUSE_AFTER_RIGHT;
          timerPauseStartMs = now;
        }
      }
      break;
    }
    case NP_PAUSE_AFTER_RIGHT:
      if (now - timerPauseStartMs >= NP_PAUSE_MS) { timerState = NP_SCROLL_LEFT; timerLastScrollMs = now; }
      break;
    case NP_SCROLL_LEFT:
      if (now - timerLastScrollMs >= spd(SPD_TIMER)) {
        timerLastScrollMs = now;
        timerScrollPos--;
        if (timerScrollPos <= 0) {
          timerScrollPos = 0;
          timerDrawAtPos(0);
          timerState = NP_PAUSE_BEFORE_NEXT;
          timerPauseStartMs = now;
        } else {
          timerDrawAtPos(timerScrollPos);
        }
      }
      break;
    case NP_PAUSE_BEFORE_NEXT:
      if (now - timerPauseStartMs >= NP_PAUSE_MS) timerInit();
      break;
    case NP_PAUSE_SHORT:
      if (now - timerPauseStartMs >= 4000UL) timerInit();
      break;
    default: break;
  }
}

void timerBlinkTick() {
  unsigned long now = millis();
  if (now - timerBlinkLastMs >= 500) {
    timerBlinkLastMs = now;
    timerBlinkOn = !timerBlinkOn;
    if (timerBlinkOn) {
      timerBuildBuffer();
      timerDrawAtPos(-((32 - timerColCount) / 2));
    } else {
      timerBlankScreen();
    }
  }
}

bool timerPriorityTick() {
  if (!timerRunning && !timerFinished) {
    if (timerWasActive) {
      timerWasActive = false;
      resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!timerWasActive) {
    timerWasActive = true;
    beginC2PCapture();
    timerInit();
    finishC2PTransition(tileTransTmr, tileTransSpd[TRSPD_TMR]);
    return true;
  }
  if (timerFinished) {
    timerBlinkTick();
    return true;
  }
  timerCheckExpiry();
  if (timerFinished) {

    timerBlinkOn = true;
    timerBlinkLastMs = millis();
    timerBuildBuffer();
    timerDrawAtPos(-((32 - timerColCount) / 2));
    return true;
  }
  timerTickRender();
  return true;
}

// ALARM DISPLAY
//
// A ringing alarm shows the clock, blinking, so the panel is still telling you
// the time while it asks to be stopped. HH:MM in 24h whatever the hour tile is
// set to, because that is the way the alarm itself was entered. Five glyphs
// never come near 32 columns, so there is nothing here to scroll.

static uint8_t alarmColBuf[80];
static int     alarmColCount     = 0;
bool           alarmBlinkOn      = true;
unsigned long  alarmBlinkLastMs  = 0;
#define ALARM_BLINK_MS 450

void alarmBuildBuffer() {
  char txt[8];
  struct tm ti;
  if (readLocalTime(ti)) {
    snprintf(txt, sizeof(txt), "%02d:%02d", ti.tm_hour, ti.tm_min);
  } else if (alarmRingingIdx >= 0) {
    // No clock to read: show what the alarm was set to, which is the only other
    // honest thing to put there.
    snprintf(txt, sizeof(txt), "%02u:%02u", (unsigned)alarms[alarmRingingIdx].hour,
                                            (unsigned)alarms[alarmRingingIdx].minute);
  } else {
    strcpy(txt, "--:--");
  }
  alarmColCount = 0;
  for (int ci = 0; txt[ci] != '\0' && alarmColCount < 70; ci++) {
    appendGlyphAuto(alarmColBuf, alarmColCount, 80, txt[ci], fontTypeAlarm);
  }
  if (alarmColCount > 0) alarmColCount--;
}

void alarmDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  for (int col = 0; col < 32; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < alarmColCount) ? alarmColBuf[src] : 0x00;
    mx.setColumn(31 - col, val);
  }
  mxCommit();
}

void alarmBlankScreen() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
  mxCommit();
}

void alarmDrawCentered() {
  alarmBuildBuffer();
  alarmDrawAtPos(-((32 - alarmColCount) / 2));
}

void alarmInit() {
  alarmBlankScreen();
  alarmBlinkOn     = true;
  alarmBlinkLastMs = millis();
  alarmDrawCentered();
}

void alarmBlinkTick() {
  unsigned long now = millis();
  if (now - alarmBlinkLastMs < ALARM_BLINK_MS) return;
  alarmBlinkLastMs = now;
  alarmBlinkOn = !alarmBlinkOn;
  // Rebuilt on the way back on rather than once at the start, so the minute
  // rolls over on screen while the alarm is still ringing.
  if (alarmBlinkOn) alarmDrawCentered();
  else              alarmBlankScreen();
}

bool alarmPriorityTick() {
  if (!alarmRinging) {
    if (alarmWasActive) {
      alarmWasActive = false;
      resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!alarmWasActive) {
    alarmWasActive = true;
    beginC2PCapture();
    alarmInit();
    finishC2PTransition(tileTransAlarm, tileTransSpd[TRSPD_ALARM]);
    return true;
  }
  alarmBlinkTick();
  return true;
}

void notifBuildBuffer() {
  char cleanBuf[204];
  sanitizeUtf8(notifBuf, cleanBuf, sizeof(cleanBuf));
  notifColCount = 0;
  if (scrollIconInBuffer(notifScrollType()) && !notifHideIcon()) {
    scrollBufPrependIcon(notifColBuf, notifColCount, NOTIF_COL_MAX,
                         notifIconOverride ? notifIconOverride : resolveTileIcon(iconSelNotif, notifIcon),
                         NP_ICON_COLS);
  }
  for (int ci = 0; cleanBuf[ci] != '\0' && notifColCount < NOTIF_COL_MAX - 12; ci++) {
    appendGlyphAuto(notifColBuf, notifColCount, NOTIF_COL_MAX, cleanBuf[ci], notifFontType());
  }
  if (notifColCount > 0) notifColCount--;
}
void notifDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  if (!notifHideIcon() && !scrollIconInBuffer(notifScrollType())) {

    const uint8_t* nIcon = notifIconOverride ? notifIconOverride
                                             : resolveTileIcon(iconSelNotif, notifIcon);
    for (int col = 0; col < NP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (nIcon[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - NP_ICON_COLS, 0x00);
  }

  bool fullWidth = notifHideIcon() || scrollIconInBuffer(notifScrollType());
  int textOffset = fullWidth ? 0 : (NP_ICON_COLS + NP_SEP_COLS);
  int textCols   = fullWidth ? 32 : NP_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < notifColCount) ? notifColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }
  mxCommit();
}

void notifInit() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < NP_DISPLAY_COLS; c++) mx.setColumn(c, 0x00);
  mxCommit();
  notifBuildBuffer();
  notifScrollPos = 0;
  notifWrapPass = 0;
  notifDrawAtPos(0);
  notifState    = NP_PAUSE_BEFORE_RIGHT;
  notifPauseMs  = millis();
}

// MEMENTO DISPLAY
void mementoBuildBuffer() {
  char cleanBuf[sizeof(mementoBuf)];
  sanitizeUtf8(mementoBuf, cleanBuf, sizeof(cleanBuf));
  mementoColCount = 0;
  if (scrollIconInBuffer(scrollTypeReminder) && !hideIconReminder) {
    scrollBufPrependIcon(mementoColBuf, mementoColCount, MEMENTO_COL_MAX, resolveTileIcon(iconSelReminder, mementoIcon), NP_ICON_COLS);
  }
  for (int ci = 0; cleanBuf[ci] != '\0' && mementoColCount < MEMENTO_COL_MAX - 12; ci++) {
    appendGlyphAuto(mementoColBuf, mementoColCount, MEMENTO_COL_MAX, cleanBuf[ci], fontTypeReminder);
  }
  if (mementoColCount > 0) mementoColCount--;
}

void mementoDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  if (!hideIconReminder && !scrollIconInBuffer(scrollTypeReminder)) {

    // Resolved once rather than 64 times inside the loop below.
    const uint8_t* memIconBmp = resolveTileIcon(iconSelReminder, mementoIcon);
    for (int col = 0; col < NP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (memIconBmp[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - NP_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconReminder || scrollIconInBuffer(scrollTypeReminder);
  int textOffset = fullWidth ? 0 : (NP_ICON_COLS + NP_SEP_COLS);
  int textCols   = fullWidth ? 32 : NP_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < mementoColCount) ? mementoColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }
  mxCommit();
}

void mementoInit() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < NP_DISPLAY_COLS; c++) mx.setColumn(c, 0x00);
  mxCommit();
  mementoBuildBuffer();
  mementoScrollPos = 0;
  mementoWrapPass = 0;
  mementoDrawAtPos(0);
  mementoState   = NP_PAUSE_BEFORE_RIGHT;
  mementoPauseMs = millis();
}

// COUNTDOWN TILE
//
// The days left to the nearest of up to five events - "Craciun 89z", and on
// the day itself "Craciun azi". An event is a name, a date and whether it
// comes back every year (a birthday, Christmas); one that doesn't is gone once
// its day has passed, and with no event ahead the tile sits its turn out, like
// an empty Memento. The five are one NVS blob, "cdEvents".
//
// The words are in the tile's own language (cdLang, in the Language Manager;
// a clock updated from before it had one starts with the web interface's), and
// the unit is a letter - "89z", "89d" - or written out (cdFullUnit): "89 de
// zile", "89 days".
CountdownEvent cdEvents[CD_MAX_EVENTS];
bool    cdFullUnit = false;
uint8_t cdLang     = 0;     // 0 = Romanian, 1 = English

#define CD_COL_MAX 240
static uint8_t  cdColBuf[CD_COL_MAX];
static int      cdColCount = 0;
static int      cdScrollPos = 0;
static unsigned long cdLastScrollMs = 0;
static int      cdWrapPass = 0;
static bool     cdNeedsScroll = false;
NpState         cdState = NP_SHOW_START;
unsigned long   cdPauseMs = 0;
// The icon of the event on the tile - its own, or the tile's (iconSelCountdown,
// from before events had one each) - as chosen when the tile was last built.
static uint8_t  cdIconSel = 0;

// Days since 1970-01-01, proleptic Gregorian (Howard Hinnant's days_from_civil).
static long cdDaysFromCivil(int y, unsigned m, unsigned d) {
  y -= m <= 2;
  const long era = (y >= 0 ? y : y - 399) / 400;
  const unsigned yoe = (unsigned)(y - era * 400);
  const unsigned doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
  const unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
  return era * 146097 + (long)doe - 719468;
}

static bool cdLeap(int y) { return (y % 4 == 0 && y % 100 != 0) || y % 400 == 0; }

static int cdMonthDays(int y, int m) {
  static const uint8_t DAYS[12] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
  return (m == 2 && cdLeap(y)) ? 29 : DAYS[m - 1];
}

// Days from today to the event, -1 once it has gone by for good. A yearly one
// is this year's, or next year's once this year's has passed; 29 February
// comes on the 28th in the years without one.
static long cdDaysLeft(int i, long today, int thisYear) {
  const CountdownEvent& e = cdEvents[i];
  if (e.flags & CD_YEARLY) {
    for (int y = thisYear; y <= thisYear + 1; y++) {
      int d = (e.month == 2 && e.day == 29 && !cdLeap(y)) ? 28 : e.day;
      long when = cdDaysFromCivil(y, e.month, d);
      if (when >= today) return when - today;
    }
    return -1;
  }
  long when = cdDaysFromCivil(e.year, e.month, e.day);
  return when >= today ? when - today : -1;
}

// The nearest event ahead, -1 when there is none - or no date to count from yet.
static int cdNearest(long& days) {
  struct tm t;
  if (!readLocalTime(t)) return -1;
  int year = t.tm_year + 1900;
  long today = cdDaysFromCivil(year, t.tm_mon + 1, t.tm_mday);
  int best = -1;
  for (int i = 0; i < CD_MAX_EVENTS; i++) {
    if (!(cdEvents[i].flags & CD_USED) || !cdEvents[i].name[0]) continue;
    long d = cdDaysLeft(i, today, year);
    if (d >= 0 && (best < 0 || d < days)) {
      best = i;
      days = d;
    }
  }
  return best;
}

static bool countdownHasEvent() {
  long d = 0;
  return cdNearest(d) >= 0;
}

// "zi", "zile" and from 20 up "de zile" (but 101 zile ... 119 zile), as
// Romanian counts; "day" and "days".
static const char* cdUnitWord(long days, bool ro) {
  if (days == 1) return ro ? "zi" : "day";
  if (!ro) return "days";
  long tail = days % 100;
  return (tail == 0 || tail >= 20) ? "de zile" : "zile";
}

// What the tile says now; false when there is nothing ahead.
static bool countdownText(char* out, size_t n) {
  long days = 0;
  int i = cdNearest(days);
  if (i < 0) return false;
  bool ro = cdLang == 0;
  if (days == 0) snprintf(out, n, "%s %s", cdEvents[i].name, ro ? "azi" : "today");
  else if (!cdFullUnit) snprintf(out, n, "%s %ld%s", cdEvents[i].name, days, ro ? "z" : "d");
  else snprintf(out, n, "%s %ld %s", cdEvents[i].name, days, cdUnitWord(days, ro));
  return true;
}

void countdownBuildBuffer(const char* text) {
  char cleanBuf[CD_NAME_MAX + 24];
  sanitizeUtf8(text, cleanBuf, sizeof(cleanBuf));
  cdColCount = 0;
  if (scrollIconInBuffer(scrollTypeCountdown) && !hideIconCountdown) {
    scrollBufPrependIcon(cdColBuf, cdColCount, CD_COL_MAX, resolveTileIcon(cdIconSel, dateIcon), NP_ICON_COLS);
  }
  for (int ci = 0; cleanBuf[ci] != '\0' && cdColCount < CD_COL_MAX - 12; ci++) {
    appendGlyphAuto(cdColBuf, cdColCount, CD_COL_MAX, cleanBuf[ci], fontTypeCountdown);
  }
  if (cdColCount > 0) cdColCount--;
}

void countdownDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  if (!hideIconCountdown && !scrollIconInBuffer(scrollTypeCountdown)) {
    const uint8_t* iconBmp = resolveTileIcon(cdIconSel, dateIcon);
    for (int col = 0; col < NP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (iconBmp[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - NP_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconCountdown || scrollIconInBuffer(scrollTypeCountdown);
  int textOffset = fullWidth ? 0 : (NP_ICON_COLS + NP_SEP_COLS);
  int textCols   = fullWidth ? 32 : NP_TEXT_COLS;
  // A text that fits sits in the middle of its window, as the counters do.
  int lead = (cdColCount < textCols) ? (textCols - cdColCount) / 2 : 0;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col - lead;
    uint8_t val = (src >= 0 && src < cdColCount) ? cdColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }
  mxCommit();
}

// Drawn from the text given (the preview's made-up one) or, with none, from
// the events.
void countdownInitWith(const char* text) {
  long days = 0;
  int nearest = cdNearest(days);
  cdIconSel = (nearest >= 0 && cdEvents[nearest].icon) ? cdEvents[nearest].icon : iconSelCountdown;
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < NP_DISPLAY_COLS; c++) mx.setColumn(c, 0x00);
  mxCommit();
  countdownBuildBuffer(text);
  int textCols = (hideIconCountdown || scrollIconInBuffer(scrollTypeCountdown)) ? 32 : NP_TEXT_COLS;
  cdNeedsScroll = cdColCount > textCols;
  cdScrollPos = 0;
  cdWrapPass = 0;
  countdownDrawAtPos(0);
  cdState   = NP_PAUSE_BEFORE_RIGHT;
  cdPauseMs = millis();
}

void countdownInit() {
  char text[CD_NAME_MAX + 24] = "";
  countdownText(text, sizeof(text));
  countdownInitWith(text);
}

// CANVAS TILE

void drawCanvas() {
  mx.update(MD_MAX72XX::OFF);
  for (int col = 0; col < CANVAS_COLS; col++) {
    mx.setColumn(31 - col, canvasBitmap[col]);
  }
  mxCommit();
}

String canvasBitmapToHex() {
  static const char* HEXD = "0123456789abcdef";
  String out;
  out.reserve(CANVAS_COLS * 2);
  for (int i = 0; i < CANVAS_COLS; i++) {
    out += HEXD[(canvasBitmap[i] >> 4) & 0x0F];
    out += HEXD[canvasBitmap[i] & 0x0F];
  }
  return out;
}

static inline int canvasHexNibble(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  return -1;
}

bool canvasBitmapFromHex(const String& hex) {
  if ((int)hex.length() != CANVAS_COLS * 2) return false;
  uint8_t tmp[CANVAS_COLS];
  for (int i = 0; i < CANVAS_COLS; i++) {
    int hi = canvasHexNibble(hex[i * 2]);
    int lo = canvasHexNibble(hex[i * 2 + 1]);
    if (hi < 0 || lo < 0) return false;
    tmp[i] = (uint8_t)((hi << 4) | lo);
  }
  memcpy(canvasBitmap, tmp, CANVAS_COLS);
  return true;
}

// SCREEN SAVER TILE

// The animations' clock: millis(), except while the Preview runs one ahead of
// time to take its frames (see TILE PREVIEW), 80 ms a frame.
static bool          gSsFakeClock = false;
static unsigned long gSsFakeNow   = 0;
static inline unsigned long ssNow() { return gSsFakeClock ? gSsFakeNow : millis(); }

uint8_t       ssAnimSelected   = SS_ANIM_RANDOM;
uint8_t       ssCurrentAnim    = SS_ANIM_EQ;
uint8_t       ssLastRandomAnim = 0;
unsigned long ssFrameMs        = 0;
#define SS_FRAME_INTERVAL 80UL

uint8_t ssFb[32];
static inline void ssClearFb() { memset(ssFb, 0, sizeof(ssFb)); }
static inline void ssSetPx(int x, int y, bool on) {
  if (x < 0 || x > 31 || y < 0 || y > 7) return;
  if (on) ssFb[x] |= (1 << y);
  else    ssFb[x] &= (uint8_t)~(1 << y);
}
static void ssRender() {
  mx.update(MD_MAX72XX::OFF);
  for (int i = 0; i < 32; i++) mx.setColumn(31 - i, ssFb[i]);
  mxCommit();
}

static void ssBlit(const uint16_t* rows, uint8_t rowCount, uint8_t w, int x0, int y0) {
  for (int r = 0; r < rowCount; r++) {
    uint16_t row = rows[r];
    for (int c = 0; c < w; c++) {
      if (row & (1 << (w - 1 - c))) ssSetPx(x0 + c, y0 + r, true);
    }
  }
}

static void ssFlashInvert(int times, int stepMs) {
  if (gSsFakeClock) return;   // frames drawn ahead for the Preview: no flash, no waiting
  for (int i = 0; i < times; i++) {
    mx.update(MD_MAX72XX::OFF);
    for (int c = 0; c < 32; c++) mx.setColumn(c, ~ssFb[31 - c]);
    mxCommit();
    delay(stepMs);
    ssRender();
    delay(stepMs);
  }
}

// PONG

static float pongBallX, pongBallY, pongVX, pongVY, pongPrevBallX, pongPrevBallY;
static int   pongLPaddleY, pongRPaddleY;
static int   pongFlashL, pongFlashR;
static bool  pongRSlowTick;
#define PONG_MAX_SPEED   1.15f
#define PONG_CENTER_Y    2

void ssPongInit() {
  pongBallX = pongPrevBallX = 16; pongBallY = pongPrevBallY = 3.5f;
  pongVX = (random(0, 2) ? 1.0f : -1.0f) * 0.55f;
  pongVY = 0.20f + (random(0, 30) / 100.0f);
  if (random(0, 2)) pongVY = -pongVY;
  pongLPaddleY = pongRPaddleY = PONG_CENTER_Y;
  pongFlashL = pongFlashR = 0; pongRSlowTick = false;
}
// Functie: ssTickPong.
bool ssTickPong() {
  unsigned long now = ssNow();
  if (now - ssFrameMs < SS_FRAME_INTERVAL) return false;
  ssFrameMs = now;
  pongPrevBallX = pongBallX; pongPrevBallY = pongBallY;
  pongBallX += pongVX; pongBallY += pongVY;
  if (pongBallY <= 0) { pongBallY = 0; pongVY = -pongVY; }
  if (pongBallY >= 7) { pongBallY = 7; pongVY = -pongVY; }
  int by = (int)round(pongBallY);

  int lTarget = (pongVX < 0) ? constrain(by - 1, 0, 5) : PONG_CENTER_Y;
  if (pongLPaddleY < lTarget) pongLPaddleY++; else if (pongLPaddleY > lTarget) pongLPaddleY--;

  int rTarget = (pongVX > 0) ? constrain(by - 1, 0, 5) : PONG_CENTER_Y;
  pongRSlowTick = !pongRSlowTick;
  if (pongRSlowTick) {
    if (pongRPaddleY < rTarget) pongRPaddleY++; else if (pongRPaddleY > rTarget) pongRPaddleY--;
  }
  if (pongBallX <= 1 && pongVX < 0) {
    pongBallX = 1; pongVX = -pongVX * 1.04f;
    pongVX = constrain(pongVX, -PONG_MAX_SPEED, PONG_MAX_SPEED);
    pongVY = constrain(pongVY + random(-15, 16) / 100.0f, -0.8f, 0.8f);
    pongFlashL = 3;
  }
  if (pongBallX >= 30 && pongVX > 0) {
    pongBallX = 30; pongVX = -pongVX * 1.04f;
    pongVX = constrain(pongVX, -PONG_MAX_SPEED, PONG_MAX_SPEED);
    pongVY = constrain(pongVY + random(-15, 16) / 100.0f, -0.8f, 0.8f);
    pongFlashR = 3;
  }
  ssClearFb();
  for (int y = 0; y < 8; y += 2) ssSetPx(16, y, true);
  int lh = pongFlashL > 0 ? 5 : 3, rh = pongFlashR > 0 ? 5 : 3;
  int lStart = constrain(pongLPaddleY - (lh - 3) / 2, 0, 8 - lh);
  int rStart = constrain(pongRPaddleY - (rh - 3) / 2, 0, 8 - rh);
  for (int i = 0; i < lh; i++) ssSetPx(0, lStart + i, true);
  for (int i = 0; i < rh; i++) ssSetPx(31, rStart + i, true);
  if (pongFlashL > 0) pongFlashL--;
  if (pongFlashR > 0) pongFlashR--;
  ssSetPx((int)round(pongPrevBallX), (int)round(pongPrevBallY), true);
  ssSetPx((int)round(pongBallX), (int)round(pongBallY), true);
  ssRender();
  return false;
}

// FIREWORKS

#define FW_MAX_PARTICLES 34
struct FwParticle { float x, y, vx, vy; int life, maxLife; bool active, rocket, willow, crackle; };
static FwParticle    fwP[FW_MAX_PARTICLES];
static unsigned long fwNextLaunchMs;
void ssFireworksInit() {
  for (int i = 0; i < FW_MAX_PARTICLES; i++) fwP[i].active = false;
  fwNextLaunchMs = ssNow() + 250;
}
static void fwCrackle(float x, float y) {
  int n = 3 + random(0, 2), placed = 0;
  for (int i = 0; i < FW_MAX_PARTICLES && placed < n; i++) {
    if (!fwP[i].active) {
      float ang = random(0, 360) * 0.0174533f;
      float spd = 0.3f + random(0, 40) / 100.0f;
      fwP[i].x = x; fwP[i].y = y;
      fwP[i].vx = cosf(ang) * spd; fwP[i].vy = sinf(ang) * spd;
      fwP[i].life = fwP[i].maxLife = 4 + random(0, 4);
      fwP[i].active = true; fwP[i].rocket = false; fwP[i].willow = false; fwP[i].crackle = false;
      placed++;
    }
  }
}
static void fwExplode(float x, float y, bool willow) {
  int n = willow ? (6 + random(0, 5)) : (9 + random(0, 8)), placed = 0;
  for (int i = 0; i < FW_MAX_PARTICLES && placed < n; i++) {
    if (!fwP[i].active) {
      float ang = (360.0f / n) * placed + random(-10, 10);
      ang *= 0.0174533f;
      float spd = willow ? (0.55f + random(0, 60) / 100.0f) : (0.45f + (random(0, 100) / 100.0f) * 0.55f);
      fwP[i].x = x; fwP[i].y = y;
      fwP[i].vx = cosf(ang) * spd; fwP[i].vy = sinf(ang) * spd;
      fwP[i].life = fwP[i].maxLife = willow ? (18 + random(0, 8)) : (9 + random(0, 7));
      fwP[i].active = true; fwP[i].rocket = false; fwP[i].willow = willow;
      fwP[i].crackle = (!willow && random(0, 100) < 30);
      placed++;
    }
  }
}

bool ssTickFireworks() {
  unsigned long now = ssNow();
  if (now - ssFrameMs < SS_FRAME_INTERVAL) return false;
  ssFrameMs = now;
  if ((long)(now - fwNextLaunchMs) >= 0) {
    int roCount = random(0, 4) == 0 ? 2 : 1;
    for (int r = 0; r < roCount; r++) {
      for (int i = 0; i < FW_MAX_PARTICLES; i++) {
        if (!fwP[i].active) {
          fwP[i].x = 3 + random(0, 26); fwP[i].y = 7;
          fwP[i].vx = random(-10, 11) / 100.0f; fwP[i].vy = -0.85f - random(0, 20) / 100.0f;
          fwP[i].life = 100; fwP[i].active = true; fwP[i].rocket = true;
          fwP[i].willow = (random(0, 2) == 0); fwP[i].crackle = false;
          break;
        }
      }
    }
    fwNextLaunchMs = now + 900 + random(0, 700);
  }
  ssClearFb();
  for (int i = 0; i < FW_MAX_PARTICLES; i++) {
    if (!fwP[i].active) continue;
    float px = fwP[i].x, py = fwP[i].y;
    fwP[i].x += fwP[i].vx; fwP[i].y += fwP[i].vy;
    if (fwP[i].rocket) {
      if (fwP[i].y <= 1 + random(0, 3) || random(0, 100) < 4) { fwExplode(fwP[i].x, fwP[i].y, fwP[i].willow); fwP[i].active = false; continue; }
      ssSetPx((int)round(px), (int)round(py) + 1, true);
      ssSetPx((int)round(px), (int)round(py) + 2, true);
    } else if (fwP[i].willow) {
      fwP[i].vx *= 0.92f; fwP[i].vy = fwP[i].vy * 0.92f + 0.05f;
      fwP[i].life--;
      if (fwP[i].life <= 0 || fwP[i].y > 7 || fwP[i].x < 0 || fwP[i].x > 31) { fwP[i].active = false; continue; }
      if (fwP[i].life < 5 && (fwP[i].life % 2 == 0)) continue;
    } else {
      fwP[i].vy += 0.028f;
      fwP[i].life--;
      bool outOfBounds = (fwP[i].y > 7 || fwP[i].x < 0 || fwP[i].x > 31);
      if (fwP[i].life <= 0 || outOfBounds) {
        if (fwP[i].crackle && !outOfBounds) fwCrackle(fwP[i].x, fwP[i].y);
        fwP[i].active = false; continue;
      }
      if (fwP[i].life < fwP[i].maxLife / 3 && (fwP[i].life % 2 == 0)) continue;
    }
    ssSetPx((int)round(fwP[i].x), (int)round(fwP[i].y), true);
  }
  ssRender();
  return false;
}

// EQUALIZER

#define EQ_BARS 8
static float          eqHeight[EQ_BARS], eqTarget[EQ_BARS], eqPeak[EQ_BARS];
static unsigned long  eqNextRetarget, eqNextBass;
void ssEqInit() {
  for (int i = 0; i < EQ_BARS; i++) { eqHeight[i] = 1; eqTarget[i] = 1 + random(0, 8); eqPeak[i] = 1; }
  eqNextRetarget = ssNow();
  eqNextBass = ssNow() + 2500UL + random(0, 1500);
}
bool ssTickEq() {
  unsigned long now = ssNow();
  if (now - ssFrameMs < SS_FRAME_INTERVAL) return false;
  ssFrameMs = now;
  if (now >= eqNextBass) {
    for (int i = 0; i < EQ_BARS; i++) eqTarget[i] = 8;
    eqNextRetarget = now + 90;
    eqNextBass = now + 2500UL + random(0, 2000);
  } else if (now >= eqNextRetarget) {
    // Adjacent bars follow a shared beat instead of jumping independently.
    int beat = 2 + random(0, 6);
    for (int i = 0; i < EQ_BARS; i++)
      eqTarget[i] = constrain(beat + random(-2, 3), 1, 8);
    eqNextRetarget = now + 220 + random(0, 120);
  }
  ssClearFb();
  float maxH = 0;
  for (int i = 0; i < EQ_BARS; i++) {
    if (eqHeight[i] < eqTarget[i]) eqHeight[i] += 1.1f;
    else if (eqHeight[i] > eqTarget[i]) eqHeight[i] -= 0.6f;
    if (eqHeight[i] > maxH) maxH = eqHeight[i];
    if (eqHeight[i] > eqPeak[i]) eqPeak[i] = eqHeight[i];
    else eqPeak[i] -= 0.12f;
    if (eqPeak[i] < 1) eqPeak[i] = 1;
    int h = constrain((int)round(eqHeight[i]), 1, 8);
    for (int y = 0; y < h; y++) { ssSetPx(i * 4, 7 - y, true); ssSetPx(i * 4 + 1, 7 - y, true); }
    int peakInt = constrain((int)round(eqPeak[i]), 1, 8);
    if (eqPeak[i] - eqHeight[i] > 0.6f) { int py = 7 - (peakInt - 1); ssSetPx(i * 4, py, true); ssSetPx(i * 4 + 1, py, true); }
  }
  if (maxH > 7.2f) { ssSetPx(0, 0, true); ssSetPx(31, 0, true); }
  ssRender();
  return false;
}

// DIGITAL RAIN: staggered two-to-four-pixel drops with a tiny floor splash.
struct SsDrop { int8_t y; uint8_t length, divisor; };
static SsDrop ssDrops[10];
static uint8_t ssRainFrame;
void ssRainInit() {
  for (SsDrop& drop : ssDrops)
    drop = { (int8_t)-random(0, 9), (uint8_t)random(2, 5), (uint8_t)random(1, 3) };
  ssRainFrame = 0;
}
bool ssTickRain() {
  unsigned long now = ssNow();
  if (now - ssFrameMs < SS_FRAME_INTERVAL) return false;
  ssFrameMs = now;
  ssRainFrame++;
  ssClearFb();
  for (uint8_t i = 0; i < 10; i++) {
    SsDrop& drop = ssDrops[i];
    if (ssRainFrame % drop.divisor == 0) drop.y++;
    if (drop.y > 7 + drop.length) {
      drop.y = (int8_t)-random(1, 9);
      drop.length = (uint8_t)random(2, 5);
      drop.divisor = (uint8_t)random(1, 3);
    }
    int x = i * 3 + 2;
    for (uint8_t tail = 0; tail < drop.length; tail++) ssSetPx(x, drop.y - tail, true);
    if (drop.y == 7) { ssSetPx(x - 1, 7, true); ssSetPx(x + 1, 7, true); }
  }
  ssRender();
  return false;
}

// SNAKE: it plays itself - heads for the food by the shortest way that is not
// its own body, grows as it eats, and starts again once it has shut itself in.
// It goes round the left and right edges; the top and bottom are walls.
#define SNAKE_MAX 40
static int8_t  snakeX[SNAKE_MAX], snakeY[SNAKE_MAX];   // [0] is the head
static uint8_t snakeLen;
static int8_t  snakeDX, snakeDY, snakeFoodX, snakeFoodY;
static bool snakeOn(int x, int y, int cells) {
  for (int i = 0; i < cells; i++) if (snakeX[i] == x && snakeY[i] == y) return true;
  return false;
}
static void snakeFood() {
  for (int tries = 0; tries < 80; tries++) {
    int x = random(0, 32), y = random(0, 8);
    if (!snakeOn(x, y, snakeLen)) { snakeFoodX = (int8_t)x; snakeFoodY = (int8_t)y; return; }
  }
}
static void snakeStart() {
  snakeLen = 4;
  int y = random(1, 7);
  for (int i = 0; i < snakeLen; i++) { snakeX[i] = (int8_t)(8 - i); snakeY[i] = (int8_t)y; }
  snakeDX = 1; snakeDY = 0;
  snakeFood();
}
void ssSnakeInit() {
  snakeStart();
}
bool ssTickSnake() {
  unsigned long now = ssNow();
  if (now - ssFrameMs < SS_FRAME_INTERVAL) return false;
  ssFrameMs = now;
  static const int8_t DX[4] = {1, 0, -1, 0}, DY[4] = {0, 1, 0, -1};
  int best = -1, bestDist = 1000;
  for (int d = 0; d < 4; d++) {
    if (DX[d] == -snakeDX && DY[d] == -snakeDY) continue;   // never back on itself
    int nx = (snakeX[0] + DX[d] + 32) & 31, ny = snakeY[0] + DY[d];
    // The tail's cell is free: it will have moved on.
    if (ny < 0 || ny > 7 || snakeOn(nx, ny, snakeLen - 1)) continue;
    int dx = abs(nx - snakeFoodX);
    if (dx > 16) dx = 32 - dx;
    int dist = 2 * (dx + abs(ny - snakeFoodY));
    if (DX[d] != snakeDX || DY[d] != snakeDY) dist++;       // straight on, between two equal ways
    if (dist < bestDist) { bestDist = dist; best = d; }
  }
  if (best < 0) {
    snakeStart();
  } else {
    snakeDX = DX[best]; snakeDY = DY[best];
    int nx = (snakeX[0] + snakeDX + 32) & 31, ny = snakeY[0] + snakeDY;
    bool eats = nx == snakeFoodX && ny == snakeFoodY;
    if (eats && snakeLen < SNAKE_MAX) snakeLen++;
    for (int i = snakeLen - 1; i > 0; i--) { snakeX[i] = snakeX[i - 1]; snakeY[i] = snakeY[i - 1]; }
    snakeX[0] = (int8_t)nx; snakeY[0] = (int8_t)ny;
    if (eats) snakeFood();
  }
  ssClearFb();
  for (int i = 0; i < snakeLen; i++) ssSetPx(snakeX[i], snakeY[i], true);
  // The food blinks, so it does not read as a piece of the snake.
  if ((now / 160UL) & 1UL) ssSetPx(snakeFoodX, snakeFoodY, true);
  ssRender();
  return false;
}

// HELIX: two strands winding round each other, a turn every 24 columns, the
// one that is at the back dotted and dotted rungs between them; the whole of
// it slides a column a frame.
static uint16_t helixShift;
void ssHelixInit() {
  helixShift = (uint16_t)random(0, 96);
}
bool ssTickHelix() {
  unsigned long now = ssNow();
  if (now - ssFrameMs < SS_FRAME_INTERVAL) return false;
  ssFrameMs = now;
  helixShift = (helixShift + 1) % 96;
  ssClearFb();
  for (int x = 0; x < 32; x++) {
    unsigned at = (unsigned)x + helixShift;
    float phase = at * 0.2618f;                  // 2 pi / 24
    int a = (int)lroundf(3.5f + 3.5f * sinf(phase));
    if (a < 0) a = 0;
    if (a > 7) a = 7;
    int b = 7 - a;
    bool aInFront = cosf(phase) > 0.0f;
    if (aInFront || (at & 1u)) ssSetPx(x, a, true);
    if (!aInFront || (at & 1u)) ssSetPx(x, b, true);
    int top = a < b ? a : b, bottom = a < b ? b : a;
    if ((at % 4u) == 0 && bottom - top > 2)
      for (int y = top + 2; y < bottom - 1; y += 2) ssSetPx(x, y, true);
  }
  ssRender();
  return false;
}

// BRICKS: a wall of bricks along the top, a paddle along the bottom and a ball
// between them, playing itself. The paddle is always under the ball in time; a
// wall knocked down is built again.
#define BRICK_ROWS 3
static uint8_t  brickRow[BRICK_ROWS];        // bit k: the brick at columns 4k..4k+2
static float    brickBallX, brickBallY, brickVX, brickVY;
static int8_t   brickPadX;                   // the paddle's left end, 5 wide
static void brickWall() { for (uint8_t& row : brickRow) row = 0xFF; }
void ssBricksInit() {
  brickWall();
  brickBallX = (float)random(6, 26);
  brickBallY = 5.0f;
  brickVX = random(0, 2) ? 0.9f : -0.9f;
  brickVY = -0.6f;
  brickPadX = (int8_t)constrain((int)brickBallX - 2, 0, 27);
}
bool ssTickBricks() {
  unsigned long now = ssNow();
  if (now - ssFrameMs < SS_FRAME_INTERVAL) return false;
  ssFrameMs = now;
  brickBallX += brickVX;
  brickBallY += brickVY;
  if (brickBallX < 0.0f)  { brickBallX = 0.0f;  brickVX = -brickVX; }
  if (brickBallX > 31.0f) { brickBallX = 31.0f; brickVX = -brickVX; }
  int bx = (int)lroundf(brickBallX), by = (int)lroundf(brickBallY);
  if (by < 0) { brickBallY = 0.0f; by = 0; brickVY = fabsf(brickVY); }
  if (by < BRICK_ROWS && (bx & 3) != 3 && (brickRow[by] & (1u << (bx >> 2)))) {
    // A brick, from under it or - once the ball is behind the wall - from
    // above: gone, and the ball turns back.
    brickRow[by] &= (uint8_t)~(1u << (bx >> 2));
    brickVY = -brickVY;
  } else if (brickVY > 0.0f && by >= 6) {
    // Off the paddle, a little to one side of where it came in.
    brickBallY = 6.0f; by = 6;
    brickVY = -brickVY;
    static const float SIDE[3] = {0.6f, 0.9f, 1.3f};
    float side = SIDE[random(0, 3)];
    brickVX = brickVX < 0.0f ? -side : side;
  }
  // The paddle goes for the ball, two columns a frame at most.
  int want = constrain(bx - 2, 0, 27);
  if (brickPadX < want) brickPadX = (int8_t)(brickPadX + (want - brickPadX > 1 ? 2 : 1));
  else if (brickPadX > want) brickPadX = (int8_t)(brickPadX - (brickPadX - want > 1 ? 2 : 1));
  bool any = false;
  for (uint8_t row : brickRow) any = any || row != 0;
  if (!any) brickWall();
  ssClearFb();
  for (int r = 0; r < BRICK_ROWS; r++)
    for (int k = 0; k < 8; k++)
      if (brickRow[r] & (1u << k))
        for (int c = 0; c < 3; c++) ssSetPx(k * 4 + c, r, true);
  for (int c = 0; c < 5; c++) ssSetPx(brickPadX + c, 7, true);
  ssSetPx(bx, by, true);
  ssRender();
  return false;
}

// RUNNER: a little figure running along a ground that slides under it, jumping
// whatever comes its way. Up to three obstacles at once, one or two columns
// wide and one or two rows high; the jump lasts as long as the one under it is
// wide.
#define RUN_X 5                              // the figure's left column, 3 wide
struct RunObstacle { int8_t x; uint8_t w, h; };
static RunObstacle runObs[3];
static uint8_t  runFrame, runJump, runJumpLen, runGap;
void ssRunnerInit() {
  for (RunObstacle& o : runObs) o.w = 0;
  runFrame = runJump = runJumpLen = 0;
  runGap = 10;
}
bool ssTickRunner() {
  unsigned long now = ssNow();
  if (now - ssFrameMs < SS_FRAME_INTERVAL) return false;
  ssFrameMs = now;
  runFrame++;
  // The ground and what stands on it move a column a frame.
  for (RunObstacle& o : runObs) {
    if (!o.w) continue;
    o.x--;
    if (o.x + o.w <= 0) o.w = 0;
    // Two columns ahead of the figure: up, and down once it is past.
    else if (o.x == RUN_X + 4 && !runJump) runJump = runJumpLen = (uint8_t)(o.w + 5);
  }
  if (runGap) runGap--;
  else {
    for (RunObstacle& o : runObs) {
      if (o.w) continue;
      o = { 32, (uint8_t)random(1, 3), (uint8_t)random(1, 3) };
      runGap = (uint8_t)(13 + random(0, 12));
      break;
    }
  }
  // One row up on the first and the last frame of a jump, two in between.
  int lift = !runJump ? 0 : (runJump == runJumpLen || runJump == 1) ? 1 : 2;
  if (runJump) runJump--;
  ssClearFb();
  for (int x = 0; x < 32; x++)
    if (((x + runFrame) & 7u) != 0) ssSetPx(x, 7, true);          // the ground, with a gap that travels
  for (const RunObstacle& o : runObs)
    for (int c = 0; c < o.w; c++)
      for (int r = 0; r < o.h; r++) ssSetPx(o.x + c, 6 - r, true);
  // Head, arms, body, and legs that change every other frame; in the air they
  // are held apart.
  static const uint8_t BODY[3] = {0b010, 0b111, 0b010};
  static const uint8_t LEGS[2][2] = { {0b010, 0b101}, {0b101, 0b010} };
  const uint8_t* legs = LEGS[lift ? 0 : (runFrame >> 1) & 1];
  int top = 2 - lift;
  for (int r = 0; r < 5; r++) {
    uint8_t bits = r < 3 ? BODY[r] : legs[r - 3];
    for (int c = 0; c < 3; c++) if (bits & (0b100 >> c)) ssSetPx(RUN_X + c, top + r, true);
  }
  ssRender();
  return false;
}

// The ones Random picks from.
static const uint8_t SS_RANDOM_POOL[] = {
  SS_ANIM_PONG, SS_ANIM_FIREWORKS, SS_ANIM_EQ, SS_ANIM_RAIN, SS_ANIM_SNAKE, SS_ANIM_HELIX, SS_ANIM_BRICKS, SS_ANIM_RUNNER
};
#define SS_RANDOM_COUNT ((int)sizeof(SS_RANDOM_POOL))

// Procedural frames keep evolving until the circuit's configured duration ends.
static bool ssStep() {
  switch (ssCurrentAnim) {
    case SS_ANIM_PONG:      return ssTickPong();
    case SS_ANIM_FIREWORKS: return ssTickFireworks();
    case SS_ANIM_RAIN:      return ssTickRain();
    case SS_ANIM_BRICKS:    return ssTickBricks();
    case SS_ANIM_RUNNER:    return ssTickRunner();
    case SS_ANIM_SNAKE:     return ssTickSnake();
    case SS_ANIM_HELIX:     return ssTickHelix();
    default:                return ssTickEq();
  }
}

// Main dispatch
void ssInit() {
  mx.update(MD_MAX72XX::OFF);
  for (int i = 0; i < 32; i++) mx.setColumn(i, 0x00);
  mxCommit();
  ssClearFb();
  ssFrameMs = 0;
  if (ssAnimSelected == SS_ANIM_RANDOM || !ssAnimValid(ssAnimSelected)) {
    uint8_t pick;
    do { pick = SS_RANDOM_POOL[random(0, SS_RANDOM_COUNT)]; } while (pick == ssLastRandomAnim);
    ssLastRandomAnim = pick;
    ssCurrentAnim = pick;
  } else {
    ssCurrentAnim = ssAnimSelected;
  }
  switch (ssCurrentAnim) {
    case SS_ANIM_PONG:      ssPongInit();      break;
    case SS_ANIM_FIREWORKS: ssFireworksInit(); break;
    case SS_ANIM_EQ:        ssEqInit();        break;
    case SS_ANIM_RAIN:      ssRainInit();      break;
    case SS_ANIM_BRICKS:    ssBricksInit();    break;
    case SS_ANIM_RUNNER:    ssRunnerInit();    break;
    case SS_ANIM_SNAKE:     ssSnakeInit();     break;
    case SS_ANIM_HELIX:     ssHelixInit();     break;
    default:                ssCurrentAnim = SS_ANIM_EQ; ssEqInit(); break;
  }
}
void ssTick() {
  ssStep();
}

void resumeAfterNotif() {
  if (ets2Active) {
    ets2LastSpeed = -1;
    ets2Init();
    return;
  }
  beginP2CCapture();
  if (!ensureCircuitSlot()) {
    finishP2CTransition();
    return;
  }
  CycleItem& cur = items[currentSlot];
  if (cur.id == ITEM_NOW_PLAYING) {
    npInit();
  } else if (cur.id == ITEM_WEATHER) {
    weatherInit();
  } else if (cur.id == ITEM_MEMENTO) {
    mementoInit();
  } else if (cur.id == ITEM_COUNTDOWN) {
    countdownInit();
  } else if (cur.id == ITEM_CANVAS) {
    drawCanvas();
  } else if (cur.id == ITEM_SCREENSAVER) {
    ssRender();
  } else if (cur.id == ITEM_CURRENCY) {
    currencyInit();
  } else if (socialIndexForItem(cur.id) >= 0) {
    socialInit(cur.id);
  } else {
    mx.update(MD_MAX72XX::OFF);
    for (int i = 0; i < 32; i++) mx.setColumn(i, 0x00);
    mxCommit();
    gLastStaticDrawMs = 0;
    gStaticDrawDone   = false;
  }
  finishP2CTransition();
}
bool notifTick() {
  if (!notifActive) return false;
  unsigned long now = millis();
  switch (notifState) {
    case NP_SHOW_START:
      break;
    case NP_PAUSE_BEFORE_RIGHT:
      if (now - notifPauseMs >= NP_PAUSE_MS) {
        if (scrollIsWrap(notifScrollType())) {
          notifWrapPass = 0;
          notifScrollPos = 0;
          notifState = NP_SCROLL_WRAP;
        } else {
          notifState = NP_SCROLL_RIGHT;
        }
        notifLastScrollMs = now;
      }
      break;
    case NP_SCROLL_WRAP: {
      if (now - notifLastScrollMs >= notifScrollSpeed()) {
        notifLastScrollMs = now;
        notifScrollPos++;
        if (notifScrollPos >= notifColCount) {
          notifWrapPass++;
          if (notifWrapPass >= SCROLL_WRAP_PASSES) {

            notifActive = false;
            notifIconOverride = nullptr;
            webAccessAlertActive = false;
            notifBuf[0] = '\0';
            resumeAfterNotif();
            return false;
          }
          notifScrollPos = -((notifHideIcon() || scrollIconInBuffer(notifScrollType())) ? 32 : NP_TEXT_COLS);
        }
        notifDrawAtPos(notifScrollPos);
      }
      break;
    }
    case NP_SCROLL_RIGHT: {
      int maxPos = notifColCount - ((notifHideIcon() || scrollIconInBuffer(notifScrollType())) ? 32 : NP_TEXT_COLS);
      if (maxPos <= 0) {
        notifState = NP_PAUSE_SHORT;
        notifPauseMs = now;
        break;
      }
      if (now - notifLastScrollMs >= notifScrollSpeed()) {
        notifLastScrollMs = now;
        notifScrollPos++;
        notifDrawAtPos(notifScrollPos);
        if (notifScrollPos >= maxPos) {
          notifScrollPos = maxPos;
          notifState = NP_PAUSE_AFTER_RIGHT;
          notifPauseMs = now;
        }
      }
      break;
    }
    case NP_PAUSE_AFTER_RIGHT:
      if (now - notifPauseMs >= NP_PAUSE_MS) {
        notifState = NP_SCROLL_LEFT;
        notifLastScrollMs = now;
      }
      break;
    case NP_SCROLL_LEFT:
      if (now - notifLastScrollMs >= notifScrollSpeed()) {
        notifLastScrollMs = now;
        notifScrollPos--;
        if (notifScrollPos <= 0) {
          notifScrollPos = 0;
          notifDrawAtPos(0);
          notifState = NP_PAUSE_BEFORE_NEXT;
          notifPauseMs = now;
        } else {
          notifDrawAtPos(notifScrollPos);
        }
      }
      break;
    case NP_PAUSE_BEFORE_NEXT:
      if (now - notifPauseMs >= NP_PAUSE_MS) {

        notifActive = false;
        notifIconOverride = nullptr;
        webAccessAlertActive = false;
        notifBuf[0] = '\0';
        resumeAfterNotif();
        return false;
      }
      break;
    case NP_PAUSE_SHORT:
      if (now - notifPauseMs >= 4000UL) {
        notifActive = false;
        notifIconOverride = nullptr;
        webAccessAlertActive = false;
        notifBuf[0] = '\0';
        resumeAfterNotif();
        return false;
      }
      break;
  }
  return true;
}

// SHOW IP ADDRESS (Touch Sensor Action)

void ipBuildBuffer() {
  char cleanBuf[24];
  sanitizeUtf8(ipBuf, cleanBuf, sizeof(cleanBuf));
  ipColCount = 0;
  if (scrollIconInBuffer(scrollTypeIp) && !hideIconIp) {
    scrollBufPrependIcon(ipColBuf, ipColCount, 128, resolveTileIcon(iconSelIp, ipIcon), NP_ICON_COLS);
  }
  for (int ci = 0; cleanBuf[ci] != '\0' && ipColCount < 120; ci++) {
    appendGlyphAuto(ipColBuf, ipColCount, 128, cleanBuf[ci], fontTypeIp);
  }
  if (ipColCount > 0) ipColCount--;
}

void ipDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  if (!hideIconIp && !scrollIconInBuffer(scrollTypeIp)) {

    // Resolved once rather than 64 times inside the loop below.
    const uint8_t* ipIconBmp = resolveTileIcon(iconSelIp, ipIcon);
    for (int col = 0; col < NP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (ipIconBmp[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - NP_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconIp || scrollIconInBuffer(scrollTypeIp);
  int textOffset = fullWidth ? 0 : (NP_ICON_COLS + NP_SEP_COLS);
  int textCols   = fullWidth ? 32 : NP_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < ipColCount) ? ipColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }
  mxCommit();
}

void ipInit() {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < NP_DISPLAY_COLS; c++) mx.setColumn(c, 0x00);
  mxCommit();
  ipBuildBuffer();
  ipScrollPos = 0;
  ipWrapPass = 0;
  ipDrawAtPos(0);
  ipState    = NP_PAUSE_BEFORE_RIGHT;
  ipPauseMs  = millis();
}

bool ipTick() {
  if (!ipShowActive) return false;
  unsigned long now = millis();
  switch (ipState) {
    case NP_SHOW_START:
      break;
    case NP_PAUSE_BEFORE_RIGHT:
      if (now - ipPauseMs >= NP_PAUSE_MS) {
        if (scrollIsWrap(scrollTypeIp)) {
          ipWrapPass = 0;
          ipScrollPos = 0;
          ipState = NP_SCROLL_WRAP;
        } else {
          ipState = NP_SCROLL_RIGHT;
        }
        ipLastScrollMs = now;
      }
      break;
    case NP_SCROLL_WRAP: {
      if (now - ipLastScrollMs >= spd(SPD_IP)) {
        ipLastScrollMs = now;
        ipScrollPos++;
        if (ipScrollPos >= ipColCount) {
          ipWrapPass++;
          if (ipWrapPass >= SCROLL_WRAP_PASSES) {

            ipShowActive = false;
            ipBuf[0] = '\0';
            resumeAfterNotif();
            return false;
          }
          ipScrollPos = -((hideIconIp || scrollIconInBuffer(scrollTypeIp)) ? 32 : NP_TEXT_COLS);
        }
        ipDrawAtPos(ipScrollPos);
      }
      break;
    }
    case NP_SCROLL_RIGHT: {
      int maxPos = ipColCount - ((hideIconIp || scrollIconInBuffer(scrollTypeIp)) ? 32 : NP_TEXT_COLS);
      if (maxPos <= 0) {
        ipState = NP_PAUSE_SHORT;
        ipPauseMs = now;
        break;
      }
      if (now - ipLastScrollMs >= spd(SPD_IP)) {
        ipLastScrollMs = now;
        ipScrollPos++;
        ipDrawAtPos(ipScrollPos);
        if (ipScrollPos >= maxPos) {
          ipScrollPos = maxPos;
          ipState = NP_PAUSE_AFTER_RIGHT;
          ipPauseMs = now;
        }
      }
      break;
    }
    case NP_PAUSE_AFTER_RIGHT:
      if (now - ipPauseMs >= NP_PAUSE_MS) {
        ipState = NP_SCROLL_LEFT;
        ipLastScrollMs = now;
      }
      break;
    case NP_SCROLL_LEFT:
      if (now - ipLastScrollMs >= spd(SPD_IP)) {
        ipLastScrollMs = now;
        ipScrollPos--;
        if (ipScrollPos <= 0) {
          ipScrollPos = 0;
          ipDrawAtPos(0);
          ipState = NP_PAUSE_BEFORE_NEXT;
          ipPauseMs = now;
        } else {
          ipDrawAtPos(ipScrollPos);
        }
      }
      break;
    case NP_PAUSE_BEFORE_NEXT:
      if (now - ipPauseMs >= NP_PAUSE_MS) {

        ipShowActive = false;
        ipBuf[0] = '\0';
        resumeAfterNotif();
        return false;
      }
      break;
    case NP_PAUSE_SHORT:
      if (now - ipPauseMs >= 4000UL) {
        ipShowActive = false;
        ipBuf[0] = '\0';
        resumeAfterNotif();
        return false;
      }
      break;
  }
  return true;
}

// starts the "Show IP Address" one-shot display; it interrupts whatever is
// currently on screen (normal circuit tile OR any other priority tile) —
// the underlying state of other features (stopwatch running, timer, etc.)
// is left untouched and simply resumes visually once the IP display ends.
void startIpShowFromTouch() {
  if (ipShowActive) return; // already showing; ignore a re-trigger mid-animation

  String localIp = provisionMode ? WiFi.softAPIP().toString() : WiFi.localIP().toString();
  localIp.toCharArray(ipBuf, sizeof(ipBuf));
  ipShowActive = true;
  beginC2PCapture();
  ipInit();
  finishC2PTransition(tileTransIp, tileTransSpd[TRSPD_IP]);
}

// NOW PLAYING (Priority Tiles)

// Now Playing as a priority tile: in the priority list, playing, and its tile
// switched on and in the manager. Octoglow Connect keeps sending songs whether
// or not the tile is on, so "playing" alone is not enough - a redraw that went
// by nowPlayingActive only put a switched-off Now Playing on the panel.
static bool npPriorityLive() {
  if (!nowPlayingIsPriority || !nowPlayingActive || !tileInManager(ITEM_NOW_PLAYING)) return false;
  for (int i = 0; i < NUM_ITEMS; i++) {
    if (items[i].id == ITEM_NOW_PLAYING) return items[i].enabled;
  }
  return false;
}

bool npPriorityTick() {
  if (!npPriorityLive()) {
    if (npPriorityWasActive) {
      npPriorityWasActive = false;
      resumeCircuitTileFromPriority();
    }
    return false;
  }

  if (!npPriorityWasActive) {
    npPriorityWasActive = true;
    beginC2PCapture();
    npInit();
    finishC2PTransition(tileTransNp, tileTransSpd[TRSPD_NP]);
    return true;
  }

  unsigned long now = millis();
  switch (npState) {

    case NP_SHOW_START:
      break;

    case NP_PAUSE_BEFORE_RIGHT:
      if (now - npPauseStartMs >= NP_PAUSE_MS) {
        if (scrollIsWrap(scrollTypeNowPlaying)) {
          npWrapPass = 0;
          npScrollPos = 0;
          npState = NP_SCROLL_WRAP;
        } else {
          npState = NP_SCROLL_RIGHT;
        }
        npLastScrollMs = now;
      }
      break;

    case NP_SCROLL_WRAP: {
      if (now - npLastScrollMs >= spd(SPD_NOWPLAYING)) {
        npLastScrollMs = now;
        npScrollPos++;
        if (npScrollPos >= npColCount) {
          npWrapPass++;
          if (npWrapPass >= SCROLL_WRAP_PASSES) {

            npInit();
            break;
          }
          npScrollPos = -((hideIconNowPlaying || scrollIconInBuffer(scrollTypeNowPlaying)) ? 32 : NP_TEXT_COLS);
        }
        npDrawAtPos(npScrollPos);
      }
      break;
    }

    case NP_SCROLL_RIGHT: {
      int maxPos = npColCount - ((hideIconNowPlaying || scrollIconInBuffer(scrollTypeNowPlaying)) ? 32 : NP_TEXT_COLS);
      if (maxPos <= 0) {
        npState = NP_PAUSE_SHORT;
        npPauseStartMs = now;
        break;
      }
      if (now - npLastScrollMs >= spd(SPD_NOWPLAYING)) {
        npLastScrollMs = now;
        npScrollPos++;
        npDrawAtPos(npScrollPos);
        if (npScrollPos >= maxPos) {
          npScrollPos = maxPos;
          npState = NP_PAUSE_AFTER_RIGHT;
          npPauseStartMs = now;
        }
      }
      break;
    }

    case NP_PAUSE_AFTER_RIGHT:
      if (now - npPauseStartMs >= NP_PAUSE_MS) {
        npState = NP_SCROLL_LEFT;
        npLastScrollMs = now;
      }
      break;

    case NP_SCROLL_LEFT:
      if (now - npLastScrollMs >= spd(SPD_NOWPLAYING)) {
        npLastScrollMs = now;
        npScrollPos--;
        if (npScrollPos <= 0) {
          npScrollPos = 0;
          npDrawAtPos(0);
          npState = NP_PAUSE_BEFORE_NEXT;
          npPauseStartMs = now;
        } else {
          npDrawAtPos(npScrollPos);
        }
      }
      break;

    case NP_PAUSE_BEFORE_NEXT:
      if (now - npPauseStartMs >= NP_PAUSE_MS) {

        npInit();
      }
      break;

    case NP_PAUSE_SHORT:
      if (now - npPauseStartMs >= 4000UL) {
        npInit();
      }
      break;
  }

  return true;
}

void rebuildNowPlayingBuf() {
  if (npDisplayMode == 1) {
    snprintf(nowPlayingBuf, sizeof(nowPlayingBuf), "%s", nowArtist);
  } else if (npDisplayMode == 2) {
    snprintf(nowPlayingBuf, sizeof(nowPlayingBuf), "%s", nowTitle);
  } else {
    snprintf(nowPlayingBuf, sizeof(nowPlayingBuf), "%s - %s", nowArtist, nowTitle);
  }
}

// WEATHER DISPLAY

static void wxBufAppend(const uint8_t* src, int n, bool gap = true) {
  for (int i = 0; i < n && wxColCount < 511; i++)
    wxColBuf[wxColCount++] = src[i];
  if (gap && wxColCount < 512)
    wxColBuf[wxColCount++] = 0x00;
}

static void wxBufAppendMD(char ch) {
  uint8_t tmp[8];
  uint8_t n = isPixelFont(fontTypeWeather)
                ? pixGetChar(fontTypeWeather, ch, sizeof(tmp), tmp)
                : mx.getChar(ch, sizeof(tmp), tmp);
  if (n == 0) return;
  for (int i = 0; i < n && wxColCount < 511; i++)
    wxColBuf[wxColCount++] = tmp[i];
  if (wxColCount < 512) wxColBuf[wxColCount++] = 0x00;
}

static void sanitizeUtf8(const char* src, char* dst, int dstLen) {
  int di = 0;
  int si = 0;
  while (src[si] != '\0' && di < dstLen - 1) {
    unsigned char c = (unsigned char)src[si];

    if (c < 0x80) {

      dst[di++] = (char)c;
      si++;
    } else if ((c & 0xE0) == 0xC0) {

      if ((unsigned char)src[si+1] == 0) { si++; continue; }
      uint32_t cp = ((c & 0x1F) << 6) | ((unsigned char)src[si+1] & 0x3F);
      si += 2;

      char rep = 0;
      switch (cp) {

        case 0x00E0: case 0x00E1: case 0x00E2: case 0x00E3:
        case 0x00E4: case 0x00E5: case 0x0103: case 0x01CE: rep = 'a'; break;
        case 0x00C0: case 0x00C1: case 0x00C2: case 0x00C3:
        case 0x00C4: case 0x00C5: case 0x0102: rep = 'A'; break;

        case 0x00E8: case 0x00E9: case 0x00EA: case 0x00EB: rep = 'e'; break;
        case 0x00C8: case 0x00C9: case 0x00CA: case 0x00CB: rep = 'E'; break;

        case 0x00EC: case 0x00ED: case 0x00EE: case 0x00EF:
        case 0x012B: rep = 'i'; break;
        case 0x00CC: case 0x00CD: case 0x00CE: case 0x00CF: rep = 'I'; break;

        case 0x00F2: case 0x00F3: case 0x00F4: case 0x00F5:
        case 0x00F6: case 0x00F8: rep = 'o'; break;
        case 0x00D2: case 0x00D3: case 0x00D4: case 0x00D5:
        case 0x00D6: case 0x00D8: rep = 'O'; break;

        case 0x00F9: case 0x00FA: case 0x00FB: case 0x00FC: rep = 'u'; break;
        case 0x00D9: case 0x00DA: case 0x00DB: case 0x00DC: rep = 'U'; break;

        case 0x015F: case 0x0219: rep = 's'; break;
        case 0x015E: case 0x0218: rep = 'S'; break;

        case 0x0163: case 0x021B: rep = 't'; break;
        case 0x0162: case 0x021A: rep = 'T'; break;

        case 0x00F1: rep = 'n'; break;
        case 0x00D1: rep = 'N'; break;

        case 0x00E7: rep = 'c'; break;
        case 0x00C7: rep = 'C'; break;

        case 0x00B0: rep = 0x01; break;

        default: rep = 0; break;
      }
      if (rep != 0 && rep != 0x01) dst[di++] = rep;

    } else if ((c & 0xF0) == 0xE0) {

      si += 3;

    } else if ((c & 0xF8) == 0xF0) {

      si += 4;

    } else {

      si++;
    }
  }
  dst[di] = '\0';
}

void weatherBuildBuffer() {
  wxColCount = 0;
  if (scrollIconInBuffer(scrollTypeWeather) && !hideIconWeather) {
    scrollBufPrependIcon(wxColBuf, wxColCount, 511, getWeatherIcon(), NP_ICON_COLS);
  }
  if (!weatherValid && !gWxSample) {

    for (int ci = 0; weatherBuf[ci] != '\0'; ci++) wxBufAppendMD(weatherBuf[ci]);
    if (wxColCount > 0) wxColCount--;
    return;
  }

  float       tempC = gWxSample ? 22.0f : weatherTempC;
  const char* desc  = gWxSample ? wxSampleDesc() : weatherDesc;
  int displayTemp = (tempUnit == 1) ? (int)(tempC * 9.0f / 5.0f + 32) : (int)tempC;
  bool neg        = (displayTemp < 0);
  int  absTemp    = abs(displayTemp);
  bool twoDigit   = (absTemp >= 10);

  bool wxTiko = isPixelFont(fontTypeWeather);   // Tiko or Mako
  uint8_t dtmp[TIKO_MAX_COLS]; int dw;

  int  hum      = gWxSample ? 54 : weatherHumidity;
  bool wantTemp = (wxPreset & WX_SHOW_TEMP) != 0;
  bool wantHum  = (wxPreset & WX_SHOW_HUM) != 0 && hum >= 0;
  bool wantDesc = (wxPreset & WX_SHOW_DESC) != 0 && desc[0] != '\0';
  // Everything the preset asked for is missing (humidity-only preset with no
  // humidity reading, say). Fall back to the temperature rather than leaving
  // the matrix blank for the whole slot.
  if (!wantTemp && !wantHum && !wantDesc) wantTemp = true;

  if (wantTemp) {
  if (neg) {
    static const uint8_t CHAR_MINUS[3] = {0x08, 0x08, 0x08};
    if (wxTiko) wxBufAppend(TIKO_MINUS, 3);
    else        wxBufAppend(CHAR_MINUS, 3);
  }

  if (wxTiko) {
    if (twoDigit) {
      wxBufAppend(digitGlyph(fontTypeWeather, absTemp / 10, dtmp, dw), dw);
      wxBufAppend(digitGlyph(fontTypeWeather, absTemp % 10, dtmp, dw), dw);
    } else {
      wxBufAppend(digitGlyph(fontTypeWeather, absTemp, dtmp, dw), dw);
    }
    wxBufAppend(pixDeg(fontTypeWeather), 3);
    uint8_t utmp[TIKO_MAX_COLS];
    uint8_t uw = pixGetChar(fontTypeWeather, (tempUnit == 1) ? 'F' : 'C', TIKO_MAX_COLS, utmp);
    wxBufAppend(utmp, uw);
  } else {
    if (twoDigit) {
      wxBufAppend(FONT[absTemp / 10], 5);
      wxBufAppend(FONT[absTemp % 10], 5);
    } else {
      wxBufAppend(FONT[absTemp], 5);
    }
    wxBufAppend(CHAR_DEG, 5);
    wxBufAppend((tempUnit == 1) ? CHAR_F : CHAR_C, 5);
  }
  }

  if (wantHum) {
    if (wantTemp) {
      if (wxColCount < 511) wxColBuf[wxColCount++] = 0x00;
      if (wxColCount < 511) wxColBuf[wxColCount++] = 0x00;
      if (wxColCount < 511) wxColBuf[wxColCount++] = 0x00;
    }
    int humTens = hum / 10;
    int humOnes = hum % 10;
    if (hum >= 100) {
      wxBufAppend(digitGlyph(fontTypeWeather, 1, dtmp, dw), dw);
      wxBufAppend(digitGlyph(fontTypeWeather, 0, dtmp, dw), dw);
      wxBufAppend(digitGlyph(fontTypeWeather, 0, dtmp, dw), dw);
    } else if (hum >= 10) {
      wxBufAppend(digitGlyph(fontTypeWeather, humTens, dtmp, dw), dw);
      wxBufAppend(digitGlyph(fontTypeWeather, humOnes, dtmp, dw), dw);
    } else {
      wxBufAppend(digitGlyph(fontTypeWeather, humOnes, dtmp, dw), dw);
    }
    wxBufAppendMD('%');
  }

  if (wantDesc) {
    if (wantTemp || wantHum) {
      if (wxColCount < 511) wxColBuf[wxColCount++] = 0x00;
      if (wxColCount < 511) wxColBuf[wxColCount++] = 0x00;
    }
    char cleanDesc[48];
    sanitizeUtf8(desc, cleanDesc, sizeof(cleanDesc));
    for (int ci = 0; cleanDesc[ci] != '\0' && wxColCount < 500; ci++)
      wxBufAppendMD(cleanDesc[ci]);
  }

  if (wxColCount > 0) wxColCount--;
}

void weatherDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  if (!hideIconWeather && !scrollIconInBuffer(scrollTypeWeather)) {

    const uint8_t* icon = getWeatherIcon();
    for (int col = 0; col < NP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (icon[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - NP_ICON_COLS, 0x00);
  }

  bool fullWidth = hideIconWeather || scrollIconInBuffer(scrollTypeWeather);
  int textOffset = fullWidth ? 0 : (NP_ICON_COLS + NP_SEP_COLS);
  int textCols   = fullWidth ? 32 : NP_TEXT_COLS;
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < wxColCount) ? wxColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }
  mxCommit();
}
void weatherInit() {

  if (!weatherValid) {
    snprintf(weatherBuf, sizeof(weatherBuf), "No data");
  }

  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < NP_DISPLAY_COLS; c++) mx.setColumn(c, 0x00);
  mxCommit();
  weatherBuildBuffer();
  wxScrollPos = 0;
  wxWrapPass = 0;
  weatherDrawAtPos(0);
  wxState = NP_PAUSE_BEFORE_RIGHT;
  wxPauseStartMs = millis();
}
// --- Non-blocking weather fetch --------------------------------------
// The HTTP request used to run directly inside loop(), which stalled the
// whole matrix (including any scroll in progress) for up to 5s (the
// http.setTimeout value) every WEATHER_FETCH_INTERVAL_MS. That is what
// caused tiles to randomly freeze mid-scroll and then resume where they
// left off. The actual network call now runs on its own FreeRTOS task
// (pinned to core 0) so loop() / the matrix refresh is never blocked.
// Results are staged in wxFetch* variables and only copied into the
// "live" weather* variables from loop() (same thread that reads them
// for drawing), so there is no risk of tearing a struct mid-read.
static TaskHandle_t  weatherFetchTaskHandle = nullptr;
static std::atomic<bool> weatherFetchBusy       = false;
static std::atomic<bool> weatherFetchReady      = false;
static volatile bool wxFetchOk              = false;
static float         wxFetchTempC;
static int           wxFetchHumidity;
static char          wxFetchDesc[sizeof(weatherDesc)];
static char          wxFetchCondition[sizeof(weatherCondition)];
static volatile uint8_t wxFetchErr = API_ERR_NONE;

static void weatherFetchTask(void* pv) {
  bool ok = false;
  uint8_t fail = API_ERR_NONE;
  if (strlen(weatherApiKey) > 0 && strlen(weatherCity) > 0 && WiFi.status() == WL_CONNECTED) {
    fail = API_ERR_NO_ANSWER;
    char url[256];
    snprintf(url, sizeof(url),
      "http://api.openweathermap.org/data/2.5/weather?lat=%.4f&lon=%.4f&appid=%s&units=metric&lang=%s",
      weatherLat, weatherLon, weatherApiKey, weatherLang);
    HTTPClient http;
    http.begin(url);
    http.setTimeout(5000);
    int code = http.GET();
    if (code == 200) {
      String payload = http.getString();
      StaticJsonDocument<1024> doc;
      DeserializationError err = deserializeJson(doc, payload);
      if (!err) {
        wxFetchTempC    = doc["main"]["temp"] | -999.0f;
        wxFetchHumidity = doc["main"]["humidity"] | -1;
        const char* desc = doc["weather"][0]["description"] | "";
        strncpy(wxFetchDesc, desc, sizeof(wxFetchDesc) - 1);
        wxFetchDesc[sizeof(wxFetchDesc)-1] = '\0';
        if (wxFetchDesc[0] >= 'a' && wxFetchDesc[0] <= 'z') wxFetchDesc[0] -= 32;

        const char* cond = doc["weather"][0]["main"] | "";
        strncpy(wxFetchCondition, cond, sizeof(wxFetchCondition) - 1);
        wxFetchCondition[sizeof(wxFetchCondition)-1] = '\0';
        ok = true;
        fail = API_ERR_NONE;
      }
    } else if (code == 401) {
      fail = API_ERR_REFUSED;
    } else if (code == 400 || code == 404) {
      fail = API_ERR_NOT_FOUND;
    }
    http.end();
  }
  wxFetchErr        = fail;
  wxFetchOk         = ok;
  weatherFetchReady = true;
  weatherFetchBusy  = false;
  vTaskDelete(nullptr);
}

void weatherFetch() {
  if (strlen(weatherApiKey) == 0) return;
  if (WiFi.status() != WL_CONNECTED) return;
  if (weatherFetchBusy || weatherFetchReady || backgroundFetchInFlight) return;

  lastWeatherFetch  = millis(); // stamp now so loop() doesn't re-trigger every iteration
  weatherFetchBusy  = true;
  weatherFetchReady = false;
  backgroundFetchInFlight = true;
  if (xTaskCreatePinnedToCore(weatherFetchTask, "wxFetch", 8192, nullptr, 1, &weatherFetchTaskHandle, 0) != pdPASS) {
    weatherFetchBusy = false;
    backgroundFetchInFlight = false;
  }
}

// Call from loop(); cheap check, only does work once a fetch has completed.
static void weatherFetchPoll() {
  if (!weatherFetchReady || weatherFetchBusy) return;
  weatherFetchReady = false;
  backgroundFetchInFlight = false;
  if (wxFetchOk) {
    weatherTempC    = wxFetchTempC;
    weatherHumidity = wxFetchHumidity;
    strncpy(weatherDesc, wxFetchDesc, sizeof(weatherDesc) - 1);
    weatherDesc[sizeof(weatherDesc)-1] = '\0';
    strncpy(weatherCondition, wxFetchCondition, sizeof(weatherCondition) - 1);
    weatherCondition[sizeof(weatherCondition)-1] = '\0';
    weatherValid = true;
    weatherErr   = API_ERR_NONE;
    weatherFails = 0;
  } else {
    apiNoteFailure(wxFetchErr, weatherValid, weatherErr, weatherFails);
    if (wxFetchErr == API_ERR_NO_ANSWER) lastWeatherFetch = apiRetryStamp(WEATHER_FETCH_INTERVAL_MS);
  }
}

// What the Tile Manager says about the weather tile (see API_ERR_*).
static uint8_t weatherErrCode() {
  if (weatherApiKey[0] == '\0') return API_ERR_NO_KEY;
  if (weatherCity[0] == '\0') return API_ERR_NO_TARGET;
  return weatherErr;
}

// CYCLE HELPERS

void sortItems() {
  for (int i = 0; i < NUM_ITEMS - 1; i++) {
    for (int j = i + 1; j < NUM_ITEMS; j++) {
      if (items[j].order < items[i].order) {
        CycleItem tmp = items[i];
        items[i] = items[j];
        items[j] = tmp;
      }
    }
  }
}

// Whether the circuit may show this slot right now. A tile taken out of the
// Tile Manager is switched off by applyTileHiddenMask(); Now Playing belongs to
// the priority list while it is there, and to nobody while nothing plays; the
// reminder needs a text.
static bool slotShowable(int idx) {
  if (idx < 0 || idx >= NUM_ITEMS || !items[idx].enabled) return false;
  if (items[idx].id == ITEM_NOW_PLAYING && (nowPlayingIsPriority || !nowPlayingActive)) return false;
  // A tile fed by an online service sits out until the service has answered,
  // rather than showing "No data" or "...": the weather without a key or a
  // first reading, YouTube before its count (no key or channel, or a refused
  // one), the exchange rate before its first rate. The fetches run whether the
  // tile is on screen or not, so each comes back once it has something.
  if (items[idx].id == ITEM_WEATHER && (strlen(weatherApiKey) == 0 || !weatherValid)) return false;
  if (items[idx].id == ITEM_YOUTUBE && !social[SOC_YT].valid) return false;
  if (items[idx].id == ITEM_CURRENCY && !currencyValid) return false;
  if (items[idx].id == ITEM_MEMENTO && strlen(mementoBuf) == 0) return false;
  // The countdown with no event ahead.
  if (items[idx].id == ITEM_COUNTDOWN && !countdownHasEvent()) return false;
  // MSI Afterburner's tile has nothing to show while its value is unavailable.
  if (items[idx].id == ITEM_AFTERBURNER && !abLive()) return false;
  return true;
}

int nextActiveSlot(int from) {
  for (int i = 1; i <= NUM_ITEMS; i++) {
    int idx = (from + i) % NUM_ITEMS;
    if (slotShowable(idx)) return idx;
  }
  return -1;
}

int prevActiveSlot(int from) {
  for (int i = 1; i <= NUM_ITEMS; i++) {
    int idx = ((from - i) % NUM_ITEMS + NUM_ITEMS) % NUM_ITEMS;
    if (slotShowable(idx)) return idx;
  }
  return -1;
}

// The tile the panel is on, by id as well as by index. /settings replaces the
// whole circuit list from the web task, so the same index can come to hold
// another tile - a switched-off one, or Now Playing while it lives among the
// priority tiles - and every redraw used to draw whatever was there.
static uint8_t currentSlotId = 0xFF;
// Nothing in the circuit could be shown, and the panel was cleared for it.
static bool gCircuitBlank = false;

static void setCurrentSlot(int idx) {
  currentSlot = (uint8_t)idx;
  currentSlotId = items[idx].id;
}

// Puts currentSlot back on the tile it was entered with if the list moved
// under it, and says whether that tile may still be shown.
static bool followCurrentSlot() {
  if (currentSlotId != 0xFF && items[currentSlot].id != currentSlotId) {
    for (int i = 0; i < NUM_ITEMS; i++) {
      if (items[i].id == currentSlotId) { currentSlot = (uint8_t)i; break; }
    }
  }
  currentSlotId = items[currentSlot].id;
  return slotShowable(currentSlot);
}

// True while Octoglow Connect streams a lit frame and the tile is switched on
// and in the manager. The stream's timeout is applied here rather than only in
// vmPriorityTick(): while a higher tile held the panel that tick never ran, the
// stale "active" stayed, and the next font or icon change drew an old frame.
static bool vmLive() {
  bool active;
  portENTER_CRITICAL(&vmMux);
  if (vmActive && millis() - vmLastPacketMs > 1500UL) vmActive = false;
  active = vmActive;
  portEXIT_CRITICAL(&vmMux);
  return active && voicemeeterEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_VOICEMEETER));
}

// The same for Browser Downloads, with its own six-second timeout.
static bool browserDownloadsLive() {
  bool active;
  portENTER_CRITICAL(&browserDownloadMux);
  if (browserDownloadActive && millis() - browserDownloadLastMs > 6000UL) browserDownloadActive = false;
  active = browserDownloadActive;
  portEXIT_CRITICAL(&browserDownloadMux);
  return active && browserDownloadsEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_BROWSER_DOWNLOADS));
}

// ETS2 only goes stale when its own tick could not run; judged without
// changing anything, so ets2Tick() still hands the panel back itself.
static inline bool ets2Live() {
  return ets2Active && millis() - lastEts2Ms <= ETS2_TIMEOUT_MS;
}

// The 32 columns the panel shows for a visualizer frame from Octoglow Connect:
// the icon, then the frame squeezed beside it - or the frame alone.
static void vmComposeFrame(const uint8_t source[32], uint8_t output[32]) {
  memset(output, 0, 32);
  if (hideIconVoicemeeter) {
    memcpy(output, source, sizeof(output));
  } else {
    const uint8_t* icon = resolveTileIcon(iconSelVoicemeeter, speakerIcon);
    for (int col = 0; col < 8; col++) {
      uint8_t value = 0;
      for (int row = 0; row < 8; row++) {
        if (icon[row] & (0x80 >> col)) value |= (1u << row);
      }
      output[col] = value;
    }
    output[8] = 0;
    // Keep the whole visualizer visible beside the icon by resampling its 32
    // logical columns into the remaining 23 physical columns.
    for (int col = 9; col < 32; col++) output[col] = source[((col - 9) * 32) / 23];
  }
}

static void vmDrawLatestFrame() {
  uint8_t source[32];
  portENTER_CRITICAL(&vmMux);
  memcpy(source, vmFrame, sizeof(source));
  vmFrameDirty = false;
  portEXIT_CRITICAL(&vmMux);

  uint8_t output[32];
  vmComposeFrame(source, output);

  mx.update(MD_MAX72XX::OFF);
  for (int col = 0; col < 32; col++) mx.setColumn(31 - col, output[col]);
  mxCommit();
}

bool vmPriorityTick() {
  bool active = vmLive();
  bool dirty;
  portENTER_CRITICAL(&vmMux);
  dirty = vmFrameDirty;
  portEXIT_CRITICAL(&vmMux);
  if (!active) {
    if (vmWasActive) {
      vmWasActive = false;
      resumeCircuitTileFromPriority();
    }
    return false;
  }

  if (!vmWasActive) {
    vmWasActive = true;
    beginC2PCapture();
    vmDrawLatestFrame();
    finishC2PTransition(tileTransVm, tileTransSpd[TRSPD_VM]);
    return true;
  }
  if (dirty) vmDrawLatestFrame();
  return true;
}

// PROGRESS BAR STYLES
//
// A progress tile's 32 columns, bit y = row y: its icon in columns 0-7 (none
// when hidden), then the bar in the tile's style. Progress lights the two
// middle rows from column 10 (1 without the icon) to 31 (30), rounded up, so
// anything above 0% shows. The framed styles draw rows 2 and 5 from column 10
// (0) to 31 with the ends closed, and fill the two middle rows inside rounded
// down - full only when the job is - but at least one column above 0%, as
// qBittorrent's bar always did. Cutted Edges leaves the frame's four corners off.
static void progressBarFrame(uint8_t style, uint8_t progress, const uint8_t* icon, uint8_t output[32]) {
  memset(output, 0, 32);
  if (progress > 100) progress = 100;
  if (icon) {
    for (int col = 0; col < 8; col++) {
      uint8_t value = 0;
      for (int row = 0; row < 8; row++) {
        if (icon[row] & (0x80 >> col)) value |= (1u << row);
      }
      output[col] = value;
    }
  }
  if (style == BAR_STYLE_PROGRESS) {
    const int barStart = icon ? 10 : 1;
    const int barWidth = icon ? 22 : 30;
    int filled = (progress * barWidth + 99) / 100;
    for (int col = 0; col < filled; col++) output[barStart + col] |= (1u << 3) | (1u << 4);
    return;
  }
  const int left = icon ? 10 : 0;
  const int right = 31;
  for (int col = left; col <= right; col++) output[col] |= (1u << 2) | (1u << 5);
  output[left] |= 0x3C;   // rows 2-5
  output[right] |= 0x3C;
  if (style == BAR_STYLE_UNDERLINE_CUT) {
    output[left] &= (uint8_t)~((1u << 2) | (1u << 5));
    output[right] &= (uint8_t)~((1u << 2) | (1u << 5));
  }
  const int inner = right - left - 1;
  int filled = (int)progress * inner / 100;
  if (progress > 0 && filled == 0) filled = 1;
  for (int col = 0; col < filled; col++) output[left + 1 + col] |= (1u << 3) | (1u << 4);
}

static void browserDownloadsDraw() {
  uint8_t progress;
  portENTER_CRITICAL(&browserDownloadMux);
  progress = browserDownloadProgress;
  browserDownloadDirty = false;
  portEXIT_CRITICAL(&browserDownloadMux);

  uint8_t output[32];
  progressBarFrame(barStyleBrowserDownloads, progress,
                   hideIconBrowserDownloads ? nullptr : resolveTileIcon(iconSelBrowserDownloads, downloadArrowIcon), output);

  mx.update(MD_MAX72XX::OFF);
  for (int col = 0; col < 32; col++) mx.setColumn(31 - col, output[col]);
  mxCommit();
}

bool browserDownloadsPriorityTick() {
  bool active = browserDownloadsLive();
  bool dirty;
  portENTER_CRITICAL(&browserDownloadMux);
  dirty = browserDownloadDirty;
  portEXIT_CRITICAL(&browserDownloadMux);
  if (!active) {
    if (browserDownloadWasActive) {
      browserDownloadWasActive = false;
      resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!browserDownloadWasActive) {
    browserDownloadWasActive = true;
    beginC2PCapture();
    browserDownloadsDraw();
    finishC2PTransition(tileTransBrowserDownloads, tileTransSpd[TRSPD_BROWSER_DOWNLOADS]);
    return true;
  }
  if (dirty) browserDownloadsDraw();
  return true;
}

// QBITTORRENT DISPLAY
//
// The icon, then the bar in the tile's style - by default in a frame, which
// shows where 100% is: a torrent can take hours, and a bar with no end in
// sight says little. With the icon hidden the bar takes the whole width.

static bool qbitLive() {
  bool active;
  portENTER_CRITICAL(&qbitMux);
  if (qbitActive && millis() - qbitLastMs > QBIT_TIMEOUT_MS) qbitActive = false;
  active = qbitActive;
  portEXIT_CRITICAL(&qbitMux);
  return active && qbitEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_QBIT));
}

// The 32 columns, bit y = row y, as the panel takes them.
static void qbitFrame(uint8_t progress, uint8_t output[32]) {
  progressBarFrame(barStyleQbit, progress, hideIconQbit ? nullptr : resolveTileIcon(iconSelQbit, downloadArrowIcon), output);
}

static void qbitDraw() {
  uint8_t progress;
  portENTER_CRITICAL(&qbitMux);
  progress = qbitProgress;
  qbitDirty = false;
  portEXIT_CRITICAL(&qbitMux);
  uint8_t output[32];
  qbitFrame(progress, output);
  mx.update(MD_MAX72XX::OFF);
  for (int col = 0; col < 32; col++) mx.setColumn(31 - col, output[col]);
  mxCommit();
}

bool qbitPriorityTick() {
  bool active = qbitLive();
  bool dirty;
  portENTER_CRITICAL(&qbitMux);
  dirty = qbitDirty;
  portEXIT_CRITICAL(&qbitMux);
  if (!active) {
    if (qbitWasActive) {
      qbitWasActive = false;
      // Handed back only if it was the tile on the panel: a tile ranked above
      // it that just ended has put the circuit back already.
      if (strcmp(gLivePrio, "qbittorrent") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!qbitWasActive) {
    qbitWasActive = true;
    beginC2PCapture();
    qbitDraw();
    finishC2PTransition(tileTransQbit, tileTransSpd[TRSPD_QBIT]);
    return true;
  }
  // A new percent, or the panel back from a tile ranked above it.
  if (dirty || strcmp(gLivePrio, "qbittorrent") != 0) qbitDraw();
  return true;
}

// BAMBU PRINT DISPLAY
static bool bambuLive() {
  bool active;
  portENTER_CRITICAL(&bambuMux);
  if (bambuActive && millis() - bambuLastMs > BAMBU_TIMEOUT_MS) bambuActive = false;
  active = bambuActive;
  portEXIT_CRITICAL(&bambuMux);
  return active && bambuEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_BAMBU));
}

static void bambuDraw() {
  uint8_t progress, kind;
  portENTER_CRITICAL(&bambuMux);
  progress = bambuProgress;
  kind = bambuKind;
  bambuDirty = false;
  portEXIT_CRITICAL(&bambuMux);
  uint8_t output[32];
  const uint8_t* fallback = kind == 1 ? warningIcon : kind == 2 ? infoIcon : objectFocusIcon;
  progressBarFrame(barStyleBambu, progress, hideIconBambu ? nullptr : resolveTileIcon(iconSelBambu, fallback), output);
  mx.update(MD_MAX72XX::OFF);
  for (int col = 0; col < 32; col++) mx.setColumn(31 - col, output[col]);
  mxCommit();
}

bool bambuPriorityTick() {
  bool active = bambuLive();
  bool dirty;
  portENTER_CRITICAL(&bambuMux);
  dirty = bambuDirty;
  portEXIT_CRITICAL(&bambuMux);
  if (!active) {
    if (bambuWasActive) {
      bambuWasActive = false;
      if (strcmp(gLivePrio, "bambu") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!bambuWasActive) {
    bambuWasActive = true;
    beginC2PCapture();
    bambuDraw();
    finishC2PTransition(tileTransBambu, tileTransSpd[TRSPD_BAMBU]);
    return true;
  }
  if (dirty || strcmp(gLivePrio, "bambu") != 0) bambuDraw();
  return true;
}

// Volume events expire three seconds after the latest update, including across millis wrap.
static bool volumeSnapshot(int& packed) {
  portENTER_CRITICAL(&volumeMux);
  packed = volumePacked;
  bool live = volumeReceived && (uint32_t)(millis() - volumeLastMs) < 3000;
  portEXIT_CRITICAL(&volumeMux);
  return live;
}
static bool volumeLive() {
  int packed;
  return volumeEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_VOLUME)) && volumeSnapshot(packed);
}

// GAME FPS DISPLAY
//
// "FPS 144" exactly as the MSI Afterburner tile draws its FPS mode, but in
// this tile's own font, icon and scroll, and only while a game is played. The
// number changes about once a second: text that fits is redrawn centred, text
// that scrolls keeps its place, so the line never jumps back to its start.

static uint8_t  gfpsColBuf[96];
static int      gfpsColCount = 0;
static int      gfpsScrollPos = 0;
static int      gfpsWrapPass = 0;
static unsigned long gfpsLastScrollMs = 0;
static unsigned long gfpsPauseMs = 0;
static NpState  gfpsState = NP_SHOW_START;
static int      gfpsDrawnFps = AB_UNDRAWN;

// Info, as the Afterburner tile's FPS has, unless another icon was picked.
static inline const uint8_t* gfpsIcon() { return resolveTileIcon(iconSelGameFps, infoIcon); }
static inline bool gfpsFullWidth() { return hideIconGameFps || scrollIconInBuffer(scrollTypeGameFps); }
static inline int  gfpsTextCols()  { return gfpsFullWidth() ? 32 : AB_TEXT_COLS; }

static void gfpsBuild(int fps) {
  gfpsColCount = abBuildText(gfpsColBuf, (int)sizeof(gfpsColBuf), AB_MODE_FPS, fps, fontTypeGameFps,
                             (!hideIconGameFps && scrollIconInBuffer(scrollTypeGameFps)) ? gfpsIcon() : nullptr);
}

static void gfpsDrawAtPos(int pos) {
  abDrawCols(gfpsColBuf, gfpsColCount, pos, gfpsFullWidth() ? nullptr : gfpsIcon(), gfpsTextCols());
}

static void gfpsDrawCentred() {
  int offset = (gfpsTextCols() - gfpsColCount) / 2;
  gfpsDrawAtPos(-(offset < 0 ? 0 : offset));
}

// From the start: still and centred when it fits, otherwise from its first
// column, with the usual pause before it moves.
static void gfpsStart(unsigned long now) {
  gfpsScrollPos = 0;
  gfpsWrapPass = 0;
  if (gfpsColCount <= gfpsTextCols()) {
    gfpsState = NP_SHOW_START;
    gfpsDrawCentred();
  } else {
    gfpsState = NP_PAUSE_BEFORE_RIGHT;
    gfpsPauseMs = now;
    gfpsDrawAtPos(0);
  }
}

void gameFpsInit() {
  int fps;
  if (!abGameFps(fps)) {
    // Only the instant the frame rate ran out between the check and here; the
    // next pass hands the panel back.
    mx.update(MD_MAX72XX::OFF);
    for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
    mxCommit();
    gfpsDrawnFps = AB_UNDRAWN;
    gfpsState = NP_SHOW_START;
    return;
  }
  gfpsDrawnFps = fps;
  gfpsBuild(fps);
  gfpsStart(millis());
}

// A new frame rate, drawn in place.
static void gfpsRefresh(unsigned long now) {
  int fps;
  if (!abGameFps(fps) || fps == gfpsDrawnFps) return;
  gfpsDrawnFps = fps;
  gfpsBuild(fps);
  int textCols = gfpsTextCols();
  if (gfpsColCount <= textCols) {
    gfpsState = NP_SHOW_START;
    gfpsDrawCentred();
    return;
  }
  if (gfpsState == NP_SHOW_START) {
    gfpsStart(now);
    return;
  }
  if (scrollIsWrap(scrollTypeGameFps)) {
    if (gfpsScrollPos > gfpsColCount) gfpsScrollPos = gfpsColCount;
  } else if (gfpsScrollPos > gfpsColCount - textCols) {
    gfpsScrollPos = gfpsColCount - textCols;
  }
  gfpsDrawAtPos(gfpsScrollPos);
}

static void gfpsTickRender() {
  unsigned long now = millis();
  gfpsRefresh(now);
  int textCols = gfpsTextCols();
  switch (gfpsState) {
    case NP_PAUSE_BEFORE_RIGHT:
      if (now - gfpsPauseMs >= NP_PAUSE_MS) {
        gfpsState = scrollIsWrap(scrollTypeGameFps) ? NP_SCROLL_WRAP : NP_SCROLL_RIGHT;
        gfpsWrapPass = 0;
        gfpsLastScrollMs = now;
      }
      break;
    case NP_SCROLL_WRAP:
      if (now - gfpsLastScrollMs >= spd(SPD_GAMEFPS)) {
        gfpsLastScrollMs = now;
        gfpsScrollPos++;
        if (gfpsScrollPos >= gfpsColCount) {
          if (++gfpsWrapPass >= SCROLL_WRAP_PASSES) {
            gfpsStart(now);
            break;
          }
          gfpsScrollPos = -textCols;
        }
        gfpsDrawAtPos(gfpsScrollPos);
      }
      break;
    case NP_SCROLL_RIGHT: {
      int maxPos = gfpsColCount - textCols;
      if (maxPos <= 0) {
        gfpsStart(now);
        break;
      }
      if (now - gfpsLastScrollMs >= spd(SPD_GAMEFPS)) {
        gfpsLastScrollMs = now;
        gfpsScrollPos++;
        if (gfpsScrollPos >= maxPos) {
          gfpsScrollPos = maxPos;
          gfpsState = NP_PAUSE_AFTER_RIGHT;
          gfpsPauseMs = now;
        }
        gfpsDrawAtPos(gfpsScrollPos);
      }
      break;
    }
    case NP_PAUSE_AFTER_RIGHT:
      if (now - gfpsPauseMs >= NP_PAUSE_MS) {
        gfpsState = NP_SCROLL_LEFT;
        gfpsLastScrollMs = now;
      }
      break;
    case NP_SCROLL_LEFT:
      if (now - gfpsLastScrollMs >= spd(SPD_GAMEFPS)) {
        gfpsLastScrollMs = now;
        if (--gfpsScrollPos <= 0) {
          gfpsScrollPos = 0;
          gfpsState = NP_PAUSE_BEFORE_RIGHT;
          gfpsPauseMs = now;
        }
        gfpsDrawAtPos(gfpsScrollPos);
      }
      break;
    default:
      // Centred and still: nothing moves until the number changes.
      break;
  }
}

bool gameFpsPriorityTick() {
  if (!gameFpsLive()) {
    if (gameFpsWasActive) {
      gameFpsWasActive = false;
      // Handed back only if it was the tile on the panel: a tile ranked above
      // it that just ended has put the circuit back already.
      if (strcmp(gLivePrio, "gamefps") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!gameFpsWasActive) {
    gameFpsWasActive = true;
    beginC2PCapture();
    gameFpsInit();
    finishC2PTransition(tileTransGameFps, tileTransSpd[TRSPD_GAMEFPS]);
    return true;
  }
  // The panel back from a tile ranked above it: drawn again from the start.
  if (strcmp(gLivePrio, "gamefps") != 0) {
    gameFpsInit();
    return true;
  }
  gfpsTickRender();
  return true;
}

// VOLUME CHANGE DISPLAY
static uint8_t  volColBuf[96];
static int      volColCount = 0;
static int      volScrollPos = 0;
static int      volWrapPass = 0;
static unsigned long volLastScrollMs = 0;
static unsigned long volPauseMs = 0;
static NpState  volState = NP_SHOW_START;
static int      volDrawnFps = AB_UNDRAWN;

// Info, as the Afterburner tile's FPS has, unless another icon was picked.
static inline const uint8_t* volIcon() { return resolveTileIcon(volumeAdaptiveIcon ? 0 : iconSelVolume,
  volumeAdaptiveIcon ? (((volDrawnFps >> 8) & 3) == 2 ? bluetoothIcon :
                       ((volDrawnFps >> 8) & 3) == 1 ? headphonesIcon : speakerBoxIcon) : speakerBoxIcon); }
static inline bool volFullWidth() { return hideIconVolume || scrollIconInBuffer(scrollTypeVolume); }
static inline int  volTextCols()  { return volFullWidth() ? 32 : AB_TEXT_COLS; }

static void volBuild(int fps) {
  volDrawnFps = fps;
  volColCount = 0;
  if (!hideIconVolume && scrollIconInBuffer(scrollTypeVolume))
    scrollBufPrependIcon(volColBuf, volColCount, sizeof(volColBuf)-1, volIcon(), TEMP_ICON_COLS);
  char text[8];
  if (fps & 128) strcpy(text, "Mute");
  else snprintf(text, sizeof(text), "%d%%", fps & 127);
  for (const char* p = text; *p; ++p)
    appendGlyphAuto(volColBuf, volColCount, sizeof(volColBuf), *p, fontTypeVolume);
}

static void volDrawAtPos(int pos) {
  abDrawCols(volColBuf, volColCount, pos, volFullWidth() ? nullptr : volIcon(), volTextCols());
}

static void volDrawCentred() {
  int offset = (volTextCols() - volColCount) / 2;
  volDrawAtPos(-(offset < 0 ? 0 : offset));
}

// From the start: still and centred when it fits, otherwise from its first
// column, with the usual pause before it moves.
static void volStart(unsigned long now) {
  volScrollPos = 0;
  volWrapPass = 0;
  if (volColCount <= volTextCols()) {
    volState = NP_SHOW_START;
    volDrawCentred();
  } else {
    volState = NP_PAUSE_BEFORE_RIGHT;
    volPauseMs = now;
    volDrawAtPos(0);
  }
}

void volumeInit() {
  int fps;
  if (!volumeSnapshot(fps)) {
    // Only the instant the frame rate ran out between the check and here; the
    // next pass hands the panel back.
    mx.update(MD_MAX72XX::OFF);
    for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
    mxCommit();
    volDrawnFps = AB_UNDRAWN;
    volState = NP_SHOW_START;
    return;
  }
  volDrawnFps = fps;
  volBuild(fps);
  volStart(millis());
}

// A new frame rate, drawn in place.
static void volRefresh(unsigned long now) {
  int fps;
  if (!volumeSnapshot(fps) || fps == volDrawnFps) return;
  volDrawnFps = fps;
  volBuild(fps);
  int textCols = volTextCols();
  if (volColCount <= textCols) {
    volState = NP_SHOW_START;
    volDrawCentred();
    return;
  }
  if (volState == NP_SHOW_START) {
    volStart(now);
    return;
  }
  if (scrollIsWrap(scrollTypeVolume)) {
    if (volScrollPos > volColCount) volScrollPos = volColCount;
  } else if (volScrollPos > volColCount - textCols) {
    volScrollPos = volColCount - textCols;
  }
  volDrawAtPos(volScrollPos);
}

static void volTickRender() {
  unsigned long now = millis();
  volRefresh(now);
  int textCols = volTextCols();
  switch (volState) {
    case NP_PAUSE_BEFORE_RIGHT:
      if (now - volPauseMs >= NP_PAUSE_MS) {
        volState = scrollIsWrap(scrollTypeVolume) ? NP_SCROLL_WRAP : NP_SCROLL_RIGHT;
        volWrapPass = 0;
        volLastScrollMs = now;
      }
      break;
    case NP_SCROLL_WRAP:
      if (now - volLastScrollMs >= spd(SPD_VOLUME)) {
        volLastScrollMs = now;
        volScrollPos++;
        if (volScrollPos >= volColCount) {
          if (++volWrapPass >= SCROLL_WRAP_PASSES) {
            volStart(now);
            break;
          }
          volScrollPos = -textCols;
        }
        volDrawAtPos(volScrollPos);
      }
      break;
    case NP_SCROLL_RIGHT: {
      int maxPos = volColCount - textCols;
      if (maxPos <= 0) {
        volStart(now);
        break;
      }
      if (now - volLastScrollMs >= spd(SPD_VOLUME)) {
        volLastScrollMs = now;
        volScrollPos++;
        if (volScrollPos >= maxPos) {
          volScrollPos = maxPos;
          volState = NP_PAUSE_AFTER_RIGHT;
          volPauseMs = now;
        }
        volDrawAtPos(volScrollPos);
      }
      break;
    }
    case NP_PAUSE_AFTER_RIGHT:
      if (now - volPauseMs >= NP_PAUSE_MS) {
        volState = NP_SCROLL_LEFT;
        volLastScrollMs = now;
      }
      break;
    case NP_SCROLL_LEFT:
      if (now - volLastScrollMs >= spd(SPD_VOLUME)) {
        volLastScrollMs = now;
        if (--volScrollPos <= 0) {
          volScrollPos = 0;
          volState = NP_PAUSE_BEFORE_RIGHT;
          volPauseMs = now;
        }
        volDrawAtPos(volScrollPos);
      }
      break;
    default:
      // Centred and still: nothing moves until the number changes.
      break;
  }
}

bool volumePriorityTick() {
  if (!volumeLive()) {
    if (volumeWasActive) {
      volumeWasActive = false;
      // Handed back only if it was the tile on the panel: a tile ranked above
      // it that just ended has put the circuit back already.
      if (strcmp(gLivePrio, "volume") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!volumeWasActive) {
    volumeWasActive = true;
    beginC2PCapture();
    volumeInit();
    finishC2PTransition(tileTransVolume, tileTransSpd[TRSPD_VOLUME]);
    return true;
  }
  // The panel back from a tile ranked above it: drawn again from the start.
  if (strcmp(gLivePrio, "volume") != 0) {
    volumeInit();
    return true;
  }
  volTickRender();
  return true;
}

// OBS STUDIO DISPLAY
//
// REC or LIVE, with how long it has been going when that is switched on,
// beside the Camera Record icon. Streaming outranks recording: with both going
// the tile says LIVE and counts the stream's time. The text is rebuilt only
// when what Connect sent changes - once a second while the duration shows -
// and scrolls the way the other text tiles do when it does not fit.

#define OBS_COL_MAX 128
static uint8_t  obsColBuf[OBS_COL_MAX];
static int      obsColCount = 0;
static int      obsScrollPos = 0;
static int      obsWrapPass = 0;
static unsigned long obsLastScrollMs = 0;
static unsigned long obsPauseMs = 0;
static NpState  obsState = NP_SHOW_START;
static char     obsShownText[24] = "";

// True while Connect says OBS streams or records and said so within
// OBS_TIMEOUT_MS. Past that the state is dropped here, so whatever asks next -
// the loop, the priority check, a redraw - finds the tile gone.
static bool obsLive() {
  bool active;
  portENTER_CRITICAL(&obsMux);
  if (obsActive && millis() - obsLastMs > OBS_TIMEOUT_MS) obsActive = false;
  active = obsActive;
  portEXIT_CRITICAL(&obsMux);
  return active && obsEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_OBS));
}

// "REC 5:42", "LIVE 1:35:05", or just the label with the duration switched off.
static void obsFormatText(char* out, size_t size) {
  bool streaming;
  uint32_t elapsedMs;
  portENTER_CRITICAL(&obsMux);
  streaming = obsStreaming;
  elapsedMs = obsElapsedMs;
  obsDirty = false;
  portEXIT_CRITICAL(&obsMux);
  const char* label = streaming ? "LIVE" : "REC";
  if (!obsShowElapsed) {
    snprintf(out, size, "%s", label);
    return;
  }
  uint32_t total = elapsedMs / 1000UL;
  unsigned long hours = (unsigned long)(total / 3600UL);
  unsigned minutes = (unsigned)((total / 60UL) % 60UL);
  unsigned seconds = (unsigned)(total % 60UL);
  if (hours > 0) snprintf(out, size, "%s %lu:%02u:%02u", label, hours, minutes, seconds);
  else           snprintf(out, size, "%s %u:%02u", label, minutes, seconds);
}

static inline bool obsFullWidth() { return hideIconObs || scrollIconInBuffer(scrollTypeObs); }
static inline int  obsTextCols()  { return obsFullWidth() ? NP_DISPLAY_COLS : NP_TEXT_COLS; }
static inline int  obsCenteredPos() { return -((obsTextCols() - obsColCount) / 2); }

static void obsBuildBuffer(const char* text) {
  obsColCount = 0;
  if (scrollIconInBuffer(scrollTypeObs) && !hideIconObs) {
    scrollBufPrependIcon(obsColBuf, obsColCount, OBS_COL_MAX,
                         resolveTileIcon(iconSelObs, cameraRecordIcon), NP_ICON_COLS);
  }
  for (int ci = 0; text[ci] != '\0' && obsColCount < OBS_COL_MAX - 12; ci++) {
    appendGlyphAuto(obsColBuf, obsColCount, OBS_COL_MAX, text[ci], fontTypeObs);
  }
  if (obsColCount > 0) obsColCount--;
}

// Icon in the 8 columns on the left and one blank column after it, the text in
// the rest; the whole width is the text's when the icon is hidden or scrolls
// along with it.
static void obsDrawAtPos(int pos) {
  mx.update(MD_MAX72XX::OFF);
  bool fullWidth = obsFullWidth();
  if (!fullWidth) {
    const uint8_t* icon = resolveTileIcon(iconSelObs, cameraRecordIcon);
    for (int col = 0; col < NP_ICON_COLS; col++) {
      uint8_t colVal = 0;
      for (int row = 0; row < 8; row++) {
        if (icon[row] & (0x80 >> col)) colVal |= (1 << row);
      }
      mx.setColumn(31 - col, colVal);
    }
    mx.setColumn(31 - NP_ICON_COLS, 0x00);
  }
  int textOffset = fullWidth ? 0 : (NP_ICON_COLS + NP_SEP_COLS);
  int textCols   = obsTextCols();
  for (int col = 0; col < textCols; col++) {
    int src = pos + col;
    uint8_t val = (src >= 0 && src < obsColCount) ? obsColBuf[src] : 0x00;
    mx.setColumn(31 - (textOffset + col), val);
  }
  mxCommit();
}

// Starts the text over: centred when it fits, otherwise from its first column
// with the usual pause before it moves.
static void obsStartText(unsigned long now) {
  obsScrollPos = 0;
  obsWrapPass = 0;
  if (obsColCount <= obsTextCols()) {
    obsState = NP_SHOW_START;
    obsDrawAtPos(obsCenteredPos());
  } else {
    obsState = NP_PAUSE_BEFORE_RIGHT;
    obsPauseMs = now;
    obsDrawAtPos(0);
  }
}

void obsInit() {
  obsFormatText(obsShownText, sizeof(obsShownText));
  obsBuildBuffer(obsShownText);
  obsStartText(millis());
}

// A new second, or LIVE where REC was. Text that fits is redrawn centred; text
// that scrolls keeps its place, so the numbers change under the reader rather
// than the line jumping back to its start.
static void obsTextChanged(unsigned long now) {
  obsBuildBuffer(obsShownText);
  int textCols = obsTextCols();
  if (obsColCount <= textCols) {
    obsState = NP_SHOW_START;
    obsDrawAtPos(obsCenteredPos());
    return;
  }
  if (obsState == NP_SHOW_START) {
    obsStartText(now);
    return;
  }
  if (scrollIsWrap(scrollTypeObs)) {
    if (obsScrollPos > obsColCount) obsScrollPos = obsColCount;
  } else if (obsScrollPos > obsColCount - textCols) {
    obsScrollPos = obsColCount - textCols;
  }
  obsDrawAtPos(obsScrollPos);
}

static void obsTickRender() {
  unsigned long now = millis();
  bool dirty;
  portENTER_CRITICAL(&obsMux);
  dirty = obsDirty;
  portEXIT_CRITICAL(&obsMux);
  if (dirty) {
    char text[sizeof(obsShownText)];
    obsFormatText(text, sizeof(text));
    if (strcmp(text, obsShownText) != 0) {
      strcpy(obsShownText, text);
      obsTextChanged(now);
    }
  }

  int textCols = obsTextCols();
  switch (obsState) {
    case NP_PAUSE_BEFORE_RIGHT:
      if (now - obsPauseMs >= NP_PAUSE_MS) {
        obsState = scrollIsWrap(scrollTypeObs) ? NP_SCROLL_WRAP : NP_SCROLL_RIGHT;
        obsWrapPass = 0;
        obsLastScrollMs = now;
      }
      break;
    case NP_SCROLL_WRAP:
      if (now - obsLastScrollMs >= spd(SPD_OBS)) {
        obsLastScrollMs = now;
        obsScrollPos++;
        if (obsScrollPos >= obsColCount) {
          if (++obsWrapPass >= SCROLL_WRAP_PASSES) {
            obsStartText(now);
            break;
          }
          obsScrollPos = -textCols;
        }
        obsDrawAtPos(obsScrollPos);
      }
      break;
    case NP_SCROLL_RIGHT: {
      int maxPos = obsColCount - textCols;
      if (maxPos <= 0) {
        obsStartText(now);
        break;
      }
      if (now - obsLastScrollMs >= spd(SPD_OBS)) {
        obsLastScrollMs = now;
        obsScrollPos++;
        if (obsScrollPos >= maxPos) {
          obsScrollPos = maxPos;
          obsState = NP_PAUSE_AFTER_RIGHT;
          obsPauseMs = now;
        }
        obsDrawAtPos(obsScrollPos);
      }
      break;
    }
    case NP_PAUSE_AFTER_RIGHT:
      if (now - obsPauseMs >= NP_PAUSE_MS) {
        obsState = NP_SCROLL_LEFT;
        obsLastScrollMs = now;
      }
      break;
    case NP_SCROLL_LEFT:
      if (now - obsLastScrollMs >= spd(SPD_OBS)) {
        obsLastScrollMs = now;
        if (--obsScrollPos <= 0) {
          obsScrollPos = 0;
          obsState = NP_PAUSE_BEFORE_RIGHT;
          obsPauseMs = now;
        }
        obsDrawAtPos(obsScrollPos);
      }
      break;
    default:
      // Centred and still: nothing to move until the text changes.
      break;
  }
}

bool obsPriorityTick() {
  if (!obsLive()) {
    if (obsWasActive) {
      obsWasActive = false;
      resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!obsWasActive) {
    obsWasActive = true;
    beginC2PCapture();
    obsInit();
    finishC2PTransition(tileTransObs, tileTransSpd[TRSPD_OBS]);
    return true;
  }
  obsTickRender();
  return true;
}

// BEAMNG.DRIVE DISPLAY

static uint8_t beamngColBuf[64];
static int     beamngColCount = 0;
static int     beamngDrawnKmh = -1;
static bool    beamngDrawnMph = false;

// True while Connect sends speeds and said so within BEAMNG_TIMEOUT_MS; past
// that the state is dropped here, so whatever asks next agrees.
static bool beamngLive() {
  bool active;
  portENTER_CRITICAL(&beamngMux);
  if (beamngActive && millis() - beamngLastMs > BEAMNG_TIMEOUT_MS) beamngActive = false;
  active = beamngActive;
  portEXIT_CRITICAL(&beamngMux);
  return active && beamngEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_BEAMNG));
}

static int beamngSpeedNow() {
  portENTER_CRITICAL(&beamngMux);
  int kmh = beamngSpeedKmh;
  portEXIT_CRITICAL(&beamngMux);
  return kmh;
}

void beamngInit() {
  int kmh = beamngSpeedNow();
  speedBuildFrame(beamngColBuf, beamngColCount, kmh, beamngSpeedMph, fontTypeBeamng, beamngGauge, beamngGaugeFull);
  speedDraw(beamngColBuf, beamngColCount);
  beamngDrawnKmh = kmh;
  beamngDrawnMph = beamngSpeedMph;
}

bool beamngPriorityTick() {
  if (!beamngLive()) {
    if (beamngWasActive) {
      beamngWasActive = false;
      // Handed back only if it was the tile on the panel: a tile ranked above
      // it that just ended has put the circuit back already.
      if (strcmp(gLivePrio, "beamng") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!beamngWasActive) {
    beamngWasActive = true;
    beginC2PCapture();
    beamngInit();
    finishC2PTransition(tileTransBeamng, tileTransSpd[TRSPD_BEAMNG]);
    return true;
  }
  // A new speed, or the panel back from a tile ranked above it.
  if (beamngSpeedNow() != beamngDrawnKmh || beamngSpeedMph != beamngDrawnMph ||
      strcmp(gLivePrio, "beamng") != 0)
    beamngInit();
  return true;
}

// ASSETTO CORSA DISPLAY

static uint8_t assettoColBuf[64];
static int     assettoColCount = 0;
static int     assettoDrawnKmh = -1;
static bool    assettoDrawnMph = false;

// True while Connect sends speeds and said so within ASSETTO_TIMEOUT_MS; past
// that the state is dropped here, so whatever asks next agrees.
static bool assettoLive() {
  bool active;
  portENTER_CRITICAL(&assettoMux);
  if (assettoActive && millis() - assettoLastMs > ASSETTO_TIMEOUT_MS) assettoActive = false;
  active = assettoActive;
  portEXIT_CRITICAL(&assettoMux);
  return active && assettoEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_ASSETTO));
}

static int assettoSpeedNow() {
  portENTER_CRITICAL(&assettoMux);
  int kmh = assettoSpeedKmh;
  portEXIT_CRITICAL(&assettoMux);
  return kmh;
}

void assettoInit() {
  int kmh = assettoSpeedNow();
  speedBuildFrame(assettoColBuf, assettoColCount, kmh, assettoSpeedMph, fontTypeAssetto, assettoGauge, assettoGaugeFull);
  speedDraw(assettoColBuf, assettoColCount);
  assettoDrawnKmh = kmh;
  assettoDrawnMph = assettoSpeedMph;
}

bool assettoPriorityTick() {
  if (!assettoLive()) {
    if (assettoWasActive) {
      assettoWasActive = false;
      // Handed back only if it was the tile on the panel: a tile ranked above
      // it that just ended has put the circuit back already.
      if (strcmp(gLivePrio, "assetto") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!assettoWasActive) {
    assettoWasActive = true;
    beginC2PCapture();
    assettoInit();
    finishC2PTransition(tileTransAssetto, tileTransSpd[TRSPD_ASSETTO]);
    return true;
  }
  // A new speed, or the panel back from a tile ranked above it.
  if (assettoSpeedNow() != assettoDrawnKmh || assettoSpeedMph != assettoDrawnMph ||
      strcmp(gLivePrio, "assetto") != 0)
    assettoInit();
  return true;
}

// NEED FOR SPEED UNDERGROUND 2 DISPLAY

static uint8_t nfsu2ColBuf[64];
static int     nfsu2ColCount = 0;
static int     nfsu2DrawnShown = -1;
static int     nfsu2DrawnKmh = -1;
static bool    nfsu2DrawnMph = false;

// True while Connect sends speeds and said so within NFSU2_TIMEOUT_MS; past
// that the state is dropped here, so whatever asks next agrees.
static bool nfsu2Live() {
  bool active;
  portENTER_CRITICAL(&nfsu2Mux);
  if (nfsu2Active && millis() - nfsu2LastMs > NFSU2_TIMEOUT_MS) nfsu2Active = false;
  active = nfsu2Active;
  portEXIT_CRITICAL(&nfsu2Mux);
  return active && nfsu2Enabled && !(prioHiddenMask & (1u << PRIORITY_ID_NFSU2));
}

static void nfsu2SpeedNow(int& kmh, int& mphValue) {
  portENTER_CRITICAL(&nfsu2Mux);
  kmh = nfsu2SpeedKmh;
  mphValue = nfsu2SpeedMphValue;
  portEXIT_CRITICAL(&nfsu2Mux);
}

// The number the tile draws: km/h as sent, or the mph Connect worked out from
// the game's own speed (from km/h, like the other tiles, if none came).
static int nfsu2Shown(int kmh, int mphValue) {
  if (!nfsu2SpeedMph) return speedShown(kmh, false);
  return mphValue >= 0 ? (mphValue > 999 ? 999 : mphValue) : speedShown(kmh, true);
}

void nfsu2Init() {
  int kmh, mphValue;
  nfsu2SpeedNow(kmh, mphValue);
  int shown = nfsu2Shown(kmh, mphValue);
  speedBuildFrame(nfsu2ColBuf, nfsu2ColCount, kmh, nfsu2SpeedMph, fontTypeNfsu2, nfsu2Gauge, nfsu2GaugeFull, shown);
  speedDraw(nfsu2ColBuf, nfsu2ColCount);
  nfsu2DrawnShown = shown;
  nfsu2DrawnKmh = kmh;
  nfsu2DrawnMph = nfsu2SpeedMph;
}

bool nfsu2PriorityTick() {
  if (!nfsu2Live()) {
    if (nfsu2WasActive) {
      nfsu2WasActive = false;
      // Handed back only if it was the tile on the panel: a tile ranked above
      // it that just ended has put the circuit back already.
      if (strcmp(gLivePrio, "nfsu2") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!nfsu2WasActive) {
    nfsu2WasActive = true;
    beginC2PCapture();
    nfsu2Init();
    finishC2PTransition(tileTransNfsu2, tileTransSpd[TRSPD_NFSU2]);
    return true;
  }
  // A new speed (the number, or the km/h a gauge fills by), another unit, or
  // the panel back from a tile ranked above it.
  int kmh, mphValue;
  nfsu2SpeedNow(kmh, mphValue);
  if (nfsu2Shown(kmh, mphValue) != nfsu2DrawnShown || kmh != nfsu2DrawnKmh ||
      nfsu2SpeedMph != nfsu2DrawnMph || strcmp(gLivePrio, "nfsu2") != 0)
    nfsu2Init();
  return true;
}

// NEED FOR SPEED MOST WANTED DISPLAY

static uint8_t nfsmwColBuf[64];
static int     nfsmwColCount = 0;
static int     nfsmwDrawnShown = -1;
static int     nfsmwDrawnKmh = -1;
static bool    nfsmwDrawnMph = false;

// True while Connect sends speeds and said so within NFSMW_TIMEOUT_MS; past
// that the state is dropped here, so whatever asks next agrees.
static bool nfsmwLive() {
  bool active;
  portENTER_CRITICAL(&nfsmwMux);
  if (nfsmwActive && millis() - nfsmwLastMs > NFSMW_TIMEOUT_MS) nfsmwActive = false;
  active = nfsmwActive;
  portEXIT_CRITICAL(&nfsmwMux);
  return active && nfsmwEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_NFSMW));
}

static void nfsmwSpeedNow(int& kmh, int& mphValue) {
  portENTER_CRITICAL(&nfsmwMux);
  kmh = nfsmwSpeedKmh;
  mphValue = nfsmwSpeedMphValue;
  portEXIT_CRITICAL(&nfsmwMux);
}

// The number the tile draws: km/h as sent, or the mph Connect worked out from
// the game's own speed (from km/h, like the other tiles, if none came).
static int nfsmwShown(int kmh, int mphValue) {
  if (!nfsmwSpeedMph) return speedShown(kmh, false);
  return mphValue >= 0 ? (mphValue > 999 ? 999 : mphValue) : speedShown(kmh, true);
}

void nfsmwInit() {
  int kmh, mphValue;
  nfsmwSpeedNow(kmh, mphValue);
  int shown = nfsmwShown(kmh, mphValue);
  speedBuildFrame(nfsmwColBuf, nfsmwColCount, kmh, nfsmwSpeedMph, fontTypeNfsmw, nfsmwGauge, nfsmwGaugeFull, shown);
  speedDraw(nfsmwColBuf, nfsmwColCount);
  nfsmwDrawnShown = shown;
  nfsmwDrawnKmh = kmh;
  nfsmwDrawnMph = nfsmwSpeedMph;
}

bool nfsmwPriorityTick() {
  if (!nfsmwLive()) {
    if (nfsmwWasActive) {
      nfsmwWasActive = false;
      // Handed back only if it was the tile on the panel: a tile ranked above
      // it that just ended has put the circuit back already.
      if (strcmp(gLivePrio, "nfsmw") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!nfsmwWasActive) {
    nfsmwWasActive = true;
    beginC2PCapture();
    nfsmwInit();
    finishC2PTransition(tileTransNfsmw, tileTransSpd[TRSPD_NFSMW]);
    return true;
  }
  // A new speed (the number, or the km/h a gauge fills by), another unit, or
  // the panel back from a tile ranked above it.
  int kmh, mphValue;
  nfsmwSpeedNow(kmh, mphValue);
  if (nfsmwShown(kmh, mphValue) != nfsmwDrawnShown || kmh != nfsmwDrawnKmh ||
      nfsmwSpeedMph != nfsmwDrawnMph || strcmp(gLivePrio, "nfsmw") != 0)
    nfsmwInit();
  return true;
}

// NEED FOR SPEED CARBON DISPLAY

static uint8_t nfscColBuf[64];
static int     nfscColCount = 0;
static int     nfscDrawnShown = -1;
static int     nfscDrawnKmh = -1;
static bool    nfscDrawnMph = false;

// True while Connect sends speeds and said so within NFSC_TIMEOUT_MS; past
// that the state is dropped here, so whatever asks next agrees.
static bool nfscLive() {
  bool active;
  portENTER_CRITICAL(&nfscMux);
  if (nfscActive && millis() - nfscLastMs > NFSC_TIMEOUT_MS) nfscActive = false;
  active = nfscActive;
  portEXIT_CRITICAL(&nfscMux);
  return active && nfscEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_NFSC));
}

static void nfscSpeedNow(int& kmh, int& mphValue) {
  portENTER_CRITICAL(&nfscMux);
  kmh = nfscSpeedKmh;
  mphValue = nfscSpeedMphValue;
  portEXIT_CRITICAL(&nfscMux);
}

// The number the tile draws: km/h as sent, or the mph Connect worked out from
// the game's own speed (from km/h, like the other tiles, if none came).
static int nfscShown(int kmh, int mphValue) {
  if (!nfscSpeedMph) return speedShown(kmh, false);
  return mphValue >= 0 ? (mphValue > 999 ? 999 : mphValue) : speedShown(kmh, true);
}

void nfscInit() {
  int kmh, mphValue;
  nfscSpeedNow(kmh, mphValue);
  int shown = nfscShown(kmh, mphValue);
  speedBuildFrame(nfscColBuf, nfscColCount, kmh, nfscSpeedMph, fontTypeNfsc, nfscGauge, nfscGaugeFull, shown);
  speedDraw(nfscColBuf, nfscColCount);
  nfscDrawnShown = shown;
  nfscDrawnKmh = kmh;
  nfscDrawnMph = nfscSpeedMph;
}

bool nfscPriorityTick() {
  if (!nfscLive()) {
    if (nfscWasActive) {
      nfscWasActive = false;
      // Handed back only if it was the tile on the panel: a tile ranked above
      // it that just ended has put the circuit back already.
      if (strcmp(gLivePrio, "nfsc") == 0) resumeCircuitTileFromPriority();
    }
    return false;
  }
  if (!nfscWasActive) {
    nfscWasActive = true;
    beginC2PCapture();
    nfscInit();
    finishC2PTransition(tileTransNfsc, tileTransSpd[TRSPD_NFSC]);
    return true;
  }
  // A new speed (the number, or the km/h a gauge fills by), another unit, or
  // the panel back from a tile ranked above it.
  int kmh, mphValue;
  nfscSpeedNow(kmh, mphValue);
  if (nfscShown(kmh, mphValue) != nfscDrawnShown || kmh != nfscDrawnKmh ||
      nfscSpeedMph != nfscDrawnMph || strcmp(gLivePrio, "nfsc") != 0)
    nfscInit();
  return true;
}

bool higherPriorityTileActive(uint8_t id) {
  int rank = -1;
  for (int i = 0; i < NUM_PRIORITY_IDS; i++) if (priorityOrder[i] == id) { rank = i; break; }
  if (rank < 0) return false;
  for (int i = 0; i < rank; i++) {
    switch (priorityOrder[i]) {
      case PRIORITY_ID_NOTIF:      if (notifActive && !webAccessAlertActive) return true; break;
      case PRIORITY_ID_WEB:        if (notifActive &&  webAccessAlertActive) return true; break;
      case PRIORITY_ID_ETS2:       if (ets2Live()) return true; break;
      case PRIORITY_ID_VOICEMEETER: if (vmLive()) return true; break;
      case PRIORITY_ID_BROWSER_DOWNLOADS: if (browserDownloadsLive()) return true; break;
      case PRIORITY_ID_QBIT:       if (qbitLive()) return true; break;
      case PRIORITY_ID_BAMBU:      if (bambuLive()) return true; break;
      case PRIORITY_ID_OBS:        if (obsLive()) return true; break;
      case PRIORITY_ID_BEAMNG:     if (beamngLive()) return true; break;
      case PRIORITY_ID_ASSETTO:    if (assettoLive()) return true; break;
      case PRIORITY_ID_GAMEFPS:    if (gameFpsLive()) return true; break;
      case PRIORITY_ID_VOLUME:     if (volumeLive()) return true; break;
      case PRIORITY_ID_NFSU2:      if (nfsu2Live()) return true; break;
      case PRIORITY_ID_NFSMW:      if (nfsmwLive()) return true; break;
      case PRIORITY_ID_NFSC:       if (nfscLive()) return true; break;
      case PRIORITY_ID_LOW_BATTERY:
        if (lowBatteryAllowed() &&
            (int32_t)(batteryAlertUntilMs - millis()) > 0) return true;
        break;
      case PRIORITY_ID_NOWPLAYING: if (npPriorityLive()) return true; break;
      case PRIORITY_ID_STOPWATCH:  if (swRunning) return true; break;
      case PRIORITY_ID_TIMER:      if (timerRunning || timerFinished) return true; break;
      case PRIORITY_ID_ALARM:      if (alarmRinging) return true; break;
    }
  }
  return false;
}

// The names the web interface knows these by. Pulled out of
// priorityOrderToString() so the live marker names them the same way the saved
// order does - two switches would be two chances to disagree.
static const char* priorityIdName(uint8_t id) {
  switch (id) {
    case PRIORITY_ID_NOTIF:      return "notif";
    case PRIORITY_ID_WEB:        return "webaccess";
    case PRIORITY_ID_ETS2:       return "ets2";
    case PRIORITY_ID_NOWPLAYING: return "nowplaying";
    case PRIORITY_ID_STOPWATCH:  return "stopwatch";
    case PRIORITY_ID_TIMER:      return "timer";
    case PRIORITY_ID_ALARM:      return "alarm";
    case PRIORITY_ID_VOICEMEETER:return "voicemeeter";
    case PRIORITY_ID_BROWSER_DOWNLOADS:return "browserdownloads";
    case PRIORITY_ID_OBS:        return "obs";
    case PRIORITY_ID_LOW_BATTERY: return "lowbattery";
    case PRIORITY_ID_BEAMNG:     return "beamng";
    case PRIORITY_ID_ASSETTO:    return "assetto";
    case PRIORITY_ID_QBIT:       return "qbittorrent";
    case PRIORITY_ID_BAMBU:      return "bambu";
    case PRIORITY_ID_GAMEFPS:    return "gamefps";
    case PRIORITY_ID_VOLUME:     return "volume";
    case PRIORITY_ID_NFSU2:      return "nfsu2";
    case PRIORITY_ID_NFSMW:      return "nfsmw";
    case PRIORITY_ID_NFSC:       return "nfsc";
  }
  return "";
}

String priorityOrderToString() {
  String out = "";
  for (int i = 0; i < NUM_PRIORITY_IDS; i++) {
    if (i > 0) out += ",";
    out += priorityIdName(priorityOrder[i]);
  }
  return out;
}

// PRIORITY ORDER, COMPLETED
//
// Every ranking that comes in - saved by an older build, restored from a
// backup, sent by a page cached before the newest tile existed - goes through
// completePriorityOrder(), so the user's own order of the tiles it names is
// kept exactly and the tiles it does not name are placed where they belong,
// rather than by guessing from how many entries it happened to have. Unknown
// and repeated ids are dropped. Missing ones go in: the alarm at the top,
// where the default order has it; each tile added in a later build right after
// the tile it sits beside by default; anything else at the end.

static int priorityIdFromName(const String& name) {
  for (uint8_t id = 0; id < NUM_PRIORITY_IDS; id++)
    if (name == priorityIdName(id)) return id;
  return -1;
}

static int priorityIndexOf(const uint8_t* order, int count, uint8_t id) {
  for (int i = 0; i < count; i++) if (order[i] == id) return i;
  return -1;
}

static void priorityInsertAt(uint8_t* order, int& count, int at, uint8_t id) {
  if (count >= NUM_PRIORITY_IDS) return;
  if (at < 0 || at > count) at = count;
  for (int i = count; i > at; i--) order[i] = order[i - 1];
  order[at] = id;
  count++;
}

// Keeps the valid, unique ids of `in` in their order and fills in the rest,
// leaving a complete ranking of NUM_PRIORITY_IDS entries in `out`.
static void completePriorityOrder(const uint8_t* in, int n, uint8_t* out) {
  int count = 0;
  bool seen[NUM_PRIORITY_IDS] = { false };
  for (int i = 0; i < n && count < NUM_PRIORITY_IDS; i++) {
    uint8_t id = in[i];
    if (id >= NUM_PRIORITY_IDS || seen[id]) continue;
    seen[id] = true;
    out[count++] = id;
  }
  if (!seen[PRIORITY_ID_ALARM]) {
    priorityInsertAt(out, count, 0, PRIORITY_ID_ALARM);
    seen[PRIORITY_ID_ALARM] = true;
  }
  // Tile added later, and the tile it goes in after.
  static const uint8_t LATER[][2] = {
    { PRIORITY_ID_VOICEMEETER,       PRIORITY_ID_NOTIF },
    { PRIORITY_ID_BROWSER_DOWNLOADS, PRIORITY_ID_NOTIF },
    { PRIORITY_ID_OBS,               PRIORITY_ID_BROWSER_DOWNLOADS },
    { PRIORITY_ID_LOW_BATTERY,       PRIORITY_ID_ALARM },
    { PRIORITY_ID_BEAMNG,            PRIORITY_ID_ETS2 },
    { PRIORITY_ID_ASSETTO,           PRIORITY_ID_BEAMNG },
    { PRIORITY_ID_QBIT,              PRIORITY_ID_BROWSER_DOWNLOADS },
    { PRIORITY_ID_BAMBU,             PRIORITY_ID_QBIT },
    { PRIORITY_ID_GAMEFPS,           PRIORITY_ID_ASSETTO },
    { PRIORITY_ID_VOLUME,            PRIORITY_ID_LOW_BATTERY },
    { PRIORITY_ID_NFSU2,             PRIORITY_ID_ASSETTO },
    { PRIORITY_ID_NFSMW,             PRIORITY_ID_NFSU2 },
    { PRIORITY_ID_NFSC,              PRIORITY_ID_NFSMW },
  };
  for (const auto& later : LATER) {
    if (seen[later[0]]) continue;
    int anchor = priorityIndexOf(out, count, later[1]);
    priorityInsertAt(out, count, anchor < 0 ? count : anchor + 1, later[0]);
    seen[later[0]] = true;
  }
  for (uint8_t id = 0; id < NUM_PRIORITY_IDS && count < NUM_PRIORITY_IDS; id++) {
    if (!seen[id]) {
      out[count++] = id;
      seen[id] = true;
    }
  }
}

// "alarm,notif,..." as the web interface and Octoglow Connect send it, into
// ids. Unknown names and repeats are skipped; returns how many were read.
static int parsePriorityOrder(const String& ord, uint8_t* out, bool* sawNowPlaying) {
  int count = 0;
  int start = 0;
  bool seen[NUM_PRIORITY_IDS] = { false };
  if (sawNowPlaying) *sawNowPlaying = false;
  for (int i = 0; i <= (int)ord.length(); i++) {
    if (i < (int)ord.length() && ord[i] != ',') continue;
    String tok = ord.substring(start, i);
    tok.trim();
    start = i + 1;
    int id = priorityIdFromName(tok);
    if (id < 0 || seen[id] || count >= NUM_PRIORITY_IDS) continue;
    seen[id] = true;
    if (id == PRIORITY_ID_NOWPLAYING && sawNowPlaying) *sawNowPlaying = true;
    out[count++] = (uint8_t)id;
  }
  return count;
}

void rebuildPriorityOrderFromEts2Order() {
  uint8_t order[NUM_PRIORITY_IDS];
  int n = 0;
  order[n++] = PRIORITY_ID_ALARM;
  order[n++] = PRIORITY_ID_LOW_BATTERY;
  order[n++] = PRIORITY_ID_VOICEMEETER;
  if (ets2OrderFirst) order[n++] = PRIORITY_ID_ETS2;
  order[n++] = PRIORITY_ID_NOTIF;
  order[n++] = PRIORITY_ID_BROWSER_DOWNLOADS;
  order[n++] = PRIORITY_ID_OBS;
  if (!ets2OrderFirst) order[n++] = PRIORITY_ID_ETS2;
  order[n++] = PRIORITY_ID_NOWPLAYING;
  order[n++] = PRIORITY_ID_STOPWATCH;
  order[n++] = PRIORITY_ID_TIMER;
  order[n++] = PRIORITY_ID_WEB;
  completePriorityOrder(order, n, priorityOrder);
}

// TILE TRANSITIONS: generic transition engine between 32x8 pixel frames
// effect: 0=Nothing 1=Scroll Left 2=Scroll Right 3=Scroll Up 4=Scroll Down 5=Morph 6=Fade 7=Expand Left 8=Expand Right 9=Expand Centre

static const uint8_t TRANS_BAYER8[8][8] = {
  { 0,32, 8,40, 2,34,10,42},
  {48,16,56,24,50,18,58,26},
  {12,44, 4,36,14,46, 6,38},
  {60,28,52,20,62,30,54,22},
  { 3,35,11,43, 1,33, 9,41},
  {51,19,59,27,49,17,57,25},
  {15,47, 7,39,13,45, 5,37},
  {63,31,55,23,61,29,53,21}
};


void transCaptureCols(uint8_t *dst) {
  for (int c = 0; c < 32; c++) dst[c] = mx.getColumn(c);
}

void transWriteCols(const uint8_t *cols) {
  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, cols[c]);
  mxCommit();
}

static void transWriteBlank() {
  uint8_t blank[32];
  for (int c = 0; c < 32; c++) blank[c] = 0x00;
  transWriteCols(blank);
}

// Delay with "ease-in-out": slower near the ends (start/finish), faster in the middle.
static int easedStepDelay(int s, int steps, int baseDelay, int extraDelay, int easeZone) {
  int distFromEdge = s < (steps - s) ? s : (steps - s);
  if (distFromEdge >= easeZone) return baseDelay;
  return baseDelay + extraDelay * (easeZone - distFromEdge) / easeZone;
}

// Pace of the transition being played, in percent (see TILE TRANSITION SPEED).
static uint8_t gTransPct = TRSPD_DEFAULT;

// delay() scaled by that pace: 200 % halves every pause, 50 % doubles it. Never
// under 1 ms, so even the fastest setting still shows every frame.
static void transDelay(int ms) {
  long d = (long)ms * 100 / gTransPct;
  delay(d > 0 ? (unsigned long)d : 1UL);
}

static void playScrollTransition(const uint8_t *oldCols, const uint8_t *newCols, bool toLeft) {
  const int GAP = 4; // black columns between tiles, during the scroll
  const int VW  = 32 + GAP + 32;
  uint8_t virt[VW];
  const int STEPS = VW - 32;
  if (toLeft) {
    // the old tile exits to the left, the new one enters from the right, with black space between them
    for (int c = 0; c < 32; c++) virt[c] = oldCols[c];
    for (int c = 0; c < GAP; c++) virt[32 + c] = 0x00;
    for (int c = 0; c < 32; c++) virt[32 + GAP + c] = newCols[c];
    for (int s = 0; s <= STEPS; s++) {
      uint8_t frame[32];
      for (int c = 0; c < 32; c++) frame[c] = virt[s + c];
      transWriteCols(frame);
      transDelay(easedStepDelay(s, STEPS, 4, 10, 8));
    }
  } else {
    // the old tile exits to the right, the new one enters from the left, with black space between them
    for (int c = 0; c < 32; c++) virt[c] = newCols[c];
    for (int c = 0; c < GAP; c++) virt[32 + c] = 0x00;
    for (int c = 0; c < 32; c++) virt[32 + GAP + c] = oldCols[c];
    for (int s = STEPS; s >= 0; s--) {
      uint8_t frame[32];
      for (int c = 0; c < 32; c++) frame[c] = virt[s + c];
      transWriteCols(frame);
      transDelay(easedStepDelay(STEPS - s, STEPS, 4, 10, 8));
    }
  }
}

static void playVerticalScrollTransition(const uint8_t *oldCols, const uint8_t *newCols, bool up) {
  const int GAP   = 2; // black rows between tiles, during the scroll
  const int STEPS = 8 + GAP;
  for (int s = 0; s <= STEPS; s++) {
    uint8_t frame[32];
    for (int c = 0; c < 32; c++) {
      uint32_t virtCol = up
        ? (((uint32_t)newCols[c] << (8 + GAP)) | oldCols[c])
        : (((uint32_t)oldCols[c] << (8 + GAP)) | newCols[c]);
      int shift = up ? s : (STEPS - s);
      frame[c] = (uint8_t)((virtCol >> shift) & 0xFF);
    }
    transWriteCols(frame);
    transDelay(easedStepDelay(s, STEPS, 14, 24, 3));
  }
}

static void playFadeTransition(const uint8_t *newCols) {
  if (curBrightness == 0) { transWriteCols(newCols); return; } // screen off, no animation
  int target = (int)curBrightness - 1; // 0..15, current hardware level
  if (target < 0) target = 0;

  const int FADE_OUT_MS = 1000; // fade out on the old tile, ~1 second
  const int PAUSE_MS    = 100;  // black pause between tiles
  const int FADE_IN_MS  = 450;  // fade in on the new tile

  int outDelay = FADE_OUT_MS / (target + 1);
  if (outDelay < 1) outDelay = 1;
  for (int lvl = target; lvl >= 0; lvl--) {
    mx.control(MD_MAX72XX::INTENSITY, (uint8_t)lvl);
    transDelay(outDelay);
  }

  transWriteCols(newCols); // change content while the screen is completely off
  transDelay(PAUSE_MS);

  int inDelay = FADE_IN_MS / (target + 1);
  if (inDelay < 1) inDelay = 1;
  for (int lvl = 0; lvl <= target; lvl++) {
    mx.control(MD_MAX72XX::INTENSITY, (uint8_t)lvl);
    transDelay(inDelay);
  }
}

static void playMorphTransition(const uint8_t *oldCols, const uint8_t *newCols) {
  // dissolve using Bayer matrix: new pixels gradually appear over the old ones
  const int STEPS = 24;
  for (int step = 1; step <= STEPS; step++) {
    int S = step * 4; // 4..96 - covers the Bayer thresholds (0..63) with an overlap of 32
    uint8_t frame[32];
    for (int c = 0; c < 32; c++) {
      uint8_t colVal = 0;
      for (int r = 0; r < 8; r++) {
        bool oldBit   = (oldCols[c] >> r) & 0x01;
        bool newBit   = (newCols[c] >> r) & 0x01;
        int  bv       = TRANS_BAYER8[r][c % 8];
        bool showOld  = bv >= (S - 32);
        bool showNew  = bv < S;
        if ((oldBit && showOld) || (newBit && showNew)) colVal |= (1 << r);
      }
      frame[c] = colVal;
    }
    transWriteCols(frame);
    transDelay(14);
  }
  transWriteCols(newCols);
}

static void expandFrameOpen(uint8_t *frame, const uint8_t *cols, uint8_t effect, int s) {
  for (int c = 0; c < 32; c++) frame[c] = 0x00;
  if (effect == 7) { // Expand Left: grows from column 0 towards 31
    for (int c = 0; c < s && c < 32; c++) frame[c] = cols[c];
  } else if (effect == 8) { // Expand Right: grows from column 31 towards 0
    for (int c = 31; c > 31 - s && c >= 0; c--) frame[c] = cols[c];
  } else { // Expand Centre: grows symmetrically from the middle towards the edges
    int half = s / 2;
    int lo = 16 - half, hi = 15 + half;
    if (s % 2 == 1) hi++;
    if (lo < 0) lo = 0;
    if (hi > 31) hi = 31;
    for (int c = lo; c <= hi; c++) frame[c] = cols[c];
  }
}

static void expandFrameClose(uint8_t *frame, const uint8_t *cols, uint8_t effect, int s) {
  // Closes the old tile from the direction OPPOSITE to the opening direction.
  for (int c = 0; c < 32; c++) frame[c] = cols[c];
  if (effect == 7) { // opposite of Left = collapses from the right
    for (int c = 31; c > 31 - s && c >= 0; c--) frame[c] = 0x00;
  } else if (effect == 8) { // opposite of Right = collapses from the left
    for (int c = 0; c < s && c < 32; c++) frame[c] = 0x00;
  } else { // opposite of Centre = collapses from the edges towards the center
    int half = s / 2;
    int lo = half, hi = 31 - half;
    if (s % 2 == 1) hi--;
    if (lo > hi) { for (int c = 0; c < 32; c++) frame[c] = 0x00; return; }
    for (int c = 0; c < lo; c++) frame[c] = 0x00;
    for (int c = hi + 1; c < 32; c++) frame[c] = 0x00;
  }
}

static void playExpandTransition(const uint8_t *oldCols, const uint8_t *newCols, uint8_t effect) {
  // effect: 7=Expand Left, 8=Expand Right, 9=Expand Centre
  const int STEPS = 32;
  // Phase 1: the old tile "closes" (collapses) from the opposite direction
  for (int s = 1; s <= STEPS; s++) {
    uint8_t frame[32];
    expandFrameClose(frame, oldCols, effect, s);
    transWriteCols(frame);
    transDelay(6);
  }
  // black pause between tiles
  transWriteBlank();
  transDelay(100);
  // Phase 2: the new tile "opens" gradually, like in Pokemon, from the chosen direction
  for (int s = 1; s <= STEPS; s++) {
    uint8_t frame[32];
    expandFrameOpen(frame, newCols, effect, s);
    transWriteCols(frame);
    transDelay(6);
  }
  transWriteCols(newCols);
}

void playTileTransition(uint8_t effect, const uint8_t *oldCols, const uint8_t *newCols, uint8_t pct) {
  // Out of range - or 0, from a caller with no setting of its own - plays at
  // the pace the effect was drawn at.
  gTransPct = (pct >= TRSPD_MIN && pct <= TRSPD_MAX) ? pct : TRSPD_DEFAULT;
  switch (effect) {
    case 1: playScrollTransition(oldCols, newCols, true);          break;
    case 2: playScrollTransition(oldCols, newCols, false);         break;
    case 3: playVerticalScrollTransition(oldCols, newCols, true);  break;
    case 4: playVerticalScrollTransition(oldCols, newCols, false); break;
    case 5: playMorphTransition(oldCols, newCols);                 break;
    case 6: playFadeTransition(newCols);                           break;
    case 7: case 8: case 9: playExpandTransition(oldCols, newCols, effect); break;
    default: transWriteCols(newCols);                              break; // 0 = Nothing
  }
}

uint8_t getCircuitTileTransition(uint8_t itemId) {
  switch (itemId) {
    case ITEM_HOUR:        return tileTransHour;
    case ITEM_DATE:        return tileTransDate;
    case ITEM_TEMP:        return tileTransTemp;
    case ITEM_NOW_PLAYING: return tileTransNp;
    case ITEM_WEATHER:     return tileTransWx;
    case ITEM_MEMENTO:     return tileTransRem;
    case ITEM_CANVAS:      return tileTransCanvas;
    case ITEM_PRESSURE:    return tileTransPress;
    case ITEM_SCREENSAVER: return tileTransSs;
    case ITEM_CURRENCY:    return tileTransCurr;
    case ITEM_YOUTUBE:     return tileTransYt;
    case ITEM_HOURWEEK:    return tileTransHw;
    case ITEM_AFTERBURNER: return tileTransAfterburner;
    case ITEM_COUNTDOWN:   return tileTransCountdown;
    default:               return tileTransGlobal;
  }
}

// The pace that goes with getCircuitTileTransition()'s effect.
uint8_t getCircuitTileTransSpeed(uint8_t itemId) {
  switch (itemId) {
    case ITEM_HOUR:        return tileTransSpd[TRSPD_HOUR];
    case ITEM_DATE:        return tileTransSpd[TRSPD_DATE];
    case ITEM_TEMP:        return tileTransSpd[TRSPD_TEMP];
    case ITEM_NOW_PLAYING: return tileTransSpd[TRSPD_NP];
    case ITEM_WEATHER:     return tileTransSpd[TRSPD_WX];
    case ITEM_MEMENTO:     return tileTransSpd[TRSPD_REM];
    case ITEM_CANVAS:      return tileTransSpd[TRSPD_CANVAS];
    case ITEM_PRESSURE:    return tileTransSpd[TRSPD_PRESS];
    case ITEM_SCREENSAVER: return tileTransSpd[TRSPD_SS];
    case ITEM_CURRENCY:    return tileTransSpd[TRSPD_CURR];
    case ITEM_YOUTUBE:     return tileTransSpd[TRSPD_YT];
    case ITEM_HOURWEEK:    return tileTransSpd[TRSPD_HW];
    case ITEM_AFTERBURNER: return tileTransSpd[TRSPD_AFTERBURNER];
    case ITEM_COUNTDOWN:   return tileTransSpd[TRSPD_COUNTDOWN];
    default:               return tileTransSpeed;
  }
}

static uint8_t gPreC2P[32];
static uint8_t gPreP2C[32];
void beginC2PCapture() {
  transCaptureCols(gPreC2P);
  gLiveTileId = -1;
  gLivePrio   = "";
}
// Each priority tile now supplies its own effect. Until this took a parameter,
// tileTransNotif / Ets2 / Sw / Tmr / Ip were saved, sent in /state and editable
// in the UI, but never read: every circuit-to-priority switch used tileTransC2P.
// Passing 0 (or nothing) keeps the old behaviour for callers with no per-tile
// setting of their own. The pace travels with the effect: the tile's own when
// it supplied one, the C2P setting's when that is what plays.
void finishC2PTransition(uint8_t tileTrans, uint8_t tileSpd) {
  uint8_t eff = tileTrans ? tileTrans : tileTransC2P;
  uint8_t pct = tileTrans ? tileSpd : tileTransSpd[TRSPD_C2P];
  if (eff == 0) return;
  uint8_t newCols[32];
  transCaptureCols(newCols);
  transWriteCols(gPreC2P);
  playTileTransition(eff, gPreC2P, newCols, pct);
}
void beginP2CCapture() {
  transCaptureCols(gPreP2C);
  gLiveTileId = -1;
  gLivePrio   = "";
}
void finishP2CTransition() {
  if (tileTransP2C == 0) return;
  uint8_t newCols[32];
  transCaptureCols(newCols);
  transWriteCols(gPreP2C);
  playTileTransition(tileTransP2C, gPreP2C, newCols, tileTransSpd[TRSPD_P2C]);
}

static void resetSlotState() {
  inScrollAnim = false;
  inFadeOut    = false;
  inFadeIn     = false;
  npState      = NP_SHOW_START;
  dateState    = NP_SHOW_START;
  gLastStaticDrawMs = 0;
  gStaticDrawDone   = false;
}

static void clearCircuitPanel() {
  mx.update(MD_MAX72XX::OFF);
  for (int i = 0; i < 32; i++) mx.setColumn(i, 0x00);
  mxCommit();
  gCircuitBlank = true;
}

// Every redraw of the circuit - a priority tile ending, a notification done, a
// font or icon changed - comes through here first, so it draws a tile the
// circuit may show now. One that was switched off or moved since it was
// entered is swapped for the next one, quietly; with nothing left to show the
// panel is cleared rather than left on a tile that is off.
static bool ensureCircuitSlot() {
  if (followCurrentSlot()) return true;
  int next = nextActiveSlot(currentSlot);
  if (next < 0) {
    clearCircuitPanel();
    return false;
  }
  setCurrentSlot(next);
  slotStartMs = millis();
  resetSlotState();
  return true;
}

void drawCircuitTileFrame() {
  if (!ensureCircuitSlot()) return;
  gCircuitBlank = false;
  if (items[currentSlot].id == ITEM_NOW_PLAYING) {
    npInit();
  } else if (items[currentSlot].id == ITEM_WEATHER) {
    weatherInit();
  } else if (items[currentSlot].id == ITEM_MEMENTO) {
    mementoInit();
  } else if (items[currentSlot].id == ITEM_COUNTDOWN) {
    countdownInit();
  } else if (items[currentSlot].id == ITEM_SCREENSAVER) {
    ssInit();
  } else if (items[currentSlot].id == ITEM_CURRENCY) {
    currencyInit();
  } else if (socialIndexForItem(items[currentSlot].id) >= 0) {
    socialInit(items[currentSlot].id);
  } else if (items[currentSlot].id == ITEM_BATTERY) {
    batteryDraw(false);
  } else if (items[currentSlot].id == ITEM_AFTERBURNER) {
    abInit();
  } else {

    mx.update(MD_MAX72XX::OFF);
    for (int i = 0; i < 32; i++) mx.setColumn(i, 0x00);
    mxCommit();

    switch (items[currentSlot].id) {
      case ITEM_HOUR:
        gLastStaticDrawMs = millis();
        drawHour();
        break;
      case ITEM_HOURWEEK:
        gLastStaticDrawMs = millis();
        drawHourWeekday();
        break;
      case ITEM_DATE:
        // Latched only if the draw happened: the clear above is already on the
        // panel, so a dateInit() that drew nothing has to be repeated rather
        // than remembered as done.
        gStaticDrawDone = dateInit();
        break;
      case ITEM_TEMP: {
        // Opportunistic fresh read, then draw unconditionally. tempInit()
        // renders "--" for TEMP_NO_READING, so the tile can no longer end up
        // with nothing on the matrix; loop() replaces the placeholder in place
        // the moment a real reading lands.
        float tf;
        if (bmpReadTempC(tf)) lastTemp = (int)round(tf);
        gStaticDrawDone = true;
        gTempShownValue = lastTemp;
        tempInit(lastTemp);
        break;
      }
      case ITEM_CANVAS:

        gStaticDrawDone = true;
        drawCanvas();
        break;
      case ITEM_PRESSURE: {

        pressureSampleTick();
        gStaticDrawDone = true;
        pressureInit((int)round(lastPressureHpa));
        break;
      }
    }
  }
}

// Web handlers only update state and enqueue one of these very small display
// commands.  The display task consumes them before advancing an animation, so
// every call that touches MD_MAX72XX stays serialized on one task.
enum : uint32_t {
  DISPLAY_OP_REFRESH_VISIBLE = 1u << 0,
  DISPLAY_OP_START_NOTIF     = 1u << 1,
  DISPLAY_OP_STOP_NOTIF      = 1u << 2,
  DISPLAY_OP_START_ETS2      = 1u << 3,
  DISPLAY_OP_STOP_ETS2       = 1u << 4,
};

static portMUX_TYPE displayOpsMux = portMUX_INITIALIZER_UNLOCKED;
static volatile uint32_t pendingDisplayOps = 0;

static void requestDisplayOp(uint32_t op) {
  portENTER_CRITICAL(&displayOpsMux);
  pendingDisplayOps |= op;
  portEXIT_CRITICAL(&displayOpsMux);
}

static uint32_t takeDisplayOps() {
  portENTER_CRITICAL(&displayOpsMux);
  uint32_t ops = pendingDisplayOps;
  pendingDisplayOps = 0;
  portEXIT_CRITICAL(&displayOpsMux);
  return ops;
}

// Rebuild whichever renderer actually owns the panel after a web-side state
// change. Priority order is honoured here just as it is in displayLoopTick().
static void initVisibleDisplay() {
  if (ipShowActive) {
    ipInit();
    return;
  }
  for (int pi = 0; pi < NUM_PRIORITY_IDS; pi++) {
    switch (priorityOrder[pi]) {
      case PRIORITY_ID_NOTIF:
        if (notifActive && !webAccessAlertActive) { notifInit(); return; }
        break;
      case PRIORITY_ID_WEB:
        if (notifActive && webAccessAlertActive) { notifInit(); return; }
        break;
      case PRIORITY_ID_ETS2:
        if (ets2Live()) { ets2Init(); return; }
        break;
      case PRIORITY_ID_VOICEMEETER:
        if (vmLive()) { vmDrawLatestFrame(); return; }
        break;
      case PRIORITY_ID_BROWSER_DOWNLOADS:
        if (browserDownloadsLive()) { browserDownloadsDraw(); return; }
        break;
      case PRIORITY_ID_QBIT:
        if (qbitLive()) { qbitDraw(); return; }
        break;
      case PRIORITY_ID_BAMBU:
        if (bambuLive()) { bambuDraw(); return; }
        break;
      case PRIORITY_ID_OBS:
        if (obsLive()) { obsInit(); return; }
        break;
      case PRIORITY_ID_BEAMNG:
        if (beamngLive()) { beamngInit(); return; }
        break;
      case PRIORITY_ID_ASSETTO:
        if (assettoLive()) { assettoInit(); return; }
        break;
      case PRIORITY_ID_GAMEFPS:
        if (gameFpsLive()) { gameFpsInit(); return; }
        break;
      case PRIORITY_ID_VOLUME:
        if (volumeLive()) { volumeInit(); return; }
        break;
      case PRIORITY_ID_NFSU2:
        if (nfsu2Live()) { nfsu2Init(); return; }
        break;
      case PRIORITY_ID_NFSMW:
        if (nfsmwLive()) { nfsmwInit(); return; }
        break;
      case PRIORITY_ID_NFSC:
        if (nfscLive()) { nfscInit(); return; }
        break;
      case PRIORITY_ID_LOW_BATTERY:
        if (lowBatteryAllowed() &&
            (int32_t)(batteryAlertUntilMs - millis()) > 0) { batteryDraw(true); return; }
        break;
      case PRIORITY_ID_NOWPLAYING:
        if (npPriorityLive()) { npInit(); return; }
        break;
      case PRIORITY_ID_STOPWATCH:
        if (swRunning) { swInit(); return; }
        break;
      case PRIORITY_ID_TIMER:
        if (timerRunning || timerFinished) { timerInit(); return; }
        break;
      case PRIORITY_ID_ALARM:
        if (alarmRinging) { alarmInit(); return; }
        break;
    }
  }
  drawCircuitTileFrame();
}

// TILE PREVIEW
//
// GET /tilepreview?t=<tile> - what a tile puts on the panel, for the Preview at
// the end of its settings in the web interface and in Octoglow Connect, which
// ask it for as long as those settings are open. t is a circuit tile's id
// ("0" the hour ... "14" MSI Afterburner) or a priority tile's name ("obs",
// "ets2", "qbittorrent"...), as the Tile Manager has them.
//
// The tile on the panel right now answers with the panel. Any other is drawn by
// the display task with the tile's own code, into the panel's buffer with the
// flush held back - the way a tile about to come on behind a transition is -
// and the panel's frame is put back before anything reaches the LEDs. A tile
// with nothing to show just now (no song, no game running, nothing
// downloading) is drawn with a made-up value, and says so.
//
//   {"live":false,"sample":false,"px":"<64 hex, as /getscreen>",
//    "scroll":{"x":9,"w":23,"wrap":false,"ms":40,"pause":1000,"buf":"<hex>"},
//    "anim":{"ms":80,"frames":["<64 hex>",...]}}
//
// scroll: text wider than its window. px is the first step; step n shows buf
// from column n in the w columns from x, the rest of px staying as it is - the
// tiles' DrawAtPos() exactly. wrap: it runs round rather than back and forth.
// anim: frames played in a loop - the screensaver, the visualizer, a speed.
#define PREVIEW_ANIM_MAX 48
static char     gPreviewKey[24];
static volatile uint32_t gPreviewAsked = 0;
static volatile uint32_t gPreviewDone  = 0;
static bool     gPreviewKnown  = false;
static bool     gPreviewLive   = false;
static bool     gPreviewSample = false;
static uint8_t  gPreviewPx[32];
static uint8_t  gPreviewScroll[NP_COL_MAX];
static int      gPreviewScrollCount = 0;
static int      gPreviewScrollX = 0, gPreviewScrollW = 0;
static bool     gPreviewScrollWrap = false;
static uint8_t  gPreviewScrollMs = SCROLL_SPEED_DEFAULT;
static uint8_t  gPreviewAnim[PREVIEW_ANIM_MAX][32];
static int      gPreviewAnimCount = 0;
static uint16_t gPreviewAnimMs = 80;

// Leftmost column first, as /getscreen gives them.
static void previewCapture(uint8_t out[32]) {
  for (int x = 0; x < 32; x++) out[x] = mx.getColumn(31 - x);
}

static void previewWrite(const uint8_t cols[32]) {
  mx.update(MD_MAX72XX::OFF);
  for (int x = 0; x < 32; x++) mx.setColumn(31 - x, cols[x]);
  mxCommit();
}

// A text window: nothing to add while the text fits in it.
static void previewScroll(const uint8_t* buf, int count, int x, int w, uint8_t scrollType, uint8_t spdIndex) {
  if (count <= w) return;
  gPreviewScrollCount = count < (int)sizeof(gPreviewScroll) ? count : (int)sizeof(gPreviewScroll);
  memcpy(gPreviewScroll, buf, gPreviewScrollCount);
  gPreviewScrollX = x;
  gPreviewScrollW = w;
  gPreviewScrollWrap = scrollIsWrap(scrollType);
  gPreviewScrollMs = scrollSpeedTile[spdIndex];
}

static void previewAnimFrame() {
  if (gPreviewAnimCount < PREVIEW_ANIM_MAX) previewCapture(gPreviewAnim[gPreviewAnimCount++]);
}

// Text tiles whose window starts after the icon unless the icon is hidden or
// scrolls along with the text.
static inline int previewTextX(bool hideIcon, uint8_t scrollType, int iconCols, int sepCols) {
  return (hideIcon || scrollIconInBuffer(scrollType)) ? 0 : iconCols + sepCols;
}

// A speed tile from 0 up to the end of its scale and back, as on the road.
static void previewSpeedSweep(bool mph, uint8_t font, uint8_t gauge, int full) {
  uint8_t buf[64];
  int count;
  for (int i = 0; i < PREVIEW_ANIM_MAX; i++) {
    int kmh = (int)lroundf(full * (0.5f - 0.5f * cosf(6.2831853f * i / PREVIEW_ANIM_MAX)));
    speedBuildFrame(buf, count, kmh, mph, font, gauge, full);
    speedDraw(buf, count);
    previewAnimFrame();
  }
  gPreviewAnimMs = 80;
  gPreviewSample = true;
}

// The speed a speed tile has now, or the sweep when no game sends one.
static void previewSpeed(bool live, int kmh, int shown, bool mph, uint8_t font, uint8_t gauge, int full) {
  if (!live) {
    previewSpeedSweep(mph, font, gauge, full);
    return;
  }
  uint8_t buf[64];
  int count;
  speedBuildFrame(buf, count, kmh, mph, font, gauge, full, shown);
  speedDraw(buf, count);
}

// A made-up visualizer frame for each of Octoglow Connect's modes, for when no
// sound is coming: the clock only ever draws the frames Connect works out.
static void previewVmSource(uint8_t mode, int t, uint8_t out[32]) {
  memset(out, 0, 32);
  if (mode == 3) {   // waveform
    int prevY = 4;
    for (int x = 0; x < 32; x++) {
      int y = 4 - (int)lroundf(3.0f * sinf(x * 0.42f + t * 0.55f) * (0.6f + 0.4f * sinf(t * 0.23f)));
      if (y < 0) y = 0;
      if (y > 7) y = 7;
      int lo = y < prevY ? y : prevY, hi = y < prevY ? prevY : y;
      for (int r = lo; r <= hi; r++) out[x] |= (uint8_t)(1u << r);
      prevY = y;
    }
    return;
  }
  if (mode == 4) {   // horizontal bars, left over right
    int left = 16 + (int)lroundf(12.0f * sinf(t * 0.40f));
    int right = 16 + (int)lroundf(12.0f * sinf(t * 0.40f + 1.3f));
    for (int x = 0; x < left; x++) out[x] |= 0x07;
    for (int x = 0; x < right; x++) out[x] |= 0xE0;
    return;
  }
  for (int x = 0; x < 32; x++) {
    float level;
    if (mode == 1) level = 0.55f + 0.4f * sinf(t * 0.45f);                                    // VU
    else if (mode == 2) level = 0.5f + 0.35f * sinf(t * 0.5f + (x % 16) * 0.55f + (x >= 16 ? 1.3f : 0.0f));   // stereo
    else level = (1.0f - x / 40.0f) * (0.55f + 0.4f * sinf(t * 0.5f + x * 0.7f));               // spectrum
    int h = (int)lroundf(level * 8.0f);
    if (h < 1) h = 1;
    if (h > 8) h = 8;
    for (int r = 8 - h; r < 8; r++) out[x] |= (uint8_t)(1u << r);
  }
}

static bool previewDraw(const char* key) {
  bool numeric = key[0] >= '0' && key[0] <= '9';
  int item = numeric ? atoi(key) : -1;
  if (item == ITEM_NOW_PLAYING || strcmp(key, "nowplaying") == 0) {
    // Nothing playing: a song of our own, put back at once.
    char keep[sizeof(nowPlayingBuf)];
    bool none = !nowPlayingActive || nowPlayingBuf[0] == '\0';
    if (none) {
      memcpy(keep, nowPlayingBuf, sizeof(keep));
      strcpy(nowPlayingBuf, "Artist - Song Title");
      gPreviewSample = true;
    }
    npInit();
    if (none) memcpy(nowPlayingBuf, keep, sizeof(keep));
    previewScroll(npColBuf, npColCount, previewTextX(hideIconNowPlaying, scrollTypeNowPlaying, NP_ICON_COLS, NP_SEP_COLS),
                  (hideIconNowPlaying || scrollIconInBuffer(scrollTypeNowPlaying)) ? 32 : NP_TEXT_COLS,
                  scrollTypeNowPlaying, SPD_NOWPLAYING);
    return true;
  }
  switch (item) {
    case ITEM_HOUR:     drawHour(); return true;
    case ITEM_HOURWEEK: drawHourWeekday(); return true;
    case ITEM_CANVAS:   drawCanvas(); return true;
    case ITEM_BATTERY:  batteryDraw(false); return true;
    case ITEM_DATE:
      if (dateInit())
        previewScroll(dateColBuf, dateColCount, previewTextX(hideIconDate, scrollTypeDate, DATE_ICON_COLS, DATE_SEP_COLS),
                      (hideIconDate || scrollIconInBuffer(scrollTypeDate)) ? 32 : DATE_TEXT_COLS, scrollTypeDate, SPD_DATE);
      return true;
    case ITEM_TEMP:
      tempInit(lastTemp);
      previewScroll(tempColBuf, tempColCount, previewTextX(hideIconTemp, scrollTypeTemp, TEMP_ICON_COLS, TEMP_SEP_COLS),
                    (hideIconTemp || scrollIconInBuffer(scrollTypeTemp)) ? 32 : TEMP_TEXT_COLS, scrollTypeTemp, SPD_TEMP);
      return true;
    case ITEM_WEATHER:
      if (!weatherValid) {
        gWxSample = true;
        gPreviewSample = true;
      }
      weatherInit();
      gWxSample = false;
      previewScroll(wxColBuf, wxColCount, previewTextX(hideIconWeather, scrollTypeWeather, NP_ICON_COLS, NP_SEP_COLS),
                    (hideIconWeather || scrollIconInBuffer(scrollTypeWeather)) ? 32 : NP_TEXT_COLS, scrollTypeWeather, SPD_WEATHER);
      return true;
    case ITEM_MEMENTO: {
      char keep[sizeof(mementoBuf)];
      bool none = mementoBuf[0] == '\0';
      if (none) {
        memcpy(keep, mementoBuf, sizeof(keep));
        strcpy(mementoBuf, "Memento");
        gPreviewSample = true;
      }
      mementoInit();
      if (none) memcpy(mementoBuf, keep, sizeof(keep));
      previewScroll(mementoColBuf, mementoColCount, previewTextX(hideIconReminder, scrollTypeReminder, NP_ICON_COLS, NP_SEP_COLS),
                    (hideIconReminder || scrollIconInBuffer(scrollTypeReminder)) ? 32 : NP_TEXT_COLS, scrollTypeReminder, SPD_REMINDER);
      return true;
    }
    case ITEM_PRESSURE:
      pressureInit((int)round(lastPressureHpa));
      previewScroll(pressureColBuf, pressureColCount,
                    previewTextX(hideIconPressure, scrollTypePressure, PRESSURE_ICON_COLS, PRESSURE_SEP_COLS),
                    (hideIconPressure || scrollIconInBuffer(scrollTypePressure)) ? 32 : PRESSURE_TEXT_COLS, scrollTypePressure, SPD_PRESSURE);
      return true;
    case ITEM_CURRENCY:
      currencyInit();
      previewScroll(currColBuf, currColCount, previewTextX(hideIconCurrency, scrollTypeCurrency, PRESSURE_ICON_COLS, PRESSURE_SEP_COLS),
                    (hideIconCurrency || scrollIconInBuffer(scrollTypeCurrency)) ? 32 : PRESSURE_TEXT_COLS, scrollTypeCurrency, SPD_CURRENCY);
      return true;
    case ITEM_YOUTUBE:
      if (!social[SOC_YT].valid) {
        gSocSample = true;
        gPreviewSample = true;
      }
      socialInit(ITEM_YOUTUBE);
      gSocSample = false;
      previewScroll(socColBuf, socColCount, previewTextX(hideIconYoutube, scrollTypeYoutube, PRESSURE_ICON_COLS, PRESSURE_SEP_COLS),
                    (hideIconYoutube || scrollIconInBuffer(scrollTypeYoutube)) ? 32 : PRESSURE_TEXT_COLS, scrollTypeYoutube, SPD_YOUTUBE);
      return true;
    case ITEM_AFTERBURNER: {
      int value;
      if (abShownValue(value)) {
        abInit();
      } else {
        // FPS 144, or 58 degrees in the Temperature tile's unit.
        value = abMode == AB_MODE_FPS ? 144 : (tempUnit == 1 ? 136 : 58);
        abBuildBuffer(value);
        if (abColCount > abTextCols()) abDrawAtPos(0);
        else abDrawCentred();
        gPreviewSample = true;
      }
      previewScroll(abColBuf, abColCount,
                    (!hideIconAfterburner && !scrollIconInBuffer(scrollTypeAfterburner)) ? TEMP_ICON_COLS + TEMP_SEP_COLS : 0,
                    abTextCols(), scrollTypeAfterburner, SPD_AFTERBURNER);
      return true;
    }
    case ITEM_COUNTDOWN: {
      char text[CD_NAME_MAX + 24] = "";
      if (!countdownText(text, sizeof(text))) {
        snprintf(text, sizeof(text), cdFullUnit ? "%s 89 %s" : "%s 89%s", cdLang == 0 ? "Craciun" : "Christmas",
                 cdFullUnit ? cdUnitWord(89, cdLang == 0) : (cdLang == 0 ? "z" : "d"));
        gPreviewSample = true;
      }
      countdownInitWith(text);
      previewScroll(cdColBuf, cdColCount, previewTextX(hideIconCountdown, scrollTypeCountdown, NP_ICON_COLS, NP_SEP_COLS),
                    (hideIconCountdown || scrollIconInBuffer(scrollTypeCountdown)) ? 32 : NP_TEXT_COLS, scrollTypeCountdown, SPD_COUNTDOWN);
      return true;
    }
    case ITEM_SCREENSAVER:
      // The animation run ahead on a clock of its own, a frame at a time.
      gSsFakeNow = millis();
      gSsFakeClock = true;
      ssInit();
      for (int i = 0; i < PREVIEW_ANIM_MAX; i++) {
        gSsFakeNow += SS_FRAME_INTERVAL;
        ssStep();
        previewAnimFrame();
      }
      gSsFakeClock = false;
      gPreviewAnimMs = SS_FRAME_INTERVAL;
      return true;
  }
  if (numeric) return false;

  if (strcmp(key, "lowbattery") == 0) {
    batteryDraw(true);
    return true;
  }
  if (strcmp(key, "browserdownloads") == 0 || strcmp(key, "qbittorrent") == 0 || strcmp(key, "bambu") == 0) {
    uint8_t progress = 64, style, out[32];
    const uint8_t* icon;
    if (key[0] == 'b' && key[1] == 'r') {
      if (browserDownloadsLive()) { portENTER_CRITICAL(&browserDownloadMux); progress = browserDownloadProgress; portEXIT_CRITICAL(&browserDownloadMux); }
      else gPreviewSample = true;
      style = barStyleBrowserDownloads;
      icon = hideIconBrowserDownloads ? nullptr : resolveTileIcon(iconSelBrowserDownloads, downloadArrowIcon);
    } else if (key[0] == 'q') {
      if (qbitLive()) { portENTER_CRITICAL(&qbitMux); progress = qbitProgress; portEXIT_CRITICAL(&qbitMux); }
      else gPreviewSample = true;
      style = barStyleQbit;
      icon = hideIconQbit ? nullptr : resolveTileIcon(iconSelQbit, downloadArrowIcon);
    } else {
      uint8_t kind = 0;
      if (bambuLive()) { portENTER_CRITICAL(&bambuMux); progress = bambuProgress; kind = bambuKind; portEXIT_CRITICAL(&bambuMux); }
      else gPreviewSample = true;
      style = barStyleBambu;
      icon = hideIconBambu ? nullptr
           : resolveTileIcon(iconSelBambu, kind == 1 ? warningIcon : kind == 2 ? infoIcon : objectFocusIcon);
    }
    progressBarFrame(style, progress, icon, out);
    previewWrite(out);
    return true;
  }
  if (strcmp(key, "voicemeeter") == 0) {
    uint8_t source[32], out[32];
    if (vmLive()) {
      portENTER_CRITICAL(&vmMux);
      memcpy(source, vmFrame, sizeof(source));
      portEXIT_CRITICAL(&vmMux);
      vmComposeFrame(source, out);
      previewWrite(out);
      return true;
    }
    for (int i = 0; i < PREVIEW_ANIM_MAX; i++) {
      previewVmSource(vmMode, i, source);
      vmComposeFrame(source, out);
      previewWrite(out);
      previewAnimFrame();
    }
    gPreviewAnimMs = 80;
    gPreviewSample = true;
    return true;
  }
  if (strcmp(key, "obs") == 0) {
    // As obsFormatText() words it, from the output running now or a made-up
    // recording of 5:42, without touching what the tile itself keeps.
    bool streaming = false;
    uint32_t elapsedMs = 342000UL;
    if (obsLive()) {
      portENTER_CRITICAL(&obsMux);
      streaming = obsStreaming;
      elapsedMs = obsElapsedMs;
      portEXIT_CRITICAL(&obsMux);
    } else {
      gPreviewSample = true;
    }
    char text[24];
    const char* label = streaming ? "LIVE" : "REC";
    uint32_t total = elapsedMs / 1000UL;
    unsigned long hours = (unsigned long)(total / 3600UL);
    if (!obsShowElapsed) snprintf(text, sizeof(text), "%s", label);
    else if (hours > 0) snprintf(text, sizeof(text), "%s %lu:%02u:%02u", label, hours, (unsigned)((total / 60UL) % 60UL), (unsigned)(total % 60UL));
    else snprintf(text, sizeof(text), "%s %u:%02u", label, (unsigned)((total / 60UL) % 60UL), (unsigned)(total % 60UL));
    obsBuildBuffer(text);
    obsDrawAtPos(obsColCount <= obsTextCols() ? obsCenteredPos() : 0);
    previewScroll(obsColBuf, obsColCount, obsFullWidth() ? 0 : NP_ICON_COLS + NP_SEP_COLS, obsTextCols(), scrollTypeObs, SPD_OBS);
    return true;
  }
  if (strcmp(key, "gamefps") == 0) {
    int fps;
    if (!abGameFps(fps)) {
      fps = 144;
      gPreviewSample = true;
    }
    gfpsBuild(fps);
    if (gfpsColCount > gfpsTextCols()) gfpsDrawAtPos(0);
    else gfpsDrawCentred();
    previewScroll(gfpsColBuf, gfpsColCount, gfpsFullWidth() ? 0 : TEMP_ICON_COLS + TEMP_SEP_COLS, gfpsTextCols(), scrollTypeGameFps, SPD_GAMEFPS);
    return true;
  }
  if (strcmp(key, "volume") == 0) {
    int fps;
    if (!volumeSnapshot(fps)) {
      fps = 50;
      gPreviewSample = true;
    }
    volBuild(fps);
    if (volColCount > volTextCols()) volDrawAtPos(0);
    else volDrawCentred();
    previewScroll(volColBuf, volColCount, volFullWidth() ? 0 : TEMP_ICON_COLS + TEMP_SEP_COLS, volTextCols(), scrollTypeVolume, SPD_VOLUME);
    return true;
  }
  if (strcmp(key, "stopwatch") == 0) {
    swBuildBuffer();
    swDrawAtPos(swColCount <= 32 ? -((32 - swColCount) / 2) : 0);
    previewScroll(swColBuf, swColCount, 0, 32, scrollTypeStopwatch, SPD_STOPWATCH);
    return true;
  }
  if (strcmp(key, "timer") == 0) {
    timerBuildBuffer();
    timerDrawAtPos(timerColCount <= 32 ? -((32 - timerColCount) / 2) : 0);
    previewScroll(timerColBuf, timerColCount, 0, 32, scrollTypeTimer, SPD_TIMER);
    return true;
  }
  if (strcmp(key, "alarm") == 0) {
    alarmDrawCentered();
    return true;
  }
  if (strcmp(key, "ets2") == 0) {
    previewSpeed(ets2Live(), ets2Speed, -1, ets2SpeedMph, fontTypeEts2, ets2Gauge, ets2GaugeFull);
    return true;
  }
  if (strcmp(key, "beamng") == 0) {
    bool live = beamngLive();
    previewSpeed(live, live ? beamngSpeedNow() : 0, -1, beamngSpeedMph, fontTypeBeamng, beamngGauge, beamngGaugeFull);
    return true;
  }
  if (strcmp(key, "assetto") == 0) {
    bool live = assettoLive();
    previewSpeed(live, live ? assettoSpeedNow() : 0, -1, assettoSpeedMph, fontTypeAssetto, assettoGauge, assettoGaugeFull);
    return true;
  }
  if (strcmp(key, "nfsu2") == 0 || strcmp(key, "nfsmw") == 0 || strcmp(key, "nfsc") == 0) {
    int kmh = 0, mphValue = -1, shown = -1;
    bool live;
    if (key[3] == 'u') {
      live = nfsu2Live();
      if (live) { nfsu2SpeedNow(kmh, mphValue); shown = nfsu2Shown(kmh, mphValue); }
      previewSpeed(live, kmh, shown, nfsu2SpeedMph, fontTypeNfsu2, nfsu2Gauge, nfsu2GaugeFull);
    } else if (key[3] == 'm') {
      live = nfsmwLive();
      if (live) { nfsmwSpeedNow(kmh, mphValue); shown = nfsmwShown(kmh, mphValue); }
      previewSpeed(live, kmh, shown, nfsmwSpeedMph, fontTypeNfsmw, nfsmwGauge, nfsmwGaugeFull);
    } else {
      live = nfscLive();
      if (live) { nfscSpeedNow(kmh, mphValue); shown = nfscShown(kmh, mphValue); }
      previewSpeed(live, kmh, shown, nfscSpeedMph, fontTypeNfsc, nfscGauge, nfscGaugeFull);
    }
    return true;
  }
  return false;
}

// On the display task, between two passes: nothing else draws meanwhile.
static void renderTilePreview() {
  const char* key = gPreviewKey;
  gPreviewKnown = true;
  gPreviewLive = false;
  gPreviewSample = false;
  gPreviewScrollCount = 0;
  gPreviewAnimCount = 0;

  bool numeric = key[0] >= '0' && key[0] <= '9';
  int item = numeric ? atoi(key) : -1;
  bool onPanel;
  if (item == ITEM_NOW_PLAYING || strcmp(key, "nowplaying") == 0)
    onPanel = (gLiveTileId == ITEM_NOW_PLAYING && gLivePrio[0] == '\0') || strcmp(gLivePrio, "nowplaying") == 0;
  else if (numeric)
    onPanel = gLiveTileId == item && gLivePrio[0] == '\0';
  else
    onPanel = strcmp(gLivePrio, key) == 0;
  if (onPanel) {
    previewCapture(gPreviewPx);
    gPreviewLive = true;
    return;
  }

  uint8_t shown[32];
  transCaptureCols(shown);
  bool held = gSuppressHwFlash;
  gSuppressHwFlash = true;
  uint8_t blank[32] = {0};
  previewWrite(blank);
  gPreviewKnown = previewDraw(key);
  // The still frame: the first of an animation, or what was drawn.
  if (gPreviewAnimCount > 0) memcpy(gPreviewPx, gPreviewAnim[0], 32);
  else previewCapture(gPreviewPx);
  transWriteCols(shown);
  gSuppressHwFlash = held;
  // Back to updating the LEDs as every draw leaves it; the buffer is what they
  // already show, so nothing changes on them.
  if (!held) mx.update(MD_MAX72XX::ON);
}

static void serviceTilePreview() {
  uint32_t asked = gPreviewAsked;
  if (asked == gPreviewDone) return;
  renderTilePreview();
  gPreviewDone = asked;
}

static void previewAppendHex(String& j, const uint8_t* cols, int n) {
  static const char HEXD[] = "0123456789abcdef";
  for (int i = 0; i < n; i++) {
    j += HEXD[cols[i] >> 4];
    j += HEXD[cols[i] & 0x0F];
  }
}

void handleTilePreview() {
  if (!checkAuth()) return;
  String t = server.arg("t");
  if (t.length() == 0 || t.length() >= sizeof(gPreviewKey)) {
    server.send(400, "text/plain", "Lipseste t");
    return;
  }
  strncpy(gPreviewKey, t.c_str(), sizeof(gPreviewKey) - 1);
  gPreviewKey[sizeof(gPreviewKey) - 1] = '\0';
  __sync_synchronize();
  uint32_t ask = gPreviewAsked + 1;
  gPreviewAsked = ask;
  if (!displayTaskHandle) {
    // No display task: this is the loop that draws.
    renderTilePreview();
    gPreviewDone = ask;
  }
  unsigned long start = millis();
  while (gPreviewDone != ask) {
    // A transition can hold the display task for half a second.
    if (millis() - start > 1500UL) {
      server.send(503, "text/plain", "Ocupat");
      return;
    }
    vTaskDelay(pdMS_TO_TICKS(4));
  }
  if (!gPreviewKnown) {
    server.send(404, "text/plain", "Tile necunoscut");
    return;
  }
  String j;
  j.reserve(160 + gPreviewScrollCount * 2 + gPreviewAnimCount * 67);
  j = "{\"live\":";
  j += gPreviewLive ? "true" : "false";
  j += ",\"sample\":";
  j += gPreviewSample ? "true" : "false";
  j += ",\"px\":\"";
  previewAppendHex(j, gPreviewPx, 32);
  j += "\"";
  if (gPreviewScrollCount > 0) {
    j += ",\"scroll\":{\"x\":";
    j += gPreviewScrollX;
    j += ",\"w\":";
    j += gPreviewScrollW;
    j += ",\"wrap\":";
    j += gPreviewScrollWrap ? "true" : "false";
    j += ",\"ms\":";
    j += (int)gPreviewScrollMs;
    j += ",\"pause\":";
    j += (int)NP_PAUSE_MS;
    j += ",\"buf\":\"";
    previewAppendHex(j, gPreviewScroll, gPreviewScrollCount);
    j += "\"}";
  }
  if (gPreviewAnimCount > 0) {
    j += ",\"anim\":{\"ms\":";
    j += (int)gPreviewAnimMs;
    j += ",\"frames\":[";
    for (int i = 0; i < gPreviewAnimCount; i++) {
      if (i) j += ",";
      j += "\"";
      previewAppendHex(j, gPreviewAnim[i], 32);
      j += "\"";
    }
    j += "]}";
  }
  j += "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", j);
}

static void processPendingDisplayOps() {
  uint32_t ops = takeDisplayOps();
  if (!ops) return;

  if (ops & DISPLAY_OP_STOP_NOTIF) {
    resumeAfterNotif();
  }
  if ((ops & DISPLAY_OP_STOP_ETS2) && !notifActive) {
    beginP2CCapture();
    initVisibleDisplay();
    finishP2CTransition();
  }
  if ((ops & DISPLAY_OP_START_ETS2) && !higherPriorityTileActive(PRIORITY_ID_ETS2)) {
    beginC2PCapture();
    ets2Init();
    finishC2PTransition(tileTransEts2, tileTransSpd[TRSPD_ETS2]);
  }
  if (ops & DISPLAY_OP_START_NOTIF) {
    beginC2PCapture();
    notifInit();
    finishC2PTransition(webAccessAlertActive ? tileTransWeb : tileTransNotif,
                        tileTransSpd[webAccessAlertActive ? TRSPD_WEB : TRSPD_NOTIF]);
  }
  if ((ops & DISPLAY_OP_REFRESH_VISIBLE) &&
      !(ops & (DISPLAY_OP_START_NOTIF | DISPLAY_OP_START_ETS2 |
               DISPLAY_OP_STOP_NOTIF | DISPLAY_OP_STOP_ETS2))) {
    initVisibleDisplay();
  }
}

void enterSlot(bool playTransition) {
  uint8_t transOldCols[32];
  transCaptureCols(transOldCols);
  gLiveTileId = -1;
  gLivePrio   = "";

  uint8_t transEffect = playTransition ? getCircuitTileTransition(items[currentSlot].id) : 0;
  uint8_t transPct    = getCircuitTileTransSpeed(items[currentSlot].id);
  gSuppressHwFlash = (transEffect != 0);
  drawCircuitTileFrame();
  gSuppressHwFlash = false;

  if (transEffect != 0) {
    uint8_t transNewCols[32];
    transCaptureCols(transNewCols);
    transWriteCols(transOldCols);
    playTileTransition(transEffect, transOldCols, transNewCols, transPct);
  }
}

// redisplays the current Circuit Tiles tile after a Priority Tile
void resumeCircuitTileFromPriority() {
  beginP2CCapture();
  drawCircuitTileFrame();
  finishP2CTransition();
}

void advanceSlot() {
  beepSwitch();
  resetSlotState();

  int next = nextActiveSlot(currentSlot);
  if (next == -1) return;
  bool sameTile = (next == currentSlot);
  setCurrentSlot(next);
  slotStartMs = millis();
  enterSlot(!sameTile);
}

void retreatSlot() {
  beepSwitch();
  resetSlotState();

  int prev = prevActiveSlot(currentSlot);
  if (prev == -1) return;
  bool sameTile = (prev == currentSlot);
  setCurrentSlot(prev);
  slotStartMs = millis();
  enterSlot(!sameTile);
}

// SAVE / LOAD
bool isValidCircuitItemId(uint8_t id) {
  switch (id) {
    case ITEM_HOUR: case ITEM_DATE: case ITEM_TEMP: case ITEM_NOW_PLAYING:
    case ITEM_WEATHER: case ITEM_MEMENTO: case ITEM_CANVAS:
    case ITEM_PRESSURE: case ITEM_SCREENSAVER: case ITEM_CURRENCY:
    case ITEM_YOUTUBE: case ITEM_HOURWEEK: case ITEM_BATTERY:
    case ITEM_AFTERBURNER: case ITEM_COUNTDOWN:
      return true;
    default:
      return false;
  }
}

void repairItemIds() {
  static const uint8_t ALL_IDS[NUM_ITEMS] = {
    ITEM_HOUR, ITEM_DATE, ITEM_TEMP, ITEM_NOW_PLAYING, ITEM_WEATHER,
    ITEM_MEMENTO, ITEM_CANVAS, ITEM_PRESSURE, ITEM_SCREENSAVER, ITEM_CURRENCY,
    ITEM_YOUTUBE, ITEM_HOURWEEK, ITEM_BATTERY, ITEM_AFTERBURNER, ITEM_COUNTDOWN
  };
  bool used[16] = { false };
  bool needsFix[NUM_ITEMS] = { false };
  for (int i = 0; i < NUM_ITEMS; i++) {
    if (!isValidCircuitItemId(items[i].id) || items[i].id >= 16 || used[items[i].id]) {
      needsFix[i] = true;
    } else {
      used[items[i].id] = true;
    }
  }
  int nextMissing = 0;
  for (int i = 0; i < NUM_ITEMS; i++) {
    if (!needsFix[i]) continue;
    while (nextMissing < NUM_ITEMS && used[ALL_IDS[nextMissing]]) nextMissing++;
    if (nextMissing < NUM_ITEMS) {
      items[i].id = ALL_IDS[nextMissing];
      used[ALL_IDS[nextMissing]] = true;
    }
  }
}

// One-byte settings as loadSettings() reads them: from the pack SettingsWriter
// keeps them in, or - on a clock that has not saved since the pack came in -
// from the keys of their own they had before.
static uint8_t* gCfgPack = nullptr;
static size_t   gCfgPackCount = 0;

static uint8_t cfgU8(const char* key, uint8_t def) {
  uint8_t v;
  return cfgPackFind(gCfgPack, gCfgPackCount, key, v) ? v : prefs.getUChar(key, def);
}

static bool cfgBool(const char* key, bool def) {
  uint8_t v;
  return cfgPackFind(gCfgPack, gCfgPackCount, key, v) ? v != 0 : prefs.getBool(key, def);
}

static bool cfgIsKey(const char* key) {
  uint8_t v;
  return cfgPackFind(gCfgPack, gCfgPackCount, key, v) || prefs.isKey(key);
}

void loadSettings() {
  prefs.begin("settings", true);
  // The one-byte settings' pack (see SettingsWriter), for cfgU8() and the rest.
  size_t packLen = prefs.isKey(CFG_PACK_KEY) ? prefs.getBytesLength(CFG_PACK_KEY) : 0;
  if (packLen && packLen % CFG_PACK_REC == 0 && packLen <= CFG_PACK_MAX * CFG_PACK_REC) {
    gCfgPack = (uint8_t*)malloc(packLen);
    if (gCfgPack && prefs.getBytes(CFG_PACK_KEY, gCfgPack, packLen) == packLen) gCfgPackCount = packLen / CFG_PACK_REC;
  }
  buzzerOn      = cfgBool("buzzer", true);
  buzzerVolume  = cfgU8("buzzerVol", 80);
  // A clock from before the categories starts all four at its one volume.
  buzzVolNotif  = cfgU8("bzVolNotif", buzzerVolume);
  buzzVolAuto   = cfgU8("bzVolAuto",  buzzerVolume);
  buzzVolAlarm  = cfgU8("bzVolAlarm", buzzerVolume);
  buzzVolTouch  = cfgU8("bzVolTouch", buzzerVolume);
  prefs.getString("buzzerPreset", buzzerPreset, sizeof(buzzerPreset));
  if (strlen(buzzerPreset) == 0) strcpy(buzzerPreset, "calm");
  prefs.getString("evSndTile",  eventSoundTile,  sizeof(eventSoundTile));
  if (strlen(eventSoundTile) == 0) strcpy(eventSoundTile, "calm");
  prefs.getString("evSndWifi",  eventSoundWifi,  sizeof(eventSoundWifi));
  if (strlen(eventSoundWifi) == 0) strcpy(eventSoundWifi, "urgent");
  prefs.getString("evSndNotif", eventSoundNotif, sizeof(eventSoundNotif));
  if (strlen(eventSoundNotif) == 0) strcpy(eventSoundNotif, "soft");
  prefs.getString("evSndWeb", eventSoundWeb, sizeof(eventSoundWeb));
  if (strlen(eventSoundWeb) == 0) strcpy(eventSoundWeb, "soft");
  prefs.getString("evSndEts2",  eventSoundEts2,  sizeof(eventSoundEts2));
  if (strlen(eventSoundEts2) == 0) strcpy(eventSoundEts2, "urgent");
  prefs.getString("evSndTouch", eventSoundTouch, sizeof(eventSoundTouch));
  if (strlen(eventSoundTouch) == 0) strcpy(eventSoundTouch, "soft");
  prefs.getString("accentColor", accentColor, sizeof(accentColor));
  if (strlen(accentColor) == 0) strcpy(accentColor, "#d0bcff");
  touchTapAction       = cfgU8("touchTap", 8);
  touchDoubleTapAction = cfgU8("touchDbl", 0);
  npDisplayMode = cfgU8("npmode", 0);
  ssAnimSelected = cfgU8("ssAnim", SS_ANIM_RANDOM);
  if (!ssAnimValid(ssAnimSelected)) ssAnimSelected = SS_ANIM_RANDOM;
  tempUnit      = cfgU8("tempunit", 0);
  abMode        = cfgU8("abMode", AB_MODE_FPS);
  if (abMode > AB_MODE_GPU) abMode = AB_MODE_FPS;
  hourFormat    = cfgU8("hourformat", 0);
  hourLeadingZero = cfgBool("hourLeadZero", false);
  hwFormat      = cfgU8("hwFmt", 0);
  hwSkin        = cfgU8("hwSkin", HW_SKIN_COMPACT);
  hwContent     = cfgU8("hwContent", HW_CONTENT_DAY);
  normalizeWatchfaceSettings();
  hwLeadZero    = cfgBool("hwLeadZero", false);
  hwBarMode     = cfgU8("hwBarMode", HW_BAR_SECONDS);
  hwBarPos      = cfgU8("hwBarPos", HW_BAR_BELOW);
  hwSwap        = cfgBool("hwSwap", false);
  netTimeSync   = cfgBool("netTimeSync", true);
  showGrayedContent = cfgBool("showGrayed", true);
  autoSleepOn  = cfgBool("autoSleep", false);
  autoSleepSec = prefs.getUInt("autoSleepSec", 1800);
  if (autoSleepSec == 0) autoSleepSec = 1800;
  liveTileHighlight = cfgBool("liveHl", true);
  webUiDark         = cfgBool("uiDark", true);
  webUiPalette      = cfgU8("uiPal", 255);
  webUiShape        = cfgU8("uiShape", 0);
  if (webUiShape > 3) webUiShape = 0;
  prefs.getString("uiColors", webUiColors, sizeof(webUiColors));
  prefs.getString("uiLang", webUiLang, sizeof(webUiLang));
  if (strcmp(webUiLang, "en") != 0 && strcmp(webUiLang, "ro") != 0) strcpy(webUiLang, "en");
  defaultStartMode = cfgU8("startMode", START_MODE_WIFI);
  if (defaultStartMode > START_MODE_AP) defaultStartMode = START_MODE_WIFI;
  dateFormat    = cfgU8("dateformat", 2);
  dateLang      = cfgU8("datelang", 0);
  prefs.getString("dateCustFmt", customDateFmt, sizeof(customDateFmt));
  notifEnabled  = cfgBool("notifEn", true);
  webAccessEnabled = cfgBool("webEn", true);
  webAlertAccess   = cfgBool("webAlAcc", true);
  webAlertLocked   = cfgBool("webAlLock", true);
  webAlertLogin    = cfgBool("webAlLogin", true);
  webAlertNet      = cfgBool("webAlNet", true);
  ets2Enabled   = cfgBool("ets2En", true);
  ets2OrderFirst = cfgBool("ets2Ord", false);
  voicemeeterEnabled = cfgBool("vmEn", false);
  browserDownloadsEnabled = cfgBool("browserDlEn", false);
  obsEnabled = cfgBool("obsEn", false);
  obsShowElapsed = cfgBool("obsShowEl", true);
  beamngEnabled = cfgBool("beamngEn", false);
  assettoEnabled = cfgBool("assettoEn", false);
  qbitEnabled = cfgBool("qbitEn", false);
  bambuEnabled = cfgBool("bambuEn", false);
  volumeAdaptiveIcon = cfgBool("volAdaptive", true);
  gameFpsEnabled = cfgBool("gfpsEn", false);
  volumeEnabled = cfgBool("volEn", false);
  nfsu2Enabled = cfgBool("nfsu2En", false);
  nfsmwEnabled = cfgBool("nfsmwEn", false);
  nfscEnabled = cfgBool("nfscEn", false);
  lowBatteryEnabled = cfgBool("lowBatEn", true);
  ets2SpeedMph = cfgBool("ets2Mph", false);
  beamngSpeedMph = cfgBool("beamngMph", false);
  assettoSpeedMph = cfgBool("assettoMph", false);
  nfsu2SpeedMph = cfgBool("nfsu2Mph", false);
  nfsmwSpeedMph = cfgBool("nfsmwMph", false);
  nfscSpeedMph = cfgBool("nfscMph", false);
  ets2Gauge = cfgU8("ets2Gauge", SPEED_GAUGE_PLAIN);
  beamngGauge = cfgU8("beamngGauge", SPEED_GAUGE_PLAIN);
  assettoGauge = cfgU8("assettoGauge", SPEED_GAUGE_PLAIN);
  nfsu2Gauge = cfgU8("nfsu2Gauge", SPEED_GAUGE_PLAIN);
  nfsmwGauge = cfgU8("nfsmwGauge", SPEED_GAUGE_PLAIN);
  nfscGauge = cfgU8("nfscGauge", SPEED_GAUGE_PLAIN);
  if (ets2Gauge >= SPEED_GAUGE_COUNT) ets2Gauge = SPEED_GAUGE_PLAIN;
  if (beamngGauge >= SPEED_GAUGE_COUNT) beamngGauge = SPEED_GAUGE_PLAIN;
  if (assettoGauge >= SPEED_GAUGE_COUNT) assettoGauge = SPEED_GAUGE_PLAIN;
  if (nfsu2Gauge >= SPEED_GAUGE_COUNT) nfsu2Gauge = SPEED_GAUGE_PLAIN;
  if (nfsmwGauge >= SPEED_GAUGE_COUNT) nfsmwGauge = SPEED_GAUGE_PLAIN;
  if (nfscGauge >= SPEED_GAUGE_COUNT) nfscGauge = SPEED_GAUGE_PLAIN;
  ets2GaugeFull = constrain(prefs.getUShort("ets2GaugeFull", ETS2_GAUGE_FULL_DEFAULT), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  beamngGaugeFull = constrain(prefs.getUShort("beamngGaugeFull", BEAMNG_GAUGE_FULL_DEFAULT), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  // "assettoGaugeFull" is one character past the 15 an NVS key may have.
  assettoGaugeFull = constrain(prefs.getUShort("assettoGFull", ASSETTO_GAUGE_FULL_DEFAULT), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  nfsu2GaugeFull = constrain(prefs.getUShort("nfsu2GFull", NFSU2_GAUGE_FULL_DEFAULT), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  nfsmwGaugeFull = constrain(prefs.getUShort("nfsmwGFull", NFSMW_GAUGE_FULL_DEFAULT), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  nfscGaugeFull = constrain(prefs.getUShort("nfscGFull", NFSC_GAUGE_FULL_DEFAULT), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  vmMode = cfgU8("vmMode", 0);
  if (vmMode > 4) vmMode = 0;
  vmSensitivity = cfgU8("vmSens", 60);
  if (vmSensitivity < 10 || vmSensitivity > 100) vmSensitivity = 60;
  rebuildPriorityOrderFromEts2Order();
  nowPlayingIsPriority = cfgBool("npIsPrio", false);
  {
    // An order saved by an older build is shorter: it predates the newest
    // tiles. completePriorityOrder() keeps the user's ranking and slots the
    // new ones in; the completed order is saved by the next saveSettings().
    uint8_t savedOrder[NUM_PRIORITY_IDS];
    size_t got = prefs.getBytes("prioOrder", savedOrder, NUM_PRIORITY_IDS);
    if (got > 0) {
      uint8_t completed[NUM_PRIORITY_IDS];
      completePriorityOrder(savedOrder, (int)got, completed);
      bool changed = (got != NUM_PRIORITY_IDS);
      for (int i = 0; i < NUM_PRIORITY_IDS; i++) {
        if (i < (int)got && savedOrder[i] != completed[i]) changed = true;
        priorityOrder[i] = completed[i];
      }
      if (changed) saveSettingsDeferred();
    }
  }
  if (!nowPlayingIsPriority) npPriorityWasActive = false;
  hideTileIcons  = cfgBool("hideIcons", false);
  hideIconDate       = cfgBool("hideIconDate",  hideTileIcons);
  hideIconTemp       = cfgBool("hideIconTemp",  hideTileIcons);
  hideIconReminder   = cfgBool("hideIconRem",   hideTileIcons);
  hideIconWeather    = cfgBool("hideIconWx",    hideTileIcons);
  hideIconNotif      = cfgBool("hideIconNotif", hideTileIcons);
  hideIconNowPlaying = cfgBool("hideIconNp",    hideTileIcons);
  npAdaptiveIcon     = cfgBool("npAdaptIcon",   true);
  hideIconPressure   = cfgBool("hideIconPress", hideTileIcons);
  hideIconCurrency   = cfgBool("hideIconCurr",  hideTileIcons);
  hideIconYoutube    = cfgBool("hideIconYt",    hideTileIcons);
  hideIconWebAccess  = cfgBool("hideIconWeb",   hideTileIcons);
  hideIconIp         = cfgBool("hideIconIp",    hideTileIcons);
  hideIconVoicemeeter= cfgBool("hideIconVm",    hideTileIcons);
  hideIconBrowserDownloads = cfgBool("hideIconBrowser", hideTileIcons);
  hideIconQbit       = cfgBool("hideIconQbit",  hideTileIcons);
  hideIconBambu      = cfgBool("hideIconBambu", hideTileIcons);
  hideIconObs        = cfgBool("hideIconObs",   hideTileIcons);
  hideIconAfterburner = cfgBool("hideIconAb",  hideTileIcons);
  hideIconCountdown = cfgBool("hideIconCd",  hideTileIcons);
  hideIconGameFps    = cfgBool("hideIconGFps", hideTileIcons);
  hideIconVolume    = cfgBool("hideIconVol", hideTileIcons);
  barStyleBrowserDownloads = cfgU8("bdBarStyle", BAR_STYLE_PROGRESS);
  barStyleQbit       = cfgU8("qbitBarStyle", BAR_STYLE_UNDERLINE);
  barStyleBambu      = cfgU8("bambuBarStyle", BAR_STYLE_UNDERLINE);
  if (barStyleBrowserDownloads >= BAR_STYLE_COUNT) barStyleBrowserDownloads = BAR_STYLE_PROGRESS;
  if (barStyleQbit >= BAR_STYLE_COUNT) barStyleQbit = BAR_STYLE_UNDERLINE;
  if (barStyleBambu >= BAR_STYLE_COUNT) barStyleBambu = BAR_STYLE_UNDERLINE;
  iconSelDate       = cfgU8("iconSelDate",  0);
  iconSelTemp       = cfgU8("iconSelTemp",  0);
  iconSelReminder   = cfgU8("iconSelRem",   0);
  iconSelNotif      = cfgU8("iconSelNotif", 0);
  {
    // Migrate the old single "iconSelNp" override (if present) into
    // both new per-source slots the first time this runs.
    uint8_t legacyIconSelNp = cfgU8("iconSelNp", 0);
    iconSelNpMusic = cfgU8("iconSelNpM", legacyIconSelNp);
    iconSelNpVideo = cfgU8("iconSelNpV", legacyIconSelNp);
  }
  iconSelNpPaused   = cfgU8("iconSelNpP",  0);
  iconSelPressure   = cfgU8("iconSelPress", 0);
  iconSelCurrency   = cfgU8("iconSelCurr",  0);
  iconSelIp         = cfgU8("iconSelIp",    0);
  iconSelVoicemeeter= cfgU8("iconSelVm",    0);
  iconSelBrowserDownloads = cfgU8("iconSelBrowser", 26);
  iconSelQbit       = cfgU8("iconSelQbit",  0);
  iconSelBambu      = cfgU8("iconSelBambu", 0);
  iconSelObs        = cfgU8("iconSelObs",   27);
  iconSelBattery    = cfgU8("iconSelBattery", 0);
  // Until it had its own, the alert showed the battery tile's icon.
  iconSelLowBattery = cfgU8("iconSelLowBat", iconSelBattery);
  iconSelAfterburner = cfgU8("iconSelAb",   0);
  iconSelCountdown = cfgU8("iconSelCd",   0);
  iconSelGameFps    = cfgU8("iconSelGFps",  0);
  iconSelVolume    = cfgU8("iconSelVol",  0);
  iconWxSunny       = cfgU8("iconWxSunny",  0);
  iconWxCloud       = cfgU8("iconWxCloud",  0);
  iconWxRain        = cfgU8("iconWxRain",   0);
  iconWxStorm       = cfgU8("iconWxStorm",  0);
  iconWxSnow        = cfgU8("iconWxSnow",   0);
  iconWxWind        = cfgU8("iconWxWind",   0);
  iconWxNight       = cfgU8("iconWxNight",  0);
  scrollType     = cfgU8("scrollType", 0);
  scrollTypeDate       = cfgU8("scrollTypeDate",   scrollType);
  scrollTypeTemp       = cfgU8("scrollTypeTemp",   scrollType);
  scrollTypeReminder   = cfgU8("scrollTypeRem",    scrollType);
  scrollTypeWeather    = cfgU8("scrollTypeWx",     scrollType);
  scrollTypeNotif      = cfgU8("scrollTypeNotif",  scrollType);
  scrollTypeNowPlaying = cfgU8("scrollTypeNp",     scrollType);
  scrollTypePressure   = cfgU8("scrollTypePress",  0);
  scrollTypeCurrency   = cfgU8("scrollTypeCurr",   0);
  scrollTypeYoutube    = cfgU8("scrollTypeYt",     scrollType);
  scrollTypeWebAccess  = cfgU8("scrollTypeWeb",    scrollType);
  scrollTypeStopwatch  = cfgU8("scrollTypeSw",     scrollType);
  scrollTypeTimer      = cfgU8("scrollTypeTmr",    0);
  scrollTypeIp         = cfgU8("scrollTypeIp",     scrollType);
  scrollTypeObs        = cfgU8("scrollTypeObs",    scrollType);
  scrollTypeAfterburner = cfgU8("scrollTypeAb",  scrollType);
  scrollTypeCountdown = cfgU8("scrollTypeCd",  scrollType);
  scrollTypeGameFps    = cfgU8("scrollTypeGFps", scrollType);
  scrollTypeVolume    = cfgU8("scrollTypeVol", scrollType);
  // Pulse Bounce was a bounce and Flow + Icon a wrap with the icon inside;
  // anything else unknown falls back to Bounce.
  {
    uint8_t* sts[] = { &scrollType, &scrollTypeDate, &scrollTypeTemp, &scrollTypeReminder,
                       &scrollTypeWeather, &scrollTypeNotif, &scrollTypeNowPlaying,
                       &scrollTypePressure, &scrollTypeCurrency, &scrollTypeYoutube,
                       &scrollTypeWebAccess, &scrollTypeStopwatch, &scrollTypeTimer, &scrollTypeIp,
                       &scrollTypeObs, &scrollTypeAfterburner, &scrollTypeCountdown, &scrollTypeGameFps, &scrollTypeVolume };
    for (uint8_t* p : sts) if (*p > SCROLL_TYPE_MAX) *p = (*p == 5) ? 3 : 0;
  }
  scrollSpeed = cfgU8("scrollSpeed", NP_SCROLL_SPEED_MS);
  if (scrollSpeed < SCROLL_SPEED_MIN || scrollSpeed > SCROLL_SPEED_MAX) scrollSpeed = NP_SCROLL_SPEED_MS;
  for (int i = 0; i < SCROLL_SPEED_COUNT; i++) {
    scrollSpeedTile[i] = cfgU8((String("spd") + SPD_KEYS[i]).c_str(), scrollSpeed);
    if (scrollSpeedTile[i] < SCROLL_SPEED_MIN || scrollSpeedTile[i] > SCROLL_SPEED_MAX)
      scrollSpeedTile[i] = scrollSpeed;
  }

  fontType         = cfgU8("fontType",     0);
  fontTypeDate       = cfgU8("fontTypeDate",   fontType);
  fontTypeTemp       = cfgU8("fontTypeTemp",   fontType);
  fontTypeReminder   = cfgU8("fontTypeRem",    fontType);
  fontTypeWeather    = cfgU8("fontTypeWx",     fontType);
  fontTypeNotif      = cfgU8("fontTypeNotif",  fontType);
  fontTypeNowPlaying = cfgU8("fontTypeNp",     fontType);
  fontTypePressure   = cfgU8("fontTypePress",  fontType);
  fontTypeCurrency   = cfgU8("fontTypeCurr",   fontType);
  fontTypeYoutube    = cfgU8("fontTypeYt",     fontType);
  fontTypeWebAccess  = cfgU8("fontTypeWeb",    fontType);
  fontTypeStopwatch  = cfgU8("fontTypeSw",     fontType);
  fontTypeTimer      = cfgU8("fontTypeTmr",    fontType);
  fontTypeIp         = cfgU8("fontTypeIp",     fontType);
  fontTypeObs        = cfgU8("fontTypeObs",    fontType);
  // The speed tiles and both battery screens drew only the classic digits
  // until they had fonts of their own, so that is what they start on; the
  // alarm drew in the clock's font, and keeps it.
  fontTypeEts2       = cfgU8("fontTypeEts2",   FONT_MODE_DEFAULT);
  fontTypeBeamng     = cfgU8("fontTypeBeamng", FONT_MODE_DEFAULT);
  fontTypeAssetto    = cfgU8("fontTypeAssetto", FONT_MODE_DEFAULT);
  fontTypeNfsu2      = cfgU8("fontTypeNfsu2",  FONT_MODE_DEFAULT);
  fontTypeNfsmw      = cfgU8("fontTypeNfsmw",  FONT_MODE_DEFAULT);
  fontTypeNfsc       = cfgU8("fontTypeNfsc",   FONT_MODE_DEFAULT);
  fontTypeBattery    = cfgU8("fontTypeBat",    FONT_MODE_DEFAULT);
  fontTypeLowBattery = cfgU8("fontTypeLowBat", FONT_MODE_DEFAULT);
  fontTypeAfterburner = cfgU8("fontTypeAb",    fontType);
  fontTypeCountdown = cfgU8("fontTypeCd",    fontType);
  fontTypeGameFps    = cfgU8("fontTypeGFps",   fontType);
  fontTypeVolume    = cfgU8("fontTypeVol",   fontType);
  fontTypeAlarm      = cfgU8("fontTypeAlarm",  fontType);
  // A value this build has no font for (saved by a newer one) would reach
  // draw paths that only know the modes above; the default always draws.
  {
    uint8_t* fts[] = { &fontType, &fontTypeDate, &fontTypeTemp, &fontTypeReminder,
                       &fontTypeWeather, &fontTypeNotif, &fontTypeNowPlaying,
                       &fontTypePressure, &fontTypeCurrency, &fontTypeYoutube,
                       &fontTypeWebAccess, &fontTypeStopwatch, &fontTypeTimer, &fontTypeIp,
                       &fontTypeObs, &fontTypeEts2, &fontTypeBeamng, &fontTypeAssetto, &fontTypeAlarm,
                       &fontTypeBattery, &fontTypeLowBattery, &fontTypeAfterburner, &fontTypeGameFps, &fontTypeVolume, &fontTypeNfsu2, &fontTypeNfsmw, &fontTypeNfsc,
                       &fontTypeCountdown };
    for (uint8_t* p : fts) {
      if (*p >= FONT_MODE_COUNT) *p = FONT_MODE_DEFAULT;
      else if (!fontModeOk(*p)) *p = FONT_MODE_MAKO;   // Umbra and Terminal were both Mako's
    }
  }
  tileTransGlobal = cfgU8("trGlobal", 0);
  tileTransHour   = cfgU8("trHour",   tileTransGlobal);
  tileTransDate   = cfgU8("trDate",   tileTransGlobal);
  tileTransTemp   = cfgU8("trTemp",   tileTransGlobal);
  tileTransNp     = cfgU8("trNp",     tileTransGlobal);
  tileTransWx     = cfgU8("trWx",     tileTransGlobal);
  tileTransRem    = cfgU8("trRem",    tileTransGlobal);
  tileTransCanvas = cfgU8("trCanvas", tileTransGlobal);
  tileTransPress  = cfgU8("trPress",  tileTransGlobal);
  tileTransSs     = cfgU8("trSs",     tileTransGlobal);
  tileTransCurr   = cfgU8("trCurr",   tileTransGlobal);
  tileTransYt     = cfgU8("trYt",     tileTransGlobal);
  tileTransHw     = cfgU8("trHw",     tileTransGlobal);
  tileTransWeb    = cfgU8("trWeb",    tileTransGlobal);
  tileTransNotif  = cfgU8("trNotif",  tileTransGlobal);
  tileTransEts2   = cfgU8("trEts2",   tileTransGlobal);
  tileTransVm     = cfgU8("trVm",     tileTransGlobal);
  tileTransBrowserDownloads = cfgU8("trBrowser", tileTransGlobal);
  tileTransObs    = cfgU8("trObs",    tileTransGlobal);
  tileTransBeamng = cfgU8("trBeamng", tileTransGlobal);
  tileTransAssetto = cfgU8("trAssetto", tileTransGlobal);
  tileTransNfsu2 = cfgU8("trNfsu2", tileTransGlobal);
  tileTransNfsmw = cfgU8("trNfsmw", tileTransGlobal);
  tileTransNfsc = cfgU8("trNfsc", tileTransGlobal);
  tileTransQbit = cfgU8("trQbit", tileTransGlobal);
  tileTransBambu = cfgU8("trBambu", tileTransGlobal);
  tileTransAfterburner = cfgU8("trAb", tileTransGlobal);
  tileTransCountdown = cfgU8("trCd", tileTransGlobal);
  tileTransGameFps = cfgU8("trGFps", tileTransGlobal);
  tileTransVolume = cfgU8("trVol", tileTransGlobal);
  tileTransSw     = cfgU8("trSw",     tileTransGlobal);
  tileTransTmr    = cfgU8("trTmr",    tileTransGlobal);
  tileTransAlarm  = cfgU8("trAlarm",  tileTransGlobal);
  tileTransIp     = cfgU8("trIp",     tileTransGlobal);
  tileTransC2P    = cfgU8("trC2P",    tileTransGlobal);
  tileTransP2C    = cfgU8("trP2C",    tileTransGlobal);
  tileTransSpeed  = cfgU8("trSpeed",  TRSPD_DEFAULT);
  if (tileTransSpeed < TRSPD_MIN || tileTransSpeed > TRSPD_MAX) tileTransSpeed = TRSPD_DEFAULT;
  // A shorter blob from an older build leaves the newer entries on the global.
  for (int i = 0; i < TRSPD_COUNT; i++) tileTransSpd[i] = tileTransSpeed;
  prefs.getBytes("trSpeeds", tileTransSpd, TRSPD_COUNT);
  for (int i = 0; i < TRSPD_COUNT; i++)
    if (tileTransSpd[i] < TRSPD_MIN || tileTransSpd[i] > TRSPD_MAX) tileTransSpd[i] = tileTransSpeed;
  prefs.getString("evSndTimer", eventSoundTimer, sizeof(eventSoundTimer));
  if (strlen(eventSoundTimer) == 0) strcpy(eventSoundTimer, "calm");
  timerDurationSec = prefs.getUInt("timerDurSec", 300);
  if (timerDurationSec == 0) timerDurationSec = 300;
  timerPreset = cfgU8("timerPreset", 0);
  timerRemainingSnapMs = (unsigned long)timerDurationSec * 1000UL;
  timerFinished = false;
  timerMode    = cfgU8("timerMode", TIMER_MODE_TIMER) == TIMER_MODE_POMODORO ? TIMER_MODE_POMODORO : TIMER_MODE_TIMER;
  pomoWorkMin  = constrain(cfgU8("pomoWork", 25), 1, 120);
  pomoShortMin = constrain(cfgU8("pomoShort", 5), 1, 60);
  pomoLongMin  = constrain(cfgU8("pomoLong", 15), 1, 60);
  pomoRounds   = constrain(cfgU8("pomoRounds", 4), 2, 8);
  prefs.getString("evSndPomoB", eventSoundPomoBreak, sizeof(eventSoundPomoBreak));
  if (strlen(eventSoundPomoBreak) == 0) strcpy(eventSoundPomoBreak, "chime");
  prefs.getString("evSndPomoW", eventSoundPomoWork, sizeof(eventSoundPomoWork));
  if (strlen(eventSoundPomoWork) == 0) strcpy(eventSoundPomoWork, "bell");
  if (timerMode == TIMER_MODE_POMODORO) pomoReset();
  timerWasActive = false;
  // Each alarm is one packed word plus its tone. A key that was never written
  // leaves the built-in default in place, which is what a first boot wants.
  for (int i = 0; i < ALARM_COUNT; i++) {
    char k[12];
    snprintf(k, sizeof(k), "alarm%d", i);
    uint32_t packed = prefs.getUInt(k, 0xFFFFFFFFUL);
    if (packed != 0xFFFFFFFFUL) {
      uint8_t h = (uint8_t)(packed & 0xFF);
      uint8_t m = (uint8_t)((packed >> 8) & 0xFF);
      if (h < 24 && m < 60) {
        alarms[i].hour   = h;
        alarms[i].minute = m;
        alarms[i].days   = (uint8_t)((packed >> 16) & 0x7F);
      }
      alarms[i].enabled = ((packed >> 23) & 1) != 0;
      // Bit 25 marks a word written by a build that knows about presence. An
      // older one has neither bit, and an alarm it had switched on is one the
      // owner set on purpose - so that is what comes back.
      alarms[i].present = ((packed >> 25) & 1)
                            ? (((packed >> 24) & 1) != 0)
                            : alarms[i].enabled;
    }
    snprintf(k, sizeof(k), "alarmTone%d", i);
    prefs.getString(k, alarms[i].tone, sizeof(alarms[i].tone));
    if (strlen(alarms[i].tone) == 0) strcpy(alarms[i].tone, "classic");
    alarmFired[i] = false;
  }
  // Nothing rings the instant the device comes up: an alarm whose minute went by
  // while it was off has gone by, and one whose minute is now gets picked up by
  // the first alarmTick() after the clock is set.
  alarmRinging    = false;
  alarmRingingIdx = -1;
  alarmWasActive  = false;
  for (int i = 0; i < NUM_ITEMS; i++) {
    String base = "it" + String(i);
    items[i].id          = cfgU8((base + "id").c_str(),  items[i].id);
    items[i].enabled     = cfgBool((base + "en").c_str(),  items[i].enabled);
    items[i].durationSec = prefs.getUShort((base + "dur").c_str(), items[i].durationSec);
    items[i].order       = cfgU8((base + "ord").c_str(),  items[i].order);
  }

  repairItemIds();
  tileHiddenMask = (uint16_t)prefs.getUShort("tileHidden", 0);
  // 32 bits since priority tile 16 (NFS Underground 2). Builds before it kept
  // 16 in "prioHidden2", and 8 before that in "prioHidden".
  prioHiddenMask = cfgIsKey("prioHidden3")
    ? prefs.getUInt("prioHidden3", 0)
    : prefs.getUShort("prioHidden2", cfgU8("prioHidden", 0));
  // A priority tile added in a newer build starts out of the Tile Manager and
  // switched off, once; it is added from "Add a tile" by whoever wants it.
  // The namespace is open read-only here, so the flags that say it happened
  // are written by the next saveSettings() - asked for below. (Written here
  // they were lost, and the tiles were taken out again at every start.)
  bool prioInitSave = false;
  // "prioHidden2" is only ever written by a build that already had Voicemeeter
  // and Browser Downloads, so its mask already holds the user's choice for
  // both - even though that build lost their flags.
  bool vmBrowserKnown = cfgIsKey("prioHidden2");
  if (!cfgBool("vmPrioInit", false) && !vmBrowserKnown) {
    prioHiddenMask |= (1u << PRIORITY_ID_VOICEMEETER);
    voicemeeterEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("browserPrioInit", false) && !vmBrowserKnown) {
    prioHiddenMask |= (1u << PRIORITY_ID_BROWSER_DOWNLOADS);
    browserDownloadsEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("obsPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_OBS);
    obsEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("beamngPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_BEAMNG);
    beamngEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("assettoPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_ASSETTO);
    assettoEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("nfsu2PrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_NFSU2);
    nfsu2Enabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("nfsmwPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_NFSMW);
    nfsmwEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("nfscPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_NFSC);
    nfscEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("qbitPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_QBIT);
    qbitEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("bambuPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_BAMBU);
    bambuEnabled = false;
    prioInitSave = true;
  }
  // Game FPS, new in this build: out of the Tile Manager and off until added.
  if (!cfgBool("gfpsPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_GAMEFPS);
    gameFpsEnabled = false;
    prioInitSave = true;
  }
  if (!cfgBool("volPrioInit", false)) {
    prioHiddenMask |= (1u << PRIORITY_ID_VOLUME);
    volumeEnabled = false;
    prioInitSave = true;
  }
  // The flag was first named "batteryTilesInit": 16 characters, one more than
  // an NVS key may have, so it was never saved, every save that carried it
  // failed and was retried, and the two battery tiles were taken out of the
  // Tile Manager at every start. "iconSelBattery" was written by those same
  // builds, so a clock that has it has had the setup already, and its saved
  // masks hold the user's own choice.
  if (!cfgBool("batTilesInit", false) && !cfgIsKey("iconSelBattery")) {
    tileHiddenMask |= (1u << ITEM_BATTERY);
    prioHiddenMask |= (1u << PRIORITY_ID_LOW_BATTERY);
    prioInitSave = true;
  }
  // MSI Afterburner's tile, new in this build, starts out of the Tile Manager
  // too, once; "abTileInit" is written by the next saveSettings().
  if (!cfgBool("abTileInit", false)) {
    tileHiddenMask |= (1u << ITEM_AFTERBURNER);
    prioInitSave = true;
  }
  // The countdown, new in this build: the same, once ("cdTileInit").
  if (!cfgBool("cdTileInit", false)) {
    tileHiddenMask |= (1u << ITEM_COUNTDOWN);
    prioInitSave = true;
  }
  if (prioInitSave) saveSettingsDeferred();
  applyTileHiddenMask();
  curBrightness = cfgU8("bright",  BRIGHTNESS);
  dimAutoOn     = cfgBool("dimAuto",  false);
  dimFromH      = cfgU8("dimFH",   22);
  dimFromM      = cfgU8("dimFM",   0);
  dimToH        = cfgU8("dimTH",   7);
  dimToM        = cfgU8("dimTM",   0);
  dimLevel      = cfgU8("dimLvl",  1);
  prefs.getString("wxCity",   weatherCity,   sizeof(weatherCity));
  prefs.getString("wxApiKey", weatherApiKey, sizeof(weatherApiKey));
  // A key mangled by the old masked-field bug is not a key. Drop it, so the
  // tile says "no key" and asks for a new one instead of spending every fetch
  // on a 401 nobody can see.
  if (weatherApiKey[0] && !wxKeyLooksValid(String(weatherApiKey))) weatherApiKey[0] = '\0';
  prefs.getString("wxLang",   weatherLang,   sizeof(weatherLang));
  wxPreset = cfgU8("wxPreset", WX_SHOW_ALL);
  if (wxPreset == 0 || wxPreset > WX_SHOW_ALL) wxPreset = WX_SHOW_ALL;
  weatherLat = prefs.getFloat("wxLat", weatherLat);
  weatherLon = prefs.getFloat("wxLon", weatherLon);
  {
    // Social tiles: three slots stored under indexed keys.
    prefs.getString("socH0", social[SOC_YT].handle, sizeof(social[SOC_YT].handle));
    prefs.getString("socK0", social[SOC_YT].apiKey, sizeof(social[SOC_YT].apiKey));
    social[SOC_YT].showName = cfgBool("socN0", false);
    social[SOC_YT].count = 0;
    social[SOC_YT].valid = false;
    iconSelYoutube = cfgU8("icoYt", 0);
  }
  prefs.getString("currBase",  currencyBase,  sizeof(currencyBase));
  if (strlen(currencyBase) == 0) strcpy(currencyBase, "EUR");
  prefs.getString("currQuote", currencyQuote, sizeof(currencyQuote));
  if (strlen(currencyQuote) == 0) strcpy(currencyQuote, "RON");
  currencyCompareEnabled = cfgBool("currCompare", false);
  prefs.getString("memoText", mementoBuf, sizeof(mementoBuf));
  prefs.getBytes("canvasBmp", canvasBitmap, CANVAS_COLS);
  cdFullUnit = cfgBool("cdFull", false);
  cdLang = cfgIsKey("cdLang") ? (cfgU8("cdLang", 0) ? 1 : 0) : (strcmp(webUiLang, "en") == 0 ? 1 : 0);
  memset(cdEvents, 0, sizeof(cdEvents));
  if (prefs.getBytesLength("cdEvents") == sizeof(cdEvents)) prefs.getBytes("cdEvents", cdEvents, sizeof(cdEvents));
  for (auto& e : cdEvents) {
    e.name[CD_NAME_MAX - 1] = '\0';
    if (e.month < 1 || e.month > 12 || e.day < 1 || e.day > 31) e.flags = 0;
    if (e.icon > ICON_CATALOG_COUNT) e.icon = 0;
    // The tile had one icon for every event; each event has its own now, so a
    // custom one becomes the icon of the events that have none.
    if ((e.flags & CD_USED) && !e.icon) e.icon = iconSelCountdown;
  }
  iconSelCountdown = 0;
  free(gCfgPack);
  gCfgPack = nullptr;
  gCfgPackCount = 0;
  prefs.end();
  sortItems();
}

// DEFERRED SETTINGS WRITE
//
// Coalesce a burst of changes, then compare against NVS and persist only values
// that actually changed, with one commit. Also used by event sound settings.
static bool          settingsDirty   = false;
static unsigned long settingsDirtyMs = 0;
#define SETTINGS_FLUSH_MS 800UL

// Set once a backup starts being written back: the settings still in RAM are
// the ones it replaces, and a save landing before the restart would put them
// back over it.
static volatile bool settingsFrozen = false;

void saveSettingsDeferred() {
  settingsDirty   = true;
  settingsDirtyMs = millis();
}

// Write now if anything is pending - used before a deliberate reboot.
void saveSettingsFlush() {
  if (!settingsDirty) return;
  settingsDirty = false;
  saveSettings();
}

// Drop anything pending - used by the factory reset, so a queued write cannot
// repopulate the namespace we just wiped.
void saveSettingsCancel() { settingsDirty = false; }

static void settingsFlushTick() {
  if (!settingsDirty) return;
  if (millis() - settingsDirtyMs < SETTINGS_FLUSH_MS) return;
  saveSettingsFlush();
}

void saveSettings() {
  if (settingsFrozen) return;
  SettingsWriter writer;
  if (!writer.begin()) {
    saveSettingsDeferred();
    return;
  }
  writer.putBool("buzzer", buzzerOn);
  writer.putUChar("buzzerVol", buzzerVolume);
  writer.putUChar("bzVolNotif", buzzVolNotif);
  writer.putUChar("bzVolAuto",  buzzVolAuto);
  writer.putUChar("bzVolAlarm", buzzVolAlarm);
  writer.putUChar("bzVolTouch", buzzVolTouch);
  writer.putString("buzzerPreset", buzzerPreset);
  writer.putUChar("npmode", npDisplayMode);
  writer.putUChar("ssAnim", ssAnimSelected);
  writer.putUChar("tempunit", tempUnit);
  writer.putUChar("abMode", abMode);
  writer.putUChar("hourformat", hourFormat);
  writer.putBool("hourLeadZero", hourLeadingZero);
  writer.putUChar("hwFmt", hwFormat);
  writer.putUChar("hwSkin", hwSkin);
  writer.putUChar("hwContent", hwContent);
  writer.putBool("hwLeadZero", hwLeadZero);
  writer.putUChar("hwBarMode", hwBarMode);
  writer.putUChar("hwBarPos", hwBarPos);
  writer.putBool("hwSwap", hwSwap);
  writer.putBool("netTimeSync", netTimeSync);
  writer.putBool("showGrayed", showGrayedContent);
  writer.putBool("autoSleep", autoSleepOn);
  writer.putUInt("autoSleepSec", autoSleepSec);
  writer.putBool("liveHl", liveTileHighlight);
  writer.putBool("uiDark", webUiDark);
  writer.putUChar("uiShape", webUiShape);
  writer.putUChar("uiPal", webUiPalette);
  writer.putString("uiColors", webUiColors);
  writer.putString("accentColor", accentColor);
  writer.putString("uiLang", webUiLang);
  writer.putUChar("startMode", defaultStartMode);
  writer.putUChar("dateformat", dateFormat);
  writer.putUChar("datelang", dateLang);
  writer.putString("dateCustFmt", customDateFmt);
  writer.putUChar("touchTap", touchTapAction);
  writer.putUChar("touchDbl", touchDoubleTapAction);
  writer.putBool("notifEn", notifEnabled);
  writer.putBool("webEn", webAccessEnabled);
  writer.putBool("webAlAcc", webAlertAccess);
  writer.putBool("webAlLock", webAlertLocked);
  writer.putBool("webAlLogin", webAlertLogin);
  writer.putBool("webAlNet", webAlertNet);
  writer.putBool("ets2En", ets2Enabled);
  writer.putBool("ets2Ord", ets2OrderFirst);
  writer.putBool("vmEn", voicemeeterEnabled);
  writer.putBool("browserDlEn", browserDownloadsEnabled);
  writer.putBool("obsEn", obsEnabled);
  writer.putBool("obsShowEl", obsShowElapsed);
  writer.putBool("beamngEn", beamngEnabled);
  writer.putBool("assettoEn", assettoEnabled);
  writer.putBool("qbitEn", qbitEnabled);
  writer.putBool("bambuEn", bambuEnabled);
  writer.putBool("volAdaptive", volumeAdaptiveIcon);
  writer.putBool("gfpsEn", gameFpsEnabled);
  writer.putBool("volEn", volumeEnabled);
  writer.putBool("nfsu2En", nfsu2Enabled);
  writer.putBool("nfsmwEn", nfsmwEnabled);
  writer.putBool("nfscEn", nfscEnabled);
  writer.putBool("lowBatEn", lowBatteryEnabled);
  writer.putBool("ets2Mph", ets2SpeedMph);
  writer.putBool("beamngMph", beamngSpeedMph);
  writer.putBool("assettoMph", assettoSpeedMph);
  writer.putBool("nfsu2Mph", nfsu2SpeedMph);
  writer.putBool("nfsmwMph", nfsmwSpeedMph);
  writer.putBool("nfscMph", nfscSpeedMph);
  writer.putUChar("ets2Gauge", ets2Gauge);
  writer.putUChar("beamngGauge", beamngGauge);
  writer.putUChar("assettoGauge", assettoGauge);
  writer.putUChar("nfsu2Gauge", nfsu2Gauge);
  writer.putUChar("nfsmwGauge", nfsmwGauge);
  writer.putUChar("nfscGauge", nfscGauge);
  writer.putUShort("ets2GaugeFull", ets2GaugeFull);
  writer.putUShort("beamngGaugeFull", beamngGaugeFull);
  writer.putUShort("assettoGFull", assettoGaugeFull);
  writer.putUShort("nfsu2GFull", nfsu2GaugeFull);
  writer.putUShort("nfsmwGFull", nfsmwGaugeFull);
  writer.putUShort("nfscGFull", nfscGaugeFull);
  writer.putUChar("vmMode", vmMode);
  writer.putUChar("vmSens", vmSensitivity);
  writer.putBool("npIsPrio", nowPlayingIsPriority);
  writer.putBytes("prioOrder", priorityOrder, NUM_PRIORITY_IDS);
  writer.putBool("hideIcons", hideTileIcons);
  writer.putBool("hideIconDate",  hideIconDate);
  writer.putBool("hideIconTemp",  hideIconTemp);
  writer.putBool("hideIconRem",   hideIconReminder);
  writer.putBool("hideIconWx",    hideIconWeather);
  writer.putBool("hideIconNotif", hideIconNotif);
  writer.putBool("hideIconNp",    hideIconNowPlaying);
  writer.putBool("npAdaptIcon",   npAdaptiveIcon);
  writer.putBool("hideIconPress", hideIconPressure);
  writer.putBool("hideIconCurr",  hideIconCurrency);
  writer.putBool("hideIconYt",    hideIconYoutube);
  writer.putBool("hideIconWeb",   hideIconWebAccess);
  writer.putBool("hideIconIp",    hideIconIp);
  writer.putBool("hideIconVm",    hideIconVoicemeeter);
  writer.putBool("hideIconBrowser", hideIconBrowserDownloads);
  writer.putBool("hideIconQbit",  hideIconQbit);
  writer.putBool("hideIconBambu", hideIconBambu);
  writer.putBool("hideIconObs",   hideIconObs);
  writer.putBool("hideIconAb",    hideIconAfterburner);
  writer.putBool("hideIconCd",    hideIconCountdown);
  writer.putBool("hideIconGFps",  hideIconGameFps);
  writer.putBool("hideIconVol",  hideIconVolume);
  writer.putUChar("bdBarStyle",   barStyleBrowserDownloads);
  writer.putUChar("qbitBarStyle", barStyleQbit);
  writer.putUChar("bambuBarStyle", barStyleBambu);
  writer.putUChar("iconSelDate",  iconSelDate);
  writer.putUChar("iconSelTemp",  iconSelTemp);
  writer.putUChar("iconSelRem",   iconSelReminder);
  writer.putUChar("iconSelNotif", iconSelNotif);
  writer.putUChar("iconSelNpM",   iconSelNpMusic);
  writer.putUChar("iconSelNpV",   iconSelNpVideo);
  writer.putUChar("iconSelNpP",   iconSelNpPaused);
  writer.putUChar("iconSelPress", iconSelPressure);
  writer.putUChar("iconSelCurr",  iconSelCurrency);
  writer.putUChar("iconSelIp",    iconSelIp);
  writer.putUChar("iconSelVm",    iconSelVoicemeeter);
  writer.putUChar("iconSelBrowser", iconSelBrowserDownloads);
  writer.putUChar("iconSelQbit",  iconSelQbit);
  writer.putUChar("iconSelBambu", iconSelBambu);
  writer.putUChar("iconSelObs",   iconSelObs);
  writer.putUChar("iconSelBattery", iconSelBattery);
  writer.putUChar("iconSelLowBat", iconSelLowBattery);
  writer.putUChar("iconSelAb",    iconSelAfterburner);
  writer.putUChar("iconSelCd",    iconSelCountdown);
  writer.putUChar("iconSelGFps",  iconSelGameFps);
  writer.putUChar("iconSelVol",  iconSelVolume);
  writer.putBool("batTilesInit", true);
  writer.putUChar("iconWxSunny",  iconWxSunny);
  writer.putUChar("iconWxCloud",  iconWxCloud);
  writer.putUChar("iconWxRain",   iconWxRain);
  writer.putUChar("iconWxStorm",  iconWxStorm);
  writer.putUChar("iconWxSnow",   iconWxSnow);
  writer.putUChar("iconWxWind",   iconWxWind);
  writer.putUChar("iconWxNight",  iconWxNight);
  writer.putUChar("scrollType", scrollType);
  writer.putUChar("scrollTypeDate",  scrollTypeDate);
  writer.putUChar("scrollTypeTemp",  scrollTypeTemp);
  writer.putUChar("scrollTypeRem",   scrollTypeReminder);
  writer.putUChar("scrollTypeWx",    scrollTypeWeather);
  writer.putUChar("scrollTypeNotif", scrollTypeNotif);
  writer.putUChar("scrollTypeNp",    scrollTypeNowPlaying);
  writer.putUChar("scrollTypePress", scrollTypePressure);
  writer.putUChar("scrollTypeCurr",  scrollTypeCurrency);
  writer.putUChar("scrollTypeYt",    scrollTypeYoutube);
  writer.putUChar("scrollTypeWeb",   scrollTypeWebAccess);
  writer.putUChar("scrollTypeSw",    scrollTypeStopwatch);
  writer.putUChar("scrollTypeTmr",   scrollTypeTimer);
  writer.putUChar("scrollTypeIp",    scrollTypeIp);
  writer.putUChar("scrollTypeObs",   scrollTypeObs);
  writer.putUChar("scrollTypeAb",    scrollTypeAfterburner);
  writer.putUChar("scrollTypeCd",    scrollTypeCountdown);
  writer.putUChar("scrollTypeGFps",  scrollTypeGameFps);
  writer.putUChar("scrollTypeVol",  scrollTypeVolume);
  writer.putUChar("scrollSpeed", scrollSpeed);
  for (int i = 0; i < SCROLL_SPEED_COUNT; i++)
    writer.putUChar((String("spd") + SPD_KEYS[i]).c_str(), scrollSpeedTile[i]);

  writer.putUChar("fontType",     fontType);
  writer.putUChar("fontTypeDate",  fontTypeDate);
  writer.putUChar("fontTypeTemp",  fontTypeTemp);
  writer.putUChar("fontTypeRem",   fontTypeReminder);
  writer.putUChar("fontTypeWx",    fontTypeWeather);
  writer.putUChar("fontTypeNotif", fontTypeNotif);
  writer.putUChar("fontTypeNp",    fontTypeNowPlaying);
  writer.putUChar("fontTypePress", fontTypePressure);
  writer.putUChar("fontTypeCurr",  fontTypeCurrency);
  writer.putUChar("fontTypeYt",    fontTypeYoutube);
  writer.putUChar("fontTypeWeb",   fontTypeWebAccess);
  writer.putUChar("fontTypeSw",    fontTypeStopwatch);
  writer.putUChar("fontTypeTmr",   fontTypeTimer);
  writer.putUChar("fontTypeIp",    fontTypeIp);
  writer.putUChar("fontTypeObs",   fontTypeObs);
  writer.putUChar("fontTypeEts2",   fontTypeEts2);
  writer.putUChar("fontTypeBeamng", fontTypeBeamng);
  writer.putUChar("fontTypeAssetto", fontTypeAssetto);
  writer.putUChar("fontTypeNfsu2",  fontTypeNfsu2);
  writer.putUChar("fontTypeNfsmw",  fontTypeNfsmw);
  writer.putUChar("fontTypeNfsc",   fontTypeNfsc);
  writer.putUChar("fontTypeAlarm",  fontTypeAlarm);
  writer.putUChar("fontTypeBat",    fontTypeBattery);
  writer.putUChar("fontTypeLowBat", fontTypeLowBattery);
  writer.putUChar("fontTypeAb",     fontTypeAfterburner);
  writer.putUChar("fontTypeCd",     fontTypeCountdown);
  writer.putUChar("fontTypeGFps",   fontTypeGameFps);
  writer.putUChar("fontTypeVol",   fontTypeVolume);
  writer.putUChar("trGlobal", tileTransGlobal);
  writer.putUChar("trHour",   tileTransHour);
  writer.putUChar("trDate",   tileTransDate);
  writer.putUChar("trTemp",   tileTransTemp);
  writer.putUChar("trNp",     tileTransNp);
  writer.putUChar("trWx",     tileTransWx);
  writer.putUChar("trRem",    tileTransRem);
  writer.putUChar("trCanvas", tileTransCanvas);
  writer.putUChar("trPress",  tileTransPress);
  writer.putUChar("trSs",     tileTransSs);
  writer.putUChar("trCurr",   tileTransCurr);
  writer.putUChar("trYt",     tileTransYt);
  writer.putUChar("trHw",     tileTransHw);
  writer.putUChar("trWeb",    tileTransWeb);
  writer.putUChar("trNotif",  tileTransNotif);
  writer.putUChar("trEts2",   tileTransEts2);
  writer.putUChar("trVm",     tileTransVm);
  writer.putUChar("trBrowser", tileTransBrowserDownloads);
  writer.putUChar("trObs",    tileTransObs);
  writer.putUChar("trBeamng", tileTransBeamng);
  writer.putUChar("trAssetto", tileTransAssetto);
  writer.putUChar("trNfsu2", tileTransNfsu2);
  writer.putUChar("trNfsmw", tileTransNfsmw);
  writer.putUChar("trNfsc", tileTransNfsc);
  writer.putUChar("trQbit", tileTransQbit);
  writer.putUChar("trBambu", tileTransBambu);
  writer.putUChar("trAb", tileTransAfterburner);
  writer.putUChar("trCd", tileTransCountdown);
  writer.putUChar("trGFps", tileTransGameFps);
  writer.putUChar("trVol", tileTransVolume);
  writer.putUChar("trSw",     tileTransSw);
  writer.putUChar("trTmr",    tileTransTmr);
  writer.putUChar("trAlarm",  tileTransAlarm);
  writer.putUChar("trIp",     tileTransIp);
  writer.putUChar("trC2P",    tileTransC2P);
  writer.putUChar("trP2C",    tileTransP2C);
  writer.putUChar("trSpeed",  tileTransSpeed);
  writer.putBytes("trSpeeds", tileTransSpd, TRSPD_COUNT);
  writer.putString("evSndTile", eventSoundTile);
  writer.putString("evSndWifi", eventSoundWifi);
  writer.putString("evSndWeb", eventSoundWeb);
  writer.putString("evSndNotif", eventSoundNotif);
  writer.putString("evSndEts2", eventSoundEts2);
  writer.putString("evSndTouch", eventSoundTouch);
  writer.putString("evSndTimer", eventSoundTimer);
  writer.putUInt("timerDurSec", timerDurationSec);
  writer.putUChar("timerPreset", timerPreset);
  writer.putUChar("timerMode", timerMode);
  writer.putUChar("pomoWork", pomoWorkMin);
  writer.putUChar("pomoShort", pomoShortMin);
  writer.putUChar("pomoLong", pomoLongMin);
  writer.putUChar("pomoRounds", pomoRounds);
  writer.putString("evSndPomoB", eventSoundPomoBreak);
  writer.putString("evSndPomoW", eventSoundPomoWork);
  for (int i = 0; i < ALARM_COUNT; i++) {
    char k[12];
    snprintf(k, sizeof(k), "alarm%d", i);
    writer.putUInt(k, (uint32_t)alarms[i].hour
                   | ((uint32_t)alarms[i].minute << 8)
                   | ((uint32_t)(alarms[i].days & 0x7F) << 16)
                   | ((uint32_t)(alarms[i].enabled ? 1 : 0) << 23)
                   | ((uint32_t)(alarms[i].present ? 1 : 0) << 24)
                   | (1UL << 25));
    snprintf(k, sizeof(k), "alarmTone%d", i);
    writer.putString(k, alarms[i].tone);
  }
  writer.putUShort("tileHidden", tileHiddenMask);
  writer.putUChar("prioHidden", (uint8_t)(prioHiddenMask & 0xFF));
  writer.putUShort("prioHidden2", (uint16_t)(prioHiddenMask & 0xFFFF));
  writer.putUInt("prioHidden3", prioHiddenMask);
  // Every priority tile's first-start setup has run by the time anything is
  // saved (loadSettings() does it), so these are simply true from now on.
  writer.putBool("vmPrioInit", true);
  writer.putBool("browserPrioInit", true);
  writer.putBool("obsPrioInit", true);
  writer.putBool("beamngPrioInit", true);
  writer.putBool("assettoPrioInit", true);
  writer.putBool("nfsu2PrioInit", true);
  writer.putBool("nfsmwPrioInit", true);
  writer.putBool("nfscPrioInit", true);
  writer.putBool("qbitPrioInit", true);
  writer.putBool("bambuPrioInit", true);
  writer.putBool("gfpsPrioInit", true);
  writer.putBool("volPrioInit", true);
  writer.putBool("abTileInit", true);
  writer.putBool("cdTileInit", true);
  for (int i = 0; i < NUM_ITEMS; i++) {
    String base = "it" + String(i);
    writer.putUChar((base + "id").c_str(),   items[i].id);
    writer.putBool((base + "en").c_str(),    items[i].enabled);
    writer.putUShort((base + "dur").c_str(), items[i].durationSec);
    writer.putUChar((base + "ord").c_str(),  items[i].order);
  }
  writer.putUChar("bright",  curBrightness);
  writer.putBool("dimAuto",  dimAutoOn);
  writer.putUChar("dimFH",   dimFromH);
  writer.putUChar("dimFM",   dimFromM);
  writer.putUChar("dimTH",   dimToH);
  writer.putUChar("dimTM",   dimToM);
  writer.putUChar("dimLvl",  dimLevel);
  writer.putString("wxCity",   weatherCity);
  writer.putString("wxApiKey", weatherApiKey);
  writer.putString("wxLang",   weatherLang);
  writer.putUChar("wxPreset", wxPreset);
  writer.putFloat("wxLat",    weatherLat);
  writer.putFloat("wxLon",    weatherLon);
  {
    writer.putString("socH0", social[SOC_YT].handle);
    writer.putString("socK0", social[SOC_YT].apiKey);
    writer.putBool("socN0", social[SOC_YT].showName);
    writer.putUChar("icoYt", iconSelYoutube);
  }
  writer.putString("currBase",  currencyBase);
  writer.putString("currQuote", currencyQuote);
  writer.putBool("currCompare", currencyCompareEnabled);
  writer.putString("memoText", mementoBuf);
  writer.putBytes("canvasBmp", canvasBitmap, CANVAS_COLS);
  writer.putBytes("cdEvents", cdEvents, sizeof(cdEvents));
  writer.putBool("cdFull", cdFullUnit);
  writer.putUChar("cdLang", cdLang);
  if (!writer.end()) {
    // A failed write remains dirty and is retried after the debounce window.
    saveSettingsDeferred();
    Serial.println("[settings] NVS write failed; retry scheduled");
  }
}

// WIFI
String scanNetworks() {
  // WiFi.scanNetworks() with no args runs SYNCHRONOUSLY and blocks the calling
  // task - and since this whole app runs on the single-threaded, synchronous
  // WebServer (not an async server), that blocked every other HTTP request
  // (including /state, and a second /scan) for the full scan duration
  // (commonly several seconds), plus froze loop() itself (display updates,
  // etc.) for that window. This is rewritten around the async SDK scan so the
  // handler always returns immediately; the client polls until results land.
  int n = WiFi.scanComplete();

  if (n == WIFI_SCAN_RUNNING) {
    // A scan (ours or one kicked off moments ago) is still in flight. Say so
    // explicitly instead of returning an empty list, so the client can tell
    // "try again shortly" apart from "no networks found".
    return "{\"scanning\":true,\"networks\":[]}";
  }

  if (n == WIFI_SCAN_FAILED) {
    // No scan has completed yet (first call ever, or the previous one
    // errored). Kick one off and tell the client to poll again.
    WiFi.scanNetworks(true /* async */);
    return "{\"scanning\":true,\"networks\":[]}";
  }

  // n >= 0: a completed scan's results are ready.
  String json;
  json.reserve(64 + n * 96);
  json = "{\"scanning\":false,\"networks\":[";
  for (int i = 0; i < n; i++) {
    if (i > 0) json += ",";
    wifi_auth_mode_t auth = WiFi.encryptionType(i);
    bool secured = (auth != WIFI_AUTH_OPEN);
    String authStr;
    switch (auth) {
      case WIFI_AUTH_OPEN:          authStr = "Open"; break;
      case WIFI_AUTH_WEP:           authStr = "WEP"; break;
      case WIFI_AUTH_WPA_PSK:       authStr = "WPA"; break;
      case WIFI_AUTH_WPA2_PSK:      authStr = "WPA2"; break;
      case WIFI_AUTH_WPA_WPA2_PSK:  authStr = "WPA/WPA2"; break;
      case WIFI_AUTH_WPA3_PSK:      authStr = "WPA3"; break;
      case WIFI_AUTH_WPA2_WPA3_PSK: authStr = "WPA2/WPA3"; break;
      default:                      authStr = "WPA2"; break;
    }
    json += "{\"ssid\":\"";
    json += jsonEscape(WiFi.SSID(i));
    json += "\",\"rssi\":";
    json += String(WiFi.RSSI(i));
    json += ",\"secured\":";
    json += (secured ? "true" : "false");
    json += ",\"auth\":\"";
    json += authStr;
    json += "\"}";
  }
  json += "]}";
  WiFi.scanDelete();
  // Kick off the next scan now so a poll a few seconds from now (silent
  // auto-scan or a manual refresh) finds fresh results already waiting
  // instead of blocking again.
  WiFi.scanNetworks(true);
  return json;
}

// HTML Web Interface for Octoglow
// The dashboard and the login shell now live in portal.html / auth.html next to
// this sketch and are compiled in gzipped, which is why they are not inline any
// more: as raw text they were 418 KB, about a quarter of the whole binary.
// After editing a web page, regenerate assets: python builders/!buildall.py
#include "web_assets.h"

// Both pages are stored gzipped, so they go out with Content-Encoding: gzip
// and the browser inflates them. Every browser sends Accept-Encoding: gzip,
// and nothing else on the device fetches these two pages - Octoglow Connect
// only talks to the JSON endpoints.
// A page body is sent by GzipPageTransfers in nonblocking slices while loop()
// keeps animating. ETags still avoid retransmitting an unchanged page.
static void sendGzipHtml(const uint8_t* body, uint32_t len, const char* etag) {
  uint32_t _t0 = micros();
  server.client().setNoDelay(true);
  String tag = String("\"") + etag + "-" + String(len) + "\"";

  if (server.hasHeader("If-None-Match") && server.header("If-None-Match") == tag) {
    hwPage304++;
    server.sendHeader("ETag", tag);
    server.sendHeader("Cache-Control", "no-cache");
    server.send(304, "text/html", "");
    { uint32_t d = micros() - _t0; if (d > hwPageUs) hwPageUs = d; }
    return;
  }

  if (!pageTransfers.available()) {
    server.sendHeader("Retry-After", "1");
    server.send(503, "text/plain", "Page transfer busy; retry shortly");
    return;
  }
  hwPageFull++;
  server.sendHeader("Content-Encoding", "gzip");
  server.sendHeader("ETag", tag);
  // no-cache means "revalidate every time", not "do not store" - the browser
  // keeps the body and we answer the revalidation with 304.
  server.sendHeader("Cache-Control", "no-cache");
  server.setContentLength(len);
  server.send(200, "text/html", "");
  // Retain only the client and flash pointer. WebServer can accept the next
  // command while loop()/mxCommit() send bounded, nonblocking page slices.
  pageTransfers.start(server.client(), body, len);
  { uint32_t d = micros() - _t0; if (d > hwPageUs) hwPageUs = d; }
}

// The fonts and the licence list come from flash like the pages, through the
// same transfer slots. The pages ask for a font as /fonts/<name>.woff2?v=<hash
// of the file> (build_web.py writes the hash in), so the browser may keep it
// for a year without asking again. With every slot taken the body goes out in
// one blocking send instead of a 503: a font that fails to load stays missing
// until the next page load.
static void sendFlashAsset(const uint8_t* body, uint32_t len, const char* etag, const char* type,
                           bool gzipped, const char* cacheControl) {
  server.client().setNoDelay(true);
  String tag = String("\"") + etag + "-" + String(len) + "\"";
  server.sendHeader("ETag", tag);
  server.sendHeader("Cache-Control", cacheControl);
  if (server.hasHeader("If-None-Match") && server.header("If-None-Match") == tag) {
    server.send(304, type, "");
    return;
  }
  if (gzipped) server.sendHeader("Content-Encoding", "gzip");
  if (!pageTransfers.available()) {
    server.send_P(200, type, (const char*)body, len);
    return;
  }
  server.setContentLength(len);
  server.send(200, type, "");
  pageTransfers.start(server.client(), body, len);
}

// Public: the sign-in page and the setup wizard use them before anyone is
// signed in.
static const char FONT_CACHE[] = "public, max-age=31536000, immutable";
void handleFontGoogleSans() {
  sendFlashAsset(GSANS_WOFF2, GSANS_WOFF2_LEN, GSANS_WOFF2_SRC_SHA, "font/woff2", false, FONT_CACHE);
}
void handleFontRoboto() {
  sendFlashAsset(ROBOTO_WOFF2, ROBOTO_WOFF2_LEN, ROBOTO_WOFF2_SRC_SHA, "font/woff2", false, FONT_CACHE);
}

// GET /licenses - About -> Licente: Octoglow's license and every component in
// the firmware, with the full license texts (built from licenses/).
void handleLicenses() {
  if (!checkAuth()) return;
  sendFlashAsset(LICENSES_JSON_GZ, LICENSES_JSON_GZ_LEN, LICENSES_JSON_GZ_SRC_SHA, "application/json", true, "no-cache");
}


// SERVER HANDLERS
// AUTH HANDLERS

static void sendSessionCookie(const char* user) {
  String newTok = makeSessionToken(user);
  server.sendHeader("Set-Cookie", String("sc_tok=") + newTok +
    "; Path=/; HttpOnly; Max-Age=" + String(SESSION_TTL_SECONDS) + "; SameSite=Lax");
}

// When the sign-in alert went up: the dashboard that sign-in opens a moment
// later does not announce itself over it.
static unsigned long gWebLoginAlertMs = 0;

// One of the tile's alerts, in the web interface's language. False when the
// tile is off or something more important has the panel.
static bool webAlertShow(const char* en, const char* ro, const uint8_t* icon, bool sound) {
  if (!webAccessEnabled || provisionMode || higherPriorityTileActive(PRIORITY_ID_WEB)) return false;
  strncpy(notifBuf, strcmp(webUiLang, "en") != 0 ? ro : en, sizeof(notifBuf) - 1);
  notifBuf[sizeof(notifBuf) - 1] = '\0';
  notifIconOverride  = icon;
  webAccessAlertActive = true;
  notifActive = true;
  requestDisplayOp(DISPLAY_OP_START_NOTIF);
  if (sound) nbPlayPreset(eventSoundWeb, BZ_CAT_NOTIF);
  return true;
}

static void sendLoginLocked() {
  server.send(429, "application/json",
    "{\"ok\":false,\"locked\":true,\"err\":\"Logarile sunt blocate pana la repornirea ceasului\"}");
}

// A wrong password. True once it is the one that closes the login - said on
// the panel too, once, so the owner knows someone tried.
static bool loginFailed() {
  if (gLoginLocked) return true;
  if (++gLoginFails < LOGIN_MAX_FAILS) return false;
  gLoginLocked = true;
  Serial.println("[auth] too many wrong passwords - login closed until restart");
  if (webAlertLocked) webAlertShow("Login blocked", "Logare blocata", blockedIcon, true);
  return true;
}

// Handler HTTP: curent (JSON) for Auth.
void handleAuthState() {
  // accent, colors, dark and lang: the theme and the language, so the sign-in
  // page wears them before anyone is signed in (a browser that has never opened
  // the dashboard has no copy).
  server.send(200, "application/json",
    String("{\"configured\":") + (authConfigured ? "true" : "false") +
    ",\"locked\":" + (gLoginLocked ? "true" : "false") +
    ",\"id\":\"" + gDeviceId + "\",\"host\":\"" + gHostName +
    "\",\"accent\":\"" + jsonEscape(String(accentColor)) +
    "\",\"colors\":\"" + jsonEscape(String(webUiColors)) +
    "\",\"dark\":" + (webUiDark ? "true" : "false") +
    ",\"lang\":\"" + webUiLang + "\"}");
}

// Handler HTTP route /authSetup on web.
void handleAuthSetup() {
  if (authConfigured) { server.send(403, "text/plain", "Cont deja existent"); return; }
  if (!server.hasArg("user") || !server.hasArg("pass")) {
    server.send(400, "text/plain", "Lipsesc campuri"); return;
  }
  String u = server.arg("user"); u.trim();
  String p = server.arg("pass");
  if (u.length() < 1 || u.length() > 32 || p.length() < 4) {
    server.send(400, "text/plain", "Date invalide"); return;
  }
  char hash[65];
  sha256hex(p.c_str(), hash);
  saveAuth(u.c_str(), hash);

  sendSessionCookie(u.c_str());
  server.send(200, "application/json", String("{\"ok\":true,\"user\":\"") + u + "\"}");
}


void handleLogin() {
  if (!authConfigured) { server.send(403, "text/plain", "Niciun cont configurat"); return; }
  // Closed: not even looked at, so a guess made now tells nothing.
  if (gLoginLocked) { sendLoginLocked(); return; }
  if (!server.hasArg("user") || !server.hasArg("pass")) {
    server.send(400, "text/plain", "Lipsesc campuri"); return;
  }
  String u = server.arg("user"); u.trim();
  String p = server.arg("pass");
  char hash[65];
  sha256hex(p.c_str(), hash);
  if (u != String(authUser) || strcmp(hash, authPassHash) != 0) {
    if (loginFailed()) { sendLoginLocked(); return; }
    server.send(401, "application/json", "{\"ok\":false,\"err\":\"Credentiale incorecte\"}");
    return;
  }
  gLoginFails = 0;
  // A browser's sign-in, not Octoglow Connect's: that one signs in by itself
  // every time it starts.
  if (webAlertLogin && server.header("User-Agent").indexOf("Mozilla") >= 0 &&
      webAlertShow("New login", "Logare noua", keyIcon, true))
    gWebLoginAlertMs = millis() | 1;

  sendSessionCookie(u.c_str());
  server.send(200, "application/json", String("{\"ok\":true,\"user\":\"") + u + "\"}");
}

void handleLogout() {
  // Token-ul e stateless (nu exista nicio lista de sesiuni pe ESP), deci
  // "logout" inseamna doar stergerea cookie-ului din browser-ul curent.
  server.sendHeader("Set-Cookie", "sc_tok=; Path=/; HttpOnly; Max-Age=0; SameSite=Lax");
  server.send(200, "text/plain", "ok");
}

// POST /hostname name=<name> - the clock's name on the network (see NAME ON THE
// NETWORK). Upper case is taken as lower case; anything else a name can't hold
// is refused rather than changed behind the user's back.
void handleHostName() {
  if (!checkAuth()) return;
  String n = server.arg("name");
  n.trim();
  n.toLowerCase();
  if (!hostNameValid(n.c_str())) {
    server.send(400, "application/json",
      "{\"ok\":false,\"err\":\"Numele poate avea litere mici, cifre si cratima (nu la capete), cel mult 32\"}");
    return;
  }
  strncpy(gHostName, n.c_str(), sizeof(gHostName) - 1);
  gHostName[sizeof(gHostName) - 1] = '\0';
  prefs.begin("wifi", false);
  bool saved = prefs.putString("host", gHostName) > 0;
  prefs.end();
  // The router hears the new name at the next lease; mDNS answers to it now.
  WiFi.setHostname(gHostName);
  if (!provisionMode && WiFi.status() == WL_CONNECTED) mdnsStart();
  if (!saved) {
    server.send(500, "application/json", "{\"ok\":false,\"err\":\"Numele nu a putut fi salvat\"}");
    return;
  }
  server.send(200, "application/json", String("{\"ok\":true,\"host\":\"") + gHostName + "\"}");
}

void handleUpdateAccount() {
  if (!provisionMode && getTokenUser().length() == 0) {
    server.send(401, "application/json", "{\"ok\":false,\"err\":\"Neautentificat\"}");
    return;
  }

  if (!provisionMode) {
    // Checking the current password is a guess too, so it counts the same.
    if (gLoginLocked) { sendLoginLocked(); return; }
    String curPass = server.hasArg("curpass") ? server.arg("curpass") : "";
    char curHash[65];
    sha256hex(curPass.c_str(), curHash);
    if (strcmp(curHash, authPassHash) != 0) {
      if (loginFailed()) { sendLoginLocked(); return; }
      server.send(401, "application/json", "{\"ok\":false,\"err\":\"Parola actuala incorecta\"}");
      return;
    }
    gLoginFails = 0;
  }
  String newUser = server.hasArg("user") ? server.arg("user") : String(authUser);
  newUser.trim();
  if (newUser.length() < 1 || newUser.length() > 32) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"Username invalid\"}");
    return;
  }
  String newPass = server.hasArg("newpass") ? server.arg("newpass") : "";
  char finalHash[65];
  if (newPass.length() > 0) {
    if (newPass.length() < 4) {
      server.send(400, "application/json", "{\"ok\":false,\"err\":\"Parola noua trebuie sa aiba cel putin 4 caractere\"}");
      return;
    }
    sha256hex(newPass.c_str(), finalHash);
  } else {
    strncpy(finalHash, authPassHash, 64); finalHash[64] = '\0';
  }
  saveAuth(newUser.c_str(), finalHash);
  server.send(200, "application/json", String("{\"ok\":true,\"user\":\"") + newUser + "\"}");
}

// SOFTWARE UPDATE (OTA)

bool swOtaAuthOk = false;
String swOtaErr = "";

String swReadUpdateError() {
  String out = "";
  StreamString ss;
  Update.printError(ss);
  out = ss.readString();
  out.trim();
  return out;
}

void handleSwUpdateUpload() {
  HTTPUpload& upload = server.upload();
  if (upload.status == UPLOAD_FILE_START) {
    // The default AP has no password during onboarding. Never accept an
    // executable firmware upload from an unauthenticated nearby client.
    swOtaAuthOk = !provisionMode && (getTokenUser().length() > 0);
    swOtaErr = "";
    if (swOtaAuthOk) {
      Serial.printf("Software Update: %s\n", upload.filename.c_str());

      if (Update.isRunning()) Update.abort();
      if (!Update.begin(UPDATE_SIZE_UNKNOWN)) {
        swOtaErr = swReadUpdateError();
        Serial.println("Update.begin a esuat: " + swOtaErr);
      }
    }
  } else if (upload.status == UPLOAD_FILE_WRITE) {
    if (swOtaAuthOk && swOtaErr.length() == 0) {
      if (Update.write(upload.buf, upload.currentSize) != upload.currentSize) {
        swOtaErr = swReadUpdateError();
        Serial.println("Update.write a esuat: " + swOtaErr);
      }
    }
  } else if (upload.status == UPLOAD_FILE_END) {
    if (swOtaAuthOk && swOtaErr.length() == 0) {
      if (Update.end(true)) {
        Serial.printf("Software Update Success: %u bytes\n", upload.totalSize);
      } else {
        swOtaErr = swReadUpdateError();
        Serial.println("Update.end a esuat: " + swOtaErr);
      }
    }
  } else if (upload.status == UPLOAD_FILE_ABORTED) {
    if (Update.isRunning()) Update.abort();
    swOtaErr = "Upload intrerupt";
  }
}

void handleSwUpdateUploadDone() {
  server.sendHeader("Connection", "close");
  if (!swOtaAuthOk) {
    server.send(401, "application/json", "{\"ok\":false,\"err\":\"Neautentificat\"}");
    return;
  }
  if (swOtaErr.length() > 0 || Update.hasError()) {
    String err = swOtaErr.length() > 0 ? swOtaErr : swReadUpdateError();
    if (err.length() == 0) err = "Eroare necunoscuta";
    String body = "{\"ok\":false,\"err\":\"" + err + "\"}";
    server.send(200, "application/json", body);
  } else {
    server.send(200, "application/json", "{\"ok\":true}");
    saveSettingsFlush();
    delay(500);
    ESP.restart();
  }
}

void handleWhoami() {
  if (provisionMode) {
    String u = authConfigured ? String(authUser) : String("Admin");
    server.send(200, "application/json", String("{\"user\":\"") + u + "\"}");
    return;
  }
  String u = getTokenUser();
  if (u.length() == 0) { server.send(401, "text/plain", "Neautentificat"); return; }
  // Near its end: a fresh cookie, so a session in use never runs out.
  time_t now = time(nullptr);
  if (TIME_LOOKS_VALID(now) &&
      (unsigned long)now + SESSION_REFRESH_SECONDS > sessionTokenExpiry(extractCookieToken()))
    sendSessionCookie(u.c_str());
  server.send(200, "application/json", String("{\"user\":\"") + u + "\"}");
}

void handleDashboard() {
  if (provisionMode && !oobeComplete) {
    sendGzipHtml(ONBOARDING_HTML_GZ, ONBOARDING_HTML_GZ_LEN, ONBOARDING_HTML_GZ_SRC_SHA);
    return;
  }
  if (!provisionMode && getTokenUser().length() == 0) {
    server.send(401, "text/plain", "Neautentificat");
    return;
  }

  // Announced on the matrix every time a browser opens the dashboard - one
  // request per page load, so a phone connecting shows it again. Deliberately
  // not gated on notifEnabled: that switch is labelled "PC Notifications" and
  // covers what Octoglow Connect sends, which this is not. Skipped while
  // something more important is on screen.
  bool justSignedIn = gWebLoginAlertMs != 0 && millis() - gWebLoginAlertMs < 8000UL;
  if (webAlertAccess && !justSignedIn)
    webAlertShow("Web interface accessed", "Interfata web accesata", keyIcon, true);
  sendGzipHtml(PORTAL_HTML_GZ, PORTAL_HTML_GZ_LEN, PORTAL_HTML_GZ_SRC_SHA);
}



void handleRoot() {
  if (provisionMode) {
    if (oobeComplete) sendGzipHtml(PORTAL_HTML_GZ, PORTAL_HTML_GZ_LEN, PORTAL_HTML_GZ_SRC_SHA);
    else sendGzipHtml(ONBOARDING_HTML_GZ, ONBOARDING_HTML_GZ_LEN, ONBOARDING_HTML_GZ_SRC_SHA);
    return;
  }
  sendGzipHtml(AUTH_SHELL_GZ, AUTH_SHELL_GZ_LEN, AUTH_SHELL_GZ_SRC_SHA);
}
void handleScan() {
  if (!checkAuth()) return;
  server.send(200, "application/json", scanNetworks());
}
// Switching the radio from inside an HTTP handler tears the interface down
// while handleClient() is still on the stack holding a socket on it. Flag the
// switch and let loop() perform it once the response has gone out.
// 0 = nothing pending, 1 = go to AP, 2 = go back to Wi-Fi.
static volatile uint8_t pendingModeSwitch = 0;

// Credential checks run with AP and STA enabled together. The access point
// remains available if association fails, so a mistyped password can be
// corrected without power-cycling or losing the setup page.
enum WifiJoinState : uint8_t { WIFI_JOIN_IDLE, WIFI_JOIN_START, WIFI_JOIN_WAIT, WIFI_JOIN_RESTORE, WIFI_JOIN_OK, WIFI_JOIN_FAILED };
static WifiJoinState wifiJoinState = WIFI_JOIN_IDLE;
static bool wifiJoinOobe = false;
static bool wifiJoinFromStation = false;
static String wifiJoinSsid;
static String wifiJoinPass;
static String wifiJoinPreviousSsid;
static String wifiJoinPreviousPass;
static const char* wifiJoinError = "";
static unsigned long wifiJoinStartedMs = 0;
static unsigned long wifiJoinFinishedMs = 0;

static bool wifiJoinBusy() {
  return wifiJoinState == WIFI_JOIN_START || wifiJoinState == WIFI_JOIN_WAIT || wifiJoinState == WIFI_JOIN_RESTORE;
}

static void wifiJoinFail(const char* reason) {
  WiFi.setAutoReconnect(false);
  WiFi.disconnect(false, true);
  wifiJoinError = reason;
  wifiJoinPass = "";
  if (wifiJoinFromStation && wifiJoinPreviousSsid.length()) {
    // A failed change from an existing Wi-Fi network returns to that network,
    // without running the clock's startup sequence or saving the bad password.
    WiFi.mode(WIFI_STA);
    WiFi.begin(wifiJoinPreviousSsid.c_str(), wifiJoinPreviousPass.c_str());
    wifiJoinStartedMs = millis();
    wifiJoinState = WIFI_JOIN_RESTORE;
    return;
  }
  if (wifiJoinFromStation) startProvisionMode();
  else WiFi.mode(WIFI_AP);
  wifiJoinState = WIFI_JOIN_FAILED;
  wifiJoinFinishedMs = millis();
}

static void wifiJoinTick() {
  // A portal client may lose the AP briefly while the STA changes channels.
  // Complete the switch even if that client never receives the final poll.
  if (wifiJoinState == WIFI_JOIN_OK && !wifiJoinOobe && !wifiJoinFromStation &&
      millis() - wifiJoinFinishedMs >= 6000UL && !pendingModeSwitch) {
    pendingModeSwitch = 2;
  }
  if (wifiJoinState == WIFI_JOIN_START) {
    esp_wifi_scan_stop();
    WiFi.scanDelete();
    WiFi.setAutoReconnect(false);
    WiFi.mode(wifiJoinFromStation ? WIFI_STA : WIFI_AP_STA);
    WiFi.disconnect(false, true);
    WiFi.begin(wifiJoinSsid.c_str(), wifiJoinPass.c_str());
    wifiJoinStartedMs = millis();
    wifiJoinState = WIFI_JOIN_WAIT;
  }
  if (wifiJoinState == WIFI_JOIN_RESTORE) {
    if (WiFi.status() == WL_CONNECTED && WiFi.SSID() == wifiJoinPreviousSsid) {
      server.stop();
      registerRoutes();
      server.begin();
      mdnsStart();
      wifiJoinPreviousPass = "";
      wifiJoinState = WIFI_JOIN_FAILED;
      wifiJoinFinishedMs = millis();
    } else if (millis() - wifiJoinStartedMs >= 15000UL) {
      wifiJoinPreviousPass = "";
      startProvisionMode();
      wifiJoinState = WIFI_JOIN_FAILED;
      wifiJoinFinishedMs = millis();
    }
    return;
  }
  if (wifiJoinState != WIFI_JOIN_WAIT) return;
  if (millis() - wifiJoinStartedMs >= 300UL &&
      WiFi.status() == WL_CONNECTED && WiFi.SSID() == wifiJoinSsid) {
    prefs.begin("wifi", false);
    size_t ssidLen = prefs.putString("ssid", wifiJoinSsid);
    size_t passLen = prefs.putString("pass", wifiJoinPass);
    bool saved = ssidLen == wifiJoinSsid.length() && passLen == wifiJoinPass.length();
    if (saved) saved = prefs.putBool("verified", true) > 0;
    prefs.end();
    if (!saved) { wifiJoinFail("save_failed"); return; }
    gWifiSsidCached = wifiJoinSsid;
    wifiJoinPass = "";
    wifiJoinPreviousPass = "";
    if (wifiJoinFromStation) {
      server.stop();
      registerRoutes();
      server.begin();
      mdnsStart();
    }
    wifiJoinState = WIFI_JOIN_OK;
    wifiJoinFinishedMs = millis();
    return;
  }
  if (millis() - wifiJoinStartedMs >= 15000UL) wifiJoinFail("connection_failed");
}

static void sendWifiJoinStatus(bool oobe) {
  const bool sameRequest = wifiJoinOobe == oobe;
  const char* state = !sameRequest || wifiJoinState == WIFI_JOIN_IDLE ? "idle" :
    wifiJoinBusy() ? "checking" : wifiJoinState == WIFI_JOIN_OK ? "ok" : "failed";
  String json = String("{\"state\":\"") + state + "\",\"ssid\":\"" +
    jsonEscape(sameRequest ? wifiJoinSsid : String("")) + "\",\"err\":\"" +
    (sameRequest ? wifiJoinError : "") + "\"}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
  // The browser has received proof of a real connection. Only now may the
  // normal portal leave AP mode; OOBE intentionally waits for its final step.
  if (!oobe && sameRequest && wifiJoinState == WIFI_JOIN_OK && !wifiJoinFromStation) pendingModeSwitch = 2;
}

void handleWifiJoinStatus() {
  if (!checkAuth()) return;
  sendWifiJoinStatus(false);
}

void handleOobeWifiStatus() {
  if (!provisionMode || oobeComplete) { server.send(404, "text/plain", "Not found"); return; }
  sendWifiJoinStatus(true);
}

void handleConnect() {
  if (!checkAuth()) return;
  if (!server.hasArg("ssid")) { server.send(400, "text/plain", "Lipseste SSID"); return; }
  String newSSID = server.arg("ssid");
  newSSID.trim();
  String newPass = server.hasArg("pass") ? server.arg("pass") : "";
  if (!newSSID.length() || newSSID.length() > 32 || newPass.length() > 64) {
    server.send(400, "text/plain", "Date de rețea invalide"); return;
  }
  if (wifiJoinBusy()) { server.send(409, "text/plain", "Verificarea rețelei este în curs"); return; }
  if (provisionMode) {
    wifiJoinSsid = newSSID;
    wifiJoinPass = newPass;
    wifiJoinOobe = false;
    wifiJoinFromStation = false;
    wifiJoinError = "";
    wifiJoinState = WIFI_JOIN_START;
    server.send(202, "application/json", "{\"checking\":true}");
    return;
  }
  // Verify the new credentials in the background. Only a successful join is
  // saved; a failed join restores the previous station connection.
  wifiJoinPreviousSsid = WiFi.SSID();
  prefs.begin("wifi", true);
  wifiJoinPreviousPass = prefs.getString("pass", "");
  prefs.end();
  wifiJoinSsid = newSSID;
  wifiJoinPass = newPass;
  wifiJoinOobe = false;
  wifiJoinFromStation = true;
  wifiJoinError = "";
  wifiJoinState = WIFI_JOIN_START;
  server.send(202, "application/json", "{\"checking\":true}");
}

// The onboarding wizard must remember the network without dropping the AP
// connection mid-flow. The normal /connect endpoint intentionally switches now;
// this one waits for /oobefinish and its restart.
void handleOobeState() {
  if (!provisionMode || oobeComplete) { server.send(404, "text/plain", "Not found"); return; }
  prefs.begin("wifi", true);
  String savedSsid = prefs.getString("ssid", "");
  bool hasWifi = savedSsid.length() > 0 && prefs.getBool("verified", false);
  prefs.end();
  String json = String("{\"account\":") + (authConfigured ? "true" : "false") +
    ",\"wifi\":" + (hasWifi ? "true" : "false") +
    ",\"ssid\":\"" + jsonEscape(hasWifi ? savedSsid : String("")) + "\"" +
    ",\"host\":\"" + gHostName + "\",\"accent\":\"" + accentColor +
    "\",\"dark\":" + (webUiDark ? "true" : "false") +
    ",\"shape\":" + String(webUiShape) + ",\"lang\":\"" + webUiLang + "\"}";
  server.send(200, "application/json", json);
}

void handleOobeLang() {
  if (!provisionMode || oobeComplete) { server.send(404, "text/plain", "Not found"); return; }
  String language = server.arg("lang");
  if (language != "en" && language != "ro") { server.send(400, "text/plain", "Invalid language"); return; }
  strcpy(webUiLang, language.c_str());
  saveSettingsDeferred();
  saveSettingsFlush();
  server.send(200, "application/json", "{\"ok\":true}");
}

void handleOobeWifi() {
  if (!provisionMode || oobeComplete) { server.send(404, "text/plain", "Not found"); return; }
  if (!authConfigured) { server.send(409, "text/plain", "Create an account first"); return; }
  String ssid = server.arg("ssid");
  String pass = server.arg("pass");
  if (ssid.length() == 0 || ssid.length() > 32 || pass.length() > 64) {
    server.send(400, "text/plain", "Invalid network credentials"); return;
  }
  if (wifiJoinBusy()) { server.send(409, "text/plain", "Network check in progress"); return; }
  wifiJoinSsid = ssid;
  wifiJoinPass = pass;
  wifiJoinOobe = true;
  wifiJoinFromStation = false;
  wifiJoinError = "";
  wifiJoinState = WIFI_JOIN_START;
  server.send(202, "application/json", "{\"checking\":true}");
}

void handleOobeFinish() {
  if (!provisionMode || oobeComplete) { server.send(404, "text/plain", "Not found"); return; }
  prefs.begin("wifi", true);
  bool hasWifi = prefs.getString("ssid", "").length() > 0 && prefs.getBool("verified", false);
  prefs.end();
  if (wifiJoinBusy()) { server.send(409, "text/plain", "Network check in progress"); return; }
  if (server.arg("ap") != "1" && !hasWifi) {
    server.send(409, "text/plain", "Network is not connected"); return;
  }
  if (!authConfigured) {
    server.send(409, "text/plain", "Account is required"); return;
  }
  // Wi-Fi is optional during first-run setup. Keep the clock in AP mode when
  // the user skips this step, even if an earlier attempt saved credentials.
  uint8_t chosenStartMode = (!hasWifi || server.arg("ap") == "1") ? START_MODE_AP : START_MODE_WIFI;
  if (!setOobeComplete(true)) {
    server.send(500, "text/plain", "Could not complete setup"); return;
  }
  defaultStartMode = chosenStartMode;
  saveSettingsDeferred();
  saveSettingsFlush();
  server.send(200, "application/json", "{\"ok\":true}");
  delay(500);
  ESP.restart();
}

void handleSettings() {
  if (!checkAuth()) return;
  if (server.hasArg("buzzer")) buzzerOn = (server.arg("buzzer") == "1");
  bool hideIconTouched = false;
  if (server.hasArg("hideIcons")) {
    bool newVal = (server.arg("hideIcons") == "1");

    if (newVal != hideTileIcons) {
      hideIconTouched = true;
      hideTileIcons = newVal;

      hideIconDate       = hideTileIcons;
      hideIconTemp       = hideTileIcons;
      hideIconReminder   = hideTileIcons;
      hideIconWeather    = hideTileIcons;
      hideIconNotif      = hideTileIcons;
      hideIconNowPlaying = hideTileIcons;
      hideIconPressure   = hideTileIcons;
      hideIconCurrency   = hideTileIcons;
      hideIconYoutube    = hideTileIcons;
      hideIconWebAccess  = hideTileIcons;
      hideIconIp         = hideTileIcons;
      hideIconVoicemeeter= hideTileIcons;
      hideIconBrowserDownloads = hideTileIcons;
      hideIconQbit       = hideTileIcons;
      hideIconBambu      = hideTileIcons;
      hideIconObs        = hideTileIcons;
      hideIconAfterburner = hideTileIcons;
      hideIconCountdown = hideTileIcons;
      hideIconGameFps    = hideTileIcons;
      hideIconVolume    = hideTileIcons;
    }
  }

  if (server.hasArg("hideIconDate"))       { bool v = (server.arg("hideIconDate")       == "1"); if (v != hideIconDate)       hideIconTouched = true; hideIconDate       = v; }
  if (server.hasArg("hideIconTemp"))       { bool v = (server.arg("hideIconTemp")       == "1"); if (v != hideIconTemp)       hideIconTouched = true; hideIconTemp       = v; }
  if (server.hasArg("hideIconReminder"))   { bool v = (server.arg("hideIconReminder")   == "1"); if (v != hideIconReminder)   hideIconTouched = true; hideIconReminder   = v; }
  if (server.hasArg("hideIconWeather"))    { bool v = (server.arg("hideIconWeather")    == "1"); if (v != hideIconWeather)    hideIconTouched = true; hideIconWeather    = v; }
  if (server.hasArg("hideIconNotif"))      { bool v = (server.arg("hideIconNotif")      == "1"); if (v != hideIconNotif)      hideIconTouched = true; hideIconNotif      = v; }
  if (server.hasArg("hideIconNowPlaying")) { bool v = (server.arg("hideIconNowPlaying") == "1"); if (v != hideIconNowPlaying) hideIconTouched = true; hideIconNowPlaying = v; }
  if (server.hasArg("hideIconPressure"))   { bool v = (server.arg("hideIconPressure")   == "1"); if (v != hideIconPressure)   hideIconTouched = true; hideIconPressure   = v; }
  if (server.hasArg("hideIconCurrency"))   { bool v = (server.arg("hideIconCurrency")   == "1"); if (v != hideIconCurrency)   hideIconTouched = true; hideIconCurrency   = v; }
  if (server.hasArg("hideIconYoutube"))    { bool v = (server.arg("hideIconYoutube")    == "1"); if (v != hideIconYoutube)    hideIconTouched = true; hideIconYoutube    = v; }
  if (server.hasArg("hideIconWebAccess"))  { bool v = (server.arg("hideIconWebAccess")  == "1"); if (v != hideIconWebAccess)  hideIconTouched = true; hideIconWebAccess  = v; }
  if (server.hasArg("hideIconIp"))         { bool v = (server.arg("hideIconIp")         == "1"); if (v != hideIconIp)         hideIconTouched = true; hideIconIp         = v; }
  if (server.hasArg("hideIconVoicemeeter")){ bool v = (server.arg("hideIconVoicemeeter")== "1"); if (v != hideIconVoicemeeter)hideIconTouched = true; hideIconVoicemeeter= v; }
  if (server.hasArg("hideIconBrowserDownloads")){ bool v = (server.arg("hideIconBrowserDownloads") == "1"); if (v != hideIconBrowserDownloads) hideIconTouched = true; hideIconBrowserDownloads = v; }
  if (server.hasArg("hideIconObs"))        { bool v = (server.arg("hideIconObs")        == "1"); if (v != hideIconObs)        hideIconTouched = true; hideIconObs        = v; }
  if (server.hasArg("hideIconQbit"))       { bool v = (server.arg("hideIconQbit")       == "1"); if (v != hideIconQbit)       hideIconTouched = true; hideIconQbit       = v; }
  if (server.hasArg("hideIconBambu"))      { bool v = (server.arg("hideIconBambu")      == "1"); if (v != hideIconBambu)      hideIconTouched = true; hideIconBambu      = v; }
  if (server.hasArg("hideIconAfterburner")) { bool v = (server.arg("hideIconAfterburner") == "1"); if (v != hideIconAfterburner) hideIconTouched = true; hideIconAfterburner = v; }
  if (server.hasArg("hideIconCountdown")) { bool v = (server.arg("hideIconCountdown") == "1"); if (v != hideIconCountdown) hideIconTouched = true; hideIconCountdown = v; }
  if (server.hasArg("hideIconGameFps"))    { bool v = (server.arg("hideIconGameFps")    == "1"); if (v != hideIconGameFps)    hideIconTouched = true; hideIconGameFps    = v; }
  if (server.hasArg("hideIconVolume"))    { bool v = (server.arg("hideIconVolume")    == "1"); if (v != hideIconVolume)    hideIconTouched = true; hideIconVolume    = v; }
  if (server.hasArg("npAdaptiveIcon"))     { npAdaptiveIcon = (server.arg("npAdaptiveIcon") == "1"); }
  bool iconSelTouched = false;
  if (server.hasArg("volumeAdaptiveIcon")) { volumeAdaptiveIcon = server.arg("volumeAdaptiveIcon") == "1"; iconSelTouched = true; }

  if (server.hasArg("iconSelYt"))    { int v = server.arg("iconSelYt").toInt();    if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelYoutube)   iconSelTouched = true; iconSelYoutube   = (uint8_t)v; } }
  if (server.hasArg("iconSelDate"))  { int v = server.arg("iconSelDate").toInt();  if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelDate)       iconSelTouched = true; iconSelDate       = (uint8_t)v; } }
  if (server.hasArg("iconSelTemp"))  { int v = server.arg("iconSelTemp").toInt();  if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelTemp)       iconSelTouched = true; iconSelTemp       = (uint8_t)v; } }
  if (server.hasArg("iconSelRem"))   { int v = server.arg("iconSelRem").toInt();   if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelReminder)   iconSelTouched = true; iconSelReminder   = (uint8_t)v; } }
  if (server.hasArg("iconSelNotif")) { int v = server.arg("iconSelNotif").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelNotif)      iconSelTouched = true; iconSelNotif      = (uint8_t)v; } }
  if (server.hasArg("iconSelNpMusic")) { int v = server.arg("iconSelNpMusic").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelNpMusic) iconSelTouched = true; iconSelNpMusic = (uint8_t)v; } }
  if (server.hasArg("iconSelNpVideo")) { int v = server.arg("iconSelNpVideo").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelNpVideo) iconSelTouched = true; iconSelNpVideo = (uint8_t)v; } }
  if (server.hasArg("iconSelNpPaused")) { int v = server.arg("iconSelNpPaused").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelNpPaused) iconSelTouched = true; iconSelNpPaused = (uint8_t)v; } }
  if (server.hasArg("iconSelPress")) { int v = server.arg("iconSelPress").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelPressure)   iconSelTouched = true; iconSelPressure   = (uint8_t)v; } }
  if (server.hasArg("iconSelCurr"))  { int v = server.arg("iconSelCurr").toInt();  if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelCurrency)   iconSelTouched = true; iconSelCurrency   = (uint8_t)v; } }
  if (server.hasArg("iconSelIp"))    { int v = server.arg("iconSelIp").toInt();    if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelIp)         iconSelTouched = true; iconSelIp         = (uint8_t)v; } }
  if (server.hasArg("iconSelVoicemeeter")) { int v = server.arg("iconSelVoicemeeter").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelVoicemeeter) iconSelTouched = true; iconSelVoicemeeter = (uint8_t)v; } }
  if (server.hasArg("iconSelBrowserDownloads")) { int v = server.arg("iconSelBrowserDownloads").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelBrowserDownloads) iconSelTouched = true; iconSelBrowserDownloads = (uint8_t)v; } }
  if (server.hasArg("iconSelObs"))   { int v = server.arg("iconSelObs").toInt();   if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelObs)        iconSelTouched = true; iconSelObs        = (uint8_t)v; } }
  if (server.hasArg("iconSelQbit"))  { int v = server.arg("iconSelQbit").toInt();  if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelQbit)       iconSelTouched = true; iconSelQbit       = (uint8_t)v; } }
  if (server.hasArg("iconSelBambu")) { int v = server.arg("iconSelBambu").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelBambu)      iconSelTouched = true; iconSelBambu      = (uint8_t)v; } }
  if (server.hasArg("iconSelBattery")) { int v = server.arg("iconSelBattery").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelBattery) iconSelTouched = true; iconSelBattery = (uint8_t)v; } }
  if (server.hasArg("iconSelLowBattery")) { int v = server.arg("iconSelLowBattery").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelLowBattery) iconSelTouched = true; iconSelLowBattery = (uint8_t)v; } }
  if (server.hasArg("iconSelAfterburner")) { int v = server.arg("iconSelAfterburner").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelAfterburner) iconSelTouched = true; iconSelAfterburner = (uint8_t)v; } }
  if (server.hasArg("iconSelCountdown")) { int v = server.arg("iconSelCountdown").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelCountdown) iconSelTouched = true; iconSelCountdown = (uint8_t)v; } }
  if (server.hasArg("iconSelGameFps")) { int v = server.arg("iconSelGameFps").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelGameFps) iconSelTouched = true; iconSelGameFps = (uint8_t)v; } }
  if (server.hasArg("iconSelVolume")) { int v = server.arg("iconSelVolume").toInt(); if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconSelVolume) iconSelTouched = true; iconSelVolume = (uint8_t)v; } }
  if (server.hasArg("iconWxSunny"))  { int v = server.arg("iconWxSunny").toInt();  if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconWxSunny)       iconSelTouched = true; iconWxSunny       = (uint8_t)v; } }
  if (server.hasArg("iconWxCloud"))  { int v = server.arg("iconWxCloud").toInt();  if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconWxCloud)       iconSelTouched = true; iconWxCloud       = (uint8_t)v; } }
  if (server.hasArg("iconWxRain"))   { int v = server.arg("iconWxRain").toInt();   if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconWxRain)        iconSelTouched = true; iconWxRain        = (uint8_t)v; } }
  if (server.hasArg("iconWxStorm"))  { int v = server.arg("iconWxStorm").toInt();  if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconWxStorm)       iconSelTouched = true; iconWxStorm       = (uint8_t)v; } }
  if (server.hasArg("iconWxSnow"))   { int v = server.arg("iconWxSnow").toInt();   if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconWxSnow)        iconSelTouched = true; iconWxSnow        = (uint8_t)v; } }
  if (server.hasArg("iconWxWind"))   { int v = server.arg("iconWxWind").toInt();   if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconWxWind)        iconSelTouched = true; iconWxWind        = (uint8_t)v; } }
  if (server.hasArg("iconWxNight"))  { int v = server.arg("iconWxNight").toInt();  if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) { if ((uint8_t)v != iconWxNight)       iconSelTouched = true; iconWxNight       = (uint8_t)v; } }
  bool scrollTypeTouched = hideIconTouched || iconSelTouched;
  if (server.hasArg("scrollType")) {
    int st = server.arg("scrollType").toInt();

    if (st >= 0 && st <= SCROLL_TYPE_MAX && (uint8_t)st != scrollType) {
      scrollTypeTouched = true;
      scrollType = (uint8_t)st;

      scrollTypeDate       = (uint8_t)st;
      scrollTypeTemp       = (uint8_t)st;
      scrollTypeReminder   = (uint8_t)st;
      scrollTypeWeather    = (uint8_t)st;
      scrollTypeNotif      = (uint8_t)st;
      scrollTypeNowPlaying = (uint8_t)st;
      scrollTypePressure   = (uint8_t)st;
      scrollTypeStopwatch  = (uint8_t)st;
      scrollTypeCurrency   = (uint8_t)st;
      scrollTypeYoutube    = (uint8_t)st;
      scrollTypeWebAccess  = (uint8_t)st;
      scrollTypeObs        = (uint8_t)st;
      scrollTypeAfterburner = (uint8_t)st;
      scrollTypeCountdown = (uint8_t)st;
      scrollTypeGameFps    = (uint8_t)st;
      scrollTypeVolume    = (uint8_t)st;
    }
  }

  if (server.hasArg("scrollTypeDate")) {
    int st = server.arg("scrollTypeDate").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeDate) scrollTypeTouched = true; scrollTypeDate = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeTemp")) {
    int st = server.arg("scrollTypeTemp").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeTemp) scrollTypeTouched = true; scrollTypeTemp = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeReminder")) {
    int st = server.arg("scrollTypeReminder").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeReminder) scrollTypeTouched = true; scrollTypeReminder = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeWeather")) {
    int st = server.arg("scrollTypeWeather").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeWeather) scrollTypeTouched = true; scrollTypeWeather = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeNotif")) {
    int st = server.arg("scrollTypeNotif").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeNotif) scrollTypeTouched = true; scrollTypeNotif = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeNowPlaying")) {
    int st = server.arg("scrollTypeNowPlaying").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeNowPlaying) scrollTypeTouched = true; scrollTypeNowPlaying = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypePressure")) {
    int st = server.arg("scrollTypePressure").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypePressure) scrollTypeTouched = true; scrollTypePressure = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeStopwatch")) {
    int st = server.arg("scrollTypeStopwatch").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeStopwatch) scrollTypeTouched = true; scrollTypeStopwatch = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeCurrency")) {
    int st = server.arg("scrollTypeCurrency").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeCurrency) scrollTypeTouched = true; scrollTypeCurrency = (uint8_t)st; }
  }
  if (server.hasArg("showgrayed")) showGrayedContent = (server.arg("showgrayed") == "1");
  if (server.hasArg("autosleep")) {
    autoSleepOn = (server.arg("autosleep") == "1");
    // Switching the timer on restarts the countdown rather than measuring from
    // whenever the clock was last touched, which could be hours ago.
    lastActivityMs = millis();
  }
  if (server.hasArg("autosleepsec")) {
    long v = server.arg("autosleepsec").toInt();
    // One minute to twenty-four hours. Anything shorter would put the panel out
    // while you were still looking at it.
    if (v >= 60 && v <= 86400) { autoSleepSec = (uint32_t)v; lastActivityMs = millis(); }
  }
  if (server.hasArg("livehl"))     liveTileHighlight = (server.arg("livehl") == "1");
  if (server.hasArg("uidark"))     webUiDark         = (server.arg("uidark") == "1");
  if (server.hasArg("uilang")) {
    String l = server.arg("uilang");
    // Only the two the interface actually ships; anything else is ignored
    // rather than stored and handed back as a language nobody can read.
    if (l == "en" || l == "ro") strcpy(webUiLang, l.c_str());
  }
  if (server.hasArg("startmode")) {
    int m = server.arg("startmode").toInt();
    // Takes effect at the next power-up; nothing switches under the user here.
    if (m >= START_MODE_WIFI && m <= START_MODE_AP) defaultStartMode = (uint8_t)m;
  }
  // Moving the global slider re-applies it to every tile, the same way the
  // global scroll type does.
  if (server.hasArg("scrollSpeed")) {
    int v = server.arg("scrollSpeed").toInt();
    if (v >= SCROLL_SPEED_MIN && v <= SCROLL_SPEED_MAX) {
      scrollSpeed = (uint8_t)v;
      for (int i = 0; i < SCROLL_SPEED_COUNT; i++) scrollSpeedTile[i] = (uint8_t)v;
    }
  }
  for (int i = 0; i < SCROLL_SPEED_COUNT; i++) {
    String a = String("scrollSpeed") + SPD_KEYS[i];
    if (server.hasArg(a)) {
      int v = server.arg(a).toInt();
      if (v >= SCROLL_SPEED_MIN && v <= SCROLL_SPEED_MAX) scrollSpeedTile[i] = (uint8_t)v;
    }
  }
  if (server.hasArg("scrollTypeYoutube")) {
    int st = server.arg("scrollTypeYoutube").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeYoutube) scrollTypeTouched = true; scrollTypeYoutube = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeWebAccess")) {
    int st = server.arg("scrollTypeWebAccess").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeWebAccess) scrollTypeTouched = true; scrollTypeWebAccess = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeIp")) {
    int st = server.arg("scrollTypeIp").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeIp) scrollTypeTouched = true; scrollTypeIp = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeObs")) {
    int st = server.arg("scrollTypeObs").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeObs) scrollTypeTouched = true; scrollTypeObs = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeAfterburner")) {
    int st = server.arg("scrollTypeAfterburner").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeAfterburner) scrollTypeTouched = true; scrollTypeAfterburner = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeCountdown")) {
    int st = server.arg("scrollTypeCountdown").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeCountdown) scrollTypeTouched = true; scrollTypeCountdown = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeGameFps")) {
    int st = server.arg("scrollTypeGameFps").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeGameFps) scrollTypeTouched = true; scrollTypeGameFps = (uint8_t)st; }
  }
  if (server.hasArg("scrollTypeVolume")) {
    int st = server.arg("scrollTypeVolume").toInt();
    if (st >= 0 && st <= SCROLL_TYPE_MAX) { if ((uint8_t)st != scrollTypeVolume) scrollTypeTouched = true; scrollTypeVolume = (uint8_t)st; }
  }

  bool fontTypeTouched = false;
  if (server.hasArg("fontType")) {
    int ft = server.arg("fontType").toInt();
    if (fontModeOk(ft) && (uint8_t)ft != fontType) {
      fontTypeTouched = true;
      fontType = (uint8_t)ft;

      fontTypeDate       = (uint8_t)ft;
      fontTypeTemp       = (uint8_t)ft;
      fontTypeReminder   = (uint8_t)ft;
      fontTypeWeather    = (uint8_t)ft;
      fontTypeNotif      = (uint8_t)ft;
      fontTypeNowPlaying = (uint8_t)ft;
      fontTypePressure   = (uint8_t)ft;
      fontTypeStopwatch  = (uint8_t)ft;
      fontTypeCurrency   = (uint8_t)ft;
      fontTypeYoutube    = (uint8_t)ft;
      fontTypeWebAccess  = (uint8_t)ft;
      fontTypeTimer      = (uint8_t)ft;
      fontTypeIp         = (uint8_t)ft;
      fontTypeObs        = (uint8_t)ft;
      fontTypeEts2       = (uint8_t)ft;
      fontTypeBeamng     = (uint8_t)ft;
      fontTypeAssetto    = (uint8_t)ft;
      fontTypeNfsu2      = (uint8_t)ft;
      fontTypeNfsmw      = (uint8_t)ft;
      fontTypeNfsc       = (uint8_t)ft;
      fontTypeAlarm      = (uint8_t)ft;
      fontTypeBattery    = (uint8_t)ft;
      fontTypeLowBattery = (uint8_t)ft;
      fontTypeAfterburner = (uint8_t)ft;
      fontTypeCountdown = (uint8_t)ft;
      fontTypeGameFps    = (uint8_t)ft;
      fontTypeVolume    = (uint8_t)ft;
    }
  }
  if (server.hasArg("fontTypeDate")) {
    int ft = server.arg("fontTypeDate").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeDate) fontTypeTouched = true; fontTypeDate = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeTemp")) {
    int ft = server.arg("fontTypeTemp").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeTemp) fontTypeTouched = true; fontTypeTemp = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeReminder")) {
    int ft = server.arg("fontTypeReminder").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeReminder) fontTypeTouched = true; fontTypeReminder = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeWeather")) {
    int ft = server.arg("fontTypeWeather").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeWeather) fontTypeTouched = true; fontTypeWeather = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeNotif")) {
    int ft = server.arg("fontTypeNotif").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeNotif) fontTypeTouched = true; fontTypeNotif = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeNowPlaying")) {
    int ft = server.arg("fontTypeNowPlaying").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeNowPlaying) fontTypeTouched = true; fontTypeNowPlaying = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypePressure")) {
    int ft = server.arg("fontTypePressure").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypePressure) fontTypeTouched = true; fontTypePressure = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeStopwatch")) {
    int ft = server.arg("fontTypeStopwatch").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeStopwatch) fontTypeTouched = true; fontTypeStopwatch = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeCurrency")) {
    int ft = server.arg("fontTypeCurrency").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeCurrency) fontTypeTouched = true; fontTypeCurrency = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeYoutube")) {
    int ft = server.arg("fontTypeYoutube").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeYoutube) fontTypeTouched = true; fontTypeYoutube = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeWebAccess")) {
    int ft = server.arg("fontTypeWebAccess").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeWebAccess) fontTypeTouched = true; fontTypeWebAccess = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeTimer")) {
    int ft = server.arg("fontTypeTimer").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeTimer) fontTypeTouched = true; fontTypeTimer = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeIp")) {
    int ft = server.arg("fontTypeIp").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeIp) fontTypeTouched = true; fontTypeIp = (uint8_t)ft; }
  }
  if (server.hasArg("fontTypeObs")) {
    int ft = server.arg("fontTypeObs").toInt();
    if (fontModeOk(ft)) { if ((uint8_t)ft != fontTypeObs) fontTypeTouched = true; fontTypeObs = (uint8_t)ft; }
  }
  {
    struct { const char* arg; uint8_t* font; } more[] = {
      { "fontTypeEts2", &fontTypeEts2 }, { "fontTypeBeamng", &fontTypeBeamng },
      { "fontTypeAssetto", &fontTypeAssetto },
      { "fontTypeNfsu2", &fontTypeNfsu2 },
      { "fontTypeNfsmw", &fontTypeNfsmw },
      { "fontTypeNfsc", &fontTypeNfsc },
      { "fontTypeAlarm", &fontTypeAlarm }, { "fontTypeBattery", &fontTypeBattery },
      { "fontTypeLowBattery", &fontTypeLowBattery },
      { "fontTypeAfterburner", &fontTypeAfterburner },
      { "fontTypeCountdown", &fontTypeCountdown },
      { "fontTypeGameFps", &fontTypeGameFps },
      { "fontTypeVolume", &fontTypeVolume },
    };
    for (auto& m : more) {
      if (!server.hasArg(m.arg)) continue;
      int ft = server.arg(m.arg).toInt();
      if (fontModeOk(ft)) { if ((uint8_t)ft != *m.font) fontTypeTouched = true; *m.font = (uint8_t)ft; }
    }
  }
  if (server.hasArg("obsShowElapsed")) {
    obsShowElapsed = server.arg("obsShowElapsed") == "1";
    portENTER_CRITICAL(&obsMux);
    obsDirty = true;
    portEXIT_CRITICAL(&obsMux);
  }
  if (fontTypeTouched || scrollTypeTouched) {
    requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  }

  if (server.hasArg("tileTransGlobal")) {
    int t = server.arg("tileTransGlobal").toInt();
    if (t >= 0 && t <= 9) tileTransGlobal = (uint8_t)t;
  }
  if (server.hasArg("tileTransHour"))   { int t = server.arg("tileTransHour").toInt();   if (t >= 0 && t <= 9) tileTransHour   = (uint8_t)t; }
  if (server.hasArg("tileTransDate"))   { int t = server.arg("tileTransDate").toInt();   if (t >= 0 && t <= 9) tileTransDate   = (uint8_t)t; }
  if (server.hasArg("tileTransTemp"))   { int t = server.arg("tileTransTemp").toInt();   if (t >= 0 && t <= 9) tileTransTemp   = (uint8_t)t; }
  if (server.hasArg("tileTransNp"))     { int t = server.arg("tileTransNp").toInt();     if (t >= 0 && t <= 9) tileTransNp     = (uint8_t)t; }
  if (server.hasArg("tileTransWx"))     { int t = server.arg("tileTransWx").toInt();     if (t >= 0 && t <= 9) tileTransWx     = (uint8_t)t; }
  if (server.hasArg("tileTransRem"))    { int t = server.arg("tileTransRem").toInt();    if (t >= 0 && t <= 9) tileTransRem    = (uint8_t)t; }
  if (server.hasArg("tileTransCanvas")) { int t = server.arg("tileTransCanvas").toInt(); if (t >= 0 && t <= 9) tileTransCanvas = (uint8_t)t; }
  if (server.hasArg("tileTransPress"))  { int t = server.arg("tileTransPress").toInt();  if (t >= 0 && t <= 9) tileTransPress  = (uint8_t)t; }
  if (server.hasArg("tileTransSs"))     { int t = server.arg("tileTransSs").toInt();     if (t >= 0 && t <= 9) tileTransSs     = (uint8_t)t; }
  if (server.hasArg("tileTransCurr"))   { int t = server.arg("tileTransCurr").toInt();   if (t >= 0 && t <= 9) tileTransCurr   = (uint8_t)t; }
  if (server.hasArg("tileTransYt"))     { int t = server.arg("tileTransYt").toInt();     if (t >= 0 && t <= 9) tileTransYt     = (uint8_t)t; }
  if (server.hasArg("tileTransHw"))     { int t = server.arg("tileTransHw").toInt();     if (t >= 0 && t <= 9) tileTransHw     = (uint8_t)t; }
  if (server.hasArg("tileTransWeb"))    { int t = server.arg("tileTransWeb").toInt();    if (t >= 0 && t <= 9) tileTransWeb    = (uint8_t)t; }
  if (server.hasArg("tileTransNotif"))  { int t = server.arg("tileTransNotif").toInt();  if (t >= 0 && t <= 9) tileTransNotif  = (uint8_t)t; }
  if (server.hasArg("tileTransEts2"))   { int t = server.arg("tileTransEts2").toInt();   if (t >= 0 && t <= 9) tileTransEts2   = (uint8_t)t; }
  if (server.hasArg("tileTransVm"))     { int t = server.arg("tileTransVm").toInt();     if (t >= 0 && t <= 9) tileTransVm     = (uint8_t)t; }
  if (server.hasArg("tileTransBrowserDownloads")) { int t = server.arg("tileTransBrowserDownloads").toInt(); if (t >= 0 && t <= 9) tileTransBrowserDownloads = (uint8_t)t; }
  if (server.hasArg("tileTransObs"))    { int t = server.arg("tileTransObs").toInt();    if (t >= 0 && t <= 9) tileTransObs    = (uint8_t)t; }
  if (server.hasArg("tileTransBeamng")) { int t = server.arg("tileTransBeamng").toInt(); if (t >= 0 && t <= 9) tileTransBeamng = (uint8_t)t; }
  if (server.hasArg("tileTransAssetto")) { int t = server.arg("tileTransAssetto").toInt(); if (t >= 0 && t <= 9) tileTransAssetto = (uint8_t)t; }
  if (server.hasArg("tileTransNfsu2")) { int t = server.arg("tileTransNfsu2").toInt(); if (t >= 0 && t <= 9) tileTransNfsu2 = (uint8_t)t; }
  if (server.hasArg("tileTransNfsmw")) { int t = server.arg("tileTransNfsmw").toInt(); if (t >= 0 && t <= 9) tileTransNfsmw = (uint8_t)t; }
  if (server.hasArg("tileTransNfsc")) { int t = server.arg("tileTransNfsc").toInt(); if (t >= 0 && t <= 9) tileTransNfsc = (uint8_t)t; }
  if (server.hasArg("tileTransQbit")) { int t = server.arg("tileTransQbit").toInt(); if (t >= 0 && t <= 9) tileTransQbit = (uint8_t)t; }
  if (server.hasArg("tileTransBambu")) { int t = server.arg("tileTransBambu").toInt(); if (t >= 0 && t <= 9) tileTransBambu = (uint8_t)t; }
  if (server.hasArg("tileTransAfterburner")) { int t = server.arg("tileTransAfterburner").toInt(); if (t >= 0 && t <= 9) tileTransAfterburner = (uint8_t)t; }
  if (server.hasArg("tileTransCountdown")) { int t = server.arg("tileTransCountdown").toInt(); if (t >= 0 && t <= 9) tileTransCountdown = (uint8_t)t; }
  if (server.hasArg("tileTransGameFps")) { int t = server.arg("tileTransGameFps").toInt(); if (t >= 0 && t <= 9) tileTransGameFps = (uint8_t)t; }
  if (server.hasArg("tileTransVolume")) { int t = server.arg("tileTransVolume").toInt(); if (t >= 0 && t <= 9) tileTransVolume = (uint8_t)t; }
  if (server.hasArg("tileTransSw"))     { int t = server.arg("tileTransSw").toInt();     if (t >= 0 && t <= 9) tileTransSw     = (uint8_t)t; }
  if (server.hasArg("tileTransTmr"))    { int t = server.arg("tileTransTmr").toInt();    if (t >= 0 && t <= 9) tileTransTmr    = (uint8_t)t; }
  if (server.hasArg("tileTransAlarm"))  { int t = server.arg("tileTransAlarm").toInt();  if (t >= 0 && t <= 9) tileTransAlarm  = (uint8_t)t; }
  if (server.hasArg("tileTransIp"))     { int t = server.arg("tileTransIp").toInt();     if (t >= 0 && t <= 9) tileTransIp     = (uint8_t)t; }
  if (server.hasArg("tileTransC2P"))    { int t = server.arg("tileTransC2P").toInt();    if (t >= 0 && t <= 9) tileTransC2P    = (uint8_t)t; }
  if (server.hasArg("tileTransP2C"))    { int t = server.arg("tileTransP2C").toInt();    if (t >= 0 && t <= 9) tileTransP2C    = (uint8_t)t; }
  if (server.hasArg("vmMode")) {
    int v = server.arg("vmMode").toInt();
    if (v >= 0 && v <= 4) vmMode = (uint8_t)v;
  }
  if (server.hasArg("vmSensitivity")) {
    int v = server.arg("vmSensitivity").toInt();
    if (v >= 10 && v <= 100) vmSensitivity = (uint8_t)v;
  }
  // Moving the global speed slider re-applies it to every transition, the way
  // the global scroll speed does.
  if (server.hasArg("tileTransSpd")) {
    int v = server.arg("tileTransSpd").toInt();
    if (v >= TRSPD_MIN && v <= TRSPD_MAX) {
      tileTransSpeed = (uint8_t)v;
      for (int i = 0; i < TRSPD_COUNT; i++) tileTransSpd[i] = (uint8_t)v;
    }
  }
  for (int i = 0; i < TRSPD_COUNT; i++) {
    String a = String("tileTransSpd") + TRSPD_KEYS[i];
    if (server.hasArg(a)) {
      int v = server.arg(a).toInt();
      if (v >= TRSPD_MIN && v <= TRSPD_MAX) tileTransSpd[i] = (uint8_t)v;
    }
  }

  // Settings-only requests (for example transition settings) contain no tile
  // payload. If a tile payload is present, it must contain the entire valid
  // circuit list; never persist a partially received/retry-corrupted list.
  if (server.hasArg("id0")) {
    CycleItem newItems[NUM_ITEMS];
    bool seenIds[16] = { false };
    for (int i = 0; i < NUM_ITEMS; i++) {
      String idKey  = "id"  + String(i);
      String enKey  = "en"  + String(i);
      String durKey = "dur" + String(i);
      if (!server.hasArg(idKey) || !server.hasArg(enKey) || !server.hasArg(durKey)) {
        server.send(400, "text/plain", "Lista de tile-uri este incompleta");
        return;
      }
      int id = server.arg(idKey).toInt();
      int duration = server.arg(durKey).toInt();
      if (id < 0 || id >= 16 || !isValidCircuitItemId((uint8_t)id) || seenIds[id] || duration < 1 || duration > 3600) {
        server.send(400, "text/plain", "Lista de tile-uri este invalida");
        return;
      }
      seenIds[id] = true;
      newItems[i].id          = (uint8_t)id;
      newItems[i].enabled     = (server.arg(enKey) == "1");
      newItems[i].durationSec = (uint16_t)duration;
      newItems[i].order       = i;
    }
    for (int i = 0; i < NUM_ITEMS; i++) items[i] = newItems[i];
  }

  repairItemIds();
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleBrightness() {
  if (!checkAuth()) return;
  if (server.hasArg("level")) {
    curBrightness = (uint8_t)constrain(server.arg("level").toInt(), 0, 16);
  }
  if (server.hasArg("dimAuto")) {
    dimAutoOn = (server.arg("dimAuto") == "1");
  }
  if (server.hasArg("dimFrom")) {
    String t = server.arg("dimFrom");
    if (t.length() == 5) {
      dimFromH = t.substring(0,2).toInt();
      dimFromM = t.substring(3,5).toInt();
    }
  }
  if (server.hasArg("dimTo")) {
    String t = server.arg("dimTo");
    if (t.length() == 5) {
      dimToH = t.substring(0,2).toInt();
      dimToM = t.substring(3,5).toInt();
    }
  }
  if (server.hasArg("dimLevel")) {
    dimLevel = (uint8_t)constrain(server.arg("dimLevel").toInt(), 0, 16);
  }

  applyBrightness();
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleBuzzerSett() {
  if (!checkAuth()) return;
  // The main slider moves every category with it; the Volume page then sets
  // them apart.
  if (server.hasArg("volume")) {
    buzzerVolume = (uint8_t)constrain(server.arg("volume").toInt(), 0, 100);
    buzzVolNotif = buzzVolAuto = buzzVolAlarm = buzzVolTouch = buzzerVolume;
  }
  if (server.hasArg("volnotif")) buzzVolNotif = (uint8_t)constrain(server.arg("volnotif").toInt(), 0, 100);
  if (server.hasArg("volauto"))  buzzVolAuto  = (uint8_t)constrain(server.arg("volauto").toInt(), 0, 100);
  if (server.hasArg("volalarm")) buzzVolAlarm = (uint8_t)constrain(server.arg("volalarm").toInt(), 0, 100);
  if (server.hasArg("voltouch")) buzzVolTouch = (uint8_t)constrain(server.arg("voltouch").toInt(), 0, 100);
  // A volume nobody can hear is a volume nobody can set: every slider asks for
  // a chime at its new level as soon as it is let go.
  String pv = server.arg("preview");
  if (pv.length() && buzzerOn && !alarmRinging) {
    nbStop();
    nbEnqCat = pv == "notif" ? BZ_CAT_NOTIF : pv == "auto"  ? BZ_CAT_AUTO  :
               pv == "alarm" ? BZ_CAT_ALARM : pv == "touch" ? BZ_CAT_TOUCH : BZ_CAT_NONE;
    nbEnqueueEnv(1568, 450, BZ_ENV_BELL);
  }
  if (server.hasArg("preset")) {
    String p = server.arg("preset");
    p.toCharArray(buzzerPreset, sizeof(buzzerPreset));
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleEventSoundSett() {
  if (!checkAuth()) return;
  if (!server.hasArg("event") || !server.hasArg("preset")) {
    server.send(400, "text/plain", "Lipseste event/preset");
    return;
  }
  String ev = server.arg("event");
  String p  = server.arg("preset");
  char* target = nullptr;
  size_t targetSize = 0;
  if (ev == "tile")       { target = eventSoundTile;  targetSize = sizeof(eventSoundTile); }
  else if (ev == "wifi")  { target = eventSoundWifi;  targetSize = sizeof(eventSoundWifi); }
  else if (ev == "notif") { target = eventSoundNotif; targetSize = sizeof(eventSoundNotif); }
  else if (ev == "web")   { target = eventSoundWeb;   targetSize = sizeof(eventSoundWeb);  }
  else if (ev == "ets2")  { target = eventSoundEts2;  targetSize = sizeof(eventSoundEts2); }
  else if (ev == "touch") { target = eventSoundTouch; targetSize = sizeof(eventSoundTouch); }
  else if (ev == "timer") { target = eventSoundTimer; targetSize = sizeof(eventSoundTimer); }
  else if (ev == "pomobreak") { target = eventSoundPomoBreak; targetSize = sizeof(eventSoundPomoBreak); }
  else if (ev == "pomowork")  { target = eventSoundPomoWork;  targetSize = sizeof(eventSoundPomoWork); }
  if (!target) {
    server.send(400, "text/plain", "Eveniment necunoscut");
    return;
  }
  p.toCharArray(target, targetSize);
  saveSettingsDeferred();
  // Picking a sound plays it, at the volume of the category it will play in.
  if (server.arg("preview") == "1" && buzzerOn && !alarmRinging) {
    uint8_t cat = (ev == "notif" || ev == "web") ? BZ_CAT_NOTIF : ev == "touch" ? BZ_CAT_TOUCH : BZ_CAT_AUTO;
    nbPlayPreset(target, cat);
  }
  server.send(200, "text/plain", "OK");
}

void handleTouchSett() {
  if (!checkAuth()) return;
  if (server.hasArg("tap")) {
    touchTapAction = (uint8_t)constrain(server.arg("tap").toInt(), 0, 8);
  }
  if (server.hasArg("dbl")) {
    touchDoubleTapAction = (uint8_t)constrain(server.arg("dbl").toInt(), 0, 8);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

bool isValidHexColor(const String& s) {
  if (s.length() != 7 || s.charAt(0) != '#') return false;
  for (int i = 1; i < 7; i++) {
    char c = s.charAt(i);
    if (!isxdigit((unsigned char)c)) return false;
  }
  return true;
}

// Four comma-separated slots, each empty or #rrggbb. An empty string is all four
// left to the theme.
bool isValidUiColors(const String& s) {
  if (s.length() == 0) return true;
  if (s.length() >= sizeof(webUiColors)) return false;
  int parts = 0, start = 0;
  for (int i = 0; i <= (int)s.length(); i++) {
    if (i == (int)s.length() || s.charAt(i) == ',') {
      String p = s.substring(start, i);
      if (p.length() && !isValidHexColor(p)) return false;
      parts++;
      start = i + 1;
    }
  }
  return parts == 4;
}

void handleAccentSett() {
  if (!checkAuth()) return;
  bool hasHex   = server.hasArg("hex");
  bool hasShape = server.hasArg("shape");
  bool hasColors = server.hasArg("colors");
  bool hasPalette = server.hasArg("palette");
  if (!hasHex && !hasShape && !hasColors && !hasPalette) {
    server.send(400, "text/plain", "Lipseste hex, shape, colors sau palette");
    return;
  }
  String hex;
  if (hasHex) {
    hex = server.arg("hex");
    hex.trim();
    hex.toLowerCase();
    if (!isValidHexColor(hex)) {
      server.send(400, "text/plain", "Culoare invalida");
      return;
    }
  }
  String colors;
  if (hasColors) {
    colors = server.arg("colors");
    colors.trim();
    colors.toLowerCase();
    if (!isValidUiColors(colors)) {
      server.send(400, "text/plain", "Culori invalide");
      return;
    }
  }
  uint8_t shape = webUiShape;
  if (hasShape) {
    long s = server.arg("shape").toInt();
    if (s < 0 || s > 3) {
      server.send(400, "text/plain", "Forma invalida");
      return;
    }
    shape = (uint8_t)s;
  }
  // Nimic nu se scrie pana cand tot ce a venit nu e valid, ca o forma gresita
  // sa nu lase in urma o culoare deja salvata.
  if (hasHex) {
    hex.toCharArray(accentColor, sizeof(accentColor));
  }
  if (hasShape) {
    webUiShape = shape;
  }
  if (hasColors) {
    colors.toCharArray(webUiColors, sizeof(webUiColors));
  }
  if (hasPalette) {
    long pal = server.arg("palette").toInt();
    webUiPalette = (pal >= 0 && pal <= 100) ? (uint8_t)pal : 255;
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// Escapes a string for safe embedding inside a JSON string literal.
// Several /state fields (WiFi SSID, memento text, custom AP name, weather
// city, ...) are free text the user can set to almost anything, including
// double quotes or backslashes. Without escaping, a single " in e.g. a
// memento note corrupts the entire JSON payload and every /state fetch on
// the dashboard fails to parse (silently retried, then surfaced as
// "Nu s-au putut incarca tile-urile. Reincarca pagina.").
String jsonEscape(const String& in) {
  String out;
  out.reserve(in.length() + 8);
  for (size_t i = 0; i < in.length(); i++) {
    char c = in[i];
    switch (c) {
      case '"':  out += "\\\""; break;
      case '\\': out += "\\\\"; break;
      case '\n': out += "\\n";  break;
      case '\r': out += "\\r";  break;
      case '\t': out += "\\t";  break;
      default:
        if ((uint8_t)c < 0x20) {
          char buf[7];
          snprintf(buf, sizeof(buf), "\\u%04x", (uint8_t)c);
          out += buf;
        } else {
          out += c;
        }
    }
  }
  return out;
}

// ChunkedResponse is defined in runtime_support.h and uses a fixed 1 KB
// buffer instead of a growable heap allocation.

// Which tiles the Tile Manager is currently showing. Its own endpoint rather
// than a pair of fields on /settings, because /settings only accepts a tile
// payload that is complete and valid - and adding a tile back is precisely the
// moment the two would have to be sent together and agree.
void handleTileHidden() {
  if (!checkAuth()) return;
  if (server.hasArg("circuit")) {
    long v = server.arg("circuit").toInt();
    if (v < 0 || v > 0xFFFF) { server.send(400, "text/plain", "Masca invalida"); return; }
    tileHiddenMask = (uint16_t)v;
  }
  if (server.hasArg("prio")) {
    long v = server.arg("prio").toInt();
    if (v < 0 || v > 0x7FFFFFFFL) { server.send(400, "text/plain", "Masca invalida"); return; }
    prioHiddenMask = (uint32_t)v;
  }
  applyTileHiddenMask();
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// Polled once a second for as long as the Tile Manager is open, so it stays
// deliberately tiny - /state carries every setting on the device and is far too
// heavy to ask for at that rate.
void handleLiveTile() {
  if (!checkAuth()) return;
  String j = "{\"tile\":";
  j += String((int)gLiveTileId);
  j += ",\"prio\":\"";
  j += gLivePrio;
  j += "\"}";
  server.send(200, "application/json", j);
}

// SCREEN MIRROR
//
// What the matrix is lighting right now, for Octoglow Connect or anything else
// that wants to draw a copy. The server cannot push, so this is polled, and it is
// built to cost next to nothing: 32 column reads from the MD_MAX72XX buffer the
// panel is fed from, a reply under 100 bytes, no heap work beyond sending it.
//
//   GET /getscreen              {"on":true,"b":8,"px":"<64 hex digits>"}
//   GET /getscreen?format=grid  8 lines of 32 '0'/'1', top row first
//   GET /getscreen?since=N      the same, plus the frames shown since frame N:
//                               ,"seq":S,"t":<millis now>,"f":"<a record a frame>"
//
// px is two hex digits per column, leftmost column first; within a column bit 0
// is the top row. b is the brightness level the panel runs at (1-16). When the
// panel is dark - sleep, screen switched off, level 0 - "on" is false and every
// pixel reads 0, because nothing is lit.
//
// A request is answered between two passes of loop(), so a frame caught halfway
// through a transition is never seen, only whole frames. The server closes every
// connection after answering, so each poll is a fresh TCP connection: one at a
// time, 50-100 ms apart, is the pace to use.
//
// SCREEN HISTORY. A copy drawn from one poll every 150 ms shows a scrolling
// line in jumps: the panel moves a column every 30-40 ms. So while someone is
// polling, every frame that differs from the one before it is kept, with the
// moment it was seen, in a ring of the last SCREEN_HIST_N; a poll that says
// which frame it had last (since=N, the "seq" of its previous answer) gets all
// the frames after it and can play them back at the pace the panel showed
// them. A record in "f" is 74 hex digits: 8 of millis(), 2 of flags (bit 0:
// panel lit) and the 64 of px. Frames are numbered from 1; "seq" is the newest.
// Recording stops by itself a few seconds after the last poll.
#define SCREEN_HIST_N       32
#define SCREEN_HIST_GAP_MS  10UL

struct ScreenHistFrame {
  uint32_t ms;
  uint8_t  lit;
  uint8_t  cols[32];
};
static ScreenHistFrame screenHist[SCREEN_HIST_N];
static uint32_t        screenHistSeq     = 0;
static unsigned long   screenHistCheckMs = 0;

// Both callers run in the loop task, so the ring needs no lock.
static void screenHistRecord(unsigned long now) {
  uint8_t cols[32];
  bool lit = mxPanelLevel > 0;
  // mx column 31 is the leftmost one on the panel.
  for (int x = 0; x < 32; x++) cols[x] = lit ? mx.getColumn(31 - x) : 0;
  if (screenHistSeq != 0) {
    const ScreenHistFrame& last = screenHist[(screenHistSeq - 1) % SCREEN_HIST_N];
    if (last.lit == (lit ? 1 : 0) && memcmp(last.cols, cols, sizeof(cols)) == 0) return;
  }
  ScreenHistFrame& f = screenHist[screenHistSeq % SCREEN_HIST_N];
  f.ms  = (uint32_t)now;
  f.lit = lit ? 1 : 0;
  memcpy(f.cols, cols, sizeof(cols));
  screenHistSeq++;
}

static char* screenHexCols(char* p, const uint8_t* cols) {
  static const char HEXD[] = "0123456789abcdef";
  for (int x = 0; x < 32; x++) {
    *p++ = HEXD[cols[x] >> 4];
    *p++ = HEXD[cols[x] & 0x0F];
  }
  return p;
}

void handleGetScreen() {
  if (!checkAuth()) return;
  unsigned long nowMs = millis();
  screenPolledMs = nowMs | 1;
  bool lit = mxPanelLevel > 0;
  uint8_t cols[32];
  // mx column 31 is the leftmost one on the panel.
  for (int x = 0; x < 32; x++) cols[x] = lit ? mx.getColumn(31 - x) : 0;
  server.sendHeader("Cache-Control", "no-store");
  if (server.hasArg("since")) {
    // gSuppressHwFlash means the buffer holds a frame the panel is not showing.
    if (!gSuppressHwFlash) screenHistRecord(nowMs);
    uint32_t since = strtoul(server.arg("since").c_str(), nullptr, 10);
    uint32_t oldest = screenHistSeq > SCREEN_HIST_N ? screenHistSeq - SCREEN_HIST_N : 0;
    // A number from before a restart, or one the ring has already let go of:
    // everything there is.
    if (since > screenHistSeq || since < oldest) since = oldest;
    static char out[192 + SCREEN_HIST_N * 74];
    char* q = out + snprintf(out, 100, "{\"on\":%s,\"b\":%u,\"seq\":%lu,\"t\":%lu,\"px\":\"",
                             lit ? "true" : "false", (unsigned)mxPanelLevel,
                             (unsigned long)screenHistSeq, nowMs);
    q = screenHexCols(q, cols);
    q += sprintf(q, "\",\"f\":\"");
    for (uint32_t i = since; i < screenHistSeq; i++) {
      const ScreenHistFrame& f = screenHist[i % SCREEN_HIST_N];
      q += sprintf(q, "%08lx%02x", (unsigned long)f.ms, (unsigned)f.lit);
      q = screenHexCols(q, f.cols);
    }
    *q++ = '"';
    *q++ = '}';
    *q = '\0';
    server.send(200, "application/json", out);
    return;
  }
  if (server.arg("format") == "grid") {
    char g[8 * 33 + 1];
    char* p = g;
    for (int y = 0; y < 8; y++) {
      for (int x = 0; x < 32; x++) *p++ = ((cols[x] >> y) & 1) ? '1' : '0';
      *p++ = '\n';
    }
    *p = '\0';
    server.send(200, "text/plain", g);
    return;
  }
  char j[100];
  char* e = j + snprintf(j, sizeof(j), "{\"on\":%s,\"b\":%u,\"px\":\"", lit ? "true" : "false", (unsigned)mxPanelLevel);
  e = screenHexCols(e, cols);
  *e++ = '"';
  *e++ = '}';
  *e = '\0';
  server.send(200, "application/json", j);
}

// SCREEN STREAM
//
// /getscreen answers when asked; this sends. Polling costs a fresh TCP connection
// per frame, and with WiFi modem sleep on every one of them waits for the next
// beacon, so a polled copy stutters. A client that wants a live copy - Octoglow
// Connect - asks /screensub for the key over its logged-in session, then sends
// that key to UDP port SCREEN_UDP_PORT about once a second from the socket it
// wants the frames on. Each such packet renews the subscription; with none for
// SCREEN_SUB_TIMEOUT_MS the stream stops. The client speaking first is also what
// gets the frames through a desktop firewall: they arrive as replies.
//
// While subscribed, a frame that differs from the last one sent goes out as one
// 40-byte datagram - at most one per SCREEN_MIN_GAP_MS - and the frame is sent
// again every SCREEN_KEEPALIVE_MS, so a lost packet heals and a still clock can be
// told from a gone one. screenStreamTick() runs from mxCommit() as well as from
// loop(), so the frames of a blocking transition go out too. Modem sleep is off
// while anyone is watching - a subscriber, or /getscreen asked within the last
// SCREEN_POLL_AWAKE_MS - and back on once they leave.
//
// Datagram: "OGF1", sequence (uint16, big endian), flags (bit 0: panel lit),
// brightness level, then 32 columns laid out like /getscreen's px - leftmost
// first, bit 0 the top row, all zero while the panel is dark.
#define SCREEN_UDP_PORT        4211
#define SCREEN_SUB_TIMEOUT_MS  4000UL
#define SCREEN_MIN_GAP_MS      25UL
#define SCREEN_CHECK_MS        20UL
#define SCREEN_KEEPALIVE_MS    500UL
#define SCREEN_POLL_AWAKE_MS   3000UL

static WiFiUDP       screenUdp;
static bool          screenUdpOpen     = false;
static uint32_t      screenKey         = 0;
static bool          screenSubActive   = false;
static IPAddress     screenSubIp;
static uint16_t      screenSubPort     = 0;
static unsigned long screenSubHeardMs  = 0;
static unsigned long screenLastSendMs  = 0;
static unsigned long screenLastCheckMs = 0;
static unsigned long screenLastReadMs  = 0;
static bool          screenSendNow     = false;
static uint16_t      screenSeq         = 0;
static uint8_t       screenLastSent[34];
static bool          screenInTick      = false;
static bool          screenWifiAwake   = false;

// "OGSK" subscribes to the screen mirror. "OGVM" carries one authenticated
// Voicemeeter frame: key, sequence, active flag and 32 display columns.
// A handful per call at most, so a flood cannot hold loop() here.
static void screenReadSubscriptions(unsigned long now) {
  for (int i = 0; i < 8; i++) {
    int len = screenUdp.parsePacket();
    if (len <= 0) break;
    uint8_t buf[43];
    int wanted = len < (int)sizeof(buf) ? len : (int)sizeof(buf);
    if (screenUdp.read(buf, wanted) != wanted || screenKey == 0) continue;
    if (len != 8 && len != 43) continue;
    uint32_t k = ((uint32_t)buf[4] << 24) | ((uint32_t)buf[5] << 16) |
                 ((uint32_t)buf[6] << 8)  |  (uint32_t)buf[7];
    if (k != screenKey) continue;

    if (len == 43 && memcmp(buf, "OGVM", 4) == 0) {
      uint16_t seq = ((uint16_t)buf[8] << 8) | buf[9];
      bool accept = true;
      portENTER_CRITICAL(&vmMux);
      // Octoglow Connect starts its sequence again when the process restarts.
      // After a stale stream timeout, accept that fresh sequence immediately.
      if (vmHaveSequence && now - vmLastPacketMs <= 1500) {
        uint16_t ahead = (uint16_t)(seq - vmLastSequence);
        if (ahead == 0 || ahead >= 0xFF00) accept = false;
      }
      if (accept) {
        bool active = (buf[10] & 1) != 0 && voicemeeterEnabled &&
                      !(prioHiddenMask & (1u << PRIORITY_ID_VOICEMEETER));
        if (memcmp(vmFrame, buf + 11, 32) != 0 || vmActive != active) vmFrameDirty = true;
        memcpy(vmFrame, buf + 11, 32);
        vmActive = active;
        vmLastPacketMs = now;
        vmLastSequence = seq;
        vmHaveSequence = true;
      }
      portEXIT_CRITICAL(&vmMux);
      continue;
    }

    if (len != 8 || memcmp(buf, "OGSK", 4) != 0) continue;
    IPAddress ip   = screenUdp.remoteIP();
    uint16_t  port = screenUdp.remotePort();
    if (!screenSubActive || !(ip == screenSubIp) || port != screenSubPort) {
      screenSubIp   = ip;
      screenSubPort = port;
      screenSendNow = true;
      screenSubActive = true;
    }
    screenSubHeardMs = now;
  }
}

void screenStreamTick() {
  if (screenInTick) return;
  unsigned long now = millis();
  bool polled = screenPolledMs != 0 && now - screenPolledMs < SCREEN_POLL_AWAKE_MS;
  if (!screenUdpOpen && !polled && !screenWifiAwake) return;
  screenInTick = true;

  // A page watching through /getscreen is owed every frame since it last asked.
  if (polled && !gSuppressHwFlash && now - screenHistCheckMs >= SCREEN_HIST_GAP_MS) {
    screenHistCheckMs = now;
    screenHistRecord(now);
  }

  if (screenUdpOpen && now - screenLastReadMs >= 50) {
    screenLastReadMs = now;
    screenReadSubscriptions(now);
  }
  if (screenSubActive && now - screenSubHeardMs > SCREEN_SUB_TIMEOUT_MS) screenSubActive = false;
  bool awake = screenSubActive || polled || vmActive;
  if (awake != screenWifiAwake) {
    screenWifiAwake = awake;
    WiFi.setSleep(!awake);
  }

  // gSuppressHwFlash means the buffer holds a frame the panel is not showing.
  if (screenSubActive && !gSuppressHwFlash &&
      (screenSendNow || (now - screenLastCheckMs >= SCREEN_CHECK_MS &&
                         now - screenLastSendMs >= SCREEN_MIN_GAP_MS))) {
    screenLastCheckMs = now;
    uint8_t frame[34];
    bool lit = mxPanelLevel > 0;
    for (int x = 0; x < 32; x++) frame[x] = lit ? mx.getColumn(31 - x) : 0;
    frame[32] = lit ? 1 : 0;
    frame[33] = mxPanelLevel;
    if (screenSendNow || now - screenLastSendMs >= SCREEN_KEEPALIVE_MS ||
        memcmp(frame, screenLastSent, sizeof(frame)) != 0) {
      uint8_t pkt[40];
      memcpy(pkt, "OGF1", 4);
      pkt[4] = (uint8_t)(screenSeq >> 8);
      pkt[5] = (uint8_t)screenSeq;
      pkt[6] = frame[32];
      pkt[7] = frame[33];
      memcpy(pkt + 8, frame, 32);
      screenSeq++;
      if (screenUdp.beginPacket(screenSubIp, screenSubPort)) {
        screenUdp.write(pkt, sizeof(pkt));
        screenUdp.endPacket();
      }
      memcpy(screenLastSent, frame, sizeof(frame));
      screenLastSendMs = now;
      screenSendNow    = false;
    }
  }
  screenInTick = false;
}

// Hands a logged-in client the key for the stream, opening the UDP socket the
// first time anyone asks. The key lasts until the clock restarts.
void handleScreenSub() {
  if (!checkAuth()) return;
  if (!screenUdpOpen) screenUdpOpen = screenUdp.begin(SCREEN_UDP_PORT);
  if (!screenUdpOpen) {
    server.send(503, "text/plain", "UDP indisponibil");
    return;
  }
  while (screenKey == 0) screenKey = esp_random();
  char j[72];
  snprintf(j, sizeof(j), "{\"port\":%u,\"key\":\"%08lx\",\"timeoutMs\":%lu}",
           (unsigned)SCREEN_UDP_PORT, (unsigned long)screenKey, (unsigned long)SCREEN_SUB_TIMEOUT_MS);
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", j);
}

void handleState() {
  uint32_t _st0 = micros();
  if (!checkAuth()) return;
  bool wifiConnected = WiFi.status() == WL_CONNECTED && !provisionMode;
  String ssid = gWifiSsidCached;

  String localIP = provisionMode ? WiFi.softAPIP().toString() : WiFi.localIP().toString();

  // Each field is appended individually (rather than one long chained `+`
  // expression) so every += only needs one small temporary String for its
  // right-hand side. The previous single chained expression created a cascade of
  // growing temporary String allocations while being evaluated (one per `+`),
  // which fragmented the heap further.
  ChunkedResponse json(server);
  json.begin("application/json");
  json += "{\"buzzer\":";
  json += buzzerOn ? "true" : "false";
  json += ",\"buzzerVolume\":";
  json += buzzerVolume;
  json += ",\"buzzerVolNotif\":";
  json += buzzVolNotif;
  json += ",\"buzzerVolAuto\":";
  json += buzzVolAuto;
  json += ",\"buzzerVolAlarm\":";
  json += buzzVolAlarm;
  json += ",\"buzzerVolTouch\":";
  json += buzzVolTouch;
  json += ",\"buzzerPreset\":\"";
  json += buzzerPreset;
  json += "\"";
  json += ",\"ssid\":\"";
  json.escaped(ssid);
  json += "\"";
  json += ",\"wifiConnected\":";
  json += wifiConnected ? "true" : "false";
  json += ",\"connectedSsid\":\"";
  json.escaped(wifiConnected ? WiFi.SSID() : String(""));
  json += "\"";
  json += ",\"ip\":\"";
  json += localIP;
  json += "\"";
  json += ",\"ap\":";
  json += provisionMode ? "true" : "false";
  json += ",\"apSsid\":\"";
  json.escaped(getApSsid());
  json += "\"";
  json += ",\"version\":\"";
  json += FW_VERSION;
  json += "\"";
  json += ",\"hostName\":\"";
  json += gHostName;
  json += "\"";
  json += ",\"uptime\":";
  json += millis() / 1000;
  json += ",\"lastTemp\":";
  json += lastTemp;
  json += ",\"bmpOk\":";
  json += (bmpOk ? "true" : "false");
  json += ",\"items\":[";
  for (int i = 0; i < NUM_ITEMS; i++) {
    if (i > 0) json += ",";
    json += "{\"id\":";
    json += items[i].id;
    json += ",\"enabled\":";
    json += (items[i].enabled ? "true" : "false");
    json += ",\"dur\":";
    json += items[i].durationSec;
    json += "}";
  }
  json += "]";
  char tFrom[6], tTo[6];
  snprintf(tFrom, 6, "%02d:%02d", dimFromH, dimFromM);
  snprintf(tTo,   6, "%02d:%02d", dimToH,   dimToM);
  json += ",\"bright\":";
  json += curBrightness;
  json += ",\"dimAuto\":";
  json += dimAutoOn ? "true" : "false";
  json += ",\"dimFrom\":\"";
  json += tFrom;
  json += "\"";
  json += ",\"dimTo\":\"";
  json += tTo;
  json += "\"";
  json += ",\"dimLevel\":";
  json += dimLevel;
  json += ",\"tempunit\":";
  json += tempUnit;
  json += ",\"hourformat\":";
  json += hourFormat;
  json += ",\"hourLeadingZero\":";
  json += hourLeadingZero ? "true" : "false";
  json += ",\"hwFormat\":";
  json += hwFormat;
  json += ",\"hwSkin\":";
  json += hwSkin;
  json += ",\"hwContent\":";
  json += hwContent;
  json += ",\"hwLeadZero\":";
  json += hwLeadZero ? "true" : "false";
  json += ",\"hwBarMode\":";
  json += hwBarMode;
  json += ",\"hwBarPos\":";
  json += hwBarPos;
  json += ",\"hwSwap\":";
  json += hwSwap ? "true" : "false";
  json += ",\"netTimeSync\":";
  json += netTimeSync ? "true" : "false";
  json += ",\"defaultStartMode\":";
  json += defaultStartMode;
  json += ",\"dateformat\":";
  json += dateFormat;
  json += ",\"datelang\":";
  json += dateLang;
  json += ",\"customdatefmt\":\"";
  json.escaped(customDateFmt);
  json += "\"";
  json += ",\"wxCity\":\"";
  json.escaped(weatherCity);
  json += "\"";
  json += ",\"wxPreset\":";
  json += wxPreset;
  json += ",\"wxLang\":\"";
  json.escaped(weatherLang);
  json += "\"";
  json += ",\"wxHasKey\":";
  json += strlen(weatherApiKey) > 0 ? "true" : "false";
  // Social tiles. The API key is never sent back to the browser - only whether
  // one is stored - the same way the weather key is handled.
  for (int i = 0; i < SOCIAL_COUNT; i++) {
    json += ",\"soc";
    json += i;
    json += "Handle\":\"";
    json.escaped(social[i].handle);
    json += "\"";
    json += ",\"soc";
    json += i;
    json += "ShowName\":";
    json += (social[i].showName ? "true" : "false");
    json += ",\"soc";
    json += i;
    json += "HasKey\":";
    json += social[i].apiKey[0] ? "true" : "false";
    json += ",\"soc";
    json += i;
    json += "KeyLen\":";
    json += strlen(social[i].apiKey);
    json += ",\"soc";
    json += i;
    json += "Count\":";
    json += social[i].count;
    json += ",\"soc";
    json += i;
    json += "Valid\":";
    json += social[i].valid ? "true" : "false";
    json += ",\"soc";
    json += i;
    json += "Err\":";
    json += socialErrCode(i);
  }
  json += ",\"wxErr\":";
  json += weatherErrCode();
  json += ",\"currencyErr\":";
  json += currencyErr;
  json += ",\"iconSelYoutube\":";
  json += iconSelYoutube;
  json += ",\"currencyBase\":\"";
  json += currencyBase;
  json += "\"";
  json += ",\"currencyQuote\":\"";
  json += currencyQuote;
  json += "\"";
  json += ",\"currencyCompare\":";
  json += currencyCompareEnabled ? "true" : "false";
  json += ",\"liveHl\":";
  json += liveTileHighlight ? "true" : "false";
  json += ",\"uiDark\":";
  json += webUiDark ? "true" : "false";
  json += ",\"uiLang\":\"";
  json += webUiLang;
  json += "\"";
  json += ",\"showGrayed\":";
  json += showGrayedContent ? "true" : "false";
  json += ",\"autoSleep\":";
  json += autoSleepOn ? "true" : "false";
  json += ",\"autoSleepSec\":";
  json += autoSleepSec;
  json += ",\"sleeping\":";
  json += sleepActive ? "true" : "false";
  json += ",\"tileHidden\":";
  json += tileHiddenMask;
  json += ",\"prioHidden\":";
  json += prioHiddenMask;
  json += ",\"webEnabled\":";
  json += webAccessEnabled ? "true" : "false";
  json += ",\"webAlAccess\":";
  json += webAlertAccess ? "true" : "false";
  json += ",\"webAlLocked\":";
  json += webAlertLocked ? "true" : "false";
  json += ",\"webAlLogin\":";
  json += webAlertLogin ? "true" : "false";
  json += ",\"webAlNet\":";
  json += webAlertNet ? "true" : "false";
  json += ",\"notifEnabled\":";
  json += notifEnabled ? "true" : "false";
  json += ",\"ets2Enabled\":";
  json += ets2Enabled ? "true" : "false";
  json += ",\"voicemeeterEnabled\":";
  json += voicemeeterEnabled ? "true" : "false";
  json += ",\"browserDownloadsEnabled\":";
  json += browserDownloadsEnabled ? "true" : "false";
  json += ",\"obsEnabled\":";
  json += obsEnabled ? "true" : "false";
  json += ",\"obsShowElapsed\":";
  json += obsShowElapsed ? "true" : "false";
  json += ",\"beamngEnabled\":";
  json += beamngEnabled ? "true" : "false";
  json += ",\"assettoEnabled\":";
  json += assettoEnabled ? "true" : "false";
  json += ",\"qbitEnabled\":";
  json += qbitEnabled ? "true" : "false";
  json += ",\"bambuEnabled\":";
  json += bambuEnabled ? "true" : "false";
  json += ",\"abMode\":";
  json += abMode;
  json += ",\"volumeAdaptiveIcon\":";
  json += volumeAdaptiveIcon ? "true" : "false";
  json += ",\"gameFpsEnabled\":";
  json += gameFpsEnabled ? "true" : "false";
  json += ",\"volumeEnabled\":";
  json += volumeEnabled ? "true" : "false";
  json += ",\"nfsu2Enabled\":";
  json += nfsu2Enabled ? "true" : "false";
  json += ",\"nfsmwEnabled\":";
  json += nfsmwEnabled ? "true" : "false";
  json += ",\"nfscEnabled\":";
  json += nfscEnabled ? "true" : "false";
  json += ",\"lowBatteryEnabled\":";
  json += lowBatteryEnabled ? "true" : "false";
  json += ",\"ets2Mph\":";
  json += ets2SpeedMph ? "true" : "false";
  json += ",\"beamngMph\":";
  json += beamngSpeedMph ? "true" : "false";
  json += ",\"assettoMph\":";
  json += assettoSpeedMph ? "true" : "false";
  json += ",\"nfsu2Mph\":";
  json += nfsu2SpeedMph ? "true" : "false";
  json += ",\"nfsmwMph\":";
  json += nfsmwSpeedMph ? "true" : "false";
  json += ",\"nfscMph\":";
  json += nfscSpeedMph ? "true" : "false";
  json += ",\"ets2Gauge\":";
  json += ets2Gauge;
  json += ",\"beamngGauge\":";
  json += beamngGauge;
  json += ",\"assettoGauge\":";
  json += assettoGauge;
  json += ",\"nfsu2Gauge\":";
  json += nfsu2Gauge;
  json += ",\"nfsmwGauge\":";
  json += nfsmwGauge;
  json += ",\"nfscGauge\":";
  json += nfscGauge;
  json += ",\"ets2GaugeFull\":";
  json += (int)ets2GaugeFull;
  json += ",\"beamngGaugeFull\":";
  json += (int)beamngGaugeFull;
  json += ",\"assettoGaugeFull\":";
  json += (int)assettoGaugeFull;
  json += ",\"nfsu2GaugeFull\":";
  json += (int)nfsu2GaugeFull;
  json += ",\"nfsmwGaugeFull\":";
  json += (int)nfsmwGaugeFull;
  json += ",\"nfscGaugeFull\":";
  json += (int)nfscGaugeFull;
  json += ",\"vmMode\":";
  json += vmMode;
  json += ",\"vmSensitivity\":";
  json += vmSensitivity;
  json += ",\"ets2OrderFirst\":";
  json += ets2OrderFirst ? "true" : "false";
  json += ",\"nowPlayingIsPriority\":";
  json += nowPlayingIsPriority ? "true" : "false";
  json += ",\"priorityOrder\":\"";
  json += priorityOrderToString();
  json += "\"";
  json += ",\"hideIcons\":";
  json += hideTileIcons ? "true" : "false";
  json += ",\"hideIconDate\":";
  json += hideIconDate ? "true" : "false";
  json += ",\"hideIconTemp\":";
  json += hideIconTemp ? "true" : "false";
  json += ",\"hideIconReminder\":";
  json += hideIconReminder ? "true" : "false";
  json += ",\"hideIconWeather\":";
  json += hideIconWeather ? "true" : "false";
  json += ",\"hideIconNotif\":";
  json += hideIconNotif ? "true" : "false";
  json += ",\"hideIconNowPlaying\":";
  json += hideIconNowPlaying ? "true" : "false";
  json += ",\"hideIconPressure\":";
  json += hideIconPressure ? "true" : "false";
  json += ",\"hideIconCurrency\":";
  json += hideIconCurrency ? "true" : "false";
  json += ",\"hideIconYoutube\":";
  json += hideIconYoutube ? "true" : "false";
  json += ",\"hideIconWebAccess\":";
  json += hideIconWebAccess ? "true" : "false";
  json += ",\"hideIconIp\":";
  json += hideIconIp ? "true" : "false";
  json += ",\"hideIconVoicemeeter\":";
  json += hideIconVoicemeeter ? "true" : "false";
  json += ",\"hideIconBrowserDownloads\":";
  json += hideIconBrowserDownloads ? "true" : "false";
  json += ",\"hideIconQbit\":";
  json += hideIconQbit ? "true" : "false";
  json += ",\"hideIconBambu\":";
  json += hideIconBambu ? "true" : "false";
  json += ",\"hideIconAfterburner\":";
  json += hideIconAfterburner ? "true" : "false";
  json += ",\"hideIconCountdown\":";
  json += hideIconCountdown ? "true" : "false";
  json += ",\"hideIconGameFps\":";
  json += hideIconGameFps ? "true" : "false";
  json += ",\"hideIconVolume\":";
  json += hideIconVolume ? "true" : "false";
  json += ",\"bdBarStyle\":";
  json += barStyleBrowserDownloads;
  json += ",\"qbitBarStyle\":";
  json += barStyleQbit;
  json += ",\"bambuBarStyle\":";
  json += barStyleBambu;
  json += ",\"hideIconObs\":";
  json += hideIconObs ? "true" : "false";
  json += ",\"npAdaptiveIcon\":";
  json += npAdaptiveIcon ? "true" : "false";
  json += ",\"iconSelDate\":";
  json += iconSelDate;
  json += ",\"iconSelTemp\":";
  json += iconSelTemp;
  json += ",\"iconSelReminder\":";
  json += iconSelReminder;
  json += ",\"iconSelNotif\":";
  json += iconSelNotif;
  json += ",\"iconSelNpMusic\":";
  json += iconSelNpMusic;
  json += ",\"iconSelNpVideo\":";
  json += iconSelNpVideo;
  json += ",\"iconSelNpPaused\":";
  json += iconSelNpPaused;
  json += ",\"iconSelPressure\":";
  json += iconSelPressure;
  json += ",\"iconSelCurrency\":";
  json += iconSelCurrency;
  json += ",\"iconSelIp\":";
  json += iconSelIp;
  json += ",\"iconSelVoicemeeter\":";
  json += iconSelVoicemeeter;
  json += ",\"iconSelBrowserDownloads\":";
  json += iconSelBrowserDownloads;
  json += ",\"iconSelQbit\":";
  json += iconSelQbit;
  json += ",\"iconSelBambu\":";
  json += iconSelBambu;
  json += ",\"iconSelObs\":";
  json += iconSelObs;
  json += ",\"iconSelBattery\":";
  json += iconSelBattery;
  json += ",\"iconSelLowBattery\":";
  json += iconSelLowBattery;
  json += ",\"iconSelAfterburner\":";
  json += iconSelAfterburner;
  json += ",\"iconSelCountdown\":";
  json += iconSelCountdown;
  json += ",\"iconSelGameFps\":";
  json += iconSelGameFps;
  json += ",\"iconSelVolume\":";
  json += iconSelVolume;
  json += ",\"iconWxSunny\":";
  json += iconWxSunny;
  json += ",\"iconWxCloud\":";
  json += iconWxCloud;
  json += ",\"iconWxRain\":";
  json += iconWxRain;
  json += ",\"iconWxStorm\":";
  json += iconWxStorm;
  json += ",\"iconWxSnow\":";
  json += iconWxSnow;
  json += ",\"iconWxWind\":";
  json += iconWxWind;
  json += ",\"iconWxNight\":";
  json += iconWxNight;
  json += ",\"scrollType\":";
  json += scrollType;
  json += ",\"scrollTypeDate\":";
  json += scrollTypeDate;
  json += ",\"scrollTypeTemp\":";
  json += scrollTypeTemp;
  json += ",\"scrollTypeReminder\":";
  json += scrollTypeReminder;
  json += ",\"scrollTypeWeather\":";
  json += scrollTypeWeather;
  json += ",\"scrollTypeNotif\":";
  json += scrollTypeNotif;
  json += ",\"scrollTypeNowPlaying\":";
  json += scrollTypeNowPlaying;
  json += ",\"scrollTypePressure\":";
  json += scrollTypePressure;
  json += ",\"scrollTypeStopwatch\":";
  json += scrollTypeStopwatch;
  json += ",\"scrollTypeCurrency\":";
  json += scrollTypeCurrency;
  json += ",\"scrollTypeYoutube\":";
  json += scrollTypeYoutube;
  json += ",\"scrollTypeWebAccess\":";
  json += scrollTypeWebAccess;
  json += ",\"scrollTypeTimer\":";
  json += scrollTypeTimer;
  json += ",\"scrollSpeed\":";
  json += scrollSpeed;
  for (int i = 0; i < SCROLL_SPEED_COUNT; i++) {
    json += ",\"scrollSpeed";
    json += SPD_KEYS[i];
    json += "\":";
    json += scrollSpeedTile[i];
  }
  json += ",\"scrollTypeIp\":";
  json += scrollTypeIp;
  json += ",\"scrollTypeObs\":";
  json += scrollTypeObs;
  json += ",\"scrollTypeAfterburner\":";
  json += scrollTypeAfterburner;
  json += ",\"scrollTypeCountdown\":";
  json += scrollTypeCountdown;
  json += ",\"scrollTypeGameFps\":";
  json += scrollTypeGameFps;
  json += ",\"scrollTypeVolume\":";
  json += scrollTypeVolume;
  json += ",\"fontType\":";
  json += fontType;
  json += ",\"fontTypeDate\":";
  json += fontTypeDate;
  json += ",\"fontTypeTemp\":";
  json += fontTypeTemp;
  json += ",\"fontTypeReminder\":";
  json += fontTypeReminder;
  json += ",\"fontTypeWeather\":";
  json += fontTypeWeather;
  json += ",\"fontTypeNotif\":";
  json += fontTypeNotif;
  json += ",\"fontTypeNowPlaying\":";
  json += fontTypeNowPlaying;
  json += ",\"fontTypePressure\":";
  json += fontTypePressure;
  json += ",\"fontTypeStopwatch\":";
  json += fontTypeStopwatch;
  json += ",\"fontTypeCurrency\":";
  json += fontTypeCurrency;
  json += ",\"fontTypeYoutube\":";
  json += fontTypeYoutube;
  json += ",\"fontTypeWebAccess\":";
  json += fontTypeWebAccess;
  json += ",\"fontTypeTimer\":";
  json += fontTypeTimer;
  json += ",\"fontTypeIp\":";
  json += fontTypeIp;
  json += ",\"fontTypeObs\":";
  json += fontTypeObs;
  json += ",\"fontTypeEts2\":";
  json += fontTypeEts2;
  json += ",\"fontTypeBeamng\":";
  json += fontTypeBeamng;
  json += ",\"fontTypeAssetto\":";
  json += fontTypeAssetto;
  json += ",\"fontTypeNfsu2\":";
  json += fontTypeNfsu2;
  json += ",\"fontTypeNfsmw\":";
  json += fontTypeNfsmw;
  json += ",\"fontTypeNfsc\":";
  json += fontTypeNfsc;
  json += ",\"fontTypeAlarm\":";
  json += fontTypeAlarm;
  json += ",\"fontTypeBattery\":";
  json += fontTypeBattery;
  json += ",\"fontTypeLowBattery\":";
  json += fontTypeLowBattery;
  json += ",\"fontTypeAfterburner\":";
  json += fontTypeAfterburner;
  json += ",\"fontTypeCountdown\":";
  json += fontTypeCountdown;
  json += ",\"fontTypeGameFps\":";
  json += fontTypeGameFps;
  json += ",\"fontTypeVolume\":";
  json += fontTypeVolume;
  json += ",\"tileTransGlobal\":";
  json += tileTransGlobal;
  json += ",\"tileTransHour\":";
  json += tileTransHour;
  json += ",\"tileTransDate\":";
  json += tileTransDate;
  json += ",\"tileTransTemp\":";
  json += tileTransTemp;
  json += ",\"tileTransNp\":";
  json += tileTransNp;
  json += ",\"tileTransWx\":";
  json += tileTransWx;
  json += ",\"tileTransRem\":";
  json += tileTransRem;
  json += ",\"tileTransCanvas\":";
  json += tileTransCanvas;
  json += ",\"tileTransPress\":";
  json += tileTransPress;
  json += ",\"tileTransSs\":";
  json += tileTransSs;
  json += ",\"tileTransCurr\":";
  json += tileTransCurr;
  json += ",\"tileTransYt\":";
  json += tileTransYt;
  json += ",\"tileTransHw\":";
  json += tileTransHw;
  json += ",\"tileTransWeb\":";
  json += tileTransWeb;
  json += ",\"tileTransNotif\":";
  json += tileTransNotif;
  json += ",\"tileTransEts2\":";
  json += tileTransEts2;
  json += ",\"tileTransVm\":";
  json += tileTransVm;
  json += ",\"tileTransBrowserDownloads\":";
  json += tileTransBrowserDownloads;
  json += ",\"tileTransObs\":";
  json += tileTransObs;
  json += ",\"tileTransBeamng\":";
  json += tileTransBeamng;
  json += ",\"tileTransAssetto\":";
  json += tileTransAssetto;
  json += ",\"tileTransNfsu2\":";
  json += tileTransNfsu2;
  json += ",\"tileTransNfsmw\":";
  json += tileTransNfsmw;
  json += ",\"tileTransNfsc\":";
  json += tileTransNfsc;
  json += ",\"tileTransQbit\":";
  json += tileTransQbit;
  json += ",\"tileTransBambu\":";
  json += tileTransBambu;
  json += ",\"tileTransAfterburner\":";
  json += tileTransAfterburner;
  json += ",\"tileTransCountdown\":";
  json += tileTransCountdown;
  // The countdown: what it says now, and its events for the dialog.
  json += ",\"cdText\":\"";
  {
    char t[CD_NAME_MAX + 24] = "";
    countdownText(t, sizeof(t));
    json.escaped(t);
  }
  json += "\",\"cdFull\":";
  json += cdFullUnit ? "true" : "false";
  json += ",\"cdLang\":";
  json += cdLang;
  json += ",\"cdEvents\":[";
  {
    bool first = true;
    for (const CountdownEvent& e : cdEvents) {
      if (!(e.flags & CD_USED)) continue;
      char date[12];
      snprintf(date, sizeof(date), "%04u-%02u-%02u", (unsigned)e.year, (unsigned)e.month, (unsigned)e.day);
      json += first ? "{\"n\":\"" : ",{\"n\":\"";
      first = false;
      json.escaped(e.name);
      json += "\",\"d\":\"";
      json += date;
      json += "\",\"y\":";
      json += (e.flags & CD_YEARLY) ? "true" : "false";
      json += ",\"i\":";
      json += e.icon;
      json += "}";
    }
  }
  json += "]";
  json += ",\"tileTransGameFps\":";
  json += tileTransGameFps;
  json += ",\"tileTransVolume\":";
  json += tileTransVolume;
  json += ",\"tileTransSw\":";
  json += tileTransSw;
  json += ",\"tileTransTmr\":";
  json += tileTransTmr;
  json += ",\"tileTransAlarm\":";
  json += tileTransAlarm;
  json += ",\"tileTransIp\":";
  json += tileTransIp;
  json += ",\"tileTransC2P\":";
  json += tileTransC2P;
  json += ",\"tileTransP2C\":";
  json += tileTransP2C;
  json += ",\"tileTransSpd\":";
  json += tileTransSpeed;
  for (int i = 0; i < TRSPD_COUNT; i++) {
    json += ",\"tileTransSpd";
    json += TRSPD_KEYS[i];
    json += "\":";
    json += tileTransSpd[i];
  }
  json += ",\"timerDurationSec\":";
  json += timerDurationSec;
  json += ",\"timerPreset\":";
  json += timerPreset;
  json += ",\"timerMode\":";
  json += timerMode;
  json += ",\"pomoWork\":";
  json += pomoWorkMin;
  json += ",\"pomoShort\":";
  json += pomoShortMin;
  json += ",\"pomoLong\":";
  json += pomoLongMin;
  json += ",\"pomoRounds\":";
  json += pomoRounds;
  json += ",\"alarms\":[";
  for (int i = 0; i < ALARM_COUNT; i++) {
    if (i > 0) json += ",";
    json += "{\"h\":";
    json += alarms[i].hour;
    json += ",\"m\":";
    json += alarms[i].minute;
    json += ",\"d\":";
    json += alarms[i].days;
    json += ",\"en\":";
    json += (alarms[i].enabled ? "true" : "false");
    json += ",\"p\":";
    json += (alarms[i].present ? "true" : "false");
    json += ",\"tone\":\"";
    json.escaped(alarms[i].tone);
    json += "\"}";
  }
  json += "]";
  json += ",\"pressureHpa\":";
  json += (int)round(lastPressureHpa);
  json += ",\"pressureTrend\":";
  json += pressureTrend;
  json += ",\"currencyValid\":";
  json += currencyValid ? "true" : "false";
  json += ",\"currencyRate\":";
  json += String((currencyValid && !isnan(currencyRateNow)) ? currencyRateNow : 0.0f, 4);
  json += ",\"currencyTrend\":";
  json += currencyTrend;
  json += ",\"mementoText\":\"";
  json.escaped(mementoBuf);
  json += "\"";
  json += ",\"canvasBmp\":\"";
  json += canvasBitmapToHex();
  json += "\"";
  json += ",\"evSndTile\":\"";
  json.escaped(eventSoundTile);
  json += "\"";
  json += ",\"evSndWifi\":\"";
  json.escaped(eventSoundWifi);
  json += "\"";
  json += ",\"evSndWeb\":\"";
  json.escaped(eventSoundWeb);
  json += "\"";
  json += ",\"evSndNotif\":\"";
  json.escaped(eventSoundNotif);
  json += "\"";
  json += ",\"evSndEts2\":\"";
  json.escaped(eventSoundEts2);
  json += "\"";
  json += ",\"evSndTouch\":\"";
  json.escaped(eventSoundTouch);
  json += "\"";
  json += ",\"evSndTimer\":\"";
  json.escaped(eventSoundTimer);
  json += "\"";
  json += ",\"evSndPomoBreak\":\"";
  json.escaped(eventSoundPomoBreak);
  json += "\",\"evSndPomoWork\":\"";
  json.escaped(eventSoundPomoWork);
  json += "\"";
  json += ",\"touchTapAction\":";
  json += touchTapAction;
  json += ",\"touchDoubleTapAction\":";
  json += touchDoubleTapAction;
  json += ",\"uiShape\":";
  json += webUiShape;
  json += ",\"uiPalette\":";
  json += webUiPalette;
  json += ",\"uiColors\":\"";
  json += webUiColors;
  json += "\"";
  json += ",\"accentColor\":\"";
  json.escaped(accentColor);
  json += "\"";
  json += ",\"ssAnim\":";
  json += ssAnimSelected;
  json += "}";
  uint32_t _stBuild = micros() - _st0;
  if (_stBuild > hwStateBuildUs) hwStateBuildUs = _stBuild;
  json.end();
  { uint32_t d = micros() - _st0; if (d > hwStateUs) hwStateUs = d; }
}

// HARDWARE MONITOR
//
// On-die temperature. Not every ESP32 variant has a usable internal sensor -
// the original ESP32 reports a constant 53.33 regardless of reality - so this
// is gated at compile time on the targets that do, and the reading is then
// range-checked before being trusted. When it is unavailable the field is
// reported as absent and the UI drops the gauge, the same way it does for a
// board with no PSRAM.
static bool readChipTemp(float& out) {
#if defined(CONFIG_IDF_TARGET_ESP32S3) || defined(CONFIG_IDF_TARGET_ESP32S2) || \
    defined(CONFIG_IDF_TARGET_ESP32C3) || defined(CONFIG_IDF_TARGET_ESP32C6)
  float t = temperatureRead();
  if (isnan(t) || t < -40.0f || t > 125.0f) return false;
  out = t;
  return true;
#else
  (void)out;
  return false;
#endif
}

// Small enough (~380 bytes) to go out in one send(); no need for the chunked
// path /state uses. Every value here is measured: read straight from the SDK,
// or counted by hwMonitorTick(). Nothing is estimated.
void handleHwReset() {
  if (!checkAuth()) return;
  hwResetPeaks();
  server.send(200, "text/plain", "OK");
}

void handleHwState() {
  if (!checkAuth()) return;
  String j;
  j.reserve(1024);
  j = "{\"cpuFreqMhz\":";
  j += String(ESP.getCpuFreqMHz());
  j += ",\"cores\":";
  j += String(ESP.getChipCores());
  j += ",\"chip\":\"";
  j += String(ESP.getChipModel());
  j += "\"";
  j += ",\"loopsPerSec\":";
  j += String(hwLoopsPerSec);
  j += ",\"loopMaxUs\":";
  j += String(hwLoopMaxUsLast);
  j += ",\"loopMaxUsEver\":";
  j += String(hwLoopMaxUsEver);
  j += ",\"httpMaxUs\":";
  j += String(hwHttpMaxUs);
  j += ",\"httpMaxUri\":\"";
  j += jsonEscape(String(hwHttpMaxUri));
  j += "\"";
  j += ",\"pageUs\":";
  j += String(hwPageUs);
  j += ",\"pageTransferUs\":";
  j += String(hwPageTransferUs);
  j += ",\"stateUs\":";
  j += String(hwStateUs);
  j += ",\"stateBuildUs\":";
  j += String(hwStateBuildUs);
  j += ",\"pageFull\":";
  j += String(hwPageFull);
  j += ",\"page304\":";
  j += String(hwPage304);
  j += ",\"heapFree\":";
  j += String(ESP.getFreeHeap());
  j += ",\"heapSize\":";
  j += String(ESP.getHeapSize());
  j += ",\"heapMinFree\":";
  j += String(ESP.getMinFreeHeap());
  j += ",\"heapMaxAlloc\":";
  j += String(ESP.getMaxAllocHeap());
  j += ",\"psramSize\":";
  j += String(ESP.getPsramSize());
  j += ",\"psramFree\":";
  j += String(ESP.getFreePsram());
  j += ",\"sketchSize\":";
  j += String(hwSketchSize);
  j += ",\"sketchFree\":";
  j += String(hwSketchFree);
  j += ",\"flashSize\":";
  j += String(ESP.getFlashChipSize());
  j += ",\"uptime\":";
  j += String(millis() / 1000);
  float chipTempC = 0.0f;
  bool hasTemp = readChipTemp(chipTempC);
  j += ",\"hasTemp\":";
  j += String(hasTemp ? "true" : "false");
  j += ",\"tempC\":";
  j += String(hasTemp ? chipTempC : 0.0f, 1);
  j += ",\"tempUnit\":";
  j += String(tempUnit);
  j += "}";
  server.send(200, "application/json", j);
}

// BACKUP AND RESTORE
//
// A backup is what the clock keeps in NVS, key by key with each key's type:
// everything it loads at start-up, so a setting added later is in the file
// without anyone having to list it here. The file is made of up to three
// parts, and whoever exports picks which go in:
//   custom - "settings": the tiles, their order, what each shows and for how
//            long, the API keys, fonts, icons, scrolling, transitions, buzzer
//            tones, touch actions, languages, the web interface's theme and
//            the account preferences;
//   conn   - "wifi" and "apcfg": the Wi-Fi network and the access point, with
//            their passwords;
//   users  - "auth": the user name and the password's hash (never the
//            password itself), so the same login works after a restore.
// Restoring clears each namespace the file carries and writes its keys back,
// then restarts. loadSettings() then reads a backup made on an older build the
// way it reads that build's own NVS after an update, migrations included, and
// a namespace the file leaves out is not touched.
struct BackupPart { const char* name; const char* spaces[2]; };
static const BackupPart BACKUP_PARTS[] = {
  { "custom", { "settings", nullptr } },
  { "conn",   { "wifi",     "apcfg" } },
  { "users",  { "auth",     nullptr } },
};
// The one-byte settings' pack (see SettingsWriter) is the largest: ~1.4 KB.
static const size_t BACKUP_BLOB_MAX = 4096;
static const size_t BACKUP_STR_MAX  = 4000;  // NVS's own limit, with the terminator

static const struct { nvs_type_t type; const char* name; } BACKUP_TYPES[] = {
  { NVS_TYPE_U8,  "u8"  }, { NVS_TYPE_I8,  "i8"  },
  { NVS_TYPE_U16, "u16" }, { NVS_TYPE_I16, "i16" },
  { NVS_TYPE_U32, "u32" }, { NVS_TYPE_I32, "i32" },
  { NVS_TYPE_U64, "u64" }, { NVS_TYPE_I64, "i64" },
  { NVS_TYPE_STR, "str" }, { NVS_TYPE_BLOB, "blob" },
};

static const char* backupTypeName(nvs_type_t type) {
  for (const auto& t : BACKUP_TYPES) if (t.type == type) return t.name;
  return nullptr;
}

static bool backupTypeFromName(const char* name, nvs_type_t& type) {
  for (const auto& t : BACKUP_TYPES)
    if (strcmp(t.name, name) == 0) { type = t.type; return true; }
  return false;
}

static bool backupIsHex(const char* s, size_t maxLen) {
  size_t n = strlen(s);
  if (n % 2 || n > maxLen) return false;
  for (size_t i = 0; i < n; i++) if (!isxdigit((unsigned char)s[i])) return false;
  return true;
}

// A 64-bit number travels as text, which every JSON reader keeps exact.
static bool backupIsInteger(const char* s, bool allowSign) {
  if (allowSign && *s == '-') s++;
  if (!*s || strlen(s) > 20) return false;
  for (; *s; s++) if (!isdigit((unsigned char)*s)) return false;
  return true;
}

// One key as ,"key":["type",value] - or nothing, when it can't be read.
static void backupExportEntry(ChunkedResponse& out, nvs_handle_t h, const char* key,
                              nvs_type_t type, bool& first) {
  const char* typeName = backupTypeName(type);
  if (!typeName) return;
  char num[26];
  char* text = nullptr;   // a string, or a blob already turned into hex
  bool quoted = false;
  esp_err_t r = ESP_FAIL;
  switch (type) {
    case NVS_TYPE_U8:  { uint8_t v = 0;  r = nvs_get_u8(h, key, &v);  snprintf(num, sizeof num, "%u", (unsigned)v); break; }
    case NVS_TYPE_I8:  { int8_t v = 0;   r = nvs_get_i8(h, key, &v);  snprintf(num, sizeof num, "%d", (int)v); break; }
    case NVS_TYPE_U16: { uint16_t v = 0; r = nvs_get_u16(h, key, &v); snprintf(num, sizeof num, "%u", (unsigned)v); break; }
    case NVS_TYPE_I16: { int16_t v = 0;  r = nvs_get_i16(h, key, &v); snprintf(num, sizeof num, "%d", (int)v); break; }
    case NVS_TYPE_U32: { uint32_t v = 0; r = nvs_get_u32(h, key, &v); snprintf(num, sizeof num, "%lu", (unsigned long)v); break; }
    case NVS_TYPE_I32: { int32_t v = 0;  r = nvs_get_i32(h, key, &v); snprintf(num, sizeof num, "%ld", (long)v); break; }
    case NVS_TYPE_U64: { uint64_t v = 0; r = nvs_get_u64(h, key, &v); snprintf(num, sizeof num, "\"%llu\"", (unsigned long long)v); break; }
    case NVS_TYPE_I64: { int64_t v = 0;  r = nvs_get_i64(h, key, &v); snprintf(num, sizeof num, "\"%lld\"", (long long)v); break; }
    case NVS_TYPE_STR: {
      size_t len = 0;
      if (nvs_get_str(h, key, nullptr, &len) != ESP_OK || len == 0) return;
      text = (char*)malloc(len);
      if (!text) return;
      r = nvs_get_str(h, key, text, &len);
      quoted = true;
      break;
    }
    case NVS_TYPE_BLOB: {
      size_t len = 0;
      if (nvs_get_blob(h, key, nullptr, &len) != ESP_OK || len > BACKUP_BLOB_MAX) return;
      uint8_t* bytes = (uint8_t*)malloc(len ? len : 1);
      text = (char*)malloc(len * 2 + 1);
      if (bytes && text && (r = nvs_get_blob(h, key, bytes, &len)) == ESP_OK)
        for (size_t i = 0; i < len; i++) snprintf(text + i * 2, 3, "%02x", bytes[i]);
      if (text) text[len * 2] = '\0';
      free(bytes);
      quoted = true;
      break;
    }
    default: return;
  }
  if (r == ESP_OK) {
    if (!first) out += ",";
    first = false;
    out += "\"";
    out.escaped(key);
    out += "\":[\"";
    out += typeName;
    out += "\",";
    if (quoted) { out += "\""; out.escaped(text); out += "\""; }
    else out += num;
    out += "]";
  }
  free(text);
}

static void backupExportSpace(ChunkedResponse& out, const char* space) {
  out += "\"";
  out += space;
  out += "\":{";
  nvs_handle_t h;
  // A namespace nothing was ever written to doesn't exist yet: it goes out empty.
  if (nvs_open(space, NVS_READONLY, &h) == ESP_OK) {
    bool first = true;
    nvs_iterator_t it = nullptr;
    esp_err_t res = nvs_entry_find_in_handle(h, NVS_TYPE_ANY, &it);
    while (res == ESP_OK) {
      nvs_entry_info_t info;
      nvs_entry_info(it, &info);
      backupExportEntry(out, h, info.key, info.type, first);
      res = nvs_entry_next(&it);
    }
    nvs_release_iterator(it);
    nvs_close(h);
  }
  out += "}";
}

// GET /backup?custom=1&conn=1&users=1 - the parts asked for, as one file.
void handleBackupExport() {
  if (!checkAuth()) return;
  bool any = false;
  for (const BackupPart& part : BACKUP_PARTS) any |= server.arg(part.name) == "1";
  if (!any) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"Alegeti ce doriti sa exportati\"}");
    return;
  }
  // A change still waiting for its deferred write belongs in the file too.
  saveSettingsFlush();
  ChunkedResponse out(server);
  out.begin("application/json");
  out += "{\"device\":\"Octoglow\",\"backupFormat\":2,\"firmwareVersion\":\"" FW_VERSION "\",\"parts\":[";
  bool first = true;
  for (const BackupPart& part : BACKUP_PARTS) {
    if (server.arg(part.name) != "1") continue;
    if (!first) out += ",";
    first = false;
    out += "\"";
    out += part.name;
    out += "\"";
  }
  out += "],\"nvs\":{";
  first = true;
  for (const BackupPart& part : BACKUP_PARTS) {
    if (server.arg(part.name) != "1") continue;
    for (const char* space : part.spaces) {
      if (!space) continue;
      if (!first) out += ",";
      first = false;
      backupExportSpace(out, space);
    }
  }
  out += "}}";
  out.end();
}

// Whether this build can write the entry back exactly as it stands.
static bool backupEntryValid(const char* key, JsonVariantConst entry) {
  size_t keyLen = strlen(key);
  if (keyLen == 0 || keyLen >= NVS_KEY_NAME_MAX_SIZE) return false;
  if (!entry.is<JsonArrayConst>() || entry.size() != 2) return false;
  nvs_type_t type;
  if (!backupTypeFromName(entry[0] | "", type)) return false;
  JsonVariantConst v = entry[1];
  switch (type) {
    case NVS_TYPE_U8:  return v.is<uint8_t>();
    case NVS_TYPE_I8:  return v.is<int8_t>();
    case NVS_TYPE_U16: return v.is<uint16_t>();
    case NVS_TYPE_I16: return v.is<int16_t>();
    case NVS_TYPE_U32: return v.is<uint32_t>();
    case NVS_TYPE_I32: return v.is<int32_t>();
    case NVS_TYPE_U64: return v.is<const char*>() && backupIsInteger(v.as<const char*>(), false);
    case NVS_TYPE_I64: return v.is<const char*>() && backupIsInteger(v.as<const char*>(), true);
    case NVS_TYPE_STR: return v.is<const char*>() && strlen(v.as<const char*>()) < BACKUP_STR_MAX;
    case NVS_TYPE_BLOB: return v.is<const char*>() && backupIsHex(v.as<const char*>(), BACKUP_BLOB_MAX * 2);
    default: return false;
  }
}

static bool backupRestoreSpace(const char* space, JsonObjectConst entries) {
  nvs_handle_t h;
  if (nvs_open(space, NVS_READWRITE, &h) != ESP_OK) return false;
  bool ok = nvs_erase_all(h) == ESP_OK;
  for (JsonPairConst kv : entries) {
    const char* key = kv.key().c_str();
    nvs_type_t type;
    backupTypeFromName(kv.value()[0] | "", type);
    JsonVariantConst v = kv.value()[1];
    esp_err_t r = ESP_FAIL;
    switch (type) {
      case NVS_TYPE_U8:  r = nvs_set_u8(h, key, v.as<uint8_t>()); break;
      case NVS_TYPE_I8:  r = nvs_set_i8(h, key, v.as<int8_t>()); break;
      case NVS_TYPE_U16: r = nvs_set_u16(h, key, v.as<uint16_t>()); break;
      case NVS_TYPE_I16: r = nvs_set_i16(h, key, v.as<int16_t>()); break;
      case NVS_TYPE_U32: r = nvs_set_u32(h, key, v.as<uint32_t>()); break;
      case NVS_TYPE_I32: r = nvs_set_i32(h, key, v.as<int32_t>()); break;
      case NVS_TYPE_U64: r = nvs_set_u64(h, key, strtoull(v.as<const char*>(), nullptr, 10)); break;
      case NVS_TYPE_I64: r = nvs_set_i64(h, key, strtoll(v.as<const char*>(), nullptr, 10)); break;
      case NVS_TYPE_STR: r = nvs_set_str(h, key, v.as<const char*>()); break;
      case NVS_TYPE_BLOB: {
        const char* hex = v.as<const char*>();
        size_t len = strlen(hex) / 2;
        uint8_t* bytes = (uint8_t*)malloc(len ? len : 1);
        if (!bytes) break;
        for (size_t i = 0; i < len; i++) {
          char pair[3] = { hex[i * 2], hex[i * 2 + 1], '\0' };
          bytes[i] = (uint8_t)strtoul(pair, nullptr, 16);
        }
        r = nvs_set_blob(h, key, bytes, len);
        free(bytes);
        break;
      }
      default: break;
    }
    if (r != ESP_OK) ok = false;
  }
  if (nvs_commit(h) != ESP_OK) ok = false;
  nvs_close(h);
  return ok;
}

// A format-2 file: everything is checked before anything is written, so a file
// that fails anywhere changes nothing.
static void restoreNvsBackup(JsonObjectConst nvs) {
  int spaces = 0;
  for (const BackupPart& part : BACKUP_PARTS) {
    for (const char* space : part.spaces) {
      if (!space || !nvs[space].is<JsonObjectConst>()) continue;
      spaces++;
      for (JsonPairConst kv : nvs[space].as<JsonObjectConst>()) {
        if (!backupEntryValid(kv.key().c_str(), kv.value())) {
          server.send(400, "application/json", "{\"ok\":false,\"err\":\"Backup-ul contine o valoare invalida\"}");
          return;
        }
      }
    }
  }
  if (spaces == 0) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"Fisierul de backup nu contine setari\"}");
    return;
  }
  // An account without a usable password hash would lock everyone out; an
  // empty one is a clock that never had its account set, and comes back so.
  JsonObjectConst auth = nvs["auth"].as<JsonObjectConst>();
  if (!auth.isNull() && auth.size() > 0) {
    const char* user = auth["user"][1] | "";
    const char* hash = auth["hash"][1] | "";
    bool strings = strcmp(auth["user"][0] | "", "str") == 0 && strcmp(auth["hash"][0] | "", "str") == 0;
    if (!strings || strlen(user) == 0 || strlen(user) > 32 || strlen(hash) != 64 || !backupIsHex(hash, 64)) {
      server.send(400, "application/json", "{\"ok\":false,\"err\":\"Utilizatorul din backup nu este valid\"}");
      return;
    }
  }

  settingsFrozen = true;
  saveSettingsCancel();
  bool ok = true;
  for (const BackupPart& part : BACKUP_PARTS)
    for (const char* space : part.spaces)
      if (space && nvs[space].is<JsonObjectConst>())
        ok = backupRestoreSpace(space, nvs[space].as<JsonObjectConst>()) && ok;

  // A full account + connection backup can complete first-run setup directly.
  // A partial restore leaves the current wizard state unchanged.
  if (ok && nvs["auth"].is<JsonObjectConst>() && nvs["wifi"].is<JsonObjectConst>()) {
    JsonObjectConst restoredAuth = nvs["auth"].as<JsonObjectConst>();
    JsonObjectConst restoredWifi = nvs["wifi"].as<JsonObjectConst>();
    const char* user = restoredAuth["user"][1] | "";
    const char* ssid = restoredWifi["ssid"][1] | "";
    if (strlen(user) > 0 && strlen(ssid) > 0 && !setOobeComplete(true)) ok = false;
  }

  // Restarted either way: what was written is already in NVS.
  if (ok) server.send(200, "application/json", "{\"ok\":true}");
  else server.send(500, "application/json", "{\"ok\":false,\"err\":\"Backup-ul nu a putut fi scris complet\"}");
  delay(500);
  ESP.restart();
}

void handleBackupRestore() {
  if (!checkAuth()) return;
  if (!server.hasArg("plain")) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"Lipseste corpul cererii\"}");
    return;
  }
  DynamicJsonDocument doc(8192);
  DeserializationError err = deserializeJson(doc, server.arg("plain"));
  if (err || !doc.is<JsonObject>()) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"JSON invalid\"}");
    return;
  }
  JsonObject s = doc.as<JsonObject>();
  if (s["nvs"].is<JsonObject>()) {
    restoreNvsBackup(s["nvs"].as<JsonObjectConst>());
    return;
  }

  // A format-1 file: the /state fields the web interface and Octoglow Connect
  // exported before format 2, set on the same globals handleSettings() and
  // loadSettings() use and saved in one go. It never carried the Wi-Fi, the
  // account or the API keys, so those are left as they are.
  if (s.containsKey("buzzer"))        buzzerOn     = s["buzzer"].as<bool>();
  if (s.containsKey("buzzerVolume")) {
    int v = s["buzzerVolume"].as<int>();
    if (v >= 0 && v <= 100) buzzerVolume = buzzVolNotif = buzzVolAuto = buzzVolAlarm = buzzVolTouch = (uint8_t)v;
  }
  // A backup from before the categories carries only the one volume, set above.
  if (s.containsKey("buzzerVolNotif")) { int v = s["buzzerVolNotif"].as<int>(); if (v >= 0 && v <= 100) buzzVolNotif = (uint8_t)v; }
  if (s.containsKey("buzzerVolAuto"))  { int v = s["buzzerVolAuto"].as<int>();  if (v >= 0 && v <= 100) buzzVolAuto  = (uint8_t)v; }
  if (s.containsKey("buzzerVolAlarm")) { int v = s["buzzerVolAlarm"].as<int>(); if (v >= 0 && v <= 100) buzzVolAlarm = (uint8_t)v; }
  if (s.containsKey("buzzerVolTouch")) { int v = s["buzzerVolTouch"].as<int>(); if (v >= 0 && v <= 100) buzzVolTouch = (uint8_t)v; }
  if (s.containsKey("buzzerPreset")) {
    const char* v = s["buzzerPreset"] | "";
    if (strlen(v) > 0) strncpy(buzzerPreset, v, sizeof(buzzerPreset) - 1), buzzerPreset[sizeof(buzzerPreset) - 1] = '\0';
  }

  if (s.containsKey("items") && s["items"].is<JsonArray>()) {
    // /state lists the tiles in the order they are shown, so that order comes
    // back too; a tile the file doesn't know keeps its place after them.
    JsonArray arr = s["items"];
    bool listed[NUM_ITEMS] = {};
    uint8_t position = 0;
    for (JsonObject it : arr) {
      if (!it.containsKey("id")) continue;
      uint8_t id = it["id"].as<uint8_t>();
      for (int i = 0; i < NUM_ITEMS; i++) {
        if (items[i].id == id && !listed[i]) {
          listed[i] = true;
          items[i].order = position++;
          if (it.containsKey("enabled")) items[i].enabled = it["enabled"].as<bool>();
          if (it.containsKey("dur")) {
            int d = it["dur"].as<int>();
            if (d > 0 && d <= 65535) items[i].durationSec = (uint16_t)d;
          }
          break;
        }
      }
    }
    for (int i = 0; i < NUM_ITEMS; i++)
      if (!listed[i]) items[i].order = position++;
    sortItems();
  }

  if (s.containsKey("bright")) {
    int v = s["bright"].as<int>();
    if (v >= 0 && v <= 255) curBrightness = (uint8_t)v;
  }
  if (s.containsKey("dimAuto"))  dimAutoOn = s["dimAuto"].as<bool>();
  if (s.containsKey("dimFrom")) {
    const char* v = s["dimFrom"] | "";
    int h, m;
    if (sscanf(v, "%d:%d", &h, &m) == 2 && h >= 0 && h <= 23 && m >= 0 && m <= 59) { dimFromH = h; dimFromM = m; }
  }
  if (s.containsKey("dimTo")) {
    const char* v = s["dimTo"] | "";
    int h, m;
    if (sscanf(v, "%d:%d", &h, &m) == 2 && h >= 0 && h <= 23 && m >= 0 && m <= 59) { dimToH = h; dimToM = m; }
  }
  if (s.containsKey("dimLevel")) {
    int v = s["dimLevel"].as<int>();
    if (v >= 0 && v <= 255) dimLevel = (uint8_t)v;
  }

  if (s.containsKey("wxPreset"))   { int v = s["wxPreset"].as<int>();   if (v >= 1 && v <= WX_SHOW_ALL) wxPreset = (uint8_t)v; }
  if (s.containsKey("tempunit"))   { int v = s["tempunit"].as<int>();   if (v == 0 || v == 1) tempUnit = (uint8_t)v; }
  if (s.containsKey("hourLeadingZero")) hourLeadingZero = s["hourLeadingZero"].as<bool>();
  if (s.containsKey("netTimeSync")) netTimeSync = s["netTimeSync"].as<bool>();
  if (s.containsKey("showGrayed")) showGrayedContent = s["showGrayed"].as<bool>();
  if (s.containsKey("autoSleep")) autoSleepOn = s["autoSleep"].as<bool>();
  if (s.containsKey("autoSleepSec")) {
    long v = s["autoSleepSec"].as<long>();
    if (v >= 60 && v <= 86400) autoSleepSec = (uint32_t)v;
  }
  if (s.containsKey("liveHl")) liveTileHighlight = s["liveHl"].as<bool>();
  if (s.containsKey("uiDark")) webUiDark = s["uiDark"].as<bool>();
  if (s.containsKey("uiLang")) {
    const char* v = s["uiLang"] | "";
    if (strcmp(v, "en") == 0 || strcmp(v, "ro") == 0) strcpy(webUiLang, v);
  }
  // Restored before the item list below, so applyTileHiddenMask() at the end of
  // this function judges the masks against the items the backup actually holds.
  if (s.containsKey("tileHidden")) {
    long v = s["tileHidden"].as<long>();
    if (v >= 0 && v <= 0xFFFF) tileHiddenMask = (uint16_t)v;
  }
  if (s.containsKey("prioHidden")) {
    long v = s["prioHidden"].as<long>();
    if (v >= 0 && v <= 0x7FFFFFFFL) prioHiddenMask = (uint32_t)v;
  }
  // Spelled out rather than via restoreRange(): that lambda is declared
  // further down this function, well after this line.
  if (s.containsKey("defaultStartMode")) {
    int v = s["defaultStartMode"].as<int>();
    if (v >= START_MODE_WIFI && v <= START_MODE_AP) defaultStartMode = (uint8_t)v;
  }
  if (s.containsKey("hourformat")) { int v = s["hourformat"].as<int>(); if (v == 0 || v == 1) hourFormat = (uint8_t)v; }
  if (s.containsKey("hwFormat"))   { int v = s["hwFormat"].as<int>();   if (v == 0 || v == 1) hwFormat = (uint8_t)v; }
  if (s.containsKey("hwSkin"))     { int v = s["hwSkin"].as<int>();     if (v >= HW_SKIN_COMPACT && v <= HW_SKIN_TEMP) hwSkin = (uint8_t)v; }
  // Old backups have no content field. Compact meant weekday; skin 2 meant sensor.
  if (s.containsKey("hwSkin") && !s.containsKey("hwContent")) hwContent = HW_CONTENT_DAY;
  if (s.containsKey("hwContent"))  { int v = s["hwContent"].as<int>(); if (v >= HW_CONTENT_DAY && v <= HW_CONTENT_WEATHER) hwContent = (uint8_t)v; }
  normalizeWatchfaceSettings();
  if (s.containsKey("hwLeadZero")) hwLeadZero = s["hwLeadZero"].as<bool>();
  if (s.containsKey("hwBarMode"))  { int v = s["hwBarMode"].as<int>();  if (v == 0 || v == 1) hwBarMode = (uint8_t)v; }
  if (s.containsKey("hwBarPos"))   { int v = s["hwBarPos"].as<int>();   if (v == 0 || v == 1) hwBarPos = (uint8_t)v; }
  if (s.containsKey("hwSwap"))     hwSwap = s["hwSwap"].as<bool>();
  if (s.containsKey("dateformat")) { int v = s["dateformat"].as<int>(); if (v >= 0 && v <= 5) dateFormat = (uint8_t)v; }
  if (s.containsKey("datelang"))   { int v = s["datelang"].as<int>();   if (v >= 0) dateLang = (uint8_t)v; }
  if (s.containsKey("customdatefmt")) {
    const char* v = s["customdatefmt"] | "";
    strncpy(customDateFmt, v, sizeof(customDateFmt) - 1); customDateFmt[sizeof(customDateFmt) - 1] = '\0';
  }

  if (s.containsKey("wxCity")) {
    const char* v = s["wxCity"] | "";
    strncpy(weatherCity, v, sizeof(weatherCity) - 1); weatherCity[sizeof(weatherCity) - 1] = '\0';
  }
  if (s.containsKey("wxLang")) {
    const char* v = s["wxLang"] | "";
    strncpy(weatherLang, v, sizeof(weatherLang) - 1); weatherLang[sizeof(weatherLang) - 1] = '\0';
  }
  // Notă: cheia API meteo (weatherApiKey) nu e inclusă în /state din motive
  // de securitate, deci nu poate fi nici restaurată - trebuie reintrodusa manual.

  if (s.containsKey("currencyBase")) {
    const char* v = s["currencyBase"] | "";
    if (strlen(v) > 0 && strlen(v) < sizeof(currencyBase)) strcpy(currencyBase, v);
  }
  if (s.containsKey("currencyQuote")) {
    const char* v = s["currencyQuote"] | "";
    if (strlen(v) > 0 && strlen(v) < sizeof(currencyQuote)) strcpy(currencyQuote, v);
  }
  if (s.containsKey("currencyCompare")) currencyCompareEnabled = s["currencyCompare"].as<bool>();

  if (s.containsKey("volumeEnabled")) volumeEnabled = s["volumeEnabled"].as<bool>();
  if (s.containsKey("volumeAdaptiveIcon")) volumeAdaptiveIcon = s["volumeAdaptiveIcon"].as<bool>();
  if (s.containsKey("hideIconVolume")) hideIconVolume = s["hideIconVolume"].as<bool>();
  if (s.containsKey("iconSelVolume")) { int v = s["iconSelVolume"].as<int>(); if (v >= 0 && v <= ICON_CATALOG_COUNT) iconSelVolume = (uint8_t)v; }
  if (s.containsKey("fontTypeVolume")) { int v = s["fontTypeVolume"].as<int>(); if (fontModeOk(v)) fontTypeVolume = (uint8_t)v; }
  if (s.containsKey("scrollTypeVolume")) { int v = s["scrollTypeVolume"].as<int>(); if (v >= 0 && v <= SCROLL_TYPE_MAX) scrollTypeVolume = (uint8_t)v; }
  if (s.containsKey("tileTransVolume")) { int v = s["tileTransVolume"].as<int>(); if (v >= 0 && v <= 9) tileTransVolume = (uint8_t)v; }
  if (s.containsKey("notifEnabled"))      notifEnabled = s["notifEnabled"].as<bool>();
  if (s.containsKey("webEnabled"))        webAccessEnabled = s["webEnabled"].as<bool>();
  if (s.containsKey("webAlAccess"))       webAlertAccess = s["webAlAccess"].as<bool>();
  if (s.containsKey("webAlLocked"))       webAlertLocked = s["webAlLocked"].as<bool>();
  if (s.containsKey("webAlLogin"))        webAlertLogin  = s["webAlLogin"].as<bool>();
  if (s.containsKey("webAlNet"))          webAlertNet    = s["webAlNet"].as<bool>();
  if (s.containsKey("ets2Enabled"))       ets2Enabled = s["ets2Enabled"].as<bool>();
  if (s.containsKey("voicemeeterEnabled")) voicemeeterEnabled = s["voicemeeterEnabled"].as<bool>();
  if (s.containsKey("browserDownloadsEnabled")) browserDownloadsEnabled = s["browserDownloadsEnabled"].as<bool>();
  if (s.containsKey("bambuEnabled")) bambuEnabled = s["bambuEnabled"].as<bool>();
  if (s.containsKey("obsEnabled"))        obsEnabled = s["obsEnabled"].as<bool>();
  if (s.containsKey("obsShowElapsed"))    obsShowElapsed = s["obsShowElapsed"].as<bool>();
  if (s.containsKey("vmMode")) { int v = s["vmMode"].as<int>(); if (v >= 0 && v <= 4) vmMode = (uint8_t)v; }
  if (s.containsKey("vmSensitivity")) { int v = s["vmSensitivity"].as<int>(); if (v >= 10 && v <= 100) vmSensitivity = (uint8_t)v; }
  if (s.containsKey("ets2OrderFirst"))    ets2OrderFirst = s["ets2OrderFirst"].as<bool>();
  if (s.containsKey("nowPlayingIsPriority")) nowPlayingIsPriority = s["nowPlayingIsPriority"].as<bool>();

  if (s.containsKey("priorityOrder")) {
    // A backup taken before a tile existed leaves it out; it is slotted in the
    // same way a shorter saved order is at start-up.
    uint8_t parsed[NUM_PRIORITY_IDS];
    int count = parsePriorityOrder(s["priorityOrder"].as<String>(), parsed, nullptr);
    if (count > 0) completePriorityOrder(parsed, count, priorityOrder);
  }

  if (s.containsKey("hideIcons"))           hideTileIcons      = s["hideIcons"].as<bool>();
  if (s.containsKey("hideIconDate"))        hideIconDate       = s["hideIconDate"].as<bool>();
  if (s.containsKey("hideIconTemp"))        hideIconTemp       = s["hideIconTemp"].as<bool>();
  if (s.containsKey("hideIconReminder"))    hideIconReminder   = s["hideIconReminder"].as<bool>();
  if (s.containsKey("hideIconWeather"))     hideIconWeather    = s["hideIconWeather"].as<bool>();
  if (s.containsKey("hideIconNotif"))       hideIconNotif      = s["hideIconNotif"].as<bool>();
  if (s.containsKey("hideIconNowPlaying"))  hideIconNowPlaying = s["hideIconNowPlaying"].as<bool>();
  if (s.containsKey("hideIconPressure"))    hideIconPressure   = s["hideIconPressure"].as<bool>();
  if (s.containsKey("hideIconCurrency"))    hideIconCurrency   = s["hideIconCurrency"].as<bool>();
  if (s.containsKey("hideIconYoutube"))     hideIconYoutube    = s["hideIconYoutube"].as<bool>();
  if (s.containsKey("hideIconWebAccess"))   hideIconWebAccess  = s["hideIconWebAccess"].as<bool>();
  if (s.containsKey("hideIconIp"))          hideIconIp         = s["hideIconIp"].as<bool>();
  if (s.containsKey("hideIconVoicemeeter")) hideIconVoicemeeter= s["hideIconVoicemeeter"].as<bool>();
  if (s.containsKey("hideIconBrowserDownloads")) hideIconBrowserDownloads = s["hideIconBrowserDownloads"].as<bool>();
  if (s.containsKey("hideIconObs"))         hideIconObs        = s["hideIconObs"].as<bool>();
  if (s.containsKey("npAdaptiveIcon"))      npAdaptiveIcon     = s["npAdaptiveIcon"].as<bool>();

  auto restoreIcon = [&](const char* key, uint8_t &target) {
    if (s.containsKey(key)) {
      int v = s[key].as<int>();
      if (v >= 0 && v <= (int)ICON_CATALOG_COUNT) target = (uint8_t)v;
    }
  };
  restoreIcon("iconSelDate",     iconSelDate);
  restoreIcon("iconSelTemp",     iconSelTemp);
  restoreIcon("iconSelReminder", iconSelReminder);
  restoreIcon("iconSelNotif",    iconSelNotif);
  restoreIcon("iconSelNpMusic",  iconSelNpMusic);
  restoreIcon("iconSelNpVideo",  iconSelNpVideo);
  restoreIcon("iconSelNpPaused", iconSelNpPaused);
  restoreIcon("iconSelPressure", iconSelPressure);
  restoreIcon("iconSelCurrency", iconSelCurrency);
  restoreIcon("iconSelIp",       iconSelIp);
  restoreIcon("iconSelVoicemeeter", iconSelVoicemeeter);
  restoreIcon("iconSelBrowserDownloads", iconSelBrowserDownloads);
  restoreIcon("iconSelObs",      iconSelObs);
  restoreIcon("iconSelBattery",  iconSelBattery);
  restoreIcon("iconWxSunny",     iconWxSunny);
  restoreIcon("iconWxCloud",     iconWxCloud);
  restoreIcon("iconWxRain",      iconWxRain);
  restoreIcon("iconWxStorm",     iconWxStorm);
  restoreIcon("iconWxSnow",      iconWxSnow);
  restoreIcon("iconWxWind",      iconWxWind);
  restoreIcon("iconWxNight",     iconWxNight);

  auto restoreRange = [&](const char* key, uint8_t &target, int lo, int hi) {
    if (s.containsKey(key)) {
      int v = s[key].as<int>();
      if (v >= lo && v <= hi) target = (uint8_t)v;
    }
  };
  restoreRange("scrollType",          scrollType,           0, 5);
  restoreRange("scrollTypeDate",      scrollTypeDate,       0, 5);
  restoreRange("scrollTypeTemp",      scrollTypeTemp,       0, 5);
  restoreRange("scrollTypeReminder",  scrollTypeReminder,   0, 5);
  restoreRange("scrollTypeWeather",   scrollTypeWeather,    0, 5);
  restoreRange("scrollTypeNotif",     scrollTypeNotif,      0, 5);
  restoreRange("scrollTypeNowPlaying",scrollTypeNowPlaying, 0, 5);
  restoreRange("scrollTypePressure",  scrollTypePressure,   0, 5);
  restoreRange("scrollTypeStopwatch", scrollTypeStopwatch,  0, 5);
  restoreRange("scrollTypeCurrency",  scrollTypeCurrency,   0, 5);
  restoreRange("scrollTypeYoutube",   scrollTypeYoutube,    0, 5);
  restoreRange("scrollTypeWebAccess", scrollTypeWebAccess,  0, 5);
  restoreRange("scrollTypeTimer",     scrollTypeTimer,      0, 5);
  restoreRange("scrollTypeIp",        scrollTypeIp,         0, 5);
  restoreRange("scrollTypeObs",       scrollTypeObs,        0, 5);
  restoreRange("scrollSpeed", scrollSpeed, SCROLL_SPEED_MIN, SCROLL_SPEED_MAX);
  for (int i = 0; i < SCROLL_SPEED_COUNT; i++) {
    String k = String("scrollSpeed") + SPD_KEYS[i];
    if (s.containsKey(k)) {
      int v = s[k].as<int>();
      if (v >= SCROLL_SPEED_MIN && v <= SCROLL_SPEED_MAX) scrollSpeedTile[i] = (uint8_t)v;
    }
  }

  restoreRange("fontType",         fontType,           0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeDate",     fontTypeDate,       0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeTemp",     fontTypeTemp,       0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeReminder", fontTypeReminder,   0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeWeather",  fontTypeWeather,    0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeNotif",    fontTypeNotif,      0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeNowPlaying",fontTypeNowPlaying,0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypePressure", fontTypePressure,   0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeStopwatch",fontTypeStopwatch,  0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeCurrency", fontTypeCurrency,   0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeYoutube",  fontTypeYoutube,    0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeWebAccess", fontTypeWebAccess, 0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeTimer",    fontTypeTimer,      0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeIp",       fontTypeIp,         0, FONT_MODE_COUNT - 1);
  restoreRange("fontTypeObs",      fontTypeObs,        0, FONT_MODE_COUNT - 1);

  restoreRange("tileTransGlobal", tileTransGlobal, 0, 255);
  restoreRange("tileTransHour",   tileTransHour,   0, 255);
  restoreRange("tileTransDate",   tileTransDate,   0, 255);
  restoreRange("tileTransTemp",   tileTransTemp,   0, 255);
  restoreRange("tileTransNp",     tileTransNp,     0, 255);
  restoreRange("tileTransWx",     tileTransWx,     0, 255);
  restoreRange("tileTransRem",    tileTransRem,    0, 255);
  restoreRange("tileTransCanvas", tileTransCanvas, 0, 255);
  restoreRange("tileTransPress",  tileTransPress,  0, 255);
  restoreRange("tileTransSs",     tileTransSs,     0, 255);
  restoreRange("tileTransCurr",   tileTransCurr,   0, 255);
  restoreRange("tileTransYt",     tileTransYt,     0, 255);
  restoreRange("tileTransHw",     tileTransHw,     0, 255);
  restoreRange("tileTransWeb",    tileTransWeb,    0, 255);
  restoreRange("tileTransNotif",  tileTransNotif,  0, 255);
  restoreRange("tileTransEts2",   tileTransEts2,   0, 255);
  restoreRange("tileTransVm",     tileTransVm,     0, 255);
  restoreRange("tileTransBrowserDownloads", tileTransBrowserDownloads, 0, 255);
  restoreRange("tileTransObs",    tileTransObs,    0, 255);
  restoreRange("tileTransSw",     tileTransSw,     0, 255);
  restoreRange("tileTransTmr",    tileTransTmr,    0, 255);
  restoreRange("tileTransAlarm",  tileTransAlarm,  0, 255);
  restoreRange("tileTransIp",     tileTransIp,     0, 255);
  restoreRange("tileTransC2P",    tileTransC2P,    0, 255);
  restoreRange("tileTransP2C",    tileTransP2C,    0, 255);
  restoreRange("tileTransSpd",    tileTransSpeed,  TRSPD_MIN, TRSPD_MAX);
  for (int i = 0; i < TRSPD_COUNT; i++) {
    String k = String("tileTransSpd") + TRSPD_KEYS[i];
    if (s.containsKey(k)) {
      int v = s[k].as<int>();
      if (v >= TRSPD_MIN && v <= TRSPD_MAX) tileTransSpd[i] = (uint8_t)v;
    }
  }

  if (s.containsKey("timerDurationSec")) {
    long v = s["timerDurationSec"].as<long>();
    if (v > 0 && v <= 359999) timerDurationSec = (uint32_t)v;
  }
  if (s.containsKey("timerPreset")) timerPreset = s["timerPreset"].as<uint8_t>();

  // Every field is range-checked on the way back in: a backup is a file the user
  // can edit, and an hour of 40 would be an alarm that never rings again.
  if (s.containsKey("alarms") && s["alarms"].is<JsonArray>()) {
    JsonArray arr = s["alarms"];
    int i = 0;
    for (JsonObject a : arr) {
      if (i >= ALARM_COUNT) break;
      int h = a["h"] | -1, m = a["m"] | -1, d = a["d"] | -1;
      if (h >= 0 && h <= 23)  alarms[i].hour   = (uint8_t)h;
      if (m >= 0 && m <= 59)  alarms[i].minute = (uint8_t)m;
      if (d >= 0 && d <= 127) alarms[i].days   = (uint8_t)d;
      if (a.containsKey("en")) alarms[i].enabled = a["en"].as<bool>();
      // A backup taken before alarms could be added carries no "p"; the alarms
      // in it are the ones its owner had, so they come back.
      alarms[i].present = a.containsKey("p") ? a["p"].as<bool>() : alarms[i].enabled;
      const char* t = a["tone"] | "";
      if (strlen(t) > 0 && strlen(t) < sizeof(alarms[i].tone)) strcpy(alarms[i].tone, t);
      i++;
    }
  }

  if (s.containsKey("mementoText")) {
    const char* v = s["mementoText"] | "";
    strncpy(mementoBuf, v, sizeof(mementoBuf) - 1); mementoBuf[sizeof(mementoBuf) - 1] = '\0';
  }
  if (s.containsKey("canvasBmp")) {
    const char* v = s["canvasBmp"] | "";
    canvasBitmapFromHex(String(v));
  }

  if (s.containsKey("evSndTile"))  { const char* v = s["evSndTile"]  | ""; if (strlen(v)) { strncpy(eventSoundTile,  v, sizeof(eventSoundTile)  - 1); eventSoundTile[sizeof(eventSoundTile) - 1]   = '\0'; } }
  if (s.containsKey("evSndWifi"))  { const char* v = s["evSndWifi"]  | ""; if (strlen(v)) { strncpy(eventSoundWifi,  v, sizeof(eventSoundWifi)  - 1); eventSoundWifi[sizeof(eventSoundWifi) - 1]   = '\0'; } }
  if (s.containsKey("evSndNotif")) { const char* v = s["evSndNotif"] | ""; if (strlen(v)) { strncpy(eventSoundNotif, v, sizeof(eventSoundNotif) - 1); eventSoundNotif[sizeof(eventSoundNotif) - 1] = '\0'; } }
  if (s.containsKey("evSndWeb")) { const char* v = s["evSndWeb"] | ""; if (strlen(v)) { strncpy(eventSoundWeb, v, sizeof(eventSoundWeb) - 1); eventSoundWeb[sizeof(eventSoundWeb) - 1] = '\0'; } }
  if (s.containsKey("evSndEts2"))  { const char* v = s["evSndEts2"]  | ""; if (strlen(v)) { strncpy(eventSoundEts2,  v, sizeof(eventSoundEts2)  - 1); eventSoundEts2[sizeof(eventSoundEts2) - 1]   = '\0'; } }
  if (s.containsKey("evSndTouch")) { const char* v = s["evSndTouch"] | ""; if (strlen(v)) { strncpy(eventSoundTouch, v, sizeof(eventSoundTouch) - 1); eventSoundTouch[sizeof(eventSoundTouch) - 1] = '\0'; } }
  if (s.containsKey("evSndTimer")) { const char* v = s["evSndTimer"] | ""; if (strlen(v)) { strncpy(eventSoundTimer, v, sizeof(eventSoundTimer) - 1); eventSoundTimer[sizeof(eventSoundTimer) - 1] = '\0'; } }

  if (s.containsKey("touchTapAction"))       touchTapAction       = s["touchTapAction"].as<uint8_t>();
  if (s.containsKey("touchDoubleTapAction")) touchDoubleTapAction = s["touchDoubleTapAction"].as<uint8_t>();

  if (s.containsKey("accentColor")) {
    const char* v = s["accentColor"] | "";
    if (strlen(v) >= 4 && strlen(v) < sizeof(accentColor) && v[0] == '#') strcpy(accentColor, v);
  }
  if (s.containsKey("uiShape")) {
    uint8_t v = s["uiShape"].as<uint8_t>();
    if (v <= 3) webUiShape = v;
  }
  if (s.containsKey("uiPalette")) {
    uint8_t v = s["uiPalette"].as<uint8_t>();
    webUiPalette = v <= 100 ? v : 255;
  }
  if (s.containsKey("uiColors")) {
    const char* v = s["uiColors"] | "";
    if (isValidUiColors(String(v))) strcpy(webUiColors, v);
  }
  if (s.containsKey("cdFull")) cdFullUnit = s["cdFull"].as<bool>();
  if (s.containsKey("cdLang")) cdLang = s["cdLang"].as<uint8_t>() ? 1 : 0;
  if (s.containsKey("ssAnim")) {
    ssAnimSelected = s["ssAnim"].as<uint8_t>();
    if (!ssAnimValid(ssAnimSelected)) ssAnimSelected = SS_ANIM_RANDOM;
  }

  // The item list and the masks both came out of the same backup; this is where
  // they are finally judged against each other.
  applyTileHiddenMask();

  saveSettings();

  server.send(200, "application/json", "{\"ok\":true}");
  delay(500);
  ESP.restart();
}

// FACTORY RESET
//
// Wipes every Preferences namespace the project uses ("settings", "wifi",
// "auth", "apcfg") so the device comes back up exactly as it would out of
// the box: no saved WiFi network -> setup() falls through to
// startProvisionMode() on the next boot, and loadAuth()/loadSettings() will
// re-populate their in-memory defaults from the (now empty) namespaces.
static void eraseFactorySettings() {
  const char* namespacesToWipe[] = { "settings", "wifi", "auth", "apcfg", "oobe" };
  // The startup gesture runs on its own task while Wi-Fi is connecting. Do not
  // borrow the shared Preferences object that other startup code may be using.
  Preferences resetPrefs;
  saveSettingsCancel();
  for (size_t i = 0; i < sizeof(namespacesToWipe) / sizeof(namespacesToWipe[0]); i++) {
    resetPrefs.begin(namespacesToWipe[i], false);
    resetPrefs.clear();
    resetPrefs.end();
  }
}

void handleFactoryReset() {
  if (!checkAuth()) return;
  eraseFactorySettings();
  server.send(200, "application/json", "{\"ok\":true}");
  delay(500);
  ESP.restart();
}

// MEMENTO HANDLER
void handleMementoSett() {
  if (!checkAuth()) return;
  if (server.hasArg("text")) {
    String txt = server.arg("text");
    txt.trim();

    if (txt.length() > 120) txt = txt.substring(0, 120);
    txt.toCharArray(mementoBuf, sizeof(mementoBuf));
    saveSettingsDeferred();

    if (items[currentSlot].id == ITEM_MEMENTO) requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste text");
  }
}

// CANVAS HANDLER

void handleCanvasSett() {
  if (!checkAuth()) return;
  if (!server.hasArg("bmp")) {
    server.send(400, "text/plain", "Lipseste bmp");
    return;
  }
  if (!canvasBitmapFromHex(server.arg("bmp"))) {
    server.send(400, "text/plain", "Format bmp invalid");
    return;
  }
  saveSettingsDeferred();

  if (items[currentSlot].id == ITEM_CANVAS) requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  server.send(200, "text/plain", "OK");
}

void handleWeatherSett() {
  if (!checkAuth()) return;
  bool changed = false;
  if (server.hasArg("apikey")) {
    String k = server.arg("apikey");
    k.trim();
    if (k.length() > 0) {
      // Refuse loudly rather than store something that will silently fail every
      // fetch from here on.
      if (!wxKeyLooksValid(k)) {
        server.send(400, "text/plain", "Cheie API invalida");
        return;
      }
      // Another key or place: the reading on hand belongs to the old one, and
      // the tile waits for the new answer instead of showing it.
      if (strcmp(k.c_str(), weatherApiKey) != 0) weatherValid = false;
      k.toCharArray(weatherApiKey, sizeof(weatherApiKey));
      changed = true;
    }
  }
  if (server.hasArg("lat") && server.hasArg("lon") && server.hasArg("name")) {
    float lat = server.arg("lat").toFloat(), lon = server.arg("lon").toFloat();
    if (lat != weatherLat || lon != weatherLon) weatherValid = false;
    weatherLat = lat;
    weatherLon = lon;
    server.arg("name").toCharArray(weatherCity, sizeof(weatherCity));
    changed = true;
  }
  if (server.hasArg("preset")) {
    int p = server.arg("preset").toInt();
    if (p >= 1 && p <= WX_SHOW_ALL) {
      wxPreset = (uint8_t)p;
      changed = true;
      // Redraw straight away if the tile is the one on screen, so the change is
      // visible without waiting for the next fetch or slot.
      if (items[currentSlot].id == ITEM_WEATHER) requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
    }
  }
  if (changed) {
    saveSettingsDeferred();
    lastWeatherFetch = 0;
    // Whatever was wrong was wrong with the old key or place.
    weatherErr   = API_ERR_NONE;
    weatherFails = 0;
  }
  server.send(200, "text/plain", "OK");
}

String urlEncode(const String& s) {
  String enc = "";
  for (int i = 0; i < (int)s.length(); i++) {
    char c = s[i];
    if (isalnum(c) || c=='-' || c=='_' || c=='.' || c=='~') {
      enc += c;
    } else if (c == ' ') {
      enc += '+';
    } else {
      char buf[4];
      snprintf(buf, sizeof(buf), "%%%02X", (unsigned char)c);
      enc += buf;
    }
  }
  return enc;
}

void handleWeatherSearch() {
  if (!checkAuth()) return;
  if (!server.hasArg("q")) {
    server.send(400, "text/plain", "Lipseste parametrul q");
    return;
  }
  String q   = server.arg("q");
  String key = server.arg("key");

  if (key.length() == 0 && strlen(weatherApiKey) > 0) {
    key = String(weatherApiKey);
  }
  if (key.length() == 0) {
    server.send(400, "text/plain", "Lipseste cheia API");
    return;
  }
  if (WiFi.status() != WL_CONNECTED) {
    server.send(503, "text/plain", "No WiFi");
    return;
  }
  String url = "http://api.openweathermap.org/geo/1.0/direct?q=" +
               urlEncode(q) + "&limit=5&appid=" + urlEncode(key);
  Serial.println("[WxSearch] q=" + q + " keyLen=" + String(key.length()));
  HTTPClient http;
  http.begin(url);
  http.setTimeout(8000);
  int code = http.GET();
  Serial.println("[WxSearch] HTTP code: " + String(code));
  if (code == 200) {
    String body = http.getString();
    server.send(200, "application/json", body);
  } else if (code == 401) {
    server.send(401, "text/plain", "Cheie API invalida");
  } else if (code <= 0) {
    server.send(503, "text/plain", "Eroare retea: " + String(code));
  } else {
    server.send(502, "text/plain", "OWM error " + String(code));
  }
  http.end();
}

void handleWxLang() {
  if (!checkAuth()) return;
  if (server.hasArg("lang")) {
    String l = server.arg("lang");
    l.trim();
    if (l.length() >= 2 && l.length() <= 7) {
      l.toCharArray(weatherLang, sizeof(weatherLang));
      saveSettingsDeferred();
      lastWeatherFetch = 0;
      server.send(200, "text/plain", "OK");
      return;
    }
  }
  server.send(400, "text/plain", "Parametru invalid");
}

void handleWeatherState() {
  if (!checkAuth()) return;
  String json = "{\"valid\":" + String(weatherValid ? "true" : "false") +
                ",\"city\":\"" + String(weatherCity) + "\"" +
                ",\"lat\":" + String(weatherLat, 4) +
                ",\"lon\":" + String(weatherLon, 4) +
                ",\"hasKey\":" + String(strlen(weatherApiKey) > 0 ? "true" : "false") + ",\"keyLen\":" + String(strlen(weatherApiKey)) +
                ",\"lang\":\"" + String(weatherLang) + "\"" +
                ",\"temp\":" + String(weatherTempC, 1) +
                ",\"humidity\":" + String(weatherHumidity) +
                ",\"desc\":\"" + String(weatherDesc) + "\"}";
  server.send(200, "application/json", json);
}

void handleWeatherKey() {
  if (!checkAuth()) return;

  String json = "{\"key\":\"" + String(weatherApiKey) + "\"}";
  server.send(200, "application/json", json);
}

// CURRENCY STANDARDS HANDLERS


// Settings for one social counter tile. The key is only overwritten when a new
// one is actually supplied, so re-saving the dialog without retyping it does
// not wipe a stored key.
void handleSocialKey() {
  if (!checkAuth()) return;
  int i = server.hasArg("i") ? server.arg("i").toInt() : 0;
  if (i < 0 || i >= SOCIAL_COUNT) {
    server.send(400, "text/plain", "Index invalid");
    return;
  }
  server.send(200, "application/json",
              String("{\"key\":\"") + jsonEscape(String(social[i].apiKey)) + "\"}");
}

void handleSocialSett() {
  if (!checkAuth()) return;
  if (!server.hasArg("i")) {
    server.send(400, "text/plain", "Lipseste i");
    return;
  }
  int i = server.arg("i").toInt();
  if (i < 0 || i >= SOCIAL_COUNT) {
    server.send(400, "text/plain", "Index invalid");
    return;
  }
  if (server.hasArg("handle")) {
    String h = server.arg("handle");
    h.trim();
    strncpy(social[i].handle, h.c_str(), sizeof(social[i].handle) - 1);
    social[i].handle[sizeof(social[i].handle) - 1] = '\0';
  }
  if (server.hasArg("key")) {
    String k = server.arg("key");
    k.trim();
    if (k.length() > 0) {
      strncpy(social[i].apiKey, k.c_str(), sizeof(social[i].apiKey) - 1);
      social[i].apiKey[sizeof(social[i].apiKey) - 1] = '\0';
    }
  }
  if (server.hasArg("clearkey") && server.arg("clearkey") == "1") social[i].apiKey[0] = '\0';
  if (server.hasArg("showname")) social[i].showName = (server.arg("showname") == "1");

  saveSettingsDeferred();
  social[i].valid    = false;
  social[i].err      = API_ERR_NONE;
  social[i].fails    = 0;
  lastSocialFetch[i] = 0;          // refetch on the next loop pass
  server.send(200, "text/plain", "OK");
}

void handleCurrencySett() {
  if (!checkAuth()) return;
  bool changed = false;
  if (server.hasArg("base")) {
    String b = server.arg("base");
    b.trim(); b.toUpperCase();
    if (b.length() == 3) { b.toCharArray(currencyBase, sizeof(currencyBase)); changed = true; }
  }
  if (server.hasArg("compare")) {
    bool c = (server.arg("compare") == "1");
    if (c != currencyCompareEnabled) { currencyCompareEnabled = c; changed = true; }
  }
  if (server.hasArg("quote")) {
    String q = server.arg("quote");
    q.trim(); q.toUpperCase();
    if (q.length() == 3) { q.toCharArray(currencyQuote, sizeof(currencyQuote)); changed = true; }
  }
  if (changed) {
    saveSettingsDeferred();
    lastCurrencyFetch = 0;
    currencyValid = false;
    currencyErr   = API_ERR_NONE;
    currencyFails = 0;

    if (items[currentSlot].id == ITEM_CURRENCY) requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  }
  server.send(200, "text/plain", "OK");
}

void handleCurrencyState() {
  if (!checkAuth()) return;
  String json = "{\"base\":\"" + jsonEscape(String(currencyBase)) + "\"" +
                ",\"quote\":\"" + jsonEscape(String(currencyQuote)) + "\"" +
                ",\"compare\":" + String(currencyCompareEnabled ? "true" : "false") +
                ",\"valid\":" + String(currencyValid ? "true" : "false") +
                ",\"rate\":" + String((currencyValid && !isnan(currencyRateNow)) ? currencyRateNow : 0.0f, 4) +
                ",\"trend\":" + String(currencyTrend) + "}";
  server.send(200, "application/json", json);
}

// WHAT COUNTS AS ACTIVITY
//
// Both of these are Octoglow Connect talking to the clock, which is the whole
// reason sleep keeps the network up: something arriving is a reason to light
// the panel again.
// Whether the panel is showing Now Playing at this instant. The tile loop
// leaves the marker behind on every pass, as a circuit tile or as a priority
// tile, so the answer is already worked out.
static bool nowPlayingOnScreen() {
  return gLiveTileId == ITEM_NOW_PLAYING || strcmp(gLivePrio, "nowplaying") == 0;
}

void handleNowPlaying() {
  if (!checkAuth()) return;

  // Playback stopped, said as it happens rather than left to the timeout. It is
  // the one thing arriving here that is not a reason to light the panel, so
  // noteActivity() waits until the update below.
  if (server.arg("state") == "stopped") {
    if (nowPlayingActive) npStopRequested = true;
    server.send(200, "text/plain", "OK");
    return;
  }

  noteActivity();
  bool updated = false;
  // "paused" is playback held while Octoglow Connect keeps the tile up. Anything
  // else, including saying nothing, is playing.
  bool wasPaused = npIsPaused;
  npIsPaused = server.arg("state") == "paused";
  // Kept to tell a new song from the same one arriving again.
  char previousText[sizeof(nowPlayingBuf)];
  snprintf(previousText, sizeof(previousText), "%s", nowPlayingBuf);
  bool wasActive = nowPlayingActive;
  bool wasVideo  = npIsVideoSource;

  // Optional playback-source classification from Octoglow Connect: "video"
  // or "music" (anything else / missing => treat as music/unknown).
  if (server.hasArg("kind")) {
    npIsVideoSource = (server.arg("kind") == "video");
  }

  if (server.hasArg("artist") && server.hasArg("title")) {
    server.arg("artist").toCharArray(nowArtist, sizeof(nowArtist));
    server.arg("title").toCharArray(nowTitle,  sizeof(nowTitle));
    rebuildNowPlayingBuf();
    updated = true;
  } else if (server.hasArg("text")) {

    String txt = server.arg("text");
    int sep = txt.indexOf(" - ");
    if (sep >= 0) {
      txt.substring(0, sep).toCharArray(nowArtist, sizeof(nowArtist));
      txt.substring(sep + 3).toCharArray(nowTitle,  sizeof(nowTitle));
    } else {
      txt.toCharArray(nowArtist, sizeof(nowArtist));
      nowTitle[0] = '\0';
    }
    rebuildNowPlayingBuf();
    updated = true;
  }
  if (updated) {
    npStopRequested = false;
    lastNowPlayingMs = millis();
    nowPlayingActive = true;
    // A different song takes the panel straight away instead of waiting for the
    // old one to finish crossing it. The same text arriving again is the
    // keep-alive, which must leave the scroll where it is.
    bool changed = strcmp(previousText, nowPlayingBuf) != 0 || npIsVideoSource != wasVideo || npIsPaused != wasPaused;
    if (wasActive && changed && nowPlayingOnScreen()) {
      requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
    }
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste artist/title sau text");
  }
}

void handleNpState() {
  if (!checkAuth()) return;
  int batteryPct;
  bool hasBattery;
  unsigned long batteryUpdated;
  portENTER_CRITICAL(&batteryMux);
  batteryPct = batteryPercent;
  hasBattery = batteryPresent;
  batteryUpdated = batteryLastMs;
  portEXIT_CRITICAL(&batteryMux);
  const bool batteryValid = hasBattery && batteryPct >= 0 && batteryUpdated != 0 && millis() - batteryUpdated < BATTERY_STATUS_TIMEOUT_MS;
  String json = "{\"active\":" + String(nowPlayingActive ? "true" : "false") +
                ",\"text\":\"" + jsonEscape(String(nowPlayingBuf)) + "\"" +
                ",\"npmode\":" + String(npDisplayMode) +
                ",\"hourformat\":" + String(hourFormat) +
                ",\"dateformat\":" + String(dateFormat) +
                ",\"lastTemp\":" + String(lastTemp) +
                ",\"batteryValid\":" + String(batteryValid ? "true" : "false") +
                ",\"batteryPercent\":" + String(batteryValid ? batteryPct : -1) +
                ",\"wxValid\":" + String(weatherValid ? "true" : "false") +
                ",\"wxTemp\":" + String(weatherTempC, 1) +
                ",\"wxHumidity\":" + String(weatherHumidity) +
                ",\"wxDesc\":\"" + String(weatherDesc) + "\"" +
                // Why an online tile has nothing to show (API_ERR_*), for the
                // Tile Manager's lines: this is what it polls while open.
                ",\"wxErr\":" + String(weatherErrCode()) +
                ",\"soc0Err\":" + String(socialErrCode(SOC_YT)) +
                ",\"soc0Valid\":" + String(social[SOC_YT].valid ? "true" : "false") +
                ",\"soc0Count\":" + String(social[SOC_YT].count) +
                ",\"currencyErr\":" + String(currencyErr) + "}";
  server.send(200, "application/json", json);
}

// SCREEN SAVER HANDLER
void handleScreensaverSett() {
  if (!checkAuth()) return;
  if (server.hasArg("anim")) {
    int a = server.arg("anim").toInt();
    if (ssAnimValid(a)) {
      ssAnimSelected = (uint8_t)a;
      saveSettingsDeferred();

      if (items[currentSlot].id == ITEM_SCREENSAVER) requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
    }
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste anim");
  }
}

void handleNpMode() {
  if (!checkAuth()) return;
  if (server.hasArg("mode")) {
    uint8_t m = (uint8_t)server.arg("mode").toInt();
    if (m <= 2) {
      npDisplayMode = m;
      rebuildNowPlayingBuf();
      saveSettingsDeferred();

      if (items[currentSlot].id == ITEM_NOW_PLAYING ||
          (nowPlayingIsPriority && nowPlayingActive)) {
        requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
      }
    }
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste mode");
  }
}

// POST /countdownsett n0..n4, d0..d4 (YYYY-MM-DD), y0..y4 (1 = every year),
// i0..i4 (the event's icon, 0 = the calendar): the whole list each time, as
// the dialog holds it. A place with no name or no date is left empty; a date
// that doesn't exist is refused. A list sent without icons (an older Octoglow
// Connect) keeps the icon each event had, found by its name.
void handleCountdownSett() {
  if (!checkAuth()) return;
  CountdownEvent next[CD_MAX_EVENTS];
  memset(next, 0, sizeof(next));
  int n = 0;
  for (int i = 0; i < CD_MAX_EVENTS; i++) {
    String name = server.arg("n" + String(i));
    name.trim();
    String date = server.arg("d" + String(i));
    int y = 0, m = 0, d = 0;
    if (name.length() == 0 || sscanf(date.c_str(), "%d-%d-%d", &y, &m, &d) != 3) continue;
    if (y < 1900 || y > 2199 || m < 1 || m > 12 || d < 1 || d > cdMonthDays(y, m)) {
      server.send(400, "application/json", "{\"ok\":false,\"err\":\"Data invalida\"}");
      return;
    }
    CountdownEvent& e = next[n++];
    e.year  = (uint16_t)y;
    e.month = (uint8_t)m;
    e.day   = (uint8_t)d;
    e.flags = CD_USED | (server.arg("y" + String(i)) == "1" ? CD_YEARLY : 0);
    // Cut at a whole character: a UTF-8 sequence is never split.
    size_t len = name.length();
    if (len > CD_NAME_MAX - 1) {
      len = CD_NAME_MAX - 1;
      while (len > 0 && ((uint8_t)name[len] & 0xC0) == 0x80) len--;
    }
    memcpy(e.name, name.c_str(), len);
    e.name[len] = '\0';
    String iconArg = "i" + String(i);
    if (server.hasArg(iconArg)) {
      int ic = server.arg(iconArg).toInt();
      e.icon = (ic > 0 && ic <= (int)ICON_CATALOG_COUNT) ? (uint8_t)ic : 0;
    } else {
      for (const CountdownEvent& old : cdEvents) {
        if ((old.flags & CD_USED) && strcmp(old.name, e.name) == 0) { e.icon = old.icon; break; }
      }
    }
  }
  memcpy(cdEvents, next, sizeof(cdEvents));
  saveSettingsDeferred();
  if (items[currentSlot].id == ITEM_COUNTDOWN) requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  // What the tile says now, for the Tile Manager's line.
  char t[CD_NAME_MAX + 24] = "";
  countdownText(t, sizeof(t));
  String j = "{\"ok\":true,\"text\":\"";
  for (const char* c = t; *c; c++) {
    if (*c == '"' || *c == '\\') j += '\\';
    if ((uint8_t)*c >= 0x20) j += *c;
  }
  j += "\"}";
  server.send(200, "application/json", j);
}

// POST /countdownopt - full=0|1 (the unit written out), lang=0|1 (Romanian,
// English): how the Events tile words what it says. Either may be left out.
void handleCountdownOpt() {
  if (!checkAuth()) return;
  if (server.hasArg("full")) cdFullUnit = server.arg("full") == "1";
  if (server.hasArg("lang")) cdLang = server.arg("lang") == "1" ? 1 : 0;
  saveSettingsDeferred();
  if (items[currentSlot].id == ITEM_COUNTDOWN) requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  char t[CD_NAME_MAX + 24] = "";
  countdownText(t, sizeof(t));
  String j = "{\"ok\":true,\"text\":\"";
  for (const char* c = t; *c; c++) {
    if (*c == '"' || *c == '\\') j += '\\';
    if ((uint8_t)*c >= 0x20) j += *c;
  }
  j += "\"}";
  server.send(200, "application/json", j);
}

// NOTIFICATION HANDLERS

void handleNotification() {
  noteActivity();
  if (!checkAuth()) return;
  if (!notifEnabled) {
    server.send(200, "text/plain", "DISABLED");
    return;
  }

  if (higherPriorityTileActive(PRIORITY_ID_NOTIF)) {
    server.send(200, "text/plain", "SUPPRESSED");
    return;
  }
  if (server.hasArg("text")) {
    String txt = server.arg("text");
    txt.trim();
    if (txt.length() == 0) {
      server.send(400, "text/plain", "Text gol");
      return;
    }

    if (txt.length() > 200) {
      txt = txt.substring(0, 200) + "...";
    }
    txt.toCharArray(notifBuf, sizeof(notifBuf));
    notifActive = true;
    requestDisplayOp(DISPLAY_OP_START_NOTIF);
    nbPlayPreset(eventSoundNotif, BZ_CAT_NOTIF);
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste text");
  }
}

// POST /lowbatterytoggle enabled=0|1 - the alert's switch in the Tile Manager.
// Switching it off also ends an alert that is on the panel.
void handleLowBatteryToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  lowBatteryEnabled = server.arg("enabled") == "1";
  if (!lowBatteryEnabled) {
    portENTER_CRITICAL(&batteryMux);
    batteryAlertUntilMs = 0;
    portEXIT_CRITICAL(&batteryMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// Octoglow Connect is the source of PC battery data. A stale or missing host
// reports no reading; the clock never invents a charge percentage.
void handleBatteryStatus() {
  if (!checkAuth()) return;
  if (!server.hasArg("pct") || !server.hasArg("present") || !server.hasArg("ac")) {
    server.send(400, "text/plain", "Missing battery status");
    return;
  }
  int pct = server.arg("pct").toInt();
  if (pct < -1 || pct > 100) {
    server.send(400, "text/plain", "Invalid battery percentage");
    return;
  }
  bool present = server.arg("present") == "1";
  bool ac = server.arg("ac") == "1";
  bool charging = server.arg("charging") == "1";
  bool changed;
  bool justTriggered = false;
  unsigned long now = millis();
  portENTER_CRITICAL(&batteryMux);
  // A reading may have expired while its percentage stayed unchanged. The
  // first fresh report must replace "--" immediately, not wait for a different
  // percentage or for the tile to cycle away and back.
  bool wasStale = batteryLastMs == 0 || now - batteryLastMs >= BATTERY_STATUS_TIMEOUT_MS;
  changed = wasStale || batteryPercent != pct || batteryPresent != present || batteryAc != ac || batteryCharging != charging;
  batteryPercent = (int8_t)pct;
  batteryPresent = present;
  batteryAc = ac;
  batteryCharging = charging;
  batteryLastMs = now | 1;
  if (!present || pct < 0 || ac || charging) {
    batteryAlertArmed = true;
    batteryAlertUntilMs = 0;
  } else if (pct > BATTERY_LOW_THRESHOLD + 3) {
    batteryAlertArmed = true;
  } else if (pct <= BATTERY_LOW_THRESHOLD && batteryAlertArmed) {
    batteryAlertArmed = false;
    batteryAlertUntilMs = now + 10000UL;
    justTriggered = true;
  }
  portEXIT_CRITICAL(&batteryMux);
  if (justTriggered && lowBatteryAllowed()) noteActivity();
  if (changed && (items[currentSlot].id == ITEM_BATTERY || strcmp(gLivePrio, "lowbattery") == 0))
    requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  server.send(200, "text/plain", "OK");
}

void handleWebToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  webAccessEnabled = (server.arg("enabled") == "1");
  if (!webAccessEnabled && webAccessAlertActive) {
    // Switching it off while its own alert is on screen clears it, rather than
    // leaving the message stranded until it finishes scrolling.
    notifActive = false;
    notifIconOverride = nullptr;
    webAccessAlertActive = false;
    notifBuf[0] = '\0';
    requestDisplayOp(DISPLAY_OP_STOP_NOTIF);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// POST /websett - access=0|1, locked=0|1, login=0|1, net=0|1: which of its
// alerts the Alerts tile shows. Any may be left out.
void handleWebSett() {
  if (!checkAuth()) return;
  if (server.hasArg("access")) webAlertAccess = server.arg("access") == "1";
  if (server.hasArg("locked")) webAlertLocked = server.arg("locked") == "1";
  if (server.hasArg("login"))  webAlertLogin  = server.arg("login") == "1";
  if (server.hasArg("net"))    webAlertNet    = server.arg("net") == "1";
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleNotifToggle() {
  if (!checkAuth()) return;
  if (server.hasArg("enabled")) {
    notifEnabled = (server.arg("enabled") == "1");
    if (!notifEnabled && notifActive) {

      notifActive = false;
      notifIconOverride = nullptr;
      webAccessAlertActive = false;
      notifBuf[0] = '\0';
      requestDisplayOp(DISPLAY_OP_STOP_NOTIF);
    }
    saveSettingsDeferred();
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste enabled");
  }
}

void handleNotifState() {
  if (!checkAuth()) return;
  String json = "{\"enabled\":" + String(notifEnabled ? "true" : "false") +
                ",\"active\":"  + String(notifActive  ? "true" : "false") + "}";
  server.send(200, "application/json", json);
}

// ETS2 HANDLER

void handleEts2Speed() {
  if (!checkAuth()) return;
  if (!ets2Enabled) {
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (server.hasArg("speed")) {
    int spd = server.arg("speed").toInt();
    if (spd < 0) spd = 0;
    if (spd > 999) spd = 999;
    ets2Speed = spd;
    lastEts2Ms = millis();
    if (!ets2Active) {
      ets2Active = true;
      requestDisplayOp(DISPLAY_OP_START_ETS2);
    }

    if (spd > ETS2_SPEED_LIMIT) {
      if (!ets2SpeedAlertFired) {
        ets2SpeedAlertFired = true;
        nbPlayPreset(eventSoundEts2, BZ_CAT_AUTO);
      }
    } else {
      ets2SpeedAlertFired = false;
    }
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste speed");
  }
}

void handleEts2Toggle() {
  if (!checkAuth()) return;
  if (server.hasArg("enabled")) {
    ets2Enabled = (server.arg("enabled") == "1");
    if (!ets2Enabled && ets2Active) {

      ets2Active = false;
      ets2Speed = 0;
      ets2LastSpeed = -1;
      requestDisplayOp(DISPLAY_OP_STOP_ETS2);
    }
    saveSettingsDeferred();
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste enabled");
  }
}

void handleEts2State() {
  if (!checkAuth()) return;
  // speed stays km/h, as it always was; shown is what the panel draws, in the
  // unit mph says.
  String json = "{\"enabled\":" + String(ets2Enabled ? "true" : "false") +
                ",\"active\":"  + String(ets2Active  ? "true" : "false") +
                ",\"speed\":"   + String(ets2Speed) +
                ",\"mph\":"     + String(ets2SpeedMph ? "true" : "false") +
                ",\"shown\":"   + String(speedShown(ets2Speed, ets2SpeedMph)) + "}";
  server.send(200, "application/json", json);
}

// BEAMNG.DRIVE HANDLERS

// POST /beamngspeed - from Octoglow Connect, out of BeamNG.drive's OutGauge:
//   speed=<km/h>   whole km/h; puts the tile up, or keeps it up
//   active=0       the packets stopped (or the vehicle stands still): down at once
void handleBeamngSpeed() {
  if (!checkAuth()) return;
  if (!beamngEnabled) {
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (server.arg("active") == "0") {
    portENTER_CRITICAL(&beamngMux);
    beamngActive = false;
    portEXIT_CRITICAL(&beamngMux);
    server.send(200, "text/plain", "OK");
    return;
  }
  if (!server.hasArg("speed")) {
    server.send(400, "text/plain", "Lipseste speed");
    return;
  }
  int spd = server.arg("speed").toInt();
  if (spd < 0) spd = 0;
  if (spd > 999) spd = 999;
  portENTER_CRITICAL(&beamngMux);
  beamngSpeedKmh = spd;
  beamngLastMs = millis();
  beamngActive = true;
  portEXIT_CRITICAL(&beamngMux);
  noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleBeamngToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  beamngEnabled = server.arg("enabled") == "1";
  if (!beamngEnabled) {
    portENTER_CRITICAL(&beamngMux);
    beamngActive = false;
    portEXIT_CRITICAL(&beamngMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// GET /beamngstate - the small state the Tile Manager line and Connect read.
void handleBeamngState() {
  if (!checkAuth()) return;
  bool active = beamngLive();
  int kmh = beamngSpeedNow();
  String json = "{\"enabled\":" + String(beamngEnabled ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"speed\":" + String(active ? kmh : 0) +
                ",\"mph\":" + String(beamngSpeedMph ? "true" : "false") +
                ",\"shown\":" + String(active ? speedShown(kmh, beamngSpeedMph) : 0) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// ASSETTO CORSA HANDLERS

// Whether Connect has anything to read the game for: the tile is switched on
// and in the Tile Manager. With either missing it leaves the game alone.
static bool assettoWanted() {
  return assettoEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_ASSETTO));
}

// POST /assettospeed - from Octoglow Connect, out of Assetto Corsa's shared memory:
//   speed=<km/h>   whole km/h; puts the tile up, or keeps it up
//   active=0       the game paused, left the session or closed (or the car
//                  stands still, if Connect is set so): down at once
// DISABLED while the tile is off or out of the Tile Manager, so Connect stops.
void handleAssettoSpeed() {
  if (!checkAuth()) return;
  if (!assettoWanted()) {
    portENTER_CRITICAL(&assettoMux);
    assettoActive = false;
    portEXIT_CRITICAL(&assettoMux);
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (server.arg("active") == "0") {
    portENTER_CRITICAL(&assettoMux);
    assettoActive = false;
    portEXIT_CRITICAL(&assettoMux);
    server.send(200, "text/plain", "OK");
    return;
  }
  if (!server.hasArg("speed")) {
    server.send(400, "text/plain", "Lipseste speed");
    return;
  }
  int spd = server.arg("speed").toInt();
  if (spd < 0) spd = 0;
  if (spd > 999) spd = 999;
  portENTER_CRITICAL(&assettoMux);
  assettoSpeedKmh = spd;
  assettoLastMs = millis();
  assettoActive = true;
  portEXIT_CRITICAL(&assettoMux);
  noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleAssettoToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  assettoEnabled = server.arg("enabled") == "1";
  if (!assettoEnabled) {
    portENTER_CRITICAL(&assettoMux);
    assettoActive = false;
    portEXIT_CRITICAL(&assettoMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// GET /assettostate - the small state the Tile Manager line and Connect read.
// "wanted" is what Connect goes by: false, and it reads nothing from the game.
void handleAssettoState() {
  if (!checkAuth()) return;
  bool active = assettoLive();
  int kmh = assettoSpeedNow();
  String json = "{\"enabled\":" + String(assettoEnabled ? "true" : "false") +
                ",\"wanted\":" + String(assettoWanted() ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"speed\":" + String(active ? kmh : 0) +
                ",\"mph\":" + String(assettoSpeedMph ? "true" : "false") +
                ",\"shown\":" + String(active ? speedShown(kmh, assettoSpeedMph) : 0) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// NEED FOR SPEED UNDERGROUND 2 HANDLERS

// Whether Connect has anything to read the game for: the tile is switched on
// and in the Tile Manager. With either missing it leaves the game alone.
static bool nfsu2Wanted() {
  return nfsu2Enabled && !(prioHiddenMask & (1u << PRIORITY_ID_NFSU2));
}

// POST /nfsu2speed - from Octoglow Connect, out of SPEED2.EXE's memory:
//   speed=<km/h>&mph=<mph>   whole numbers, each from the game's metres per
//                            second; puts the tile up, or keeps it up
//   active=0                 the race paused or ended, the game closed (or the
//                            car stands still, if Connect is set so): down at once
// DISABLED while the tile is off or out of the Tile Manager, so Connect stops.
void handleNfsu2Speed() {
  if (!checkAuth()) return;
  if (!nfsu2Wanted()) {
    portENTER_CRITICAL(&nfsu2Mux);
    nfsu2Active = false;
    portEXIT_CRITICAL(&nfsu2Mux);
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (server.arg("active") == "0") {
    portENTER_CRITICAL(&nfsu2Mux);
    nfsu2Active = false;
    portEXIT_CRITICAL(&nfsu2Mux);
    server.send(200, "text/plain", "OK");
    return;
  }
  if (!server.hasArg("speed")) {
    server.send(400, "text/plain", "Lipseste speed");
    return;
  }
  int spd = constrain((int)server.arg("speed").toInt(), 0, 999);
  int mph = server.hasArg("mph") ? constrain((int)server.arg("mph").toInt(), 0, 999) : -1;
  portENTER_CRITICAL(&nfsu2Mux);
  nfsu2SpeedKmh = spd;
  nfsu2SpeedMphValue = mph;
  nfsu2LastMs = millis();
  nfsu2Active = true;
  portEXIT_CRITICAL(&nfsu2Mux);
  noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleNfsu2Toggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  nfsu2Enabled = server.arg("enabled") == "1";
  if (!nfsu2Enabled) {
    portENTER_CRITICAL(&nfsu2Mux);
    nfsu2Active = false;
    portEXIT_CRITICAL(&nfsu2Mux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// GET /nfsu2state - the small state the Tile Manager line and Connect read.
// "wanted" is what Connect goes by: false, and it reads nothing from the game.
void handleNfsu2State() {
  if (!checkAuth()) return;
  bool active = nfsu2Live();
  int kmh, mphValue;
  nfsu2SpeedNow(kmh, mphValue);
  String json = "{\"enabled\":" + String(nfsu2Enabled ? "true" : "false") +
                ",\"wanted\":" + String(nfsu2Wanted() ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"speed\":" + String(active ? kmh : 0) +
                ",\"mph\":" + String(nfsu2SpeedMph ? "true" : "false") +
                ",\"shown\":" + String(active ? nfsu2Shown(kmh, mphValue) : 0) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// NEED FOR SPEED MOST WANTED HANDLERS

// Whether Connect has anything to read the game for: the tile is switched on
// and in the Tile Manager. With either missing it leaves the game alone.
static bool nfsmwWanted() {
  return nfsmwEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_NFSMW));
}

// POST /nfsmwspeed - from Octoglow Connect, out of speed.exe's memory:
//   speed=<km/h>&mph=<mph>   whole numbers, each from the game's metres per
//                            second; puts the tile up, or keeps it up
//   active=0                 the race paused or ended, the game closed (or the
//                            car stands still, if Connect is set so): down at once
// DISABLED while the tile is off or out of the Tile Manager, so Connect stops.
void handleNfsmwSpeed() {
  if (!checkAuth()) return;
  if (!nfsmwWanted()) {
    portENTER_CRITICAL(&nfsmwMux);
    nfsmwActive = false;
    portEXIT_CRITICAL(&nfsmwMux);
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (server.arg("active") == "0") {
    portENTER_CRITICAL(&nfsmwMux);
    nfsmwActive = false;
    portEXIT_CRITICAL(&nfsmwMux);
    server.send(200, "text/plain", "OK");
    return;
  }
  if (!server.hasArg("speed")) {
    server.send(400, "text/plain", "Lipseste speed");
    return;
  }
  int spd = constrain((int)server.arg("speed").toInt(), 0, 999);
  int mph = server.hasArg("mph") ? constrain((int)server.arg("mph").toInt(), 0, 999) : -1;
  portENTER_CRITICAL(&nfsmwMux);
  nfsmwSpeedKmh = spd;
  nfsmwSpeedMphValue = mph;
  nfsmwLastMs = millis();
  nfsmwActive = true;
  portEXIT_CRITICAL(&nfsmwMux);
  noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleNfsmwToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  nfsmwEnabled = server.arg("enabled") == "1";
  if (!nfsmwEnabled) {
    portENTER_CRITICAL(&nfsmwMux);
    nfsmwActive = false;
    portEXIT_CRITICAL(&nfsmwMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// GET /nfsmwstate - the small state the Tile Manager line and Connect read.
// "wanted" is what Connect goes by: false, and it reads nothing from the game.
void handleNfsmwState() {
  if (!checkAuth()) return;
  bool active = nfsmwLive();
  int kmh, mphValue;
  nfsmwSpeedNow(kmh, mphValue);
  String json = "{\"enabled\":" + String(nfsmwEnabled ? "true" : "false") +
                ",\"wanted\":" + String(nfsmwWanted() ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"speed\":" + String(active ? kmh : 0) +
                ",\"mph\":" + String(nfsmwSpeedMph ? "true" : "false") +
                ",\"shown\":" + String(active ? nfsmwShown(kmh, mphValue) : 0) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// NEED FOR SPEED CARBON HANDLERS

// Whether Connect has anything to read the game for: the tile is switched on
// and in the Tile Manager. With either missing it leaves the game alone.
static bool nfscWanted() {
  return nfscEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_NFSC));
}

// POST /nfscspeed - from Octoglow Connect, out of NFSC.exe's memory:
//   speed=<km/h>&mph=<mph>   whole numbers, each from the game's metres per
//                            second; puts the tile up, or keeps it up
//   active=0                 the race paused or ended, the game closed (or the
//                            car stands still, if Connect is set so): down at once
// DISABLED while the tile is off or out of the Tile Manager, so Connect stops.
void handleNfscSpeed() {
  if (!checkAuth()) return;
  if (!nfscWanted()) {
    portENTER_CRITICAL(&nfscMux);
    nfscActive = false;
    portEXIT_CRITICAL(&nfscMux);
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (server.arg("active") == "0") {
    portENTER_CRITICAL(&nfscMux);
    nfscActive = false;
    portEXIT_CRITICAL(&nfscMux);
    server.send(200, "text/plain", "OK");
    return;
  }
  if (!server.hasArg("speed")) {
    server.send(400, "text/plain", "Lipseste speed");
    return;
  }
  int spd = constrain((int)server.arg("speed").toInt(), 0, 999);
  int mph = server.hasArg("mph") ? constrain((int)server.arg("mph").toInt(), 0, 999) : -1;
  portENTER_CRITICAL(&nfscMux);
  nfscSpeedKmh = spd;
  nfscSpeedMphValue = mph;
  nfscLastMs = millis();
  nfscActive = true;
  portEXIT_CRITICAL(&nfscMux);
  noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleNfscToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  nfscEnabled = server.arg("enabled") == "1";
  if (!nfscEnabled) {
    portENTER_CRITICAL(&nfscMux);
    nfscActive = false;
    portEXIT_CRITICAL(&nfscMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// GET /nfscstate - the small state the Tile Manager line and Connect read.
// "wanted" is what Connect goes by: false, and it reads nothing from the game.
void handleNfscState() {
  if (!checkAuth()) return;
  bool active = nfscLive();
  int kmh, mphValue;
  nfscSpeedNow(kmh, mphValue);
  String json = "{\"enabled\":" + String(nfscEnabled ? "true" : "false") +
                ",\"wanted\":" + String(nfscWanted() ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"speed\":" + String(active ? kmh : 0) +
                ",\"mph\":" + String(nfscSpeedMph ? "true" : "false") +
                ",\"shown\":" + String(active ? nfscShown(kmh, mphValue) : 0) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// POST /speedunit - how each speed tile is drawn: ets2Mph / beamngMph /
// assettoMph = 0|1 for the unit, ets2Gauge / beamngGauge / assettoGauge = 0-3
// for the plain number or a gauge (SPEED_GAUGE_*), ets2GaugeFull /
// beamngGaugeFull / assettoGaugeFull = 40-500 for the km/h at the end of the
// gauge's scale. Any of them, in one request or apart.
// Redrawn at once if one of the tiles is on the panel.
void handleSpeedUnit() {
  if (!checkAuth()) return;
  static const char* const KEYS[] = { "ets2Mph", "beamngMph", "assettoMph", "nfsu2Mph", "nfsmwMph", "nfscMph",
                                      "ets2Gauge", "beamngGauge", "assettoGauge", "nfsu2Gauge", "nfsmwGauge",
                                      "nfscGauge", "ets2GaugeFull", "beamngGaugeFull", "assettoGaugeFull",
                                      "nfsu2GaugeFull", "nfsmwGaugeFull", "nfscGaugeFull" };
  bool any = false;
  for (const char* key : KEYS) any = any || server.hasArg(key);
  if (!any) {
    server.send(400, "text/plain", "Lipseste unitatea, stilul sau capatul scalei (ets2*, beamng*, assetto*, nfsu2*, nfsmw*, nfsc*)");
    return;
  }
  if (server.hasArg("ets2Mph")) ets2SpeedMph = server.arg("ets2Mph") == "1";
  if (server.hasArg("beamngMph")) beamngSpeedMph = server.arg("beamngMph") == "1";
  if (server.hasArg("assettoMph")) assettoSpeedMph = server.arg("assettoMph") == "1";
  if (server.hasArg("nfsu2Mph")) nfsu2SpeedMph = server.arg("nfsu2Mph") == "1";
  if (server.hasArg("nfsmwMph")) nfsmwSpeedMph = server.arg("nfsmwMph") == "1";
  if (server.hasArg("nfscMph")) nfscSpeedMph = server.arg("nfscMph") == "1";
  if (server.hasArg("ets2Gauge")) ets2Gauge = constrain(server.arg("ets2Gauge").toInt(), 0, SPEED_GAUGE_COUNT - 1);
  if (server.hasArg("beamngGauge")) beamngGauge = constrain(server.arg("beamngGauge").toInt(), 0, SPEED_GAUGE_COUNT - 1);
  if (server.hasArg("assettoGauge")) assettoGauge = constrain(server.arg("assettoGauge").toInt(), 0, SPEED_GAUGE_COUNT - 1);
  if (server.hasArg("nfsu2Gauge")) nfsu2Gauge = constrain(server.arg("nfsu2Gauge").toInt(), 0, SPEED_GAUGE_COUNT - 1);
  if (server.hasArg("nfsmwGauge")) nfsmwGauge = constrain(server.arg("nfsmwGauge").toInt(), 0, SPEED_GAUGE_COUNT - 1);
  if (server.hasArg("nfscGauge")) nfscGauge = constrain(server.arg("nfscGauge").toInt(), 0, SPEED_GAUGE_COUNT - 1);
  if (server.hasArg("ets2GaugeFull"))
    ets2GaugeFull = constrain(server.arg("ets2GaugeFull").toInt(), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  if (server.hasArg("beamngGaugeFull"))
    beamngGaugeFull = constrain(server.arg("beamngGaugeFull").toInt(), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  if (server.hasArg("assettoGaugeFull"))
    assettoGaugeFull = constrain(server.arg("assettoGaugeFull").toInt(), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  if (server.hasArg("nfsu2GaugeFull"))
    nfsu2GaugeFull = constrain(server.arg("nfsu2GaugeFull").toInt(), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  if (server.hasArg("nfsmwGaugeFull"))
    nfsmwGaugeFull = constrain(server.arg("nfsmwGaugeFull").toInt(), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  if (server.hasArg("nfscGaugeFull"))
    nfscGaugeFull = constrain(server.arg("nfscGaugeFull").toInt(), SPEED_GAUGE_FULL_MIN, SPEED_GAUGE_FULL_MAX);
  saveSettingsDeferred();
  requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  server.send(200, "text/plain", "OK");
}

void handleVoicemeeterToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  voicemeeterEnabled = server.arg("enabled") == "1";
  if (!voicemeeterEnabled) {
    portENTER_CRITICAL(&vmMux);
    vmActive = false;
    vmFrameDirty = true;
    portEXIT_CRITICAL(&vmMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleVoicemeeterState() {
  if (!checkAuth()) return;
  bool active;
  portENTER_CRITICAL(&vmMux);
  active = vmActive;
  portEXIT_CRITICAL(&vmMux);
  String json = "{\"enabled\":" + String(voicemeeterEnabled ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"mode\":" + String(vmMode) +
                ",\"sensitivity\":" + String(vmSensitivity) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

void handleBrowserDownload() {
  if (!checkAuth()) return;
  if (!browserDownloadsEnabled) {
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (!server.hasArg("active")) {
    server.send(400, "text/plain", "Lipseste active");
    return;
  }
  bool active = server.arg("active") == "1";
  int progress = server.hasArg("progress") ? server.arg("progress").toInt() : 0;
  int count = server.hasArg("count") ? server.arg("count").toInt() : 0;
  progress = constrain(progress, 0, 100);
  count = constrain(count, 0, 1000);
  portENTER_CRITICAL(&browserDownloadMux);
  browserDownloadActive = active;
  browserDownloadProgress = (uint8_t)progress;
  browserDownloadCount = (uint16_t)count;
  browserDownloadLastMs = millis();
  browserDownloadDirty = true;
  portEXIT_CRITICAL(&browserDownloadMux);
  if (active) noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleBrowserDownloadToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  browserDownloadsEnabled = server.arg("enabled") == "1";
  if (!browserDownloadsEnabled) {
    portENTER_CRITICAL(&browserDownloadMux);
    browserDownloadActive = false;
    browserDownloadDirty = true;
    portEXIT_CRITICAL(&browserDownloadMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleBrowserDownloadState() {
  if (!checkAuth()) return;
  bool active;
  uint8_t progress;
  uint16_t count;
  portENTER_CRITICAL(&browserDownloadMux);
  active = browserDownloadActive;
  progress = browserDownloadProgress;
  count = browserDownloadCount;
  portEXIT_CRITICAL(&browserDownloadMux);
  String json = "{\"enabled\":" + String(browserDownloadsEnabled ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"progress\":" + String(progress) +
                ",\"count\":" + String(count) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// QBITTORRENT HANDLERS

// Whether Connect has anything to ask qBittorrent for: the tile is switched on
// and in the Tile Manager. With either missing Connect leaves qBittorrent alone.
static bool qbitWanted() {
  return qbitEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_QBIT));
}

// POST /qbitstatus - from Octoglow Connect, out of qBittorrent's WebUI API:
//   active=1&progress=<0-100>  the chosen torrent's percent; puts the tile up
//                              or keeps it up
//   active=0                   nothing downloading: down at once
// DISABLED while the tile is off or out of the Tile Manager, so Connect stops.
void handleQbitStatus() {
  if (!checkAuth()) return;
  if (!qbitWanted()) {
    portENTER_CRITICAL(&qbitMux);
    qbitActive = false;
    portEXIT_CRITICAL(&qbitMux);
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (!server.hasArg("active")) {
    server.send(400, "text/plain", "Lipseste active");
    return;
  }
  bool active = server.arg("active") == "1";
  int progress = server.hasArg("progress") ? server.arg("progress").toInt() : 0;
  progress = constrain(progress, 0, 100);
  portENTER_CRITICAL(&qbitMux);
  // Redrawn only when what the panel shows changes, not at every keepalive.
  if (active && (!qbitActive || qbitProgress != (uint8_t)progress)) qbitDirty = true;
  qbitActive = active;
  qbitProgress = active ? (uint8_t)progress : 0;
  qbitLastMs = millis();
  portEXIT_CRITICAL(&qbitMux);
  if (active) noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleQbitToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  qbitEnabled = server.arg("enabled") == "1";
  if (!qbitEnabled) {
    portENTER_CRITICAL(&qbitMux);
    qbitActive = false;
    portEXIT_CRITICAL(&qbitMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// GET /qbitstate - the small state the Tile Manager line and Connect read.
// "wanted" is what Connect goes by: false, and it asks qBittorrent nothing.
void handleQbitState() {
  if (!checkAuth()) return;
  bool active = qbitLive();
  uint8_t progress;
  portENTER_CRITICAL(&qbitMux);
  progress = qbitProgress;
  portEXIT_CRITICAL(&qbitMux);
  String json = "{\"enabled\":" + String(qbitEnabled ? "true" : "false") +
                ",\"wanted\":" + String(qbitWanted() ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"progress\":" + String(active ? progress : 0) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// MSI AFTERBURNER HANDLERS
// The circuit tile is wanted while it is in the Tile Manager and switched on.
static bool abTileWanted() {
  if (!tileInManager(ITEM_AFTERBURNER)) return false;
  for (int i = 0; i < NUM_ITEMS; i++)
    if (items[i].id == ITEM_AFTERBURNER) return items[i].enabled;
  return false;
}

// Whether Octoglow Connect should read MSI Afterburner and RTSS at all: for
// the circuit tile, the Game FPS priority tile, or both.
static bool abWanted() {
  return abTileWanted() || gameFpsWanted();
}

// One whole number from the post, or AB_NONE when it is left out or not one.
static int16_t abArg(const char* name, long lo, long hi) {
  if (!server.hasArg(name)) return AB_NONE;
  String s = server.arg(name);
  if (s.length() == 0 || s.length() > 6) return AB_NONE;
  for (unsigned i = 0; i < s.length(); i++) {
    if (!isDigit(s[i]) && !(i == 0 && s[i] == '-' && s.length() > 1)) return AB_NONE;
  }
  long v = s.toInt();
  return (v < lo || v > hi) ? AB_NONE : (int16_t)v;
}

// POST /abstatus - from Octoglow Connect, out of MSI Afterburner's and RTSS's
// shared memory on the PC:
//   fps=<0-9999>        the frame rate RTSS measures for the 3D application in
//                       the foreground
//   cpu=<tenths of C>   MSI Afterburner's CPU temperature
//   gpu=<tenths of C>   MSI Afterburner's temperature of the chosen GPU
//   game=1              the frame rate is a game's, played in front (Connect
//                       decides: Windows lists it as a game, or it fills the
//                       screen); the Game FPS tile is up only then
// Each post carries everything available at that moment; a value left out is
// unavailable, so an empty post takes the tiles off at once. Readings alone do
// not count as activity - they arrive all day and would keep the clock from
// sleeping - but a game being played does while the Game FPS tile is wanted.
// DISABLED while neither tile is on and in the Tile Manager, so Connect stops.
void handleAbStatus() {
  if (!checkAuth()) return;
  if (!abWanted()) {
    portENTER_CRITICAL(&abMux);
    abFps = AB_NONE;
    abCpuDeci = AB_NONE;
    abGpuDeci = AB_NONE;
    abFpsGame = false;
    portEXIT_CRITICAL(&abMux);
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  int16_t fps = abArg("fps", 0, 9999);
  int16_t cpu = abArg("cpu", -500, 1500);
  int16_t gpu = abArg("gpu", -500, 1500);
  bool game = fps != AB_NONE && server.hasArg("game") && server.arg("game") == "1";
  portENTER_CRITICAL(&abMux);
  abFps = fps;
  abCpuDeci = cpu;
  abGpuDeci = gpu;
  abFpsGame = game;
  // Never ahead of millis(): 0 means nothing has come in yet, so a post made
  // in the very first millisecond counts as 1.
  abLastMs = millis();
  if (abLastMs == 0) abLastMs = 1;
  portEXIT_CRITICAL(&abMux);
  if (game && gameFpsWanted()) noteActivity();
  server.send(200, "text/plain", "OK");
}

// POST /abmode - mode=0 the frame rate, 1 the CPU temperature, 2 the GPU's.
// The loop redraws the tile if it is up, or moves on if the new value is not
// available.
void handleAbMode() {
  if (!checkAuth()) return;
  if (!server.hasArg("mode")) {
    server.send(400, "text/plain", "Lipseste mode");
    return;
  }
  int m = server.arg("mode").toInt();
  if (m < AB_MODE_FPS || m > AB_MODE_GPU) {
    server.send(400, "text/plain", "Mod invalid");
    return;
  }
  abMode = (uint8_t)m;
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// GET /abstate - what the Tile Manager lines and Connect read. "wanted" is
// what Connect goes by: false, and it reads nothing. Temperatures are in tenths
// of a degree Celsius; "value" is what the circuit tile shows, in the unit it
// shows. "gameLive" is whether the Game FPS tile has a game's frame rate.
void handleAbState() {
  if (!checkAuth()) return;
  int16_t fps, cpu, gpu;
  bool game;
  unsigned long last;
  portENTER_CRITICAL(&abMux);
  fps = abFps;
  cpu = abCpuDeci;
  gpu = abGpuDeci;
  game = abFpsGame;
  last = abLastMs;
  portEXIT_CRITICAL(&abMux);
  bool fresh = last != 0 && millis() - last < AB_TIMEOUT_MS;
  int shown = 0;
  bool live = abShownValue(shown);
  String json = "{\"wanted\":";
  json += abWanted() ? "true" : "false";
  json += ",\"mode\":";
  json += abMode;
  json += ",\"fahrenheit\":";
  json += tempUnit == 1 ? "true" : "false";
  json += ",\"live\":";
  json += live ? "true" : "false";
  json += ",\"value\":";
  json += live ? String(shown) : String("null");
  json += ",\"fps\":";
  json += (fresh && fps != AB_NONE) ? String(fps) : String("null");
  json += ",\"cpu\":";
  json += (fresh && cpu != AB_NONE) ? String(cpu) : String("null");
  json += ",\"gpu\":";
  json += (fresh && gpu != AB_NONE) ? String(gpu) : String("null");
  json += ",\"game\":";
  json += (fresh && fps != AB_NONE && game) ? "true" : "false";
  json += ",\"tileWanted\":";
  json += abTileWanted() ? "true" : "false";
  json += ",\"volumeAdaptiveIcon\":";
  json += volumeAdaptiveIcon ? "true" : "false";
  json += ",\"gameFpsEnabled\":";
  json += gameFpsEnabled ? "true" : "false";
  json += ",\"volumeEnabled\":";
  json += volumeEnabled ? "true" : "false";
  json += ",\"gameFpsWanted\":";
  json += gameFpsWanted() ? "true" : "false";
  json += ",\"gameLive\":";
  json += gameFpsLive() ? "true" : "false";
  json += "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// Authenticated, transient desktop events: no flash writes for volume changes.
void handleVolumeStatus() {
  if (!checkAuth()) return;
  if (!server.hasArg("pct") || !server.hasArg("mute") || !server.hasArg("source")) {
    server.send(400, "text/plain", "Missing volume fields"); return;
  }
  String pct = server.arg("pct");
  bool valid = pct.length() > 0 && pct.length() <= 3;
  for (unsigned i=0; i<pct.length(); ++i) valid = valid && pct[i] >= '0' && pct[i] <= '9';
  int percent = pct.toInt();
  String mute = server.arg("mute"), source = server.arg("source");
  if (!valid || percent > 100 || (mute != "0" && mute != "1") ||
      (source != "0" && source != "1" && source != "2")) {
    server.send(400, "text/plain", "Invalid volume fields"); return;
  }
  if (volumeEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_VOLUME))) {
    int packed = percent | (mute == "1" ? 128 : 0) | (source.toInt() << 8);
    portENTER_CRITICAL(&volumeMux);
    volumePacked = packed;
    volumeLastMs = millis();
    volumeReceived = true;
    portEXIT_CRITICAL(&volumeMux);
  }
  server.send(200, "text/plain", "OK");
}
void handleVolumeToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled") || (server.arg("enabled") != "0" && server.arg("enabled") != "1")) {
    server.send(400, "text/plain", "Invalid enabled"); return;
  }
  volumeEnabled = server.arg("enabled") == "1";
  portENTER_CRITICAL(&volumeMux);
  volumeReceived = false;
  portEXIT_CRITICAL(&volumeMux);
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// POST /gamefpstoggle - enabled=1|0, the Game FPS tile's switch. Its values
// are the Afterburner tile's, so switching it off clears nothing they share.
void handleGameFpsToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  gameFpsEnabled = server.arg("enabled") == "1";
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// BAMBU PRINT HANDLERS
static bool bambuWanted() {
  return bambuEnabled && !(prioHiddenMask & (1u << PRIORITY_ID_BAMBU));
}

// Only the PC knows the printer credentials. The clock receives no names or secrets.
void handleBambuStatus() {
  if (!checkAuth()) return;
  if (!bambuWanted()) {
    portENTER_CRITICAL(&bambuMux);
    bambuActive = false;
    portEXIT_CRITICAL(&bambuMux);
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (!server.hasArg("active")) { server.send(400, "text/plain", "Missing active"); return; }
  bool active = server.arg("active") == "1";
  uint8_t progress = (uint8_t)constrain(server.arg("progress").toInt(), 0, 100);
  uint8_t kind = (uint8_t)constrain(server.arg("kind").toInt(), 0, 2);
  portENTER_CRITICAL(&bambuMux);
  if (active && (!bambuActive || bambuProgress != progress || bambuKind != kind)) bambuDirty = true;
  bambuActive = active;
  bambuProgress = active ? progress : 0;
  bambuKind = kind;
  bambuLastMs = millis();
  portEXIT_CRITICAL(&bambuMux);
  if (active) noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleBambuToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) { server.send(400, "text/plain", "Missing enabled"); return; }
  bambuEnabled = server.arg("enabled") == "1";
  if (!bambuEnabled) {
    portENTER_CRITICAL(&bambuMux);
    bambuActive = false;
    portEXIT_CRITICAL(&bambuMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleBambuState() {
  if (!checkAuth()) return;
  bool active = bambuLive();
  uint8_t progress, kind;
  portENTER_CRITICAL(&bambuMux);
  progress = bambuProgress;
  kind = bambuKind;
  portEXIT_CRITICAL(&bambuMux);
  String json = "{\"enabled\":" + String(bambuEnabled ? "true" : "false") +
                ",\"wanted\":" + String(bambuWanted() ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"progress\":" + String(active ? progress : 0) +
                ",\"kind\":" + String(kind) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

// POST /barstyle - how the progress tiles draw their bar: bdBarStyle (Browser
// Downloads), qbitBarStyle, bambuBarStyle = 0 Progress, 1 Underline Progress,
// 2 Underline Progress (Cutted Edges). Any of them, in one request or apart;
// redrawn at once if one of the tiles is on the panel.
void handleBarStyle() {
  if (!checkAuth()) return;
  if (!server.hasArg("bdBarStyle") && !server.hasArg("qbitBarStyle") && !server.hasArg("bambuBarStyle")) {
    server.send(400, "text/plain", "Lipseste stilul (bdBarStyle, qbitBarStyle, bambuBarStyle)");
    return;
  }
  if (server.hasArg("bdBarStyle"))
    barStyleBrowserDownloads = constrain(server.arg("bdBarStyle").toInt(), 0, BAR_STYLE_COUNT - 1);
  if (server.hasArg("qbitBarStyle"))
    barStyleQbit = constrain(server.arg("qbitBarStyle").toInt(), 0, BAR_STYLE_COUNT - 1);
  if (server.hasArg("bambuBarStyle"))
    barStyleBambu = constrain(server.arg("bambuBarStyle").toInt(), 0, BAR_STYLE_COUNT - 1);
  saveSettingsDeferred();
  requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  server.send(200, "text/plain", "OK");
}

// OBS STUDIO HANDLERS

// POST /obsstatus - what OBS is doing, from Octoglow Connect:
//   active=0|1     OBS records or streams; 0 takes the tile down at once
//   streaming=0|1  a live stream is running
//   recording=0|1  a recording is running
//   elapsed=<ms>   milliseconds the shown output has run, as OBS reports it in
//                  outputDuration: the stream's while streaming, else the
//                  recording's
// Connect repeats it at least every two seconds while OBS is busy; silence for
// OBS_TIMEOUT_MS takes the tile down too.
void handleObsStatus() {
  if (!checkAuth()) return;
  if (!obsEnabled) {
    server.send(200, "text/plain", "DISABLED");
    return;
  }
  if (!server.hasArg("active")) {
    server.send(400, "text/plain", "Lipseste active");
    return;
  }
  bool streaming = server.arg("streaming") == "1";
  bool recording = server.arg("recording") == "1";
  bool active = server.arg("active") == "1" && (streaming || recording);
  long elapsed = server.hasArg("elapsed") ? server.arg("elapsed").toInt() : 0;
  if (elapsed < 0) elapsed = 0;
  portENTER_CRITICAL(&obsMux);
  if (active != obsActive || streaming != obsStreaming || recording != obsRecording ||
      (uint32_t)elapsed != obsElapsedMs) obsDirty = true;
  obsActive = active;
  obsStreaming = streaming;
  obsRecording = recording;
  obsElapsedMs = (uint32_t)elapsed;
  obsLastMs = millis();
  portEXIT_CRITICAL(&obsMux);
  if (active) noteActivity();
  server.send(200, "text/plain", "OK");
}

void handleObsToggle() {
  if (!checkAuth()) return;
  if (!server.hasArg("enabled")) {
    server.send(400, "text/plain", "Lipseste enabled");
    return;
  }
  obsEnabled = server.arg("enabled") == "1";
  if (!obsEnabled) {
    portENTER_CRITICAL(&obsMux);
    obsActive = false;
    obsDirty = true;
    portEXIT_CRITICAL(&obsMux);
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// POST /obssett - showElapsed=0|1, the one OBS setting the clock keeps.
void handleObsSett() {
  if (!checkAuth()) return;
  if (!server.hasArg("showElapsed")) {
    server.send(400, "text/plain", "Lipseste showElapsed");
    return;
  }
  obsShowElapsed = server.arg("showElapsed") == "1";
  portENTER_CRITICAL(&obsMux);
  obsDirty = true;
  portEXIT_CRITICAL(&obsMux);
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

// GET /obsstate - the small state the Tile Manager line and Connect read.
void handleObsState() {
  if (!checkAuth()) return;
  bool active = obsLive();
  bool streaming, recording;
  uint32_t elapsedMs;
  portENTER_CRITICAL(&obsMux);
  streaming = obsStreaming;
  recording = obsRecording;
  elapsedMs = obsElapsedMs;
  portEXIT_CRITICAL(&obsMux);
  String json = "{\"enabled\":" + String(obsEnabled ? "true" : "false") +
                ",\"showElapsed\":" + String(obsShowElapsed ? "true" : "false") +
                ",\"active\":" + String(active ? "true" : "false") +
                ",\"streaming\":" + String(active && streaming ? "true" : "false") +
                ",\"recording\":" + String(active && recording ? "true" : "false") +
                ",\"elapsedMs\":" + String(active ? elapsedMs : 0UL) + "}";
  server.sendHeader("Cache-Control", "no-store");
  server.send(200, "application/json", json);
}

void handleEts2Order() {
  if (!checkAuth()) return;
  if (server.hasArg("first")) {
    ets2OrderFirst = (server.arg("first") == "1");
    saveSettingsDeferred();
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste first");
  }
}

// STOPWATCH HANDLERS

void handleSwStart() {
  if (!checkAuth()) return;
  swRunning   = true;
  swStartMs   = millis();
  swElapsedMs = 0;
  server.send(200, "text/plain", "OK");
}

void handleSwStop() {
  if (!checkAuth()) return;
  if (swRunning) {
    swElapsedMs = millis() - swStartMs;
    swRunning   = false;
    swWasActive = false;

    requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  }
  char txt[16];
  swFormatFull(txt, sizeof(txt), swElapsedMs);
  String json = "{\"elapsedMs\":" + String(swElapsedMs) + ",\"text\":\"" + String(txt) + "\"}";
  server.send(200, "application/json", json);
}

void handleSwState() {
  if (!checkAuth()) return;
  unsigned long ms = swGetElapsedMs();
  char txt[16];
  swFormatFull(txt, sizeof(txt), ms);
  String json = "{\"running\":" + String(swRunning ? "true" : "false") +
                ",\"elapsedMs\":" + String(ms) +
                ",\"text\":\"" + String(txt) + "\"}";
  server.send(200, "application/json", json);
}

// TIMER HANDLERS

void handleTimerSett() {
  if (!checkAuth()) return;
  if (server.hasArg("durationSec")) {
    long d = server.arg("durationSec").toInt();
    if (d > 0 && d <= 359999 && !timerRunning && !timerFinished) {
      timerDurationSec = (uint32_t)d;
      if (timerMode == TIMER_MODE_TIMER) timerRemainingSnapMs = (unsigned long)timerDurationSec * 1000UL;
    }
  }
  if (server.hasArg("preset")) {
    uint8_t p = (uint8_t)server.arg("preset").toInt();
    if (p <= 4) timerPreset = p;
  }
  if (server.hasArg("scrollType")) {
    uint8_t st = (uint8_t)server.arg("scrollType").toInt();
    if (st <= SCROLL_TYPE_MAX) scrollTypeTimer = st;
  }
  // The mode and the Pomodoro's lengths change only while it is stopped (the
  // interface locks them while it runs), and any change starts the cycle over.
  if (!timerRunning && !timerFinished) {
    bool pomoChanged = false;
    if (server.hasArg("mode")) {
      uint8_t m = server.arg("mode") == "1" ? TIMER_MODE_POMODORO : TIMER_MODE_TIMER;
      if (m != timerMode) { timerMode = m; pomoChanged = true; }
    }
    struct { const char* arg; uint8_t* v; long lo, hi; } POMO[] = {
      { "pomoWork", &pomoWorkMin, 1, 120 }, { "pomoShort", &pomoShortMin, 1, 60 },
      { "pomoLong", &pomoLongMin, 1, 60 },  { "pomoRounds", &pomoRounds, 2, 8 },
    };
    for (auto& f : POMO) {
      if (!server.hasArg(f.arg)) continue;
      long v = server.arg(f.arg).toInt();
      if (v >= f.lo && v <= f.hi && (uint8_t)v != *f.v) { *f.v = (uint8_t)v; pomoChanged = true; }
    }
    if (server.arg("reset") == "1") pomoChanged = true;
    if (pomoChanged) {
      if (timerMode == TIMER_MODE_POMODORO) pomoReset();
      else timerRemainingSnapMs = (unsigned long)timerDurationSec * 1000UL;
    }
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}


void handleTimerStart() {
  if (!checkAuth()) return;
  timerStart();
  server.send(200, "text/plain", "OK");
}


void handleTimerPause() {
  if (!checkAuth()) return;
  if (timerFinished) {
    timerDismiss();
  } else {
    timerPause();
  }
  timerWasActive = false;
  server.send(200, "text/plain", "OK");
}

// ALARM HANDLERS

void handleAlarmSett() {
  if (!checkAuth()) return;
  if (!server.hasArg("idx")) { server.send(400, "text/plain", "Lipseste indexul alarmei"); return; }
  int idx = server.arg("idx").toInt();
  if (idx < 0 || idx >= ALARM_COUNT) { server.send(400, "text/plain", "Index invalid"); return; }
  AlarmCfg& a = alarms[idx];
  bool wasEnabled = a.enabled;
  bool timeChanged = false;

  if (server.hasArg("hour")) {
    int v = server.arg("hour").toInt();
    if (v < 0 || v > 23) { server.send(400, "text/plain", "Ora invalida"); return; }
    if ((uint8_t)v != a.hour) timeChanged = true;
    a.hour = (uint8_t)v;
  }
  if (server.hasArg("minute")) {
    int v = server.arg("minute").toInt();
    if (v < 0 || v > 59) { server.send(400, "text/plain", "Minut invalid"); return; }
    if ((uint8_t)v != a.minute) timeChanged = true;
    a.minute = (uint8_t)v;
  }
  if (server.hasArg("days")) {
    int v = server.arg("days").toInt();
    if (v < 0 || v > 127) { server.send(400, "text/plain", "Zile invalide"); return; }
    if ((uint8_t)v != a.days) timeChanged = true;
    a.days = (uint8_t)v;
  }
  if (server.hasArg("enabled")) a.enabled = (server.arg("enabled") == "1");
  if (server.hasArg("present")) a.present = (server.arg("present") == "1");
  if (server.hasArg("tone")) {
    String t = server.arg("tone");
    if (t.length() > 0 && t.length() < sizeof(a.tone)) t.toCharArray(a.tone, sizeof(a.tone));
  }

  // The latch has no meaning once the time it was latched against has changed,
  // and none either for an alarm that was off. Setting it when the new time is
  // the minute it already is happens on purpose: entering 07:30 while it is
  // 07:30 should not set the thing off under your fingers. It rings next time.
  if (timeChanged || (!wasEnabled && a.enabled)) {
    // A newly added alarm has no history either; the same suppression applies,
    // so adding one at 07:30 while it is 07:30 does not go off under your hand.
    alarmFired[idx] = false;
    struct tm ti;
    if (readLocalTime(ti) && ti.tm_hour == a.hour && ti.tm_min == a.minute &&
        (a.days & ALARM_WDAY_BIT(ti.tm_wday))) {
      alarmFired[idx] = true;
    }
  }
  // Switching off the alarm that is ringing stops it, and so does deleting it.
  // Changing its tone or its days does not: it is still the alarm that woke you.
  if (alarmRinging && alarmRingingIdx == idx && (!a.enabled || !a.present)) alarmStopRinging();

  // Alarm tones run for seconds rather than the fraction of a second an event
  // beep does, so there is a way to hear one before committing to being woken
  // by it. Never while an alarm is actually ringing - that queue is spoken for.
  if (server.arg("preview") == "1" && buzzerOn && !alarmRinging) {
    nbStop();
    alarmEnqueueTone(a.tone);
  }

  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleAlarmStop() {
  if (!checkAuth()) return;
  alarmStopRinging();
  server.send(200, "text/plain", "OK");
}

// Polled while the Tile Manager is open, so it stays small. The countdown is
// computed here rather than in the browser because the alarm fires on this
// clock, and the phone looking at the page may not be on the same one.
void handleAlarmState() {
  if (!checkAuth()) return;
  int nextIdx = -1;
  int mins = alarmMinutesUntilNext(&nextIdx);
  String j = "{\"ringing\":";
  j += (alarmRinging ? "true" : "false");
  j += ",\"idx\":";
  j += String((int)alarmRingingIdx);
  j += ",\"nextMin\":";
  j += String(mins);
  j += ",\"nextIdx\":";
  j += String(nextIdx);
  j += ",\"nextTime\":\"";
  if (nextIdx >= 0) {
    char t[8];
    snprintf(t, sizeof(t), "%02u:%02u", (unsigned)alarms[nextIdx].hour, (unsigned)alarms[nextIdx].minute);
    j += t;
  }
  j += "\"}";
  server.send(200, "application/json", j);
}

void handleTimerState() {
  if (!checkAuth()) return;
  timerCheckExpiry();
  char txt[16];
  timerFormatText(txt, sizeof(txt));
  String json = "{\"running\":" + String(timerRunning ? "true" : "false") +
                ",\"finished\":" + String(timerFinished ? "true" : "false") +
                ",\"remainingSec\":" + String(timerGetRemainingSec()) +
                ",\"durationSec\":" + String(timerDurationSec) +
                ",\"mode\":" + String(timerMode) +
                ",\"phase\":" + String(pomoPhase) +
                ",\"round\":" + String(pomoRound) +
                ",\"rounds\":" + String(pomoRounds) +
                ",\"inCycle\":" + String(pomoInCycle ? "true" : "false") +
                ",\"text\":\"" + String(txt) + "\"}";
  server.send(200, "application/json", json);
}


void handlePriorityOrder() {
  if (!checkAuth()) return;
  if (!server.hasArg("order")) {
    server.send(400, "text/plain", "Lipseste order");
    return;
  }
  // A client cached before a tile existed leaves it out of the order; it is
  // slotted in where it belongs rather than refused or dumped at the end.
  uint8_t parsed[NUM_PRIORITY_IDS];
  bool sawNowPlaying = false;
  int count = parsePriorityOrder(server.arg("order"), parsed, &sawNowPlaying);
  if (count == 0) {
    server.send(400, "text/plain", "Ordine invalida");
    return;
  }
  completePriorityOrder(parsed, count, priorityOrder);
  // nowPlayingIsPriority used to be inferred purely from whether "nowplaying"
  // appeared in the submitted order string. That made it collateral damage of
  // ANY priority-list save that didn't happen to include nowplaying (e.g. the
  // client's hardcoded default priorityItems before /state has loaded, or a
  // stale client copy left over from a failed /state fetch) - such a save
  // would silently flip Now Playing back into Circuit Tiles even though the
  // user never touched it. The client now sends an explicit npPriority flag
  // reflecting its actual current UI state; fall back to the old inference
  // only if an older/other client omits it, for backward compatibility.
  if (server.hasArg("npPriority")) {
    nowPlayingIsPriority = server.arg("npPriority") == "1";
  } else {
    nowPlayingIsPriority = sawNowPlaying;
  }
  if (!nowPlayingIsPriority) npPriorityWasActive = false;

  int notifRank = -1, ets2Rank = -1;
  for (int i = 0; i < NUM_PRIORITY_IDS; i++) {
    if (priorityOrder[i] == PRIORITY_ID_NOTIF) notifRank = i;
    if (priorityOrder[i] == PRIORITY_ID_ETS2)  ets2Rank  = i;
  }
  ets2OrderFirst = (ets2Rank < notifRank);
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleHourFormat() {
  if (!checkAuth()) return;
  if (server.hasArg("leadzero")) {
    hourLeadingZero = (server.arg("leadzero") == "1");
    saveSettingsDeferred();
    // The hour tile redraws itself every 200 ms, so it picks this up on its own.
    server.send(200, "text/plain", "OK");
    return;
  }
  if (server.hasArg("fmt")) {
    uint8_t f = (uint8_t)server.arg("fmt").toInt();
    if (f <= 1) {
      hourFormat = f;
      saveSettingsDeferred();
    }
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste fmt");
  }
}

// Takes any of skin / content / fmt / leadzero / barmode / barpos / swap. The tile redraws itself every
// 200 ms, so the panel picks the change up on its own.
void handleHourWeekSett() {
  if (!checkAuth()) return;
  bool any = false;
  if (server.hasArg("skin"))     { int v = server.arg("skin").toInt(); if (v >= HW_SKIN_COMPACT && v <= HW_SKIN_TEMP) { hwSkin = (uint8_t)v; any = true; } }
  if (server.hasArg("content"))  { int v = server.arg("content").toInt(); if (v >= HW_CONTENT_DAY && v <= HW_CONTENT_WEATHER) { hwContent = (uint8_t)v; any = true; } }
  if (server.hasArg("fmt"))      { int v = server.arg("fmt").toInt();     if (v == 0 || v == 1) { hwFormat  = (uint8_t)v; any = true; } }
  if (server.hasArg("leadzero")) { hwLeadZero = (server.arg("leadzero") == "1"); any = true; }
  if (server.hasArg("barmode"))  { int v = server.arg("barmode").toInt(); if (v == 0 || v == 1) { hwBarMode = (uint8_t)v; any = true; } }
  if (server.hasArg("barpos"))   { int v = server.arg("barpos").toInt();  if (v == 0 || v == 1) { hwBarPos  = (uint8_t)v; any = true; } }
  if (server.hasArg("swap"))     { hwSwap = (server.arg("swap") == "1"); any = true; }
  normalizeWatchfaceSettings();
  if (!any) {
    server.send(400, "text/plain", "Lipsesc setarile");
    return;
  }
  saveSettingsDeferred();
  server.send(200, "text/plain", "OK");
}

void handleDateFormat() {
  if (!checkAuth()) return;
  if (server.hasArg("fmt")) {
    uint8_t f = (uint8_t)server.arg("fmt").toInt();
    if (f <= 5) {
      dateFormat = f;
      saveSettingsDeferred();

      if (items[currentSlot].id == ITEM_DATE) {
        requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
      }
    }
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste fmt");
  }
}

void handleDateLang() {
  if (!checkAuth()) return;
  if (server.hasArg("lang")) {
    uint8_t l = (uint8_t)server.arg("lang").toInt();
    if (l <= 1) {
      dateLang = l;
      saveSettingsDeferred();

      if (items[currentSlot].id == ITEM_DATE) {
        requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
      }
      server.send(200, "text/plain", "OK");
      return;
    }
  }
  server.send(400, "text/plain", "Parametru invalid");
}

void handleDateCustomFmt() {
  if (!checkAuth()) return;
  if (!server.hasArg("pattern")) {
    server.send(400, "text/plain", "Lipseste pattern");
    return;
  }
  String p = server.arg("pattern");
  p.toUpperCase();
  p.trim();
  String err;
  if (!dateValidateCustomFormat(p.c_str(), &err)) {
    server.send(400, "text/plain", err);
    return;
  }
  p.toCharArray(customDateFmt, sizeof(customDateFmt));
  dateFormat = 5;
  saveSettingsDeferred();

  if (items[currentSlot].id == ITEM_DATE) {
    requestDisplayOp(DISPLAY_OP_REFRESH_VISIBLE);
  }
  server.send(200, "text/plain", "OK");
}

void handleTempUnit() {
  if (!checkAuth()) return;
  if (server.hasArg("unit")) {
    uint8_t u = (uint8_t)server.arg("unit").toInt();
    if (u <= 1) {
      tempUnit = u;
      saveSettingsDeferred();
    }
    server.send(200, "text/plain", "OK");
  } else {
    server.send(400, "text/plain", "Lipseste unit");
  }
}

// Every route the interface and Octoglow Connect use. server.stop() closes the
// listening socket but keeps the handlers - only the server's destructor frees
// them - so they are added once. Registering them again on every switch between
// the access point and Wi-Fi appended another ~75 handlers each time: memory that
// never came back, and a longer list to walk for every request after it.
static bool routesRegistered = false;

static void registerRoutes() {
  if (routesRegistered) return;
  routesRegistered = true;
  server.on("/",           HTTP_GET,  handleRoot);
  server.on("/scan",       HTTP_GET,  handleScan);
  server.on("/connect",    HTTP_POST, handleConnect);
  server.on("/oobestate",  HTTP_GET,  handleOobeState);
  server.on("/oobelang",   HTTP_POST, handleOobeLang);
  server.on("/oobewifi",   HTTP_POST, handleOobeWifi);
  server.on("/oobewifistatus", HTTP_GET, handleOobeWifiStatus);
  server.on("/wifijoinstatus", HTTP_GET, handleWifiJoinStatus);
  server.on("/oobefinish", HTTP_POST, handleOobeFinish);
  server.on("/settings",   HTTP_POST, handleSettings);
  server.on("/backup",     HTTP_GET,  handleBackupExport);
  server.on("/restorebackup", HTTP_POST, handleBackupRestore);
  server.on("/factoryreset", HTTP_POST, handleFactoryReset);
  server.on("/brightness",  HTTP_POST, handleBrightness);
  server.on("/buzzersett", HTTP_POST, handleBuzzerSett);
  server.on("/state",      HTTP_GET,  handleState);
  server.on("/hwstate",    HTTP_GET,  handleHwState);
  server.on("/hwreset",    HTTP_POST, handleHwReset);
  server.on("/livetile",   HTTP_GET,  handleLiveTile);
  server.on("/getscreen",  HTTP_GET,  handleGetScreen);
  server.on("/screensub",  HTTP_GET,  handleScreenSub);
  server.on("/tilehidden", HTTP_POST, handleTileHidden);
  server.on("/socialsett", HTTP_POST, handleSocialSett);
  server.on("/socialkey",  HTTP_GET,  handleSocialKey);
  server.on("/nowplaying", HTTP_POST, handleNowPlaying);
  server.on("/npstate",    HTTP_GET,  handleNpState);
  server.on("/npmode",     HTTP_POST, handleNpMode);
  server.on("/screensaversett", HTTP_POST, handleScreensaverSett);
  server.on("/tempunit",   HTTP_POST, handleTempUnit);
  server.on("/hourformat",  HTTP_POST, handleHourFormat);
  server.on("/hwsett",      HTTP_POST, handleHourWeekSett);
  server.on("/dateformat",  HTTP_POST, handleDateFormat);
  server.on("/datelang",    HTTP_POST, handleDateLang);
  server.on("/datecustomfmt", HTTP_POST, handleDateCustomFmt);
  server.on("/weathersett",  HTTP_POST, handleWeatherSett);
  server.on("/weathersearch", HTTP_GET,  handleWeatherSearch);
  server.on("/weatherstate", HTTP_GET,  handleWeatherState);
  server.on("/weatherkey",   HTTP_GET,  handleWeatherKey);
  server.on("/wxlang",       HTTP_POST, handleWxLang);
  server.on("/currencysett",  HTTP_POST, handleCurrencySett);
  server.on("/currencystate", HTTP_GET,  handleCurrencyState);
  server.on("/notification", HTTP_POST, handleNotification);
  server.on("/batterystatus", HTTP_POST, handleBatteryStatus);
  server.on("/lowbatterytoggle", HTTP_POST, handleLowBatteryToggle);
  server.on("/notiftoggle",  HTTP_POST, handleNotifToggle);
  server.on("/webtoggle",    HTTP_POST, handleWebToggle);
  server.on("/websett",      HTTP_POST, handleWebSett);
  server.on("/notifstate",   HTTP_GET,  handleNotifState);
  server.on("/ets2speed",    HTTP_POST, handleEts2Speed);
  server.on("/ets2toggle",   HTTP_POST, handleEts2Toggle);
  server.on("/ets2state",    HTTP_GET,  handleEts2State);
  server.on("/beamngspeed",  HTTP_POST, handleBeamngSpeed);
  server.on("/beamngtoggle", HTTP_POST, handleBeamngToggle);
  server.on("/beamngstate",  HTTP_GET,  handleBeamngState);
  server.on("/assettospeed",  HTTP_POST, handleAssettoSpeed);
  server.on("/assettotoggle", HTTP_POST, handleAssettoToggle);
  server.on("/assettostate",  HTTP_GET,  handleAssettoState);
  server.on("/nfsu2speed",    HTTP_POST, handleNfsu2Speed);
  server.on("/nfsmwspeed",    HTTP_POST, handleNfsmwSpeed);
  server.on("/nfscspeed",     HTTP_POST, handleNfscSpeed);
  server.on("/nfsu2toggle",   HTTP_POST, handleNfsu2Toggle);
  server.on("/nfsmwtoggle",   HTTP_POST, handleNfsmwToggle);
  server.on("/nfsctoggle",    HTTP_POST, handleNfscToggle);
  server.on("/nfsu2state",    HTTP_GET,  handleNfsu2State);
  server.on("/nfsmwstate",    HTTP_GET,  handleNfsmwState);
  server.on("/nfscstate",     HTTP_GET,  handleNfscState);
  server.on("/speedunit",    HTTP_POST, handleSpeedUnit);
  server.on("/voicemeetertoggle", HTTP_POST, handleVoicemeeterToggle);
  server.on("/voicemeeterstate",  HTTP_GET,  handleVoicemeeterState);
  server.on("/browserdownload",       HTTP_POST, handleBrowserDownload);
  server.on("/browserdownloadtoggle", HTTP_POST, handleBrowserDownloadToggle);
  server.on("/browserdownloadstate",  HTTP_GET,  handleBrowserDownloadState);
  server.on("/qbitstatus",            HTTP_POST, handleQbitStatus);
  server.on("/qbittoggle",            HTTP_POST, handleQbitToggle);
  server.on("/qbitstate",             HTTP_GET,  handleQbitState);
  server.on("/abstatus",              HTTP_POST, handleAbStatus);
  server.on("/abmode",                HTTP_POST, handleAbMode);
  server.on("/abstate",               HTTP_GET,  handleAbState);
  server.on("/volumestatus", HTTP_POST, handleVolumeStatus);
  server.on("/volumetoggle", HTTP_POST, handleVolumeToggle);
  server.on("/gamefpstoggle",         HTTP_POST, handleGameFpsToggle);
  server.on("/bambustatus",           HTTP_POST, handleBambuStatus);
  server.on("/bambutoggle",           HTTP_POST, handleBambuToggle);
  server.on("/bambustate",            HTTP_GET,  handleBambuState);
  server.on("/barstyle",              HTTP_POST, handleBarStyle);
  server.on("/tilepreview",           HTTP_GET,  handleTilePreview);
  server.on("/obsstatus",  HTTP_POST, handleObsStatus);
  server.on("/obstoggle",  HTTP_POST, handleObsToggle);
  server.on("/obssett",    HTTP_POST, handleObsSett);
  server.on("/obsstate",   HTTP_GET,  handleObsState);
  server.on("/ets2order",    HTTP_POST, handleEts2Order);
  server.on("/priorityorder", HTTP_POST, handlePriorityOrder);
  server.on("/swstart",       HTTP_POST, handleSwStart);
  server.on("/swstop",        HTTP_POST, handleSwStop);
  server.on("/swstate",       HTTP_GET,  handleSwState);
  server.on("/timersett",     HTTP_POST, handleTimerSett);
  server.on("/timerstart",    HTTP_POST, handleTimerStart);
  server.on("/timerpause",    HTTP_POST, handleTimerPause);
  server.on("/timerstate",    HTTP_GET,  handleTimerState);
  server.on("/alarmsett",     HTTP_POST, handleAlarmSett);
  server.on("/alarmstop",     HTTP_POST, handleAlarmStop);
  server.on("/alarmstate",    HTTP_GET,  handleAlarmState);
  server.on("/mementosett",   HTTP_POST, handleMementoSett);
  server.on("/canvassett",    HTTP_POST, handleCanvasSett);
  server.on("/eventsoundsett", HTTP_POST, handleEventSoundSett);
  server.on("/touchsett",     HTTP_POST, handleTouchSett);
  server.on("/accentsett",    HTTP_POST, handleAccentSett);
  server.on("/startap",       HTTP_POST, handleStartAp);
  server.on("/apstate",       HTTP_GET,  handleApState);
  server.on("/wifiinfo",      HTTP_GET,  handleWifiInfo);
  server.on("/power",         HTTP_POST, handlePower);
  server.on("/apsett",        HTTP_POST, handleApSett);
  server.on("/timesett",      HTTP_POST, handleTimeSett);
  server.on("/appass",        HTTP_GET,  handleApPass);
  server.on("/stopap",        HTTP_POST, handleStopAp);
  // AUTH
  server.on("/authstate",  HTTP_GET,  handleAuthState);
  server.on("/hostname",   HTTP_POST, handleHostName);
  server.on("/countdownsett", HTTP_POST, handleCountdownSett);
  server.on("/countdownopt",  HTTP_POST, handleCountdownOpt);
  server.on("/authsetup",  HTTP_POST, handleAuthSetup);
  server.on("/login",      HTTP_POST, handleLogin);
  server.on("/logout",     HTTP_POST, handleLogout);
  server.on("/updateaccount", HTTP_POST, handleUpdateAccount);
  server.on("/whoami",     HTTP_GET,  handleWhoami);
  server.on("/dashboard",  HTTP_GET,  handleDashboard);
  server.on("/fonts/google-sans.woff2", HTTP_GET, handleFontGoogleSans);
  server.on("/fonts/roboto.woff2",      HTTP_GET, handleFontRoboto);
  server.on("/licenses",   HTTP_GET,  handleLicenses);
  server.on("/swupdateupload", HTTP_POST, handleSwUpdateUploadDone, handleSwUpdateUpload);
  const char* hdrs[] = {"Cookie", "If-None-Match", "User-Agent"};
  server.collectHeaders(hdrs, 3);
}

// WIFI MANAGEMENT
void startProvisionMode() {
  Serial.println("==> Pornire mod Provisioning");
  provisionMode = true;
  // Nothing here will ever reach an NTP server, so if the clock has never been
  // set it never will be while we are in this mode. Give it a plausible date:
  // every getLocalTime() below returns straight away instead of spinning, and
  // session cookies stay verifiable. A clock that is already right is left
  // alone - seedManualClock() only fills in an invalid one.
  seedManualClock();
  // Same reason: the clock has just been made valid, so the schedule can be
  // applied before the access point badge goes up.
  applyBrightness();
  WiFi.disconnect(true);
  delay(200);
  // With AP as the start mode the badge is an announcement and the spinner
  // covers the handover to the tile loop. Otherwise the badge IS the screen
  // from here on, so it goes up once and stays - no hold, no spinner, none of
  // the badge/spinner/badge flicker that bought nothing.
  const bool apIsHome = (defaultStartMode == START_MODE_AP);
  drawApScreen();
  startupFactoryResetArm();
  if (apIsHome) {
    delay(AP_SCREEN_HOLD_MS);
    // softAP() and the route table below both block, so the spinner turns
    // either side of them rather than through them - same as the Wi-Fi path,
    // where the frames only advance between WiFi.status() polls.
    loadAnimWait(400);
  }
  WiFi.mode(WIFI_AP);
  String apSsid = getApSsid();
  String apPass = getApPass();
  WiFi.softAP(apSsid.c_str(), apPass.length() > 0 ? apPass.c_str() : "");
  if (apIsHome) loadAnimWait(400);
  Serial.print("AP pornit: ");
  Serial.print(apSsid);
  Serial.print(" - IP: ");
  Serial.println(WiFi.softAPIP());
  registerRoutes();
  server.begin();

  if (apIsHome) {
    loadAnimWait(400);
    // The tile loop takes the panel from here. Hand the current slot a fresh
    // timer rather than let it inherit a stale one from before the switch and
    // flip straight away.
    slotStartMs = millis();
  }
}

void handleStartAp() {
  if (!checkAuth()) return;
  server.send(200, "text/plain", "ok");
  pendingModeSwitch = 1;
}

// What the station connection actually is. There is no call on this chip that
// reports the rate a frame just went out at, so "speed" here is the ceiling of
// the mode that was negotiated - said as much in the interface, next to the
// RSSI, which is the number that actually moves.
void handleWifiInfo() {
  if (!checkAuth()) return;
  bool conn = (WiFi.status() == WL_CONNECTED);
  String j = "{\"connected\":";
  j += (conn ? "true" : "false");
  if (conn) {
    j += ",\"ssid\":\"";
    j += jsonEscape(WiFi.SSID());
    j += "\",\"bssid\":\"";
    j += WiFi.BSSIDstr();
    j += "\",\"rssi\":";
    j += String(WiFi.RSSI());
    j += ",\"channel\":";
    j += String(WiFi.channel());
    j += ",\"ip\":\"";
    j += WiFi.localIP().toString();
    j += "\",\"gw\":\"";
    j += WiFi.gatewayIP().toString();
    j += "\",\"mask\":\"";
    j += WiFi.subnetMask().toString();
    j += "\",\"dns\":\"";
    j += WiFi.dnsIP().toString();
    j += "\",\"mac\":\"";
    j += WiFi.macAddress();
    j += "\"";
    wifi_ap_record_t ap;
    if (esp_wifi_sta_get_ap_info(&ap) == ESP_OK) {
      const char* phy = ap.phy_11ax ? "Wi-Fi 6 (802.11ax)"
                      : ap.phy_11n  ? "Wi-Fi 4 (802.11n)"
                      : ap.phy_11g  ? "802.11g"
                      : ap.phy_11b  ? "802.11b"
                                    : "802.11";
      bool ht40 = (ap.bandwidth == WIFI_BW_HT40);
      // One spatial stream, which is all this radio has. HT20 tops out at 72,
      // HT40 at 150; g and b have no channel width to speak of.
      int mbps = ap.phy_11n ? (ht40 ? 150 : 72) : ap.phy_11g ? 54 : ap.phy_11b ? 11 : 0;
      j += ",\"phy\":\"";
      j += phy;
      j += "\",\"bw\":";
      j += String(ht40 ? 40 : 20);
      j += ",\"maxMbps\":";
      j += String(mbps);
    }
  }
  j += "}";
  server.send(200, "application/json", j);
}

// The three power options. Each answers before it acts, because acting means
// the connection this request came in on is about to go away.
void handlePower() {
  if (!checkAuth()) return;
  String a = server.arg("action");
  if (a == "sleep") {
    sleepEnter();
    server.send(200, "text/plain", "OK");
    return;
  }
  if (a == "wake") {
    sleepWake();
    server.send(200, "text/plain", "OK");
    return;
  }
  if (a == "reboot") {
    server.send(200, "text/plain", "OK");
    saveSettingsFlush();
    delay(500);
    ESP.restart();
    return;
  }
  if (a == "off") {
    // Checked before answering, not after: going dark with no armed wake source
    // would leave the USB cable as the only way to switch the clock back on.
    if (esp_sleep_enable_ext0_wakeup((gpio_num_t)TOUCH_PIN, 1) != ESP_OK) {
      server.send(500, "text/plain", "Senzorul tactil nu poate porni ceasul inapoi");
      return;
    }
    server.send(200, "text/plain", "OK");
    delay(500);
    powerOffNow();
    return;
  }
  server.send(400, "text/plain", "Actiune necunoscuta");
}

void handleApState() {
  if (!checkAuth()) return;
  String ssid = getApSsid();
  String pass = getApPass();
  // The time block rides along with the AP state because that is the one screen
  // showing it - one request instead of two every time the screen opens.
  struct tm lt;
  char nowBuf[24] = "";
  if (readLocalTime(lt)) strftime(nowBuf, sizeof(nowBuf), "%Y-%m-%dT%H:%M:%S", &lt);
  String json = "{\"ssid\":\"" + ssid + "\"" +
                ",\"hasPass\":" + String(pass.length() > 0 ? "true" : "false") +
                ",\"passLen\":" + String(pass.length()) +
                ",\"netTime\":" + String(netTimeSync ? "true" : "false") +
                ",\"devTime\":\"" + String(nowBuf) + "\"" +
                ",\"timeValid\":" + String(TIME_LOOKS_VALID(time(nullptr)) ? "true" : "false") +
                ",\"mode\":\"" + String(provisionMode ? "ap" : "sta") + "\"}";
  server.send(200, "application/json", json);
}

void handleApPass() {
  if (!checkAuth()) return;
  String json = "{\"pass\":\"" + getApPass() + "\"}";
  server.send(200, "application/json", json);
}

void handleStopAp() {
  if (!checkAuth()) return;
  if (!provisionMode) { server.send(409, "application/json", "{\"ok\":false,\"err\":\"AP is not active\"}"); return; }
  if (wifiJoinBusy()) { server.send(409, "application/json", "{\"ok\":false,\"err\":\"Network check in progress\"}"); return; }
  prefs.begin("wifi", true);
  String ssid = prefs.getString("ssid", "");
  String pass = prefs.getString("pass", "");
  prefs.end();
  if (ssid.length() == 0) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"Nicio retea WiFi salvata\"}");
    return;
  }
  // Test even previously saved credentials: the router password may have
  // changed since the last successful connection. Stay in AP on failure.
  wifiJoinSsid = ssid;
  wifiJoinPass = pass;
  wifiJoinOobe = false;
  wifiJoinFromStation = false;
  wifiJoinError = "";
  wifiJoinState = WIFI_JOIN_START;
  server.send(202, "application/json", "{\"ok\":true,\"checking\":true}");
}

void handleTimeSett() {
  if (!checkAuth()) return;

  if (server.hasArg("netsync")) {
    bool on = (server.arg("netsync") == "1");
    if (on != netTimeSync) {
      netTimeSync = on;
      saveSettingsDeferred();
      if (on) {
        // Go and fetch the real time now rather than making the user reboot.
        if (WiFi.status() == WL_CONNECTED) {
          configTime(0, 0, "pool.ntp.org", "time.nist.gov");
          setenv("TZ", "UTC0", 1); tzset();
          tzDetectAsync();
        }
      } else {
        stopNtp();
        seedManualClock();
      }
    }
  }

  if (server.hasArg("y")) {
    if (netTimeSync) {
      server.send(400, "application/json",
                  "{\"ok\":false,\"err\":\"Opriti mai intai preluarea orei de pe retea\"}");
      return;
    }
    if (!applyManualTime(server.arg("y").toInt(),  server.arg("mo").toInt(),
                         server.arg("d").toInt(),  server.arg("h").toInt(),
                         server.arg("mi").toInt(),
                         server.hasArg("s") ? server.arg("s").toInt() : 0)) {
      server.send(400, "application/json",
                  "{\"ok\":false,\"err\":\"Data sau ora invalida\"}");
      return;
    }
  }

  server.send(200, "application/json", "{\"ok\":true}");
}

void handleApSett() {
  if (!checkAuth()) return;
  if (!server.hasArg("ssid")) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"Lipseste SSID\"}");
    return;
  }
  String newSsid = server.arg("ssid");
  newSsid.trim();
  if (newSsid.length() == 0) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"SSID-ul nu poate fi gol\"}");
    return;
  }
  if (newSsid.length() > 32) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"SSID prea lung (max 32 caractere)\"}");
    return;
  }
  String newPass = server.hasArg("pass") ? server.arg("pass") : getApPass();
  if (newPass.length() > 0 && newPass.length() < 8) {
    server.send(400, "application/json", "{\"ok\":false,\"err\":\"Parola trebuie sa aiba minim 8 caractere (sau lasata goala)\"}");
    return;
  }
  prefs.begin("apcfg", false);
  prefs.putString("ssid", newSsid);
  gApSsidCached = newSsid;
  gApSsidLoaded = true;
  prefs.putString("pass", newPass);
  prefs.end();

  if (provisionMode) {
    WiFi.softAPdisconnect(true);
    delay(150);
    WiFi.softAP(newSsid.c_str(), newPass.length() > 0 ? newPass.c_str() : "");
  }
  server.send(200, "application/json", "{\"ok\":true}");
}

void stopProvisionMode() {
  Serial.println("==> Oprire mod Provisioning");
  server.stop();
  WiFi.softAPdisconnect(true);
  delay(200);
  provisionMode = false;
}

bool connectToWiFi(const char* ssid, const char* pass) {
  if (strlen(ssid) == 0) return false;

  mx.update(MD_MAX72XX::OFF);
  for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);

  // In the font picked under Tile Manager -> Font Type, like the tiles, and
  // kept clear of the logo in columns 24..31.
  uint8_t lbl[24]; int lblN = 0;
  for (const char* p = "Wifi"; *p; p++) appendGlyphAuto(lbl, lblN, sizeof(lbl), *p, fontType);
  for (int i = 0; i < lblN; i++) mx.setColumn(31 - i, lbl[i]);

  for (int col = 0; col < 8; col++) {
    uint8_t colVal = 0;
    for (int row = 0; row < 8; row++) {
      if (wifiLogo[row] & (0x80 >> col)) colVal |= (1 << row);
    }
    mx.setColumn(31 - (24 + col), colVal);
  }
  mxCommit();
  startupFactoryResetArm();
  Serial.printf("Conectare la: %s\n", ssid);
  if (!gHostName[0]) netIdentityInit();
  WiFi.setHostname(gHostName);
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(false);
  WiFi.disconnect(false, true);
  WiFi.begin(ssid, pass);
  // Hold the Wi-Fi logo for the first slice of the wait, then hand over to the
  // spinner, which runs for as long as the connection actually takes.
  const int logoHold = (WIFI_CONNECT_ATTEMPTS * LOAD_LOGO_HOLD_PCT) / 100;
  int attempts = 0;
  while (WiFi.status() != WL_CONNECTED && attempts < WIFI_CONNECT_ATTEMPTS) {
    if (attempts < logoHold) delay(500);   // logo stays on screen
    else                     loadAnimWait(500);
    Serial.print("."); attempts++;
  }
  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("\nConectat! IP: " + WiFi.localIP().toString());

    registerRoutes();
    server.begin();
    mdnsStart();

    if (netTimeSync) {
      configTime(0, 0, "pool.ntp.org", "time.nist.gov");
      setenv("TZ", "UTC0", 1); tzset();
      autoDetectTimezoneAnimated();
      struct tm ti_init;
      int retry = 0;
      while (!readLocalTime(ti_init) && retry < 10) { loadAnimWait(500); retry++; }
    } else {
      // Manual mode: no NTP poll and no timezone lookup, so the hand-set clock
      // stays exactly where the user put it. TZ is deliberately left alone -
      // resetting it here would shift an already-set clock on every reconnect.
      stopNtp();
      seedManualClock();
    }
    // Whichever branch ran, there is a clock now where there may not have been
    // one before - and the dim schedule is a function of the clock. Waiting for
    // loop()'s next tick would show a bright frame first.
    applyBrightness();
    slotStartMs = millis();
    return true;
  } else {
    Serial.println("\nConectare esuata!");
    // In the font picked under Tile Manager -> Font Type, like the Wifi screen
    // it replaces - the library font here was the odd one out.
    mx.update(MD_MAX72XX::OFF);
    for (int c = 0; c < 32; c++) mx.setColumn(c, 0x00);
    uint8_t lbl[24]; int lblN = 0;
    for (const char* p = "ERR"; *p; p++) appendGlyphAuto(lbl, lblN, sizeof(lbl), *p, fontType);
    if (lblN > 0 && lbl[lblN - 1] == 0x00) lblN--;
    for (int i = 0, at = (32 - lblN) / 2; i < lblN; i++) mx.setColumn(31 - (at + i), lbl[i]);
    mxCommit();
    delay(1500);
    return false;
  }
}

// POWER
//
// Sleeping only darkens the panel - the tile loop, the web server and the
// station connection all carry on, which is what lets a notification wake it.
// The MAX7219's own shutdown is what applyBrightness() already reaches for at
// level 0, so there is nothing new here to turn the display off with.
void sleepEnter() {
  if (sleepActive) return;
  sleepActive = true;
  applyBrightness();
}

void sleepWake() {
  lastActivityMs = millis();
  if (!sleepActive) return;
  sleepActive = false;
  applyBrightness();
  // No redraw: the loop never stopped writing frames, so the panel lights up on
  // whatever it was already showing rather than on a stale one.
}

// Anything that means somebody, or something, is using the clock. Called from
// the touch sensor and from every route Octoglow Connect comes in on.
void noteActivity() {
  lastActivityMs = millis();
  if (sleepActive) sleepWake();
}

static void autoSleepTick() {
  if (!autoSleepOn || autoSleepSec == 0 || sleepActive) return;
  // An alarm mid-ring is not an idle clock.
  if (alarmRinging) { lastActivityMs = millis(); return; }
  if (millis() - lastActivityMs >= (unsigned long)autoSleepSec * 1000UL) sleepEnter();
}

// True once the wake source is armed. Checked rather than assumed: without it
// the device would go to sleep with no way back short of unplugging it.
static bool armTouchWake() {
  return esp_sleep_enable_ext0_wakeup((gpio_num_t)TOUCH_PIN, 1) == ESP_OK;
}

// Deep sleep. Everything stops - no display, no WiFi, no web server - and only
// a held touch brings it back. setup() is where that hold is checked.
bool powerOffNow() {
  if (!armTouchWake()) return false;
  saveSettingsFlush();
  buzzOff();
  applyMaxBrightness(0);
  WiFi.disconnect(true, false);
  WiFi.mode(WIFI_OFF);
  // A finger still on the pad would be read as the wake it is waiting for, and
  // the device would come straight back up.
  unsigned long t0 = millis();
  while (digitalRead(TOUCH_PIN) == HIGH && millis() - t0 < 5000UL) delay(20);
  delay(80);
  esp_deep_sleep_start();
  return true;  // never reached
}

// Called from setup() before anything is brought up. A wake that is not a
// deliberate hold goes straight back to sleep, so brushing the pad does
// nothing.
static void powerOnHoldGate() {
  if (esp_sleep_get_wakeup_cause() != ESP_SLEEP_WAKEUP_EXT0) return;
  pinMode(TOUCH_PIN, INPUT);
  unsigned long t0 = millis();
  while (millis() - t0 < POWER_ON_HOLD_MS) {
    if (digitalRead(TOUCH_PIN) != HIGH) {
      // Released too early. Back to sleep - but only if the wake source arms
      // again, because a device that cannot wake is a device that is bricked
      // until somebody unplugs it. If it will not arm, carry on booting.
      if (!armTouchWake()) return;
      delay(120);
      esp_deep_sleep_start();
    }
    delay(10);
  }
}

// Watch touch independently while the Wi-Fi/AP startup screen is actually
// visible. A press may begin during the first three seconds; holding it for
// five more seconds resets the device. This must not block setup(), which is
// exactly why the old gate missed presses made on the Wi-Fi/loading screen.
static std::atomic<bool> startupTouchGuard{false};
static bool startupFactoryResetArmed = false;

static void startupFactoryResetTask(void*) {
  const unsigned long armMs = millis();
  while (millis() - armMs < 3000UL) {
    if (digitalRead(TOUCH_PIN) == HIGH) {
      const unsigned long holdStartMs = millis();
      while (digitalRead(TOUCH_PIN) == HIGH) {
        if (millis() - holdStartMs >= 5000UL) {
          Serial.println("Factory reset requested by startup touch hold");
          eraseFactorySettings();
          delay(100);
          ESP.restart();
        }
        vTaskDelay(pdMS_TO_TICKS(20));
      }
    }
    vTaskDelay(pdMS_TO_TICKS(20));
  }
  startupTouchGuard.store(false);
  vTaskDelete(nullptr);
}

static void startupFactoryResetArm() {
  if (startupFactoryResetArmed || esp_reset_reason() != ESP_RST_POWERON) return;
  startupFactoryResetArmed = true;
  startupTouchGuard.store(true);
  if (xTaskCreate(startupFactoryResetTask, "startup-reset", 3072, nullptr, 2, nullptr) != pdPASS) {
    startupTouchGuard.store(false);
    Serial.println("Could not start startup touch monitor");
  }
}

// TOUCH

void executeTouchAction(uint8_t action) {
  // In provisioning mode the AP badge is the only thing on the panel - loop()
  // returns before any tile or IP renderer runs. Moving through the slots or
  // starting the IP display would leave a frame that nothing ever advances,
  // which is how "Show IP Address" used to freeze the display after a switch to
  // AP mode. The rest only change brightness or the buzzer, so they pass.
  if (provisionMode && (action == 1 || action == 2 || action == 8)) return;

  switch (action) {
    case 1:
      retreatSlot();
      break;
    case 2:
      advanceSlot();
      break;
    case 3:
      toggleScreenPower();
      break;
    case 4:
      touchScreenOff = false;
      curBrightness = (uint8_t)constrain((int)curBrightness + 1, 0, 16);
      applyBrightness();
      saveSettingsDeferred();
      break;
    case 5:
      touchScreenOff = false;
      curBrightness = (uint8_t)constrain((int)curBrightness - 1, 0, 16);
      applyBrightness();
      saveSettingsDeferred();
      break;
    case 6:
      buzzerOn = !buzzerOn;
      saveSettingsDeferred();
      break;
    case 7:
      saveSettingsFlush();
      ESP.restart();
      break;
    case 8:
      startIpShowFromTouch();
      break;
    default:
      break;
  }
}

void toggleScreenPower() {
  touchScreenOff = !touchScreenOff;
  applyBrightness();
}

void registerTouchTap() {
  unsigned long now = millis();
  if (tapPending && (now - lastTapReleaseMs) <= DOUBLE_TAP_WINDOW_MS) {
    tapPending = false;
    executeTouchAction(touchDoubleTapAction);
  } else {
    tapPending = true;
    lastTapReleaseMs = now;
  }
}

void checkPendingTap() {
  if (tapPending && (millis() - lastTapReleaseMs) > DOUBLE_TAP_WINDOW_MS) {
    tapPending = false;
    executeTouchAction(touchTapAction);
  }
}

void handleTouch() {
  // A startup hold belongs exclusively to the reset monitor. It must not
  // become a normal tap or the two-second AP/Wi-Fi switch after setup finishes.
  if (startupTouchGuard.load()) {
    touchActive = false;
    touchShortFired = false;
    tapPending = false;
    return;
  }
  bool touched = (digitalRead(TOUCH_PIN) == HIGH);
  if (touched && !touchActive) {
    touchActive = true; touchStartMs = millis(); touchShortFired = false;
    // A ringing alarm owns the touch sensor. Stopping it happens on the press,
    // not on the release, so it is instant - and touchShortFired swallows that
    // whole press: the configured tap action does not also run, and holding
    // the finger down afterwards does not fall into the access point switch.
    // tapPending is cleared as well, or a tap from just before the alarm rang
    // could still fire behind it. The next press behaves normally.
    if (alarmRinging) {
      alarmStopRinging();
      touchShortFired = true;
      tapPending = false;
    } else if (sleepActive) {
      // Waking is what this press is for, the same way stopping the alarm is.
      // The configured action does not also run; the next press is normal.
      sleepWake();
      touchShortFired = true;
      tapPending = false;
    } else {
      noteActivity();
    }
  } else if (!touched && touchActive) {
    if (!touchShortFired) {
      beepTouch();
      registerTouchTap();
    }
    touchActive = false; touchShortFired = false;
  }
  if (touchActive && !touchShortFired && (millis() - touchStartMs >= TOUCH_HOLD_MS)) {
    // touchActive deliberately stays true: clearing it here re-armed the
    // detector while the finger was still down, so one long press fired the
    // mode switch again a second later, and the release that followed counted
    // as a fresh tap. touchShortFired alone suppresses both until a real
    // release is seen.
    touchShortFired = true;
    // Use the same deferred path as the web buttons. This prevents a long
    // press from tearing down the server while a request is being decoded.
    pendingModeSwitch = provisionMode ? 2 : 1;
  }
  checkPendingTap();
}

// SETUP
void setup() {
  Serial.begin(115200);
  // Before anything is powered up: a wake from Power Off that was not a
  // deliberate hold goes straight back to sleep.
  powerOnHoldGate();
  loadAuth();
  loadOobeState();
  netIdentityInit();
  pinMode(TOUCH_PIN, INPUT);
  buzzOff();
  buzzStartTask();
  Wire.begin(BMP_SDA, BMP_SCL);
#if !OCTOGLOW_SOFTWARE_SPI
  SPI.begin(CLK_PIN, -1, DATA_PIN, CS_PIN);
#endif
  mx.begin();
  hwSketchSize = ESP.getSketchSize();
  const esp_partition_t* running = esp_ota_get_running_partition();
  hwSketchFree = running && running->size >= hwSketchSize ? running->size - hwSketchSize : 0;
  if (!bmpInitSensor()) {
    // Not fatal any more: bmpHealthTick() keeps retrying while the clock runs,
    // so a sensor that lost the power-up race comes back on its own instead of
    // needing a reboot.
    Serial.println("BMP280 negasit - se va reincerca automat");
  } else {
    delay(50);
    tempSampleTick();
    pressureSampleTick();
  }
  loadSettings();
  // Before WiFi, so the fallback is in place even when the board never leaves
  // provisioning mode and connectToWiFi() is never reached.
  if (!netTimeSync) seedManualClock();
  // applyBrightness(), not applyMaxBrightness(curBrightness): the raw level
  // ignores the dim schedule, so everything drawn between here and the first
  // pass through loop() - the whole connection sequence - used to run at full
  // brightness however late at night it was. In manual-time mode the clock was
  // just seeded above, so the schedule already has a time to work from.
  applyBrightness();
  refreshWifiSsidCache();
  prefs.begin("wifi", true);
  String savedSSID = prefs.getString("ssid", "");
  String savedPass = prefs.getString("pass", "");
  prefs.end();
  // Default Start Mode decides what comes up after a power cut. The Wi-Fi path
  // still falls back to the access point when there is nothing to connect to.
  if (!oobeComplete || defaultStartMode == START_MODE_AP) {
    startProvisionMode();
  } else if (savedSSID.length() > 0) {
    if (!connectToWiFi(savedSSID.c_str(), savedPass.c_str())) startProvisionMode();
  } else {
    startProvisionMode();
  }

  setCurrentSlot(0);
  if (!slotShowable(0)) {
    int next = nextActiveSlot(0);
    if (next != -1) setCurrentSlot(next);
  }
  slotStartMs = millis();

  // Keep all matrix animation on this core, one priority above Arduino's
  // network loop. A handler may take several milliseconds; the display task
  // pre-empts it whenever an animation frame becomes due.
  BaseType_t taskOk = xTaskCreatePinnedToCore(
    displayTaskMain,
    "octoglow-display",
    6144,
    nullptr,
    2,
    &displayTaskHandle,
    xPortGetCoreID()
  );
  if (taskOk != pdPASS) {
    displayTaskHandle = nullptr;
    Serial.println("Display task unavailable; using cooperative loop");
  }
}

// LOOP
void applyBrightness() {
  static uint8_t lastApplied = 255;
  uint8_t target;
  if (sleepActive || touchScreenOff) {
    target = 0;
  } else if (!dimAutoOn) {
    target = curBrightness;
  } else {
    struct tm ti;
    if (!readLocalTime(ti)) {
      target = curBrightness;
    } else {
      int nowMins  = ti.tm_hour * 60 + ti.tm_min;
      int fromMins = dimFromH   * 60 + dimFromM;
      int toMins   = dimToH     * 60 + dimToM;
      bool inDim;
      if (fromMins <= toMins) {
        inDim = (nowMins >= fromMins && nowMins < toMins);
      } else {
        inDim = (nowMins >= fromMins || nowMins < toMins);
      }
      target = inDim ? dimLevel : curBrightness;
    }
  }
  if (target != lastApplied) {
    applyMaxBrightness(target);
    lastApplied = target;
  }
}

// Runs from the top of loop(), so handleClient() has finished with the client
// and closed it before the interface underneath goes away.
static void doPendingModeSwitch() {
  uint8_t m = pendingModeSwitch;
  pendingModeSwitch = 0;
  if (m == 1) {
    wifiJoinState = WIFI_JOIN_IDLE;
    wifiJoinPass = "";
    wifiJoinPreviousPass = "";
    WiFi.disconnect(true);
    delay(300);
    startProvisionMode();
  } else if (m == 2) {
    prefs.begin("wifi", true);
    String s = prefs.getString("ssid", "");
    String p = prefs.getString("pass", "");
    prefs.end();
    if (provisionMode) stopProvisionMode();
    if (!connectToWiFi(s.c_str(), p.c_str())) {
      // Failed credentials must not strand the user off-network.
      startProvisionMode();
    }
    wifiJoinPass = "";
    wifiJoinState = WIFI_JOIN_IDLE;
  }
}

// The work that has to go on whatever the panel is showing. Called from the top
// of loop(), ahead of every early return.
static void loopHousekeeping() {
  unsigned long nowMs = millis();

  static unsigned long lastBrightCheck = 0;
  if (nowMs - lastBrightCheck >= 1000UL) {
    lastBrightCheck = nowMs;
    applyBrightness();
  }

  // Both fetches below only kick off a background task and return
  // immediately (see weatherFetch()/currencyFetch() definitions) - they
  // no longer block loop() while the HTTP request is in flight.
  // Both are gated on the tile still being in the manager: a removed tile has
  // no row to show the answer on, and each refresh is an HTTPS request plus an
  // 8 KB task. The poll calls stay unconditional so a fetch already in flight
  // when the tile was removed still gets collected instead of leaking its flag.
  if ((lastWeatherFetch == 0 || nowMs - lastWeatherFetch >= WEATHER_FETCH_INTERVAL_MS) &&
      (tileInManager(ITEM_WEATHER) || (tileInManager(ITEM_HOURWEEK) && hwSkin == HW_SKIN_COMPACT && hwContent == HW_CONTENT_WEATHER)) &&
      strlen(weatherApiKey) > 0) {
    weatherFetch();
  }
  weatherFetchPoll();

  if ((lastCurrencyFetch == 0 || nowMs - lastCurrencyFetch >= CURRENCY_FETCH_INTERVAL_MS) &&
      tileInManager(ITEM_CURRENCY)) {
    currencyFetch();
  }
  currencyFetchPoll();

  socialTickFetch();
  socialFetchPoll();

  bmpHealthTick();
  tempSampleTick();
  pressureSampleTick();
}

static void displayLoopTick() {
  hwMonitorTick();
  hwClearPeaksAfterBoot();
  processPendingDisplayOps();
  serviceTilePreview();
  handleTouch();
  // Do not retune the radio while handleClient() is still sending the reply
  // that started the join. This is the same guard used for mode switches.
  if (!backgroundServiceActive) wifiJoinTick();

  if (pendingModeSwitch && !backgroundServiceActive) {
    doPendingModeSwitch();
    return;
  }

  // Changing Default Start Mode while the access point is already up flips this
  // without going through startProvisionMode(), so the badge has to be put back
  // by hand - otherwise the panel keeps whatever tile frame was drawn last.
  static bool tileLoopWasSuspended = false;
  bool tileLoopNowSuspended = tileLoopSuspended();
  if (tileLoopNowSuspended && !tileLoopWasSuspended) drawApScreen();
  tileLoopWasSuspended = tileLoopNowSuspended;

  // Ahead of the early return below: an alarm goes off at its time whether or
  // not the access point happens to be up. The panel belongs to the AP badge
  // while that is showing, so in AP mode the alarm is heard rather than seen -
  // and the touch sensor still stops it.
  alarmTick();
  alarmToneTick();
  autoSleepTick();
  // Ahead of every early return below. It used to sit at the bottom of loop(),
  // which only the plain tiles ever reach: while a priority tile held the panel
  // (Now Playing from Octoglow Connect, the stopwatch, ETS2) or a tile was
  // scrolling, a setting changed in the interface never reached NVS and was gone
  // at the next power cut, the dim schedule stopped being applied, and the
  // sensor, weather and currency readings stopped refreshing.
  loopHousekeeping();

  if (tileLoopNowSuspended) {
    // The access point badge is not a tile, so nothing in the manager is live.
    gLiveTileId = -1;
    gLivePrio   = "";
    serviceHttp();
    return;
  }

  // WiFi Connection Lost detects the connected > disconnected transition.
  // There is no station connection to lose while the access point is up, and
  // wifiWasConnected starts out true - so without this the first iteration
  // after booting into AP announced a drop that never happened. Holding it
  // false also means reconnecting later reads as a clean false -> true, with no
  // spurious alert on the way back.
  if (provisionMode) {
    wifiWasConnected = false;
  } else {
    bool wifiNowConnected = (WiFi.status() == WL_CONNECTED);
    // A network being tried takes the station off its own on purpose - to test
    // the new one, and again to go back after a wrong password. That is not a
    // connection lost: what was true before the try stands until it has ended,
    // and only a clock that did not get back to a network then says so.
    if (wifiJoinBusy()) wifiNowConnected = wifiWasConnected;
    if (wifiWasConnected && !wifiNowConnected) {
      nbPlayPreset(eventSoundWifi, BZ_CAT_AUTO);
      // On the panel too, if the Alerts tile is asked to - without its own
      // sound: the one above is this event's.
      if (webAlertNet) webAlertShow("No internet", "Fara internet", warningIcon, false);
      // Something the owner would want to see, so it counts as activity.
      noteActivity();
    }
    wifiWasConnected = wifiNowConnected;
  }

  if (nowPlayingActive && (npStopRequested || millis() - lastNowPlayingMs > NOW_PLAYING_TIMEOUT_MS)) {
    npStopRequested = false;
    nowPlayingActive = false;
    npIsPaused = false;
    nowPlayingBuf[0] = '\0';
    // npPriorityWasActive is left alone on purpose: npPriorityTick() sees the
    // tile is no longer active on this same pass and hands the panel back to
    // the circuit tile with its transition. Clearing it here skipped that, and
    // a tile that redraws only when its content changes - the date, a canvas -
    // was left showing the song that had stopped.

    if (!nowPlayingIsPriority && items[currentSlot].id == ITEM_NOW_PLAYING) {
      advanceSlot();
      serviceHttp();
      if (tileLoopSuspended()) return;
      return;
    }
  }

  // Show IP Address (touch action) takes precedence over everything else; it is a
  // short, user-triggered, one-shot display and is not part of the reorderable
  // Priority Tiles order.
  if (ipTick()) {
    // Show IP Address is a touch action, not a row in the manager.
    gLiveTileId = -1;
    gLivePrio   = "";
    serviceHttp();
    if (tileLoopSuspended()) return;
    return;
  }

  // Priority Tiles: the first active tile in the configured order interrupts the normal loop
  for (int pi = 0; pi < NUM_PRIORITY_IDS; pi++) {
    bool handled = false;
    switch (priorityOrder[pi]) {
      // Both slots run the same renderer; webAccessAlertActive decides which
      // one the message on screen belongs to, so each gets its own rank.
      case PRIORITY_ID_NOTIF:      handled = !webAccessAlertActive && notifTick(); break;
      case PRIORITY_ID_WEB:        handled =  webAccessAlertActive && notifTick(); break;
      case PRIORITY_ID_ETS2:       handled = ets2Tick();       break;
      case PRIORITY_ID_VOICEMEETER: handled = vmPriorityTick(); break;
      case PRIORITY_ID_BROWSER_DOWNLOADS: handled = browserDownloadsPriorityTick(); break;
      case PRIORITY_ID_QBIT:       handled = qbitPriorityTick(); break;
      case PRIORITY_ID_BAMBU:      handled = bambuPriorityTick(); break;
      case PRIORITY_ID_OBS:        handled = obsPriorityTick(); break;
      case PRIORITY_ID_BEAMNG:     handled = beamngPriorityTick(); break;
      case PRIORITY_ID_ASSETTO:    handled = assettoPriorityTick(); break;
      case PRIORITY_ID_GAMEFPS:    handled = gameFpsPriorityTick(); break;
      case PRIORITY_ID_VOLUME:     handled = volumePriorityTick(); break;
      case PRIORITY_ID_NFSU2:      handled = nfsu2PriorityTick(); break;
      case PRIORITY_ID_NFSMW:      handled = nfsmwPriorityTick(); break;
      case PRIORITY_ID_NFSC:       handled = nfscPriorityTick(); break;
      case PRIORITY_ID_LOW_BATTERY: handled = batteryAlertTick(); break;
      case PRIORITY_ID_NOWPLAYING: handled = npPriorityTick(); break;
      case PRIORITY_ID_STOPWATCH:  handled = swPriorityTick(); break;
      case PRIORITY_ID_TIMER:      handled = timerPriorityTick(); break;
      case PRIORITY_ID_ALARM:      handled = alarmPriorityTick(); break;
    }
    if (handled) {
      gLiveTileId = -1;
      gLivePrio   = priorityIdName(priorityOrder[pi]);
      serviceHttp();
      if (tileLoopSuspended()) return;
      return;
    }
  }

  // The tile on the panel may have been switched off, taken out of the Tile
  // Manager or moved into the priority list since it was entered. Move on at
  // once, the way a tile's time running out does, rather than keep it up.
  if (!followCurrentSlot()) {
    if (nextActiveSlot(currentSlot) >= 0) advanceSlot();
    else if (!gCircuitBlank) clearCircuitPanel();
    gLiveTileId = -1;
    gLivePrio   = "";
    serviceHttp();
    return;
  }
  if (gCircuitBlank) {
    // Something to show again after a spell with nothing.
    slotStartMs = millis();
    resetSlotState();
    enterSlot(false);
  }

  CycleItem& cur = items[currentSlot];
  gLiveTileId = (int8_t)cur.id;
  gLivePrio   = "";

  if (cur.id == ITEM_NOW_PLAYING) {

    if (notifActive) {
      serviceHttp();
      if (tileLoopSuspended()) return;
      return;
    }
    unsigned long now = millis();
    switch (npState) {

      case NP_SHOW_START:
        break;

      case NP_PAUSE_BEFORE_RIGHT:
        if (now - npPauseStartMs >= NP_PAUSE_MS) {
          if (scrollIsWrap(scrollTypeNowPlaying)) {
            npWrapPass = 0;
            npScrollPos = 0;
            npState = NP_SCROLL_WRAP;
          } else {
            npState = NP_SCROLL_RIGHT;
          }
          npLastScrollMs = now;
        }
        break;

      case NP_SCROLL_WRAP: {
        if (now - npLastScrollMs >= spd(SPD_NOWPLAYING)) {
          npLastScrollMs = now;
          npScrollPos++;
          if (npScrollPos >= npColCount) {
            npWrapPass++;
            if (npWrapPass >= SCROLL_WRAP_PASSES) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            npScrollPos = -((hideIconNowPlaying || scrollIconInBuffer(scrollTypeNowPlaying)) ? 32 : NP_TEXT_COLS);
          }
          npDrawAtPos(npScrollPos);
        }
        break;
      }

      case NP_SCROLL_RIGHT: {
        int maxPos = npColCount - ((hideIconNowPlaying || scrollIconInBuffer(scrollTypeNowPlaying)) ? 32 : NP_TEXT_COLS);
        if (maxPos <= 0) {
          npState = NP_PAUSE_SHORT;
          npPauseStartMs = now;
          break;
        }
        if (now - npLastScrollMs >= spd(SPD_NOWPLAYING)) {
          npLastScrollMs = now;
          npScrollPos++;
          npDrawAtPos(npScrollPos);
          if (npScrollPos >= maxPos) {
            npScrollPos = maxPos;
            npState = NP_PAUSE_AFTER_RIGHT;
            npPauseStartMs = now;
          }
        }
        break;
      }

      case NP_PAUSE_AFTER_RIGHT:
        if (now - npPauseStartMs >= NP_PAUSE_MS) {
          npState = NP_SCROLL_LEFT;
          npLastScrollMs = now;
        }
        break;

      case NP_SCROLL_LEFT:
        if (now - npLastScrollMs >= spd(SPD_NOWPLAYING)) {
          npLastScrollMs = now;
          npScrollPos--;
          if (npScrollPos <= 0) {
            npScrollPos = 0;
            npDrawAtPos(0);
            npState = NP_PAUSE_BEFORE_NEXT;
            npPauseStartMs = now;
          } else {
            npDrawAtPos(npScrollPos);
          }
        }
        break;

      case NP_PAUSE_BEFORE_NEXT:
        if (now - npPauseStartMs >= NP_PAUSE_MS) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;

      case NP_PAUSE_SHORT:
        if (now - npPauseStartMs >= 4000UL) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
    }

    serviceHttp();
    if (tileLoopSuspended()) return;
    return;
  }

  // WEATHER SCROLL (identical to NP)
  if (cur.id == ITEM_WEATHER) {
    unsigned long now = millis();
    switch (wxState) {
      case NP_SHOW_START:
        break;
      case NP_PAUSE_BEFORE_RIGHT:
        if (now - wxPauseStartMs >= NP_PAUSE_MS) {
          if (scrollIsWrap(scrollTypeWeather)) {
            wxWrapPass = 0;
            wxScrollPos = 0;
            wxState = NP_SCROLL_WRAP;
          } else {
            wxState = NP_SCROLL_RIGHT;
          }
          wxLastScrollMs = now;
        }
        break;
      case NP_SCROLL_WRAP: {
        if (now - wxLastScrollMs >= spd(SPD_WEATHER)) {
          wxLastScrollMs = now;
          wxScrollPos++;
          if (wxScrollPos >= wxColCount) {
            wxWrapPass++;
            if (wxWrapPass >= SCROLL_WRAP_PASSES) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            wxScrollPos = -((hideIconWeather || scrollIconInBuffer(scrollTypeWeather)) ? 32 : NP_TEXT_COLS);
          }
          weatherDrawAtPos(wxScrollPos);
        }
        break;
      }
      case NP_SCROLL_RIGHT: {
        int maxPos = wxColCount - ((hideIconWeather || scrollIconInBuffer(scrollTypeWeather)) ? 32 : NP_TEXT_COLS);
        if (maxPos <= 0) {
          wxState = NP_PAUSE_SHORT;
          wxPauseStartMs = now;
          break;
        }
        if (now - wxLastScrollMs >= spd(SPD_WEATHER)) {
          wxLastScrollMs = now;
          wxScrollPos++;
          weatherDrawAtPos(wxScrollPos);
          if (wxScrollPos >= maxPos) {
            wxScrollPos = maxPos;
            wxState = NP_PAUSE_AFTER_RIGHT;
            wxPauseStartMs = now;
          }
        }
        break;
      }
      case NP_PAUSE_AFTER_RIGHT:
        if (now - wxPauseStartMs >= NP_PAUSE_MS) {
          wxState = NP_SCROLL_LEFT;
          wxLastScrollMs = now;
        }
        break;
      case NP_SCROLL_LEFT:
        if (now - wxLastScrollMs >= spd(SPD_WEATHER)) {
          wxLastScrollMs = now;
          wxScrollPos--;
          if (wxScrollPos <= 0) {
            wxScrollPos = 0;
            weatherDrawAtPos(0);
            wxState = NP_PAUSE_BEFORE_NEXT;
            wxPauseStartMs = now;
          } else {
            weatherDrawAtPos(wxScrollPos);
          }
        }
        break;
      case NP_PAUSE_BEFORE_NEXT:
        if (now - wxPauseStartMs >= NP_PAUSE_MS) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
      case NP_PAUSE_SHORT:
        if (now - wxPauseStartMs >= 4000UL) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
    }
    serviceHttp();
    if (tileLoopSuspended()) return;
    return;
  }

  // CURRENCY STANDARDS 
  if (cur.id == ITEM_CURRENCY) {
    unsigned long now = millis();
    switch (currState) {
      case NP_SHOW_START:
        break;
      case NP_PAUSE_BEFORE_RIGHT:
        if (now - currPauseMs >= NP_PAUSE_MS) {
          if (scrollIsWrap(scrollTypeCurrency)) {
            currWrapPass = 0;
            currScrollPos = 0;
            currState = NP_SCROLL_WRAP;
          } else {
            currState = NP_SCROLL_RIGHT;
          }
          currLastScrollMs = now;
        }
        break;
      case NP_SCROLL_WRAP: {
        if (now - currLastScrollMs >= spd(SPD_CURRENCY)) {
          currLastScrollMs = now;
          currScrollPos++;
          if (currScrollPos >= currColCount) {
            currWrapPass++;
            if (currWrapPass >= SCROLL_WRAP_PASSES) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            currScrollPos = -((hideIconCurrency || scrollIconInBuffer(scrollTypeCurrency)) ? 32 : PRESSURE_TEXT_COLS);
          }
          currencyDrawAtPos(currScrollPos);
        }
        break;
      }
      case NP_SCROLL_RIGHT: {
        int maxPos = currColCount - ((hideIconCurrency || scrollIconInBuffer(scrollTypeCurrency)) ? 32 : PRESSURE_TEXT_COLS);
        if (maxPos <= 0) {
          currState = NP_PAUSE_SHORT;
          currPauseMs = now;
          break;
        }
        if (now - currLastScrollMs >= spd(SPD_CURRENCY)) {
          currLastScrollMs = now;
          currScrollPos++;
          currencyDrawAtPos(currScrollPos);
          if (currScrollPos >= maxPos) {
            currScrollPos = maxPos;
            currState = NP_PAUSE_AFTER_RIGHT;
            currPauseMs = now;
          }
        }
        break;
      }
      case NP_PAUSE_AFTER_RIGHT:
        if (now - currPauseMs >= NP_PAUSE_MS) {
          currState = NP_SCROLL_LEFT;
          currLastScrollMs = now;
        }
        break;
      case NP_SCROLL_LEFT:
        if (now - currLastScrollMs >= spd(SPD_CURRENCY)) {
          currLastScrollMs = now;
          currScrollPos--;
          if (currScrollPos <= 0) {
            currScrollPos = 0;
            currencyDrawAtPos(0);
            currState = NP_PAUSE_BEFORE_NEXT;
            currPauseMs = now;
          } else {
            currencyDrawAtPos(currScrollPos);
          }
        }
        break;
      case NP_PAUSE_BEFORE_NEXT:
        if (now - currPauseMs >= NP_PAUSE_MS) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
      case NP_PAUSE_SHORT:
        if (now - currPauseMs >= 4000UL) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
    }
    serviceHttp();
    if (tileLoopSuspended()) return;
    return;
  }
  // SOCIAL COUNTER TILES - same scroll behaviour as the currency tile
  // When the text already fits, nothing below runs: the tile falls through to
  // the generic slot timer at the end of loop(), so it honours its configured
  // display time instead of the scroll machine's own pauses.
  if (socialIndexForItem(cur.id) >= 0 && socNeedsScroll) {
    unsigned long now = millis();
    switch (socState) {
      case NP_SHOW_START:
        break;
      case NP_PAUSE_BEFORE_RIGHT:
        if (now - socPauseMs >= NP_PAUSE_MS) {
          if (scrollIsWrap(scrollTypeYoutube)) {
            socWrapPass = 0;
            socScrollPos = 0;
            socState = NP_SCROLL_WRAP;
          } else {
            socState = NP_SCROLL_RIGHT;
          }
          socLastScrollMs = now;
        }
        break;
      case NP_SCROLL_WRAP: {
        if (now - socLastScrollMs >= spd(SPD_YOUTUBE)) {
          socLastScrollMs = now;
          socScrollPos++;
          if (socScrollPos >= socColCount) {
            socWrapPass++;
            if (socWrapPass >= SCROLL_WRAP_PASSES) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            socScrollPos = -((hideIconYoutube || scrollIconInBuffer(scrollTypeYoutube)) ? 32 : PRESSURE_TEXT_COLS);
          }
          socialDrawAtPos(socScrollPos);
        }
        break;
      }
      case NP_SCROLL_RIGHT: {
        int maxPos = socColCount - ((hideIconYoutube || scrollIconInBuffer(scrollTypeYoutube)) ? 32 : PRESSURE_TEXT_COLS);
        if (maxPos <= 0) {
          socState = NP_PAUSE_SHORT;
          socPauseMs = now;
          break;
        }
        if (now - socLastScrollMs >= spd(SPD_YOUTUBE)) {
          socLastScrollMs = now;
          socScrollPos++;
          socialDrawAtPos(socScrollPos);
          if (socScrollPos >= maxPos) {
            socScrollPos = maxPos;
            socState = NP_PAUSE_AFTER_RIGHT;
            socPauseMs = now;
          }
        }
        break;
      }
      case NP_PAUSE_AFTER_RIGHT:
        if (now - socPauseMs >= NP_PAUSE_MS) {
          socState = NP_SCROLL_LEFT;
          socLastScrollMs = now;
        }
        break;
      case NP_SCROLL_LEFT:
        if (now - socLastScrollMs >= spd(SPD_YOUTUBE)) {
          socLastScrollMs = now;
          socScrollPos--;
          if (socScrollPos <= 0) {
            socScrollPos = 0;
            socialDrawAtPos(0);
            socState = NP_PAUSE_BEFORE_NEXT;
            socPauseMs = now;
          } else {
            socialDrawAtPos(socScrollPos);
          }
        }
        break;
      case NP_PAUSE_BEFORE_NEXT:
        if (now - socPauseMs >= NP_PAUSE_MS) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
      case NP_PAUSE_SHORT:
        if (now - socPauseMs >= 4000UL) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
    }
    serviceHttp();
    if (tileLoopSuspended()) return;
    return;
  }


  // COUNTDOWN - as the social counters: a text that fits falls through to the
  // generic slot timer, so it keeps the tile's display time; a longer one
  // scrolls, and moves on once it has been read.
  if (cur.id == ITEM_COUNTDOWN && cdNeedsScroll) {
    unsigned long now = millis();
    switch (cdState) {
      case NP_SHOW_START:
        break;
      case NP_PAUSE_BEFORE_RIGHT:
        if (now - cdPauseMs >= NP_PAUSE_MS) {
          if (scrollIsWrap(scrollTypeCountdown)) {
            cdWrapPass = 0;
            cdScrollPos = 0;
            cdState = NP_SCROLL_WRAP;
          } else {
            cdState = NP_SCROLL_RIGHT;
          }
          cdLastScrollMs = now;
        }
        break;
      case NP_SCROLL_WRAP: {
        if (now - cdLastScrollMs >= spd(SPD_COUNTDOWN)) {
          cdLastScrollMs = now;
          cdScrollPos++;
          if (cdScrollPos >= cdColCount) {
            cdWrapPass++;
            if (cdWrapPass >= SCROLL_WRAP_PASSES) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            cdScrollPos = -((hideIconCountdown || scrollIconInBuffer(scrollTypeCountdown)) ? 32 : NP_TEXT_COLS);
          }
          countdownDrawAtPos(cdScrollPos);
        }
        break;
      }
      case NP_SCROLL_RIGHT: {
        int maxPos = cdColCount - ((hideIconCountdown || scrollIconInBuffer(scrollTypeCountdown)) ? 32 : NP_TEXT_COLS);
        if (maxPos <= 0) {
          cdState = NP_PAUSE_SHORT;
          cdPauseMs = now;
          break;
        }
        if (now - cdLastScrollMs >= spd(SPD_COUNTDOWN)) {
          cdLastScrollMs = now;
          cdScrollPos++;
          countdownDrawAtPos(cdScrollPos);
          if (cdScrollPos >= maxPos) {
            cdScrollPos = maxPos;
            cdState = NP_PAUSE_AFTER_RIGHT;
            cdPauseMs = now;
          }
        }
        break;
      }
      case NP_PAUSE_AFTER_RIGHT:
        if (now - cdPauseMs >= NP_PAUSE_MS) {
          cdState = NP_SCROLL_LEFT;
          cdLastScrollMs = now;
        }
        break;
      case NP_SCROLL_LEFT:
        if (now - cdLastScrollMs >= spd(SPD_COUNTDOWN)) {
          cdLastScrollMs = now;
          cdScrollPos--;
          if (cdScrollPos <= 0) {
            cdScrollPos = 0;
            countdownDrawAtPos(0);
            cdState = NP_PAUSE_BEFORE_NEXT;
            cdPauseMs = now;
          } else {
            countdownDrawAtPos(cdScrollPos);
          }
        }
        break;
      case NP_PAUSE_BEFORE_NEXT:
        if (now - cdPauseMs >= NP_PAUSE_MS) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
      case NP_PAUSE_SHORT:
        if (now - cdPauseMs >= 4000UL) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
    }
    serviceHttp();
    if (tileLoopSuspended()) return;
    return;
  }

  // MEMENTO 
  if (cur.id == ITEM_MEMENTO) {
    unsigned long now = millis();
    switch (mementoState) {
      case NP_SHOW_START:
        break;
      case NP_PAUSE_BEFORE_RIGHT:
        if (now - mementoPauseMs >= NP_PAUSE_MS) {
          if (scrollIsWrap(scrollTypeReminder)) {
            mementoWrapPass = 0;
            mementoScrollPos = 0;
            mementoState = NP_SCROLL_WRAP;
          } else {
            mementoState = NP_SCROLL_RIGHT;
          }
          mementoLastScrollMs = now;
        }
        break;
      case NP_SCROLL_WRAP: {
        if (now - mementoLastScrollMs >= spd(SPD_REMINDER)) {
          mementoLastScrollMs = now;
          mementoScrollPos++;
          if (mementoScrollPos >= mementoColCount) {
            mementoWrapPass++;
            if (mementoWrapPass >= SCROLL_WRAP_PASSES) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            mementoScrollPos = -((hideIconReminder || scrollIconInBuffer(scrollTypeReminder)) ? 32 : NP_TEXT_COLS);
          }
          mementoDrawAtPos(mementoScrollPos);
        }
        break;
      }
      case NP_SCROLL_RIGHT: {
        int maxPos = mementoColCount - ((hideIconReminder || scrollIconInBuffer(scrollTypeReminder)) ? 32 : NP_TEXT_COLS);
        if (maxPos <= 0) {
          mementoState = NP_PAUSE_SHORT;
          mementoPauseMs = now;
          break;
        }
        if (now - mementoLastScrollMs >= spd(SPD_REMINDER)) {
          mementoLastScrollMs = now;
          mementoScrollPos++;
          mementoDrawAtPos(mementoScrollPos);
          if (mementoScrollPos >= maxPos) {
            mementoScrollPos = maxPos;
            mementoState = NP_PAUSE_AFTER_RIGHT;
            mementoPauseMs = now;
          }
        }
        break;
      }
      case NP_PAUSE_AFTER_RIGHT:
        if (now - mementoPauseMs >= NP_PAUSE_MS) {
          mementoState = NP_SCROLL_LEFT;
          mementoLastScrollMs = now;
        }
        break;
      case NP_SCROLL_LEFT:
        if (now - mementoLastScrollMs >= spd(SPD_REMINDER)) {
          mementoLastScrollMs = now;
          mementoScrollPos--;
          if (mementoScrollPos <= 0) {
            mementoScrollPos = 0;
            mementoDrawAtPos(0);
            mementoState = NP_PAUSE_BEFORE_NEXT;
            mementoPauseMs = now;
          } else {
            mementoDrawAtPos(mementoScrollPos);
          }
        }
        break;
      case NP_PAUSE_BEFORE_NEXT:
        if (now - mementoPauseMs >= NP_PAUSE_MS) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
      case NP_PAUSE_SHORT:
        if (now - mementoPauseMs >= 4000UL) {
          advanceSlot();
          serviceHttp();
          if (tileLoopSuspended()) return;
          return;
        }
        break;
    }
    serviceHttp();
    if (tileLoopSuspended()) return;
    return;
  }

  serviceHttp();
  if (tileLoopSuspended()) return;

  unsigned long nowMs = millis();

  switch (cur.id) {
    case ITEM_HOUR:

      if (nowMs - gLastStaticDrawMs >= 200) {
        gLastStaticDrawMs = nowMs;
        drawHour();
      }
      break;
    case ITEM_HOURWEEK:
      if (nowMs - gLastStaticDrawMs >= 200) {
        gLastStaticDrawMs = nowMs;
        drawHourWeekday();
      }
      break;
    case ITEM_DATE: {

      if (!gStaticDrawDone) {
        gStaticDrawDone = dateInit();
      }

      // dateState stays NP_SHOW_START until dateInit() starts the scroll, and
      // dateNeedsScroll survives from the previous time this tile was shown -
      // so the two can disagree. Entering the state machine on that combination
      // lands on `default:` and returns out of loop() on every pass, without
      // ever reaching the slot-duration check below: the tile could never hand
      // over. Falling through instead keeps the rotation going whatever
      // happened to the draw.
      if (dateNeedsScroll && dateState != NP_SHOW_START) {
        unsigned long now = millis();
        unsigned long halfSlot = (unsigned long)(cur.durationSec / 2) * 1000UL;
        if (halfSlot < 1000UL) halfSlot = 1000UL;
        int dateEffTextCols = (hideIconDate || scrollIconInBuffer(scrollTypeDate)) ? 32 : DATE_TEXT_COLS;
        int maxScroll = dateColCount - dateEffTextCols;
        if (maxScroll < 0) maxScroll = 0;
        switch (dateState) {
          case NP_PAUSE_BEFORE_RIGHT:

            if (now - datePauseMs >= halfSlot) {
              if (scrollIsWrap(scrollTypeDate)) {
                dateWrapPass = 0;
                dateScrollPos = 0;
                dateState = NP_SCROLL_WRAP;
              } else {
                dateState = NP_SCROLL_RIGHT;
              }
              dateLastScrollMs = now;
            }
            break;
          case NP_SCROLL_WRAP:
            if (now - dateLastScrollMs >= spd(SPD_DATE)) {
              dateLastScrollMs = now;
              dateScrollPos++;
              if (dateScrollPos >= dateColCount) {
                dateWrapPass++;
                if (dateWrapPass >= SCROLL_WRAP_PASSES) {
                  advanceSlot();
                  serviceHttp();
                  if (tileLoopSuspended()) return;
                  return;
                }
                dateScrollPos = -dateEffTextCols;
              }
              dateDrawAtPos(dateScrollPos);
            }
            break;
          case NP_SCROLL_RIGHT:
            if (now - dateLastScrollMs >= spd(SPD_DATE)) {
              dateLastScrollMs = now;
              dateScrollPos++;
              dateDrawAtPos(dateScrollPos);
              if (dateScrollPos >= maxScroll) {
                dateScrollPos = maxScroll;
                dateState = NP_PAUSE_AFTER_RIGHT;
                datePauseMs = now;
              }
            }
            break;
          case NP_PAUSE_AFTER_RIGHT:
            if (now - datePauseMs >= NP_PAUSE_MS) {
              dateState = NP_SCROLL_LEFT;
              dateLastScrollMs = now;
            }
            break;
          case NP_SCROLL_LEFT:
            if (now - dateLastScrollMs >= spd(SPD_DATE)) {
              dateLastScrollMs = now;
              dateScrollPos--;
              if (dateScrollPos <= 0) {
                dateScrollPos = 0;
                dateDrawAtPos(0);
                dateState = NP_PAUSE_BEFORE_NEXT;
                datePauseMs = now;
              } else {
                dateDrawAtPos(dateScrollPos);
              }
            }
            break;
          case NP_PAUSE_BEFORE_NEXT:

            if (now - datePauseMs >= halfSlot) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            break;
          default: break;
        }

        serviceHttp();
        if (tileLoopSuspended()) return;
        return;
      }
      break;
    }
    case ITEM_TEMP: {

      if (!gStaticDrawDone) {
        float tf;
        if (bmpReadTempC(tf)) lastTemp = (int)round(tf);
        gStaticDrawDone = true;
        gTempShownValue = lastTemp;
        tempInit(lastTemp);
      } else if (gTempShownValue == TEMP_NO_READING && lastTemp != TEMP_NO_READING) {
        // The "--" placeholder is on screen and the sensor just answered.
        // Only re-init in this direction, so a normal reading changing by a
        // degree never restarts a scroll that is already running.
        gTempShownValue = lastTemp;
        tempInit(lastTemp);
      }

      if (tempNeedsScroll) {
        unsigned long now = millis();
        unsigned long halfSlot = (unsigned long)(cur.durationSec / 2) * 1000UL;
        if (halfSlot < 1000UL) halfSlot = 1000UL;
        int tempEffTextCols = (hideIconTemp || scrollIconInBuffer(scrollTypeTemp)) ? 32 : TEMP_TEXT_COLS;
        int maxScroll = tempColCount - tempEffTextCols;
        if (maxScroll < 0) maxScroll = 0;
        switch (tempState) {
          case NP_PAUSE_BEFORE_RIGHT:

            if (now - tempPauseMs >= halfSlot) {
              if (scrollIsWrap(scrollTypeTemp)) {
                tempWrapPass = 0;
                tempScrollPos = 0;
                tempState = NP_SCROLL_WRAP;
              } else {
                tempState = NP_SCROLL_RIGHT;
              }
              tempLastScrollMs = now;
            }
            break;
          case NP_SCROLL_WRAP:
            if (now - tempLastScrollMs >= spd(SPD_TEMP)) {
              tempLastScrollMs = now;
              tempScrollPos++;
              if (tempScrollPos >= tempColCount) {
                tempWrapPass++;
                if (tempWrapPass >= SCROLL_WRAP_PASSES) {
                  advanceSlot();
                  serviceHttp();
                  if (tileLoopSuspended()) return;
                  return;
                }
                tempScrollPos = -tempEffTextCols;
              }
              tempDrawAtPos(tempScrollPos);
            }
            break;
          case NP_SCROLL_RIGHT:
            if (now - tempLastScrollMs >= spd(SPD_TEMP)) {
              tempLastScrollMs = now;
              tempScrollPos++;
              tempDrawAtPos(tempScrollPos);
              if (tempScrollPos >= maxScroll) {
                tempScrollPos = maxScroll;
                tempState = NP_PAUSE_AFTER_RIGHT;
                tempPauseMs = now;
              }
            }
            break;
          case NP_PAUSE_AFTER_RIGHT:
            if (now - tempPauseMs >= NP_PAUSE_MS) {
              tempState = NP_SCROLL_LEFT;
              tempLastScrollMs = now;
            }
            break;
          case NP_SCROLL_LEFT:
            if (now - tempLastScrollMs >= spd(SPD_TEMP)) {
              tempLastScrollMs = now;
              tempScrollPos--;
              if (tempScrollPos <= 0) {
                tempScrollPos = 0;
                tempDrawAtPos(0);
                tempState = NP_PAUSE_BEFORE_NEXT;
                tempPauseMs = now;
              } else {
                tempDrawAtPos(tempScrollPos);
              }
            }
            break;
          case NP_PAUSE_BEFORE_NEXT:

            if (now - tempPauseMs >= halfSlot) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            break;
          default: break;
        }

        serviceHttp();
        if (tileLoopSuspended()) return;
        return;
      }
      break;
    }
    case ITEM_AFTERBURNER: {
      // New readings arrive about once a second; drawn only when the text changes.
      abRefresh();
      if (abNeedsScroll && abState != NP_SHOW_START) {
        unsigned long now = millis();
        unsigned long halfSlot = (unsigned long)(cur.durationSec / 2) * 1000UL;
        if (halfSlot < 1000UL) halfSlot = 1000UL;
        int abEffTextCols = abTextCols();
        int maxScroll = abColCount - abEffTextCols;
        if (maxScroll < 0) maxScroll = 0;
        switch (abState) {
          case NP_PAUSE_BEFORE_RIGHT:
            if (now - abPauseMs >= halfSlot) {
              if (scrollIsWrap(scrollTypeAfterburner)) {
                abWrapPass = 0;
                abScrollPos = 0;
                abState = NP_SCROLL_WRAP;
              } else {
                abState = NP_SCROLL_RIGHT;
              }
              abLastScrollMs = now;
            }
            break;
          case NP_SCROLL_WRAP:
            if (now - abLastScrollMs >= spd(SPD_AFTERBURNER)) {
              abLastScrollMs = now;
              abScrollPos++;
              if (abScrollPos >= abColCount) {
                abWrapPass++;
                if (abWrapPass >= SCROLL_WRAP_PASSES) {
                  advanceSlot();
                  serviceHttp();
                  if (tileLoopSuspended()) return;
                  return;
                }
                abScrollPos = -abEffTextCols;
              }
              abDrawAtPos(abScrollPos);
            }
            break;
          case NP_SCROLL_RIGHT:
            if (now - abLastScrollMs >= spd(SPD_AFTERBURNER)) {
              abLastScrollMs = now;
              abScrollPos++;
              if (abScrollPos >= maxScroll) {
                abScrollPos = maxScroll;
                abState = NP_PAUSE_AFTER_RIGHT;
                abPauseMs = now;
              }
              abDrawAtPos(abScrollPos);
            }
            break;
          case NP_PAUSE_AFTER_RIGHT:
            if (now - abPauseMs >= NP_PAUSE_MS) {
              abState = NP_SCROLL_LEFT;
              abLastScrollMs = now;
            }
            break;
          case NP_SCROLL_LEFT:
            if (now - abLastScrollMs >= spd(SPD_AFTERBURNER)) {
              abLastScrollMs = now;
              abScrollPos--;
              if (abScrollPos <= 0) {
                abScrollPos = 0;
                abDrawAtPos(0);
                abState = NP_PAUSE_BEFORE_NEXT;
                abPauseMs = now;
              } else {
                abDrawAtPos(abScrollPos);
              }
            }
            break;
          case NP_PAUSE_BEFORE_NEXT:
            if (now - abPauseMs >= halfSlot) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            break;
          default: break;
        }
        serviceHttp();
        if (tileLoopSuspended()) return;
        return;
      }
      break;
    }
    case ITEM_PRESSURE: {

      if (!gStaticDrawDone) {
        pressureSampleTick();
        gStaticDrawDone = true;
        pressureInit((int)round(lastPressureHpa));
      }

      if (pressureNeedsScroll) {
        unsigned long now = millis();
        unsigned long halfSlot = (unsigned long)(cur.durationSec / 2) * 1000UL;
        if (halfSlot < 1000UL) halfSlot = 1000UL;
        int pressureEffTextCols = (hideIconPressure || scrollIconInBuffer(scrollTypePressure)) ? 32 : PRESSURE_TEXT_COLS;
        int maxScroll = pressureColCount - pressureEffTextCols;
        if (maxScroll < 0) maxScroll = 0;
        switch (pressureState) {
          case NP_PAUSE_BEFORE_RIGHT:
            if (now - pressurePauseMs >= halfSlot) {
              if (scrollIsWrap(scrollTypePressure)) {
                pressureWrapPass = 0;
                pressureScrollPos = 0;
                pressureState = NP_SCROLL_WRAP;
              } else {
                pressureState = NP_SCROLL_RIGHT;
              }
              pressureLastScrollMs = now;
            }
            break;
          case NP_SCROLL_WRAP:
            if (now - pressureLastScrollMs >= spd(SPD_PRESSURE)) {
              pressureLastScrollMs = now;
              pressureScrollPos++;
              if (pressureScrollPos >= pressureColCount) {
                pressureWrapPass++;
                if (pressureWrapPass >= SCROLL_WRAP_PASSES) {
                  advanceSlot();
                  serviceHttp();
                  if (tileLoopSuspended()) return;
                  return;
                }
                pressureScrollPos = -pressureEffTextCols;
              }
              pressureDrawAtPos(pressureScrollPos);
            }
            break;
          case NP_SCROLL_RIGHT:
            if (now - pressureLastScrollMs >= spd(SPD_PRESSURE)) {
              pressureLastScrollMs = now;
              pressureScrollPos++;
              pressureDrawAtPos(pressureScrollPos);
              if (pressureScrollPos >= maxScroll) {
                pressureScrollPos = maxScroll;
                pressureState = NP_PAUSE_AFTER_RIGHT;
                pressurePauseMs = now;
              }
            }
            break;
          case NP_PAUSE_AFTER_RIGHT:
            if (now - pressurePauseMs >= NP_PAUSE_MS) {
              pressureState = NP_SCROLL_LEFT;
              pressureLastScrollMs = now;
            }
            break;
          case NP_SCROLL_LEFT:
            if (now - pressureLastScrollMs >= spd(SPD_PRESSURE)) {
              pressureLastScrollMs = now;
              pressureScrollPos--;
              if (pressureScrollPos <= 0) {
                pressureScrollPos = 0;
                pressureDrawAtPos(0);
                pressureState = NP_PAUSE_BEFORE_NEXT;
                pressurePauseMs = now;
              } else {
                pressureDrawAtPos(pressureScrollPos);
              }
            }
            break;
          case NP_PAUSE_BEFORE_NEXT:
            if (now - pressurePauseMs >= halfSlot) {
              advanceSlot();
              serviceHttp();
              if (tileLoopSuspended()) return;
              return;
            }
            break;
          default: break;
        }
        serviceHttp();
        if (tileLoopSuspended()) return;
        return;
      }
      break;
    }
    case ITEM_SCREENSAVER: {

      if (nowMs - slotStartMs >= (unsigned long)cur.durationSec * 1000UL) {
        if (nextActiveSlot(currentSlot) != currentSlot) {
          advanceSlot();
          return;
        }
        // A single selected animation can run indefinitely without resetting.
        // Random still chooses a new scene at the user's configured interval.
        slotStartMs = nowMs;
        if (ssAnimSelected == SS_ANIM_RANDOM) ssInit();
      }
      ssTick();
      return;
    }
  }

  if (nowMs - slotStartMs >= (unsigned long)cur.durationSec * 1000UL) {
    advanceSlot();
  }
}

static void displayTaskMain(void* parameter) {
  (void)parameter;
  for (;;) {
    displayLoopTick();
    // One RTOS tick is enough room for the lower-priority web task while the
    // millisecond timestamps above keep animation speed independent of load.
    vTaskDelay(1);
  }
}

void loop() {
  // If task allocation failed, retain the old cooperative behaviour rather
  // than booting with a frozen panel.
  if (!displayTaskHandle) {
    displayLoopTick();
    serviceHttp();
    screenStreamTick();
    settingsFlushTick();
    return;
  }

  backgroundServiceActive = true;
  serviceHttp();
  screenStreamTick();
  // Preferences writes and page transfers no longer sit on the animation path.
  settingsFlushTick();
  backgroundServiceActive = false;
  vTaskDelay(1);
}
