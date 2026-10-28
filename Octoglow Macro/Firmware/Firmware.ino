/*
 * Octoglow Macro firmware 0.2 - configuration protocol 12, Waveshare RP2040 Zero
 * Fiecare buton (b1..b7, knob) + rotirea encoder-ului (CW/CCW) are o
 * ACTIUNE configurabila, salvata in flash (EEPROM emulat). Configurarea
 * se face prin portul serial USB (CDC), care coexista cu interfata HID
 * de tastatura/media keys pe acelasi cablu USB - nu e nevoie de alt soft
 * in fundal, doar un terminal serial (Serial Monitor, PuTTY, etc) sau
 * aplicatia Octoglow Connect.
 *
 * Dupa ce salvezi o configuratie (SAVE / orice SET), placa functioneaza
 * complet standalone, fara PC-ul conectat la vreun soft - config-ul
 * ramane in flash intre reporniri.
 *
 * Biblioteci necesare:
 *   - Adafruit TinyUSB Library (for HID + CDC Serial)
 *   - Adafruit NeoPixel (for the onboard WS2812B)
 *   - RP2040 board package (Earle Philhower)
 *
 * Board settings in Arduino IDE:
 *   Board:    Raspberry Pi RP2040 Boards -> Waveshare RP2040 Zero
 *   USB Stack: Adafruit TinyUSB   (Serial devine automat CDC si se
 *              compune cu HID-ul custom de mai jos)
 *
 * See ../readme.md for the Connect configuration channel and flash format.
 */

#include "Adafruit_TinyUSB.h"
#include <Adafruit_NeoPixel.h>
#include <EEPROM.h>
#include <hardware/clocks.h>
#include <hardware/pwm.h>

// ───────────────────────────────────────────────
// PIN DEFINITIONS (conform layout fizic)
// ───────────────────────────────────────────────
#define PIN_BUZZER      1
#define PIN_LED         2
#define PIN_RGB_LED    16   // Waveshare RP2040 Zero onboard WS2812B

#define PIN_B1          3
#define PIN_B2          4
#define PIN_B3          5
#define PIN_B4          6
#define PIN_B5          7
#define PIN_B6          8
#define PIN_B7          14
#define PIN_KNOB_BTN    15

#define PIN_ENC_A       27
#define PIN_ENC_B       26

// Change only this switch for a build with eight ordinary keys on GPIO 15.
// The serial slot name stays "knob" so saved key mappings remain compatible.
#define OCTOGLOW_MACRO_HAS_ENCODER 1
#define OCTOGLOW_MACRO_FIRMWARE_VERSION "0.2"

#define SERIAL_BAUD     115200

// This Macro's onboard LED expects RGB wire order (red/green were swapped
// with GRB). Other board revisions can override this at build time.
#ifndef OCTOGLOW_MACRO_RGB_ORDER
#define OCTOGLOW_MACRO_RGB_ORDER NEO_RGB
#endif
Adafruit_NeoPixel tabPixel(1, PIN_RGB_LED, OCTOGLOW_MACRO_RGB_ORDER + NEO_KHZ800);

// ───────────────────────────────────────────────
// HID DESCRIPTOR (Consumer + Keyboard)
// ───────────────────────────────────────────────
uint8_t const hid_report_descriptor[] = {
  TUD_HID_REPORT_DESC_KEYBOARD(HID_REPORT_ID(1)),
  TUD_HID_REPORT_DESC_CONSUMER(HID_REPORT_ID(2)),
  TUD_HID_REPORT_DESC_MOUSE(HID_REPORT_ID(3))
};

Adafruit_USBD_HID usb_hid(hid_report_descriptor, sizeof(hid_report_descriptor),
                           HID_ITF_PROTOCOL_NONE, 2, false);

// ───────────────────────────────────────────────
// ACTION SYSTEM
// ───────────────────────────────────────────────
enum ActionType : uint8_t {
  ACT_NONE  = 0,
  ACT_KEY   = 1,   // modifiers + pana la 2 taste (acopera si combinatii proprii)
  ACT_MEDIA = 2,   // consumer control (volum, play/pause, etc)
  ACT_SCROLL = 3,  // key1: 1=up, 2=down
  ACT_TAB_NEXT = 4,
  ACT_TAB_PREV = 5,
  ACT_TAB_GOTO = 6, // key1: 1..tabCount
  ACT_APP = 7       // media: host-side application ID; never a path
};

struct ButtonAction {
  uint8_t  type;
  uint8_t  modifiers;  // bitmask KEYBOARD_MODIFIER_*
  uint8_t  key1;
  uint8_t  key2;       // 0 daca nu e folosita
  uint16_t media;
};

enum Slot : uint8_t {
  SLOT_B1 = 0, SLOT_B2, SLOT_B3, SLOT_B4, SLOT_B5, SLOT_B6, SLOT_B7,
  SLOT_KNOB, SLOT_ENC_CW, SLOT_ENC_CCW,
  SLOT_COUNT
};

const char* const SLOT_NAMES[SLOT_COUNT] = {
  "b1", "b2", "b3", "b4", "b5", "b6", "b7", "knob", "cw", "ccw"
};

#define MAX_TABS 5
ButtonAction tabActions[MAX_TABS][SLOT_COUNT];
uint8_t tabCount = 1;
uint8_t activeTab = 0;
// Preserve the existing terminal commands, which edit the active tab.
#define actions tabActions[activeTab]

// ───────────────────────────────────────────────
// STATE (nelegat de actiuni: LED / buzzer)
// ───────────────────────────────────────────────
bool     buzzerEnabled = true;
uint8_t  buzzerVolume  = 100; // 0..100; 100 preserves the original loudness
uint8_t  ledEffect     = 0;   // 0=static, 1=fade, 2=blink
uint8_t  ledOutputMode = 0;   // 0=backlight + RGB, 1=RGB only
bool     lightingEnabled = true;
uint8_t  tabEffects[MAX_TABS] = {0, 0, 0, 0, 0};
uint8_t  tabRgbEffects[MAX_TABS] = {0, 0, 0, 0, 0};
uint8_t  tonePack = 0;       // 0..4 built-in, 5..9 custom
uint8_t  tabTonePacks[MAX_TABS] = {0, 0, 0, 0, 0};
uint8_t  ledBrightness = 100; // 0..100, external LEDs
uint8_t  rgbBrightness = 100; // 0..100, addressable LED
#define TAB_NAME_BYTES 24
char     tabNames[MAX_TABS][TAB_NAME_BYTES + 1] = {};
uint8_t  tabIcons[MAX_TABS] = {0, 0, 0, 0, 0};
#define TONE_EVENTS 18
#define CUSTOM_TONE_PACKS 5
uint16_t customTones[CUSTOM_TONE_PACKS][TONE_EVENTS];
// Empty name means an unused custom preset; frequencies remain independently stored.
char customToneNames[CUSTOM_TONE_PACKS][TAB_NAME_BYTES + 1] = {};
struct TabColor { uint8_t red, green, blue; };
const TabColor DEFAULT_TAB_COLOR = {36, 171, 255};
TabColor tabColors[MAX_TABS];
uint8_t  shownRgbTab = 0xFF;
bool     rgbDirty = true;
uint32_t buzzerStopAt  = 0;
uint16_t buzzerDuty = 0;

#define HOLD_THRESHOLD_MS 600

struct HoldBtnState {
  bool     pressed;
  bool     holdFired;
  uint32_t pressTime;
  bool     lastRaw;
  bool     stableState;
  uint32_t lastDebounce;
};

HoldBtnState btnKnob = {false, false, 0, HIGH, HIGH, 0};
HoldBtnState btnB7   = {false, false, 0, HIGH, HIGH, 0};

uint32_t lastLedUpdate  = 0;
int16_t  fadeBrightness = 0;
int8_t   fadeDir        = 1;
bool     blinkState     = false;

// ───────────────────────────────────────────────
// NAME LOOKUP TABLES (pentru protocolul serial)
// ───────────────────────────────────────────────
struct NameVal8  { const char* name; uint8_t  val; };
struct NameVal16 { const char* name; uint16_t val; };

const NameVal8 MOD_NAMES[] = {
  {"CTRL",  KEYBOARD_MODIFIER_LEFTCTRL},  {"LCTRL",  KEYBOARD_MODIFIER_LEFTCTRL},
  {"RCTRL", KEYBOARD_MODIFIER_RIGHTCTRL},
  {"SHIFT", KEYBOARD_MODIFIER_LEFTSHIFT}, {"LSHIFT", KEYBOARD_MODIFIER_LEFTSHIFT},
  {"RSHIFT",KEYBOARD_MODIFIER_RIGHTSHIFT},
  {"ALT",   KEYBOARD_MODIFIER_LEFTALT},   {"LALT",   KEYBOARD_MODIFIER_LEFTALT},
  {"RALT",  KEYBOARD_MODIFIER_RIGHTALT},
  {"GUI",   KEYBOARD_MODIFIER_LEFTGUI},   {"WIN",    KEYBOARD_MODIFIER_LEFTGUI},
  {"LGUI",  KEYBOARD_MODIFIER_LEFTGUI},
  {"RGUI",  KEYBOARD_MODIFIER_RIGHTGUI},  {"RWIN",   KEYBOARD_MODIFIER_RIGHTGUI},
};
const uint8_t MOD_NAMES_COUNT = sizeof(MOD_NAMES) / sizeof(MOD_NAMES[0]);

const NameVal8 KEY_NAMES[] = {
  {"A",HID_KEY_A},{"B",HID_KEY_B},{"C",HID_KEY_C},{"D",HID_KEY_D},{"E",HID_KEY_E},
  {"F",HID_KEY_F},{"G",HID_KEY_G},{"H",HID_KEY_H},{"I",HID_KEY_I},{"J",HID_KEY_J},
  {"K",HID_KEY_K},{"L",HID_KEY_L},{"M",HID_KEY_M},{"N",HID_KEY_N},{"O",HID_KEY_O},
  {"P",HID_KEY_P},{"Q",HID_KEY_Q},{"R",HID_KEY_R},{"S",HID_KEY_S},{"T",HID_KEY_T},
  {"U",HID_KEY_U},{"V",HID_KEY_V},{"W",HID_KEY_W},{"X",HID_KEY_X},{"Y",HID_KEY_Y},{"Z",HID_KEY_Z},
  {"0",HID_KEY_0},{"1",HID_KEY_1},{"2",HID_KEY_2},{"3",HID_KEY_3},{"4",HID_KEY_4},
  {"5",HID_KEY_5},{"6",HID_KEY_6},{"7",HID_KEY_7},{"8",HID_KEY_8},{"9",HID_KEY_9},
  {"F1",HID_KEY_F1},{"F2",HID_KEY_F2},{"F3",HID_KEY_F3},{"F4",HID_KEY_F4},
  {"F5",HID_KEY_F5},{"F6",HID_KEY_F6},{"F7",HID_KEY_F7},{"F8",HID_KEY_F8},
  {"F9",HID_KEY_F9},{"F10",HID_KEY_F10},{"F11",HID_KEY_F11},{"F12",HID_KEY_F12},
  {"F13",HID_KEY_F13},{"F14",HID_KEY_F14},{"F15",HID_KEY_F15},{"F16",HID_KEY_F16},
  {"F17",HID_KEY_F17},{"F18",HID_KEY_F18},{"F19",HID_KEY_F19},{"F20",HID_KEY_F20},
  {"F21",HID_KEY_F21},{"F22",HID_KEY_F22},{"F23",HID_KEY_F23},{"F24",HID_KEY_F24},
  {"UP",HID_KEY_ARROW_UP},{"DOWN",HID_KEY_ARROW_DOWN},
  {"LEFT",HID_KEY_ARROW_LEFT},{"RIGHT",HID_KEY_ARROW_RIGHT},
  {"ESC",HID_KEY_ESCAPE},{"ESCAPE",HID_KEY_ESCAPE},
  {"ENTER",HID_KEY_ENTER},{"RETURN",HID_KEY_ENTER},
  {"TAB",HID_KEY_TAB},
  {"SPACE",HID_KEY_SPACE},
  {"BACKSPACE",HID_KEY_BACKSPACE},
  {"DELETE",HID_KEY_DELETE},{"DEL",HID_KEY_DELETE},
  {"INSERT",HID_KEY_INSERT},{"INS",HID_KEY_INSERT},
  {"HOME",HID_KEY_HOME},
  {"END",HID_KEY_END},
  {"PAGEUP",HID_KEY_PAGE_UP},{"PGUP",HID_KEY_PAGE_UP},
  {"PAGEDOWN",HID_KEY_PAGE_DOWN},{"PGDN",HID_KEY_PAGE_DOWN},
  {"CAPSLOCK",HID_KEY_CAPS_LOCK},
  {"PRTSCR",HID_KEY_PRINT_SCREEN},{"PRINTSCREEN",HID_KEY_PRINT_SCREEN},
  {"PAUSE",HID_KEY_PAUSE},
  {"MENU",HID_KEY_APPLICATION},{"APP",HID_KEY_APPLICATION},
  {"MINUS",HID_KEY_MINUS},{"EQUAL",HID_KEY_EQUAL},
  {"COMMA",HID_KEY_COMMA},{"PERIOD",HID_KEY_PERIOD},{"SLASH",HID_KEY_SLASH},
};
const uint8_t KEY_NAMES_COUNT = sizeof(KEY_NAMES) / sizeof(KEY_NAMES[0]);

const NameVal16 MEDIA_NAMES[] = {
  {"MUTE",HID_USAGE_CONSUMER_MUTE},
  {"BRIGHTUP",HID_USAGE_CONSUMER_BRIGHTNESS_INCREMENT},
  {"BRIGHTDOWN",HID_USAGE_CONSUMER_BRIGHTNESS_DECREMENT},
  {"VOLUP",HID_USAGE_CONSUMER_VOLUME_INCREMENT},{"VOLUMEUP",HID_USAGE_CONSUMER_VOLUME_INCREMENT},
  {"VOLDOWN",HID_USAGE_CONSUMER_VOLUME_DECREMENT},{"VOLUMEDOWN",HID_USAGE_CONSUMER_VOLUME_DECREMENT},
  {"PLAY",HID_USAGE_CONSUMER_PLAY_PAUSE},{"PLAYPAUSE",HID_USAGE_CONSUMER_PLAY_PAUSE},
  {"NEXT",HID_USAGE_CONSUMER_SCAN_NEXT_TRACK},
  {"PREV",HID_USAGE_CONSUMER_SCAN_PREVIOUS_TRACK},{"PREVIOUS",HID_USAGE_CONSUMER_SCAN_PREVIOUS_TRACK},
  {"STOP",HID_USAGE_CONSUMER_STOP},
  {"CALC",HID_USAGE_CONSUMER_AL_CALCULATOR},{"CALCULATOR",HID_USAGE_CONSUMER_AL_CALCULATOR},
  {"MAIL",HID_USAGE_CONSUMER_AL_EMAIL_READER},{"EMAIL",HID_USAGE_CONSUMER_AL_EMAIL_READER},
  {"BROWSER",HID_USAGE_CONSUMER_AL_LOCAL_MACHINE_BROWSER},{"WWW",HID_USAGE_CONSUMER_AL_LOCAL_MACHINE_BROWSER},
  {"WEBSEARCH",HID_USAGE_CONSUMER_AC_SEARCH},
  {"WEBHOME",HID_USAGE_CONSUMER_AC_HOME},
  {"WEBBACK",HID_USAGE_CONSUMER_AC_BACK},
  {"WEBFORWARD",HID_USAGE_CONSUMER_AC_FORWARD},
  {"WEBREFRESH",HID_USAGE_CONSUMER_AC_REFRESH},
};
const uint8_t MEDIA_NAMES_COUNT = sizeof(MEDIA_NAMES) / sizeof(MEDIA_NAMES[0]);

// Preseturi complete (SET rapid al unei combinatii cunoscute)
struct PresetDef { const char* name; ButtonAction action; };
const PresetDef PRESETS[] = {
  {"CTRLSHIFTESC", {ACT_KEY, (uint8_t)(KEYBOARD_MODIFIER_LEFTCTRL|KEYBOARD_MODIFIER_LEFTSHIFT), HID_KEY_ESCAPE, 0, 0}},
  {"CTRLALTDEL",   {ACT_KEY, (uint8_t)(KEYBOARD_MODIFIER_LEFTCTRL|KEYBOARD_MODIFIER_LEFTALT),   HID_KEY_DELETE, 0, 0}},
  {"ALTF4",        {ACT_KEY, KEYBOARD_MODIFIER_LEFTALT, HID_KEY_F4, 0, 0}},
  {"ALTTAB",       {ACT_KEY, KEYBOARD_MODIFIER_LEFTALT, HID_KEY_TAB, 0, 0}},
  {"WINL",         {ACT_KEY, KEYBOARD_MODIFIER_LEFTGUI, HID_KEY_L, 0, 0}},
  {"WIND",         {ACT_KEY, KEYBOARD_MODIFIER_LEFTGUI, HID_KEY_D, 0, 0}},
  {"WINE",         {ACT_KEY, KEYBOARD_MODIFIER_LEFTGUI, HID_KEY_E, 0, 0}},
  {"SNIP",         {ACT_KEY, (uint8_t)(KEYBOARD_MODIFIER_LEFTGUI|KEYBOARD_MODIFIER_LEFTSHIFT), HID_KEY_S, 0, 0}},
  {"COPY",         {ACT_KEY, KEYBOARD_MODIFIER_LEFTCTRL, HID_KEY_C, 0, 0}},
  {"PASTE",        {ACT_KEY, KEYBOARD_MODIFIER_LEFTCTRL, HID_KEY_V, 0, 0}},
  {"UNDO",         {ACT_KEY, KEYBOARD_MODIFIER_LEFTCTRL, HID_KEY_Z, 0, 0}},
  {"REDO",         {ACT_KEY, (uint8_t)(KEYBOARD_MODIFIER_LEFTCTRL|KEYBOARD_MODIFIER_LEFTSHIFT), HID_KEY_Z, 0, 0}},
};
const uint8_t PRESETS_COUNT = sizeof(PRESETS) / sizeof(PRESETS[0]);

// ───────────────────────────────────────────────
// DEFAULT CONFIG (comportamentul din V2.ino)
// ───────────────────────────────────────────────
void loadDefaults() {
  for (uint8_t tab = 0; tab < MAX_TABS; tab++)
    for (uint8_t slot = 0; slot < SLOT_COUNT; slot++)
      tabActions[tab][slot] = {ACT_NONE, 0, 0, 0, 0};
  tabCount = 1;
  activeTab = 0;
  actions[SLOT_B1] = {ACT_MEDIA, 0, 0, 0, HID_USAGE_CONSUMER_SCAN_PREVIOUS_TRACK};
  actions[SLOT_B2] = {ACT_MEDIA, 0, 0, 0, HID_USAGE_CONSUMER_PLAY_PAUSE};
  actions[SLOT_B3] = {ACT_MEDIA, 0, 0, 0, HID_USAGE_CONSUMER_SCAN_NEXT_TRACK};
  actions[SLOT_B4] = {ACT_KEY, KEYBOARD_MODIFIER_LEFTGUI, HID_KEY_L, 0, 0};
  actions[SLOT_B5] = {ACT_KEY, KEYBOARD_MODIFIER_LEFTGUI, HID_KEY_PRINT_SCREEN, 0, 0};
  actions[SLOT_B6] = {ACT_KEY, (uint8_t)(KEYBOARD_MODIFIER_LEFTCTRL|KEYBOARD_MODIFIER_LEFTSHIFT), HID_KEY_ESCAPE, 0, 0};
  actions[SLOT_B7] = {ACT_KEY, KEYBOARD_MODIFIER_LEFTGUI, HID_KEY_D, 0, 0};
#if OCTOGLOW_MACRO_HAS_ENCODER
  actions[SLOT_KNOB]    = {ACT_MEDIA, 0, 0, 0, HID_USAGE_CONSUMER_MUTE};
  actions[SLOT_ENC_CW]  = {ACT_MEDIA, 0, 0, 0, HID_USAGE_CONSUMER_VOLUME_INCREMENT};
  actions[SLOT_ENC_CCW] = {ACT_MEDIA, 0, 0, 0, HID_USAGE_CONSUMER_VOLUME_DECREMENT};
#endif
  ledEffect = 0;
  ledOutputMode = 0;
  lightingEnabled = true;
  tonePack = 0;
  ledBrightness = 100;
  rgbBrightness = 100;
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) tabRgbEffects[tab] = 0;
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) tabEffects[tab] = 0;
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    tabTonePacks[tab] = 0;
    tabIcons[tab] = 0;
    tabNames[tab][0] = 0;
  }
  memset(customToneNames, 0, sizeof(customToneNames));
  for (uint8_t pack = 0; pack < CUSTOM_TONE_PACKS; pack++)
    for (uint8_t event = 0; event < TONE_EVENTS; event++)
      customTones[pack][event] = 0;
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) tabColors[tab] = DEFAULT_TAB_COLOR;
  rgbDirty = true;
  buzzerEnabled = true;
  buzzerVolume = 100;
}

// ───────────────────────────────────────────────
// EEPROM PERSISTENCE
// ───────────────────────────────────────────────
#define EEPROM_SIZE       1024
#define EEPROM_MAGIC      0xC2
#define EEPROM_VERSION    8
#define EEPROM_ADDR_MAGIC   0
#define EEPROM_ADDR_VER     1
#define EEPROM_ADDR_LED     2
#define EEPROM_ADDR_BUZZER  3
#define EEPROM_ADDR_TAB_COUNT 4
#define EEPROM_ADDR_ACTIVE 5
#define EEPROM_ADDR_SLOTS   6
#define ACTION_BYTES        6   // type,modifiers,key1,key2,media_lo,media_hi
#define EEPROM_ADDR_BUZZER_VOLUME (EEPROM_ADDR_SLOTS + MAX_TABS * SLOT_COUNT * ACTION_BYTES)
#define EEPROM_ADDR_LED_MODE (EEPROM_ADDR_BUZZER_VOLUME + 1)
#define EEPROM_ADDR_TAB_COLORS (EEPROM_ADDR_LED_MODE + 1)
#define EEPROM_ADDR_TAB_EFFECTS (EEPROM_ADDR_TAB_COLORS + MAX_TABS * 3)
#define EEPROM_ADDR_LIGHT_ENABLED (EEPROM_ADDR_TAB_EFFECTS + MAX_TABS)
#define EEPROM_ADDR_TONE_PACK (EEPROM_ADDR_LIGHT_ENABLED + 1)
#define EEPROM_ADDR_CUSTOM_TONES (EEPROM_ADDR_TONE_PACK + 1)
#define EEPROM_ADDR_TAB_TONES (EEPROM_ADDR_CUSTOM_TONES + CUSTOM_TONE_PACKS * TONE_EVENTS * 2)
#define EEPROM_ADDR_LED_BRIGHTNESS (EEPROM_ADDR_TAB_TONES + MAX_TABS)
#define EEPROM_ADDR_TAB_ICONS (EEPROM_ADDR_LED_BRIGHTNESS + 1)
#define EEPROM_ADDR_TAB_NAMES (EEPROM_ADDR_TAB_ICONS + MAX_TABS)
#define EEPROM_ADDR_TONE_NAMES (EEPROM_ADDR_TAB_NAMES + MAX_TABS * TAB_NAME_BYTES)
#define EEPROM_ADDR_RGB_BRIGHTNESS (EEPROM_ADDR_TONE_NAMES + CUSTOM_TONE_PACKS * TAB_NAME_BYTES)
#define EEPROM_ADDR_RGB_EFFECTS (EEPROM_ADDR_RGB_BRIGHTNESS + 1)
#if EEPROM_ADDR_RGB_EFFECTS + MAX_TABS > EEPROM_SIZE
#error Macro configuration exceeds the EEPROM reservation
#endif

// Legacy clients can still assign/edit an unnamed slot. Give it a unique
// identity so Connect never hides an existing preset after such an edit.
void ensureToneName(uint8_t index) {
  if (index >= CUSTOM_TONE_PACKS || customToneNames[index][0]) return;
  for (uint8_t suffix = 1; suffix <= 10; suffix++) {
    char candidate[TAB_NAME_BYTES + 1];
    snprintf(candidate, sizeof(candidate), "Custom %u", suffix);
    bool taken = false;
    for (uint8_t other = 0; other < CUSTOM_TONE_PACKS; other++)
      if (!strcasecmp(candidate, customToneNames[other])) taken = true;
    if (!taken) { strcpy(customToneNames[index], candidate); return; }
  }
}

void writeActionToEEPROM(int addr, const ButtonAction &a) {
  EEPROM.write(addr + 0, a.type);
  EEPROM.write(addr + 1, a.modifiers);
  EEPROM.write(addr + 2, a.key1);
  EEPROM.write(addr + 3, a.key2);
  EEPROM.write(addr + 4, (uint8_t)(a.media & 0xFF));
  EEPROM.write(addr + 5, (uint8_t)((a.media >> 8) & 0xFF));
}

ButtonAction readActionFromEEPROM(int addr) {
  ButtonAction a;
  a.type      = EEPROM.read(addr + 0);
  a.modifiers = EEPROM.read(addr + 1);
  a.key1      = EEPROM.read(addr + 2);
  a.key2      = EEPROM.read(addr + 3);
  uint16_t lo = EEPROM.read(addr + 4);
  uint16_t hi = EEPROM.read(addr + 5);
  a.media     = (uint16_t)((hi << 8) | lo);
  return a;
}

// Slider changes apply in RAM immediately; coalesce flash writes while dragging.
bool sliderSavePending = false;
uint32_t sliderChangedAt = 0;

void scheduleSliderSave() {
  sliderSavePending = true;
  sliderChangedAt = millis();
}

// Restore is staged in RAM. Until COMMITCFG, EEPROM remains the rollback copy.
bool configTransaction = false;
uint32_t configTransactionAt = 0;
bool lastConfigSaveSucceeded = true;
void saveConfig() {
  if (configTransaction) return;
  EEPROM.write(EEPROM_ADDR_MAGIC, EEPROM_MAGIC);
  EEPROM.write(EEPROM_ADDR_VER, EEPROM_VERSION);
  EEPROM.write(EEPROM_ADDR_LED, ledEffect);
  EEPROM.write(EEPROM_ADDR_BUZZER, buzzerEnabled ? 1 : 0);
  EEPROM.write(EEPROM_ADDR_TAB_COUNT, tabCount);
  EEPROM.write(EEPROM_ADDR_ACTIVE, activeTab);
  for (uint8_t tab = 0; tab < MAX_TABS; tab++)
    for (uint8_t slot = 0; slot < SLOT_COUNT; slot++)
      writeActionToEEPROM(EEPROM_ADDR_SLOTS + (tab * SLOT_COUNT + slot) * ACTION_BYTES,
                          tabActions[tab][slot]);
  EEPROM.write(EEPROM_ADDR_BUZZER_VOLUME, buzzerVolume);
  EEPROM.write(EEPROM_ADDR_LED_MODE, ledOutputMode);
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    const int address = EEPROM_ADDR_TAB_COLORS + tab * 3;
    EEPROM.write(address, tabColors[tab].red);
    EEPROM.write(address + 1, tabColors[tab].green);
    EEPROM.write(address + 2, tabColors[tab].blue);
  }
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) EEPROM.write(EEPROM_ADDR_TAB_EFFECTS + tab, tabEffects[tab]);
  EEPROM.write(EEPROM_ADDR_LIGHT_ENABLED, lightingEnabled ? 1 : 0);
  EEPROM.write(EEPROM_ADDR_TONE_PACK, tonePack);
  for (uint8_t pack = 0; pack < CUSTOM_TONE_PACKS; pack++)
    for (uint8_t event = 0; event < TONE_EVENTS; event++) {
      const int address = EEPROM_ADDR_CUSTOM_TONES + (pack * TONE_EVENTS + event) * 2;
      EEPROM.write(address, (uint8_t)(customTones[pack][event] & 0xFF));
      EEPROM.write(address + 1, (uint8_t)(customTones[pack][event] >> 8));
    }
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    EEPROM.write(EEPROM_ADDR_TAB_TONES + tab, tabTonePacks[tab]);
    EEPROM.write(EEPROM_ADDR_TAB_ICONS + tab, tabIcons[tab]);
    for (uint8_t i = 0; i < TAB_NAME_BYTES; i++)
      EEPROM.write(EEPROM_ADDR_TAB_NAMES + tab * TAB_NAME_BYTES + i, tabNames[tab][i]);
  }
  EEPROM.write(EEPROM_ADDR_LED_BRIGHTNESS, ledBrightness);
  EEPROM.write(EEPROM_ADDR_RGB_BRIGHTNESS, rgbBrightness);
  for (uint8_t tab = 0; tab < MAX_TABS; tab++)
    EEPROM.write(EEPROM_ADDR_RGB_EFFECTS + tab, tabRgbEffects[tab]);
  for (uint8_t pack = 0; pack < CUSTOM_TONE_PACKS; pack++)
    for (uint8_t i = 0; i < TAB_NAME_BYTES; i++)
      EEPROM.write(EEPROM_ADDR_TONE_NAMES + pack * TAB_NAME_BYTES + i, customToneNames[pack][i]);
  lastConfigSaveSucceeded = EEPROM.commit();
  sliderSavePending = false;
}

void loadConfig() {
  uint8_t magic = EEPROM.read(EEPROM_ADDR_MAGIC);
  uint8_t ver   = EEPROM.read(EEPROM_ADDR_VER);
  if (magic != EEPROM_MAGIC || (ver < 1 || ver > EEPROM_VERSION)) {
    loadDefaults();
    saveConfig();
    return;
  }
  if (ver == 1) {
    // Keep every mapping from firmware V3 when the flash layout gains tabs.
    loadDefaults();
    ledEffect = EEPROM.read(EEPROM_ADDR_LED);
    if (ledEffect > 7) ledEffect = 0;
    tabEffects[0] = tabRgbEffects[0] = ledEffect;
    buzzerEnabled = EEPROM.read(EEPROM_ADDR_BUZZER) != 0;
    for (uint8_t slot = 0; slot < SLOT_COUNT; slot++) {
      tabActions[0][slot] = readActionFromEEPROM(4 + slot * ACTION_BYTES);
      if (tabActions[0][slot].type > ACT_MEDIA)
        tabActions[0][slot] = {ACT_NONE, 0, 0, 0, 0};
    }
    saveConfig();
    return;
  }
  ledEffect     = EEPROM.read(EEPROM_ADDR_LED);
  buzzerEnabled = EEPROM.read(EEPROM_ADDR_BUZZER) != 0;
  buzzerVolume = ver == 2 ? 100 : EEPROM.read(EEPROM_ADDR_BUZZER_VOLUME);
  if (buzzerVolume > 100) buzzerVolume = 100;
  ledOutputMode = ver >= 4 ? EEPROM.read(EEPROM_ADDR_LED_MODE) : 0;
  if (ledOutputMode > 2) ledOutputMode = 0;
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    const int address = EEPROM_ADDR_TAB_COLORS + tab * 3;
    tabColors[tab] = ver >= 4
      ? TabColor{EEPROM.read(address), EEPROM.read(address + 1), EEPROM.read(address + 2)}
      : DEFAULT_TAB_COLOR;
  }
  rgbDirty = true;
  if (ledEffect > 7) ledEffect = 0;
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    tabEffects[tab] = ver >= 5 ? EEPROM.read(EEPROM_ADDR_TAB_EFFECTS + tab) : ledEffect;
    if (tabEffects[tab] > 7) tabEffects[tab] = 0;
  }
  lightingEnabled = ver >= 5 ? EEPROM.read(EEPROM_ADDR_LIGHT_ENABLED) != 0 : true;
  tonePack = ver >= 5 ? EEPROM.read(EEPROM_ADDR_TONE_PACK) : 0;
  if (tonePack > 9) tonePack = 0;
  ledBrightness = ver >= 6 ? EEPROM.read(EEPROM_ADDR_LED_BRIGHTNESS) : 100;
  if (ledBrightness > 100) ledBrightness = 100;
  // Append-only migration: retain the previous appearance on both outputs.
  rgbBrightness = ver >= 8 ? EEPROM.read(EEPROM_ADDR_RGB_BRIGHTNESS) : ledBrightness;
  if (rgbBrightness > 100) rgbBrightness = ledBrightness;
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    tabRgbEffects[tab] = ver >= 8 ? EEPROM.read(EEPROM_ADDR_RGB_EFFECTS + tab) : tabEffects[tab];
    if (tabRgbEffects[tab] > 7) tabRgbEffects[tab] = tabEffects[tab];
  }
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    tabTonePacks[tab] = ver >= 6 ? EEPROM.read(EEPROM_ADDR_TAB_TONES + tab) : tonePack;
    if (tabTonePacks[tab] > 9) tabTonePacks[tab] = 0;
    tabIcons[tab] = ver >= 6 ? EEPROM.read(EEPROM_ADDR_TAB_ICONS + tab) : 0;
    if (tabIcons[tab] > 9) tabIcons[tab] = 0;
    for (uint8_t i = 0; i < TAB_NAME_BYTES; i++) {
      char c = ver >= 6 ? (char)EEPROM.read(EEPROM_ADDR_TAB_NAMES + tab * TAB_NAME_BYTES + i) : 0;
      tabNames[tab][i] = c == (char)0xFF ? 0 : c;
    }
    tabNames[tab][TAB_NAME_BYTES] = 0;
  }
  for (uint8_t pack = 0; pack < CUSTOM_TONE_PACKS; pack++)
    for (uint8_t event = 0; event < TONE_EVENTS; event++) {
      const int address = EEPROM_ADDR_CUSTOM_TONES + (pack * TONE_EVENTS + event) * 2;
      customTones[pack][event] = ver >= 5
        ? (uint16_t)(EEPROM.read(address) | (EEPROM.read(address + 1) << 8)) : 0;
      if (customTones[pack][event] > 8000) customTones[pack][event] = 0;
    }
  tabCount = EEPROM.read(EEPROM_ADDR_TAB_COUNT);
  if (tabCount < 1 || tabCount > MAX_TABS) tabCount = 1;
  activeTab = EEPROM.read(EEPROM_ADDR_ACTIVE);
  if (activeTab >= tabCount) activeTab = 0;
  for (uint8_t tab = 0; tab < MAX_TABS; tab++)
    for (uint8_t slot = 0; slot < SLOT_COUNT; slot++) {
      ButtonAction &action = tabActions[tab][slot];
      action = readActionFromEEPROM(EEPROM_ADDR_SLOTS + (tab * SLOT_COUNT + slot) * ACTION_BYTES);
      if (action.type > ACT_APP) action = {ACT_NONE, 0, 0, 0, 0};
    }
  for (uint8_t pack = 0; pack < CUSTOM_TONE_PACKS; pack++) {
    memset(customToneNames[pack], 0, TAB_NAME_BYTES + 1);
    if (ver >= 7) {
      for (uint8_t i = 0; i < TAB_NAME_BYTES; i++) {
        uint8_t c = EEPROM.read(EEPROM_ADDR_TONE_NAMES + pack * TAB_NAME_BYTES + i);
        if (!c || c == 0xFF) break;
        customToneNames[pack][i] = (char)c;
      }
    } else {
      // Preserve every previously edited or assigned pack during migration.
      bool used = tonePack == pack + 5;
      for (uint8_t tab = 0; tab < MAX_TABS; tab++) used |= tabTonePacks[tab] == pack + 5;
      for (uint8_t event = 0; event < TONE_EVENTS; event++) used |= customTones[pack][event] != 0;
      if (used) snprintf(customToneNames[pack], TAB_NAME_BYTES + 1, "Custom %u", pack + 1);
    }
  }
  if (ver < EEPROM_VERSION) saveConfig(); // retain mappings, tones and identities
}

// ───────────────────────────────────────────────
// ENCODER (quadrature decode)
// ───────────────────────────────────────────────
const int8_t ENC_TABLE[16] = {
   0, -1,  1,  0,
   1,  0,  0, -1,
  -1,  0,  0,  1,
   0,  1, -1,  0
};

uint8_t encState = 0;
int8_t  encAccum = 0;

// ───────────────────────────────────────────────
// BUZZER TONES
// ───────────────────────────────────────────────
void stopBuzzer() {
  pwm_set_gpio_level(PIN_BUZZER, 0);
  // Disabling a running PWM can otherwise leave the pin high mid-cycle.
  gpio_set_function(PIN_BUZZER, GPIO_FUNC_SIO);
  gpio_put(PIN_BUZZER, false);
  pwm_set_enabled(pwm_gpio_to_slice_num(PIN_BUZZER), false);
  buzzerStopAt = 0;
  buzzerDuty = 0;
}

void beep(uint16_t freq, uint16_t durationMs) {
  if (!buzzerEnabled || buzzerVolume == 0 || freq == 0) { stopBuzzer(); return; }
  // Continuous hardware PWM: never chop the carrier at an audible 125 Hz.
  // Fundamental amplitude is proportional to sin(pi*duty). This curve keeps
  // the requested frequency constant; a passive piezo is not an analog speaker.
  constexpr uint16_t wrap = 4095;
  pwm_config settings = pwm_get_default_config();
  pwm_config_set_wrap(&settings, wrap);
  pwm_config_set_clkdiv(&settings,
      (float)clock_get_hz(clk_sys) / ((float)freq * (wrap + 1)));
  gpio_set_function(PIN_BUZZER, GPIO_FUNC_PWM);
  pwm_init(pwm_gpio_to_slice_num(PIN_BUZZER), &settings, true);
  const float level = buzzerVolume / 100.0f;
  buzzerDuty = max(1, (int)roundf((wrap + 1) * asinf(level * level) / PI));
  pwm_set_gpio_level(PIN_BUZZER, buzzerDuty);
  buzzerStopAt = millis() + durationMs;
}

void updateBuzzer() {
  if (!buzzerStopAt) return;
  if ((int32_t)(millis() - buzzerStopAt) >= 0 || !buzzerEnabled || buzzerVolume == 0) {
    stopBuzzer(); return;
  }
  // PWM runs autonomously between start and stop, independent of loop timing.
}
uint16_t toneFrequency(uint8_t event) {
  if (event >= TONE_EVENTS) return 0;
  const uint8_t selectedPack = tabTonePacks[activeTab];
  if (selectedPack >= 5) return customTones[selectedPack - 5][event];
  if (selectedPack != 0) {
    // Four harmonic, intentionally distinct palettes. Each press/hold bank
    // follows a musical scale instead of arbitrary 80 Hz jumps.
    static const uint16_t notes[4][TONE_EVENTS] = {
      {523, 587, 659, 784, 880, 1047, 1175, 392, 440, 523, 587, 659, 784, 880, 784, 659, 523, 1047},
      {784, 880, 988, 1175, 1319, 1568, 1760, 587, 659, 784, 880, 988, 1175, 1319, 1319, 1175, 988, 1568},
      {659, 784, 988, 1047, 1319, 1568, 1976, 523, 659, 784, 988, 1047, 1319, 1568, 1976, 1568, 1319, 1047},
      {262, 294, 330, 392, 440, 523, 587, 196, 220, 262, 294, 330, 392, 440, 392, 330, 262, 523}
    };
    return notes[selectedPack - 1][event];
  }
  const uint16_t bases[5] = {900, 650, 1200, 1550, 480};
  const int16_t intervals[TONE_EVENTS] = {
    0, 80, 160, 240, 320, 400, 480,
    -120, -40, 40, 120, 200, 280, 360,
    560, 300, -100, 700
  };
  int32_t frequency = (int32_t)bases[0] + intervals[event];
  return (uint16_t)constrain(frequency, 100, 8000);
}
void playTone(uint8_t event) {
  beep(toneFrequency(event), event == 17 ? 100 : event >= 7 && event <= 13 ? 65 : 38);
}
void beepToggle(bool on) { if (on) beep(1800, 60); else beep(400, 80); }

// ───────────────────────────────────────────────
// HID SEND HELPERS
// ───────────────────────────────────────────────
void sendConsumer(uint16_t usage) {
  if (!usb_hid.ready() || usage == 0) return;
  usb_hid.sendReport16(2, usage);
  delay(10);
  usb_hid.sendReport16(2, 0);
}

void executeAction(uint8_t slot) {
  if (slot >= SLOT_COUNT) return;
  const ButtonAction &a = actions[slot];
  if (Serial) {
    Serial.print(F("EVENT "));
    Serial.print(activeTab + 1);
    Serial.print(' ');
    Serial.print(SLOT_NAMES[slot]);
    if (a.type == ACT_APP) {
      Serial.print(F(" APP "));
      Serial.print(a.media);
    }
    Serial.println();
  }
  if (a.type == ACT_KEY) {
    if (!usb_hid.ready()) return;
    if (a.modifiers == 0 && a.key1 == 0 && a.key2 == 0) return; // slot gol
    uint8_t keycodes[6] = {a.key1, a.key2, 0, 0, 0, 0};
    usb_hid.keyboardReport(1, a.modifiers, keycodes);
    delay(10);
    uint8_t empty[6] = {0};
    usb_hid.keyboardReport(1, 0, empty);
  } else if (a.type == ACT_MEDIA) {
    sendConsumer(a.media);
  } else if (a.type == ACT_SCROLL) {
    if (usb_hid.ready()) usb_hid.mouseScroll(3, a.key1 == 1 ? 1 : -1, 0);
  } else if (a.type == ACT_TAB_NEXT || a.type == ACT_TAB_PREV || a.type == ACT_TAB_GOTO) {
    if (a.type == ACT_TAB_NEXT) activeTab = (activeTab + 1) % tabCount;
    else if (a.type == ACT_TAB_PREV) activeTab = (activeTab + tabCount - 1) % tabCount;
    else if (a.key1 >= 1 && a.key1 <= tabCount) activeTab = a.key1 - 1;
    rgbDirty = true;
    if (Serial) { Serial.print(F("TAB_ACTIVE ")); Serial.println(activeTab + 1); }
  }
  // ACT_NONE and ACT_APP do not send HID. Connect handles application IDs.
}

void pollEncoder() {
  uint8_t a = digitalRead(PIN_ENC_A);
  uint8_t b = digitalRead(PIN_ENC_B);
  uint8_t cur = (a << 1) | b;
  uint8_t idx = (encState << 2) | cur;
  encState = cur;
  encAccum += ENC_TABLE[idx];

  if (encAccum >= 2) {
    encAccum = 0;
    playTone(15);
    executeAction(SLOT_ENC_CW);
  } else if (encAccum <= -2) {
    encAccum = 0;
    playTone(16);
    executeAction(SLOT_ENC_CCW);
  }
}

// ───────────────────────────────────────────────
// LED EFFECT
// ───────────────────────────────────────────────
uint8_t effectBrightness = 255;
uint8_t rgbEffectBrightness = 255;
uint8_t lastRgbBrightness = 0xFF;
uint8_t lightingEffectLevel(uint8_t effect, uint32_t now) {
  const uint16_t phase = now % 2000;
  switch (effect) {
    case 1: return (phase < 1000 ? phase : 2000 - phase) / 4;
    case 2: return phase < 1000 ? 255 : 0;
    case 3: return phase < 180 || (phase > 340 && phase < 520) ? 255 : 12;
    case 4: return phase < 140 || (phase > 230 && phase < 420) ? 255 : 24;
    case 5: return (now / 6) % 256;
    case 6: return ((now / 125 + activeTab * 13) * 37) % 5 == 0 ? 255 : 18;
    case 7: return (now / 75) % 4 == 0 ? 255 : 0;
    default: return 255;
  }
}
void updateLed() {
  const uint32_t now = millis();
  if (now - lastLedUpdate < 25 && !rgbDirty) return;
  lastLedUpdate = now;
  effectBrightness = lightingEffectLevel(tabEffects[activeTab], now);
  rgbEffectBrightness = lightingEffectLevel(tabRgbEffects[activeTab], now);
  analogWrite(PIN_LED, lightingEnabled && ledOutputMode != 1
      ? (uint16_t)effectBrightness * ledBrightness / 100 : 0);
}

void updateAddressableLed() {
  const uint8_t brightness = lightingEnabled && ledOutputMode != 2
      ? (uint16_t)rgbEffectBrightness * rgbBrightness / 100 : 0;
  if (!rgbDirty && shownRgbTab == activeTab && lastRgbBrightness == brightness) return;
  const TabColor &color = tabColors[activeTab];
  tabPixel.setPixelColor(0, tabPixel.Color(
    (uint16_t)color.red * brightness / 255,
    (uint16_t)color.green * brightness / 255,
    (uint16_t)color.blue * brightness / 255));
  tabPixel.show();
  shownRgbTab = activeTab;
  lastRgbBrightness = brightness;
  rgbDirty = false;
}

void cycleLedEffect() {
  if (ledOutputMode != 1) tabEffects[activeTab] = (tabEffects[activeTab] + 1) % 8;
  if (ledOutputMode != 2) tabRgbEffects[activeTab] = (tabRgbEffects[activeTab] + 1) % 8;
  saveConfig();
  fadeBrightness = 0;
  fadeDir = 1;
  blinkState = false;
  rgbDirty = true;
}

// ───────────────────────────────────────────────
// SIMPLE BUTTONS (b1..b6, fara hold)
// ───────────────────────────────────────────────
struct SimpleBtn {
  uint8_t  pin;
  uint8_t  slot;
  bool     lastState;
  uint32_t lastDebounce;
  bool     triggered;
  uint32_t pressedAt;
  bool     holdTonePlayed;
};

#define NUM_SIMPLE_BTNS (OCTOGLOW_MACRO_HAS_ENCODER ? 6 : 7)
SimpleBtn simpleBtns[NUM_SIMPLE_BTNS] = {
  {PIN_B1, SLOT_B1, HIGH, 0, false},
  {PIN_B2, SLOT_B2, HIGH, 0, false},
  {PIN_B3, SLOT_B3, HIGH, 0, false},
  {PIN_B4, SLOT_B4, HIGH, 0, false},
  {PIN_B5, SLOT_B5, HIGH, 0, false},
  {PIN_B6, SLOT_B6, HIGH, 0, false},
#if !OCTOGLOW_MACRO_HAS_ENCODER
  {PIN_KNOB_BTN, SLOT_KNOB, HIGH, 0, false},
#endif
};

void pollSimpleButtons() {
  uint32_t now = millis();
  for (uint8_t i = 0; i < NUM_SIMPLE_BTNS; i++) {
    bool cur = digitalRead(simpleBtns[i].pin);
    if (cur != simpleBtns[i].lastState) {
      simpleBtns[i].lastDebounce = now;
      simpleBtns[i].lastState = cur;
    }
    if ((now - simpleBtns[i].lastDebounce) > 20) {
      if (cur == LOW && !simpleBtns[i].triggered) {
        simpleBtns[i].triggered = true;
        simpleBtns[i].pressedAt = now;
        simpleBtns[i].holdTonePlayed = false;
        playTone(simpleBtns[i].slot == SLOT_KNOB ? 14 : simpleBtns[i].slot);
        executeAction(simpleBtns[i].slot);
      }
      if (simpleBtns[i].slot != SLOT_KNOB && cur == LOW && simpleBtns[i].triggered && !simpleBtns[i].holdTonePlayed &&
          now - simpleBtns[i].pressedAt >= HOLD_THRESHOLD_MS) {
        simpleBtns[i].holdTonePlayed = true;
        playTone(7 + simpleBtns[i].slot);
      }
      if (cur == HIGH) {
        simpleBtns[i].triggered = false;
      }
    }
  }
}

// ───────────────────────────────────────────────
// HOLD BUTTONS (b7 si knob) - press configurabil,
// hold ramane functie de sistem fixa (ca in V2)
// ───────────────────────────────────────────────
void pollHoldButton(uint8_t pin, HoldBtnState &s, void (*onPress)(), void (*onHold)()) {
  bool raw = digitalRead(pin);
  uint32_t now = millis();

  if (raw != s.lastRaw) {
    s.lastDebounce = now;
    s.lastRaw = raw;
  }
  if ((now - s.lastDebounce) < 20) return;

  bool cur = s.lastRaw;

  if (cur == LOW && s.stableState == HIGH) {
    s.pressed   = true;
    s.holdFired = false;
    s.pressTime = now;
  }
  if (s.pressed && !s.holdFired && (now - s.pressTime) >= HOLD_THRESHOLD_MS) {
    s.holdFired = true;
    onHold();
  }
  if (cur == HIGH && s.stableState == LOW) {
    if (s.pressed && !s.holdFired) onPress();
    s.pressed = false;
  }
  s.stableState = cur;
}

void onKnobPress() { playTone(14); executeAction(SLOT_KNOB); }
void onKnobHold()  {
  buzzerEnabled = !buzzerEnabled;
  saveConfig();
  beepToggle(buzzerEnabled);
}

void onB7Press() { playTone(6); executeAction(SLOT_B7); }
void onB7Hold()  { playTone(13); cycleLedEffect(); }

// ───────────────────────────────────────────────
// SERIAL COMMAND INTERFACE
// ───────────────────────────────────────────────
bool ieq(const char* a, const char* b) {
  while (*a && *b) {
    if (toupper((unsigned char)*a) != toupper((unsigned char)*b)) return false;
    a++; b++;
  }
  return *a == 0 && *b == 0;
}

int findSlot(const char* name) {
  for (uint8_t i = 0; i < SLOT_COUNT; i++) if (ieq(name, SLOT_NAMES[i])) return i;
  return -1;
}

bool findKey(const char* name, uint8_t &out) {
  for (uint8_t i = 0; i < KEY_NAMES_COUNT; i++)
    if (ieq(name, KEY_NAMES[i].name)) { out = KEY_NAMES[i].val; return true; }
  return false;
}

bool findMedia(const char* name, uint16_t &out) {
  for (uint8_t i = 0; i < MEDIA_NAMES_COUNT; i++)
    if (ieq(name, MEDIA_NAMES[i].name)) { out = MEDIA_NAMES[i].val; return true; }
  return false;
}

int findPreset(const char* name) {
  for (uint8_t i = 0; i < PRESETS_COUNT; i++) if (ieq(name, PRESETS[i].name)) return i;
  return -1;
}

// "CTRL,SHIFT" / "CTRL+SHIFT" / "NONE" -> bitmask; intoarce false daca un nume nu exista
bool parseModifiers(char* tok, uint8_t &out) {
  out = 0;
  if (ieq(tok, "NONE") || tok[0] == 0) return true;
  char* p = tok;
  while (p && *p) {
    char* sep = p;
    while (*sep && *sep != ',' && *sep != '+') sep++;
    char save = *sep;
    *sep = 0;
    bool found = false;
    for (uint8_t i = 0; i < MOD_NAMES_COUNT; i++) {
      if (ieq(p, MOD_NAMES[i].name)) { out |= MOD_NAMES[i].val; found = true; break; }
    }
    if (!found) return false;
    p = (save == 0) ? nullptr : sep + 1;
  }
  return true;
}

void printAction(const char* slotName, const ButtonAction &a) {
  Serial.print(slotName);
  Serial.print(" = ");
  if (a.type == ACT_NONE) {
    Serial.println("NONE");
  } else if (a.type == ACT_MEDIA) {
    const char* mname = "?";
    for (uint8_t i = 0; i < MEDIA_NAMES_COUNT; i++)
      if (MEDIA_NAMES[i].val == a.media) { mname = MEDIA_NAMES[i].name; break; }
    Serial.print("MEDIA ");
    Serial.println(mname);
  } else if (a.type == ACT_KEY) {
    Serial.print("KEY ");
    bool any = false;
    uint8_t bits[8] = {KEYBOARD_MODIFIER_LEFTCTRL, KEYBOARD_MODIFIER_LEFTSHIFT, KEYBOARD_MODIFIER_LEFTALT, KEYBOARD_MODIFIER_LEFTGUI,
                        KEYBOARD_MODIFIER_RIGHTCTRL, KEYBOARD_MODIFIER_RIGHTSHIFT, KEYBOARD_MODIFIER_RIGHTALT, KEYBOARD_MODIFIER_RIGHTGUI};
    const char* bitNames[8] = {"CTRL","SHIFT","ALT","GUI","RCTRL","RSHIFT","RALT","RGUI"};
    for (uint8_t i = 0; i < 8; i++) {
      if (a.modifiers & bits[i]) {
        if (any) Serial.print("+");
        Serial.print(bitNames[i]);
        any = true;
      }
    }
    if (!any) Serial.print("NONE");
    Serial.print(" ");
    const char* kname = "?";
    for (uint8_t i = 0; i < KEY_NAMES_COUNT; i++)
      if (KEY_NAMES[i].val == a.key1) { kname = KEY_NAMES[i].name; break; }
    Serial.print(kname);
    if (a.key2 != 0) {
      const char* k2name = "?";
      for (uint8_t i = 0; i < KEY_NAMES_COUNT; i++)
        if (KEY_NAMES[i].val == a.key2) { k2name = KEY_NAMES[i].name; break; }
      Serial.print(" ");
      Serial.print(k2name);
    }
    Serial.println();
  } else if (a.type == ACT_SCROLL) {
    Serial.println(a.key1 == 1 ? F("SCROLL UP") : F("SCROLL DOWN"));
  } else if (a.type == ACT_TAB_NEXT) {
    Serial.println(F("TAB NEXT"));
  } else if (a.type == ACT_TAB_PREV) {
    Serial.println(F("TAB PREV"));
  } else if (a.type == ACT_TAB_GOTO) {
    Serial.print(F("TAB ")); Serial.println(a.key1);
  } else if (a.type == ACT_APP) {
    Serial.print(F("APP ")); Serial.println(a.media);
  }
}

void cmdList() {
  for (uint8_t i = 0; i < SLOT_COUNT; i++) printAction(SLOT_NAMES[i], actions[i]);
}

void cmdKeys() {
  Serial.print("Taste disponibile: ");
  for (uint8_t i = 0; i < KEY_NAMES_COUNT; i++) { Serial.print(KEY_NAMES[i].name); Serial.print(" "); }
  Serial.println();
}

void cmdMedias() {
  Serial.print("Media disponibile: ");
  for (uint8_t i = 0; i < MEDIA_NAMES_COUNT; i++) { Serial.print(MEDIA_NAMES[i].name); Serial.print(" "); }
  Serial.println();
}

void cmdPresets() {
  Serial.print("Presets disponibile: ");
  for (uint8_t i = 0; i < PRESETS_COUNT; i++) { Serial.print(PRESETS[i].name); Serial.print(" "); }
  Serial.println();
}

void cmdHelp() {
  Serial.println(F("Comenzi:"));
  Serial.println(F("  LIST                              - afiseaza toate mapările"));
  Serial.println(F("  GET <slot>                        - afiseaza o mapare"));
  Serial.println(F("  SET <slot> NONE                   - dezactiveaza butonul"));
  Serial.println(F("  SET <slot> KEY <mod|NONE> <k1> [k2]  - ex: SET b6 KEY CTRL+SHIFT ESC"));
  Serial.println(F("  SET <slot> MEDIA <nume>           - ex: SET b2 MEDIA PLAYPAUSE"));
  Serial.println(F("  PRESET <slot> <nume>               - ex: PRESET b7 ALTF4"));
  Serial.println(F("  PRESETS / KEYS / MEDIAS           - listeaza valori posibile"));
  Serial.println(F("  RESET                              - reseteaza la valorile din fabrica"));
  Serial.println(F("  SAVE                                - salveaza explicit in flash"));
  Serial.println(F("  BUZZER ON|OFF                       - porneste/opreste buzzer-ul"));
  Serial.println(F("  BUZZERVOL <0..100>                  - volum buzzer"));
  Serial.println(F("  TABFX <tab> <0..7>                  - effect for one tab"));
  Serial.println(F("  TABFXALL <0..7>                    - effect for all tabs"));
  Serial.println(F("  LIGHT ON|OFF                       - lighting power"));
  Serial.println(F("  LEDMODE <0|1|2>                     - both / RGB / external"));
  Serial.println(F("  TONEPACK <0..9>                    - five built-in + five custom"));
  Serial.println(F("  TONESET <pack 1..5> <event 0..17> <Hz 0..8000>"));
  Serial.println(F("  TONEALL <pack 1..5> <PRESS|HOLD> <Hz 0..8000>"));
  Serial.println(F("  TABRGB <tab> <red> <green> <blue>   - per-tab WS2812B color"));
  Serial.println(F("  ADDTAB <b1..b7|knob>                - add tab and cycle key"));
  Serial.println(F("  BRIGHT <0..100>                    - lighting brightness"));
  Serial.println(F("  TABTONE <tab> <0..9>               - per-tab buzzer theme"));
  Serial.println(F("  TABMETA <tab> <icon 0..9> <name hex or ->"));
  Serial.println(F("  DELTAB <tab>                      - remove any tab and shift mappings"));
  Serial.println(F("  MOVETAB <from> <to>               - move a tab with all its settings"));
  Serial.println(F("  RGBBRIGHT|EXTBRIGHT <0..100>        - separate output brightness"));
  Serial.println(F("  RGBFX|EXTFX <tab 1..5> <0..7>       - separate per-tab effects"));
  Serial.println(F("  RGBFXALL|EXTFXALL <0..7>            - output effect on all tabs"));
  Serial.println(F("  DUMP / SETMAP / SETTABCOUNT / ACTIVE - protocol Octoglow Connect 12"));
  Serial.println(F("  BEGINCFG / COMMITCFG / ABORTCFG     - transactional configuration restore"));
  Serial.println(F("  HELLO                               - identificare placa"));
  Serial.print(F("Sloturi: "));
  for (uint8_t i = 0; i < SLOT_COUNT; i++) { Serial.print(SLOT_NAMES[i]); Serial.print(" "); }
  Serial.println();
}

bool parseUnsigned(const char* text, uint16_t &value) {
  if (!text || !*text) return false;
  uint32_t result = 0;
  for (const char* p = text; *p; p++) {
    if (*p < '0' || *p > '9') return false;
    result = result * 10 + (*p - '0');
    if (result > 65535) return false;
  }
  value = (uint16_t)result;
  return true;
}

int8_t hexNibble(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  return -1;
}

void dumpConfig() {
  Serial.print(F("CFG 12 "));
  Serial.print(tabCount); Serial.print(' ');
  Serial.print(activeTab + 1); Serial.print(' ');
  Serial.print(ledEffect); Serial.print(' ');
  Serial.print(buzzerEnabled ? 1 : 0); Serial.print(' ');
  Serial.print(buzzerVolume); Serial.print(' ');
  Serial.print(ledOutputMode); Serial.print(' ');
  Serial.print(lightingEnabled ? 1 : 0); Serial.print(' ');
  Serial.print(tonePack); Serial.print(' ');
  Serial.print(OCTOGLOW_MACRO_HAS_ENCODER ? 1 : 0); Serial.print(' ');
  Serial.println(ledBrightness);
  Serial.print(F("RGBBRIGHT ")); Serial.println(rgbBrightness);
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    Serial.print(F("COLOR ")); Serial.print(tab + 1); Serial.print(' ');
    Serial.print(tabColors[tab].red); Serial.print(' ');
    Serial.print(tabColors[tab].green); Serial.print(' ');
    Serial.println(tabColors[tab].blue);
  }
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    Serial.print(F("FX ")); Serial.print(tab + 1); Serial.print(' ');
    Serial.println(tabEffects[tab]);
    Serial.print(F("RGBFX ")); Serial.print(tab + 1); Serial.print(' ');
    Serial.println(tabRgbEffects[tab]);
  }
  const char hex[] = "0123456789ABCDEF";
  for (uint8_t pack = 0; pack < CUSTOM_TONE_PACKS; pack++) {
    Serial.print(F("PACK ")); Serial.print(pack + 1); Serial.print(' ');
    if (!customToneNames[pack][0]) Serial.print('-');
    else for (uint8_t i = 0; i < TAB_NAME_BYTES && customToneNames[pack][i]; i++) {
      uint8_t c = (uint8_t)customToneNames[pack][i];
      Serial.print(hex[c >> 4]); Serial.print(hex[c & 15]);
    }
    Serial.println();
  }
  for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
    Serial.print(F("META ")); Serial.print(tab + 1); Serial.print(' ');
    Serial.print(tabTonePacks[tab]); Serial.print(' ');
    Serial.print(tabIcons[tab]); Serial.print(' ');
    if (!tabNames[tab][0]) Serial.print('-');
    else for (uint8_t i = 0; i < TAB_NAME_BYTES && tabNames[tab][i]; i++) {
      const uint8_t c = (uint8_t)tabNames[tab][i];
      Serial.print(hex[c >> 4]); Serial.print(hex[c & 0x0F]);
    }
    Serial.println();
  }
  for (uint8_t pack = 0; pack < CUSTOM_TONE_PACKS; pack++)
    for (uint8_t event = 0; event < TONE_EVENTS; event++) {
      Serial.print(F("TONE ")); Serial.print(pack + 1); Serial.print(' ');
      Serial.print(event); Serial.print(' ');
      Serial.println(customTones[pack][event]);
    }
  for (uint8_t tab = 0; tab < MAX_TABS; tab++)
    for (uint8_t slot = 0; slot < SLOT_COUNT; slot++) {
      const ButtonAction &a = tabActions[tab][slot];
      Serial.print(F("MAP ")); Serial.print(tab + 1); Serial.print(' ');
      Serial.print(SLOT_NAMES[slot]); Serial.print(' ');
      Serial.print(a.type); Serial.print(' ');
      Serial.print(a.modifiers); Serial.print(' ');
      Serial.print(a.key1); Serial.print(' ');
      Serial.print(a.key2); Serial.print(' ');
      Serial.println(a.media);
  }
  Serial.println(F("END"));
}

#define MAX_TOKENS 9
void handleCommand(char* line) {
  // tokenizare simpla pe spatii
  char* tokens[MAX_TOKENS];
  uint8_t ntok = 0;
  char* p = strtok(line, " \t");
  while (p != nullptr && ntok < MAX_TOKENS) {
    tokens[ntok++] = p;
    p = strtok(nullptr, " \t");
  }
  if (ntok == 0) return;
  if (configTransaction) configTransactionAt = millis();
  if (ieq(tokens[0], "BEGINCFG")) {
    if (configTransaction) { Serial.println(F("ERR restore already active")); return; }
    saveConfig();
    if (!lastConfigSaveSucceeded) { Serial.println(F("ERR could not preserve configuration")); return; }
    stopBuzzer();
    configTransaction = true;
    configTransactionAt = millis();
    Serial.println(F("OK")); return;
  }
  if (ieq(tokens[0], "ABORTCFG")) {
    configTransaction = false;
    sliderSavePending = false;
    loadConfig();
    rgbDirty = true;
    Serial.println(F("OK")); return;
  }
  if (ieq(tokens[0], "COMMITCFG")) {
    if (!configTransaction) { Serial.println(F("ERR no restore active")); return; }
    configTransaction = false;
    saveConfig();
    rgbDirty = true;
    Serial.println(lastConfigSaveSucceeded ? F("OK") : F("ERR EEPROM commit failed")); return;
  }

  if (ieq(tokens[0], "HELLO")) {
    Serial.print(F("OCTOGLOW MACRO "));
    Serial.print(OCTOGLOW_MACRO_FIRMWARE_VERSION);
    Serial.println(F(" READY"));
    return;
  }
  if (ieq(tokens[0], "DUMP")) { dumpConfig(); return; }
  if (ieq(tokens[0], "ADDTAB")) {
    int slot = ntok == 2 ? findSlot(tokens[1]) : -1;
    if (slot < SLOT_B1 || slot > SLOT_KNOB || tabCount >= MAX_TABS) {
      Serial.println(F("ERR choose a button; maximum 5 tabs")); return;
    }
    ButtonAction switchAction = {ACT_TAB_NEXT, 0, 0, 0, 0};
    for (uint8_t key = 0; key < SLOT_COUNT; key++)
      tabActions[tabCount][key] = {ACT_NONE, 0, 0, 0, 0};
    for (uint8_t tab = 0; tab <= tabCount; tab++) tabActions[tab][slot] = switchAction;
    tabColors[tabCount] = DEFAULT_TAB_COLOR;
    tabEffects[tabCount] = tabEffects[activeTab];
    tabRgbEffects[tabCount] = tabRgbEffects[activeTab];
    tabTonePacks[tabCount] = tabTonePacks[activeTab];
    tabIcons[tabCount] = 0;
    tabNames[tabCount][0] = 0;
    tabCount++;
    saveConfig();
    Serial.println(F("OK"));
    return;
  }
  if (ieq(tokens[0], "DELTAB")) {
    uint16_t requested;
    if (ntok != 2 || !parseUnsigned(tokens[1], requested) || requested < 1 ||
        requested > tabCount || tabCount <= 1) {
      Serial.println(F("ERR DELTAB <1..tabCount>, at least one tab required")); return;
    }
    const uint8_t removed = requested - 1;
    for (uint8_t tab = removed; tab + 1 < tabCount; tab++) {
      for (uint8_t slot = 0; slot < SLOT_COUNT; slot++)
        tabActions[tab][slot] = tabActions[tab + 1][slot];
      tabColors[tab] = tabColors[tab + 1];
      tabEffects[tab] = tabEffects[tab + 1];
      tabRgbEffects[tab] = tabRgbEffects[tab + 1];
      tabTonePacks[tab] = tabTonePacks[tab + 1];
      tabIcons[tab] = tabIcons[tab + 1];
      memcpy(tabNames[tab], tabNames[tab + 1], TAB_NAME_BYTES + 1);
    }
    const uint8_t last = tabCount - 1;
    for (uint8_t slot = 0; slot < SLOT_COUNT; slot++)
      tabActions[last][slot] = {ACT_NONE, 0, 0, 0, 0};
    tabColors[last] = DEFAULT_TAB_COLOR;
    tabEffects[last] = 0;
    tabRgbEffects[last] = 0;
    tabTonePacks[last] = 0;
    tabIcons[last] = 0;
    tabNames[last][0] = 0;
    tabCount--;
    for (uint8_t tab = 0; tab < tabCount; tab++)
      for (uint8_t slot = 0; slot < SLOT_COUNT; slot++) {
        ButtonAction &action = tabActions[tab][slot];
        if (action.type != ACT_TAB_GOTO) continue;
        if (action.key1 == requested) action = {ACT_TAB_NEXT, 0, 0, 0, 0};
        else if (action.key1 > requested) action.key1--;
      }
    if (activeTab > removed) activeTab--;
    else if (activeTab >= tabCount) activeTab = tabCount - 1;
    ledEffect = tabEffects[activeTab];
    rgbDirty = true;
    saveConfig();
    Serial.println(F("OK"));
    if (Serial) { Serial.print(F("TAB_ACTIVE ")); Serial.println(activeTab + 1); }
    return;
  }
  if (ieq(tokens[0], "MOVETAB")) {
    uint16_t from, to;
    if (ntok != 3 || !parseUnsigned(tokens[1], from) || !parseUnsigned(tokens[2], to) ||
        from < 1 || from > tabCount || to < 1 || to > tabCount) {
      Serial.println(F("ERR MOVETAB <from 1..tabCount> <to 1..tabCount>")); return;
    }
    if (from != to) {
      const uint8_t source = from - 1;
      const uint8_t target = to - 1;
      ButtonAction movedActions[SLOT_COUNT];
      memcpy(movedActions, tabActions[source], sizeof(movedActions));
      const TabColor movedColor = tabColors[source];
      const uint8_t movedEffect = tabEffects[source];
      const uint8_t movedRgbEffect = tabRgbEffects[source];
      const uint8_t movedTone = tabTonePacks[source];
      const uint8_t movedIcon = tabIcons[source];
      char movedName[TAB_NAME_BYTES + 1];
      memcpy(movedName, tabNames[source], sizeof(movedName));
      const int8_t step = source < target ? 1 : -1;
      for (int8_t position = source; position != target; position += step) {
        const uint8_t next = position + step;
        memcpy(tabActions[position], tabActions[next], sizeof(movedActions));
        tabColors[position] = tabColors[next];
        tabEffects[position] = tabEffects[next];
        tabRgbEffects[position] = tabRgbEffects[next];
        tabTonePacks[position] = tabTonePacks[next];
        tabIcons[position] = tabIcons[next];
        memcpy(tabNames[position], tabNames[next], sizeof(movedName));
      }
      memcpy(tabActions[target], movedActions, sizeof(movedActions));
      tabColors[target] = movedColor;
      tabEffects[target] = movedEffect;
      tabRgbEffects[target] = movedRgbEffect;
      tabTonePacks[target] = movedTone;
      tabIcons[target] = movedIcon;
      memcpy(tabNames[target], movedName, sizeof(movedName));
      // Go-to-tab actions point to the same logical tab after its number changes.
      for (uint8_t tab = 0; tab < tabCount; tab++)
        for (uint8_t slot = 0; slot < SLOT_COUNT; slot++) {
          ButtonAction &action = tabActions[tab][slot];
          if (action.type != ACT_TAB_GOTO || action.key1 < 1 || action.key1 > tabCount) continue;
          if (action.key1 == from) action.key1 = to;
          else if (from < to && action.key1 > from && action.key1 <= to) action.key1--;
          else if (from > to && action.key1 >= to && action.key1 < from) action.key1++;
        }
      const uint8_t current = activeTab + 1;
      if (current == from) activeTab = target;
      else if (from < to && current > from && current <= to) activeTab--;
      else if (from > to && current >= to && current < from) activeTab++;
      ledEffect = tabEffects[activeTab];
      rgbDirty = true;
      saveConfig();
    }
    Serial.println(F("OK"));
    if (Serial) { Serial.print(F("TAB_ACTIVE ")); Serial.println(activeTab + 1); }
    return;
  }
  if (ieq(tokens[0], "SETTABCOUNT")) {
    uint16_t count;
    if (ntok != 2 || !parseUnsigned(tokens[1], count) || count < 1 || count > MAX_TABS) {
      Serial.println(F("ERR tab count must be 1..5")); return;
    }
    tabCount = (uint8_t)count;
    if (activeTab >= tabCount) activeTab = 0;
    Serial.println(F("OK"));
    return;
  }
  if (ieq(tokens[0], "ACTIVE")) {
    uint16_t tab;
    if (ntok != 2 || !parseUnsigned(tokens[1], tab) || tab < 1 || tab > tabCount) {
      Serial.println(F("ERR invalid active tab")); return;
    }
    activeTab = (uint8_t)(tab - 1);
    ledEffect = tabEffects[activeTab];
    rgbDirty = true;
    Serial.println(F("OK"));
    return;
  }
  if (ieq(tokens[0], "SETMAP")) {
    uint16_t tab, type, modifiers, key1, key2, value;
    int slot = ntok >= 3 ? findSlot(tokens[2]) : -1;
    if (ntok != 8 || !parseUnsigned(tokens[1], tab) || tab < 1 || tab > MAX_TABS || slot < 0 ||
        !parseUnsigned(tokens[3], type) || type > ACT_APP ||
        !parseUnsigned(tokens[4], modifiers) || modifiers > 255 ||
        !parseUnsigned(tokens[5], key1) || key1 > 255 ||
        !parseUnsigned(tokens[6], key2) || key2 > 255 ||
        !parseUnsigned(tokens[7], value) ||
        (type == ACT_TAB_GOTO && (key1 < 1 || key1 > tabCount)) ||
        (type == ACT_SCROLL && key1 != 1 && key1 != 2) ||
        (type == ACT_APP && value == 0)) {
      Serial.println(F("ERR invalid mapping")); return;
    }
    tabActions[tab - 1][slot] = {(uint8_t)type, (uint8_t)modifiers,
                                (uint8_t)key1, (uint8_t)key2, value};
    Serial.println(F("OK"));
    return;
  }
  if (ieq(tokens[0], "HELP")) { cmdHelp(); return; }
  if (ieq(tokens[0], "LIST")) { cmdList(); return; }
  if (ieq(tokens[0], "KEYS")) { cmdKeys(); return; }
  if (ieq(tokens[0], "MEDIAS")) { cmdMedias(); return; }
  if (ieq(tokens[0], "PRESETS")) { cmdPresets(); return; }
  if (ieq(tokens[0], "SAVE")) { saveConfig(); Serial.println(F("OK SAVED")); return; }

  if (ieq(tokens[0], "RESET")) {
    loadDefaults();
    saveConfig();
    Serial.println(F("OK RESET"));
    return;
  }

  if (ieq(tokens[0], "BUZZER") && ntok >= 2) {
    if (ieq(tokens[1], "ON")) buzzerEnabled = true;
    else if (ieq(tokens[1], "OFF")) buzzerEnabled = false;
    else { Serial.println(F("ERR buzzer trebuie ON sau OFF")); return; }
    saveConfig();
    Serial.println(F("OK"));
    return;
  }

  if (ieq(tokens[0], "BUZZERVOL")) {
    uint16_t volume;
    if (ntok != 2 || !parseUnsigned(tokens[1], volume) || volume > 100) {
      Serial.println(F("ERR buzzer volume must be 0..100")); return;
    }
    buzzerVolume = (uint8_t)volume;
    if (buzzerVolume == 0) stopBuzzer();
    scheduleSliderSave();
    beep(1047, 85);
    Serial.println(F("OK"));
    return;
  }

  if (ieq(tokens[0], "BRIGHT") || ieq(tokens[0], "RGBBRIGHT") || ieq(tokens[0], "EXTBRIGHT")) {
    uint16_t level;
    if (ntok != 2 || !parseUnsigned(tokens[1], level) || level > 100) {
      Serial.println(F("ERR BRIGHT <0..100>")); return;
    }
    if (!ieq(tokens[0], "RGBBRIGHT")) ledBrightness = (uint8_t)level;
    if (!ieq(tokens[0], "EXTBRIGHT")) rgbBrightness = (uint8_t)level;
    rgbDirty = true;
    scheduleSliderSave(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "LED") && ntok == 2) {
    uint16_t v;
    if (!parseUnsigned(tokens[1], v) || v > 7) { Serial.println(F("ERR effect must be 0..7")); return; }
    ledEffect = tabEffects[activeTab] = (uint8_t)v;
    tabRgbEffects[activeTab] = (uint8_t)v;
    rgbDirty = true;
    saveConfig();
    Serial.println(F("OK"));
    return;
  }

  if (ieq(tokens[0], "TABFX") || ieq(tokens[0], "RGBFX") || ieq(tokens[0], "EXTFX")) {
    uint16_t tab, effect;
    if (ntok != 3 || !parseUnsigned(tokens[1], tab) || tab < 1 || tab > MAX_TABS ||
        !parseUnsigned(tokens[2], effect) || effect > 7) {
      Serial.println(F("ERR TABFX <1..5> <0..7>")); return;
    }
    if (!ieq(tokens[0], "RGBFX")) tabEffects[tab - 1] = (uint8_t)effect;
    if (!ieq(tokens[0], "EXTFX")) tabRgbEffects[tab - 1] = (uint8_t)effect;
    if (activeTab == tab - 1) { ledEffect = tabEffects[activeTab]; rgbDirty = true; }
    saveConfig(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "TABFXALL") || ieq(tokens[0], "RGBFXALL") || ieq(tokens[0], "EXTFXALL")) {
    uint16_t effect;
    if (ntok != 2 || !parseUnsigned(tokens[1], effect) || effect > 7) {
      Serial.println(F("ERR TABFXALL <0..7>")); return;
    }
    for (uint8_t tab = 0; tab < MAX_TABS; tab++) {
      if (!ieq(tokens[0], "RGBFXALL")) tabEffects[tab] = (uint8_t)effect;
      if (!ieq(tokens[0], "EXTFXALL")) tabRgbEffects[tab] = (uint8_t)effect;
    }
    ledEffect = tabEffects[activeTab];
    rgbDirty = true;
    saveConfig(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "LIGHT")) {
    if (ntok != 2 || (!ieq(tokens[1], "ON") && !ieq(tokens[1], "OFF"))) {
      Serial.println(F("ERR LIGHT ON|OFF")); return;
    }
    lightingEnabled = ieq(tokens[1], "ON");
    rgbDirty = true;
    saveConfig(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "LEDMODE")) {
    uint16_t mode;
    if (ntok != 2 || !parseUnsigned(tokens[1], mode) || mode > 2) {
      Serial.println(F("ERR led mode must be 0, 1 or 2")); return;
    }
    ledOutputMode = (uint8_t)mode;
    rgbDirty = true;
    saveConfig();
    Serial.println(F("OK"));
    return;
  }

  if (ieq(tokens[0], "TONEPACK")) {
    uint16_t pack;
    if (ntok != 2 || !parseUnsigned(tokens[1], pack) || pack > 9) {
      Serial.println(F("ERR TONEPACK <0..9>")); return;
    }
    tonePack = (uint8_t)pack;
    if (pack >= 5 && !configTransaction) ensureToneName(pack - 5);
    for (uint8_t tab = 0; tab < MAX_TABS; tab++) tabTonePacks[tab] = (uint8_t)pack;
    saveConfig(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "TABTONE")) {
    uint16_t tab, pack;
    if (ntok != 3 || !parseUnsigned(tokens[1], tab) || tab < 1 || tab > MAX_TABS ||
        !parseUnsigned(tokens[2], pack) || pack > 9) {
      Serial.println(F("ERR TABTONE <1..5> <0..9>")); return;
    }
    tabTonePacks[tab - 1] = (uint8_t)pack;
    if (pack >= 5 && !configTransaction) ensureToneName(pack - 5);
    saveConfig(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "TABMETA")) {
    uint16_t tab, icon;
    if (ntok != 4 || !parseUnsigned(tokens[1], tab) || tab < 1 || tab > MAX_TABS ||
        !parseUnsigned(tokens[2], icon) || icon > 9) {
      Serial.println(F("ERR TABMETA <1..5> <0..9> <UTF8 hex or ->")); return;
    }
    char name[TAB_NAME_BYTES + 1] = {};
    if (strcmp(tokens[3], "-") != 0) {
      const size_t hexLength = strlen(tokens[3]);
      if (hexLength == 0 || hexLength > TAB_NAME_BYTES * 2 || hexLength % 2 != 0) {
        Serial.println(F("ERR tab name must be <=24 UTF8 bytes")); return;
      }
      for (size_t i = 0; i < hexLength / 2; i++) {
        const int8_t high = hexNibble(tokens[3][i * 2]);
        const int8_t low = hexNibble(tokens[3][i * 2 + 1]);
        if (high < 0 || low < 0 || (high == 0 && low == 0)) {
          Serial.println(F("ERR invalid name hex")); return;
        }
        name[i] = (char)((high << 4) | low);
      }
    }
    tabIcons[tab - 1] = (uint8_t)icon;
    memcpy(tabNames[tab - 1], name, TAB_NAME_BYTES + 1);
    saveConfig(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "DELPACK")) {
    uint16_t pack;
    if (ntok != 2 || !parseUnsigned(tokens[1], pack) || pack < 1 || pack > CUSTOM_TONE_PACKS) {
      Serial.println(F("ERR DELPACK <1..5>")); return;
    }
    memset(customTones[pack - 1], 0, sizeof(customTones[pack - 1]));
    memset(customToneNames[pack - 1], 0, sizeof(customToneNames[pack - 1]));
    if (tonePack == pack + 4) tonePack = 0;
    for (uint8_t tab = 0; tab < MAX_TABS; tab++)
      if (tabTonePacks[tab] == pack + 4) tabTonePacks[tab] = 0;
    stopBuzzer();
    saveConfig(); Serial.println(F("OK")); return;
  }
  if (ieq(tokens[0], "PACKNAME")) {
    uint16_t pack;
    if (ntok != 3 || !parseUnsigned(tokens[1], pack) || pack < 1 || pack > CUSTOM_TONE_PACKS) {
      Serial.println(F("ERR PACKNAME <1..5> <UTF8 hex>")); return;
    }
    char name[TAB_NAME_BYTES + 1] = {};
    const size_t len = strlen(tokens[2]);
    if (len < 2 || len > TAB_NAME_BYTES * 2 || len % 2) {
      Serial.println(F("ERR name must be 1..24 UTF8 bytes")); return;
    }
    for (size_t i = 0; i < len / 2; i++) {
      int8_t high = hexNibble(tokens[2][2 * i]), low = hexNibble(tokens[2][2 * i + 1]);
      if (high < 0 || low < 0 || ((high << 4) | low) < 32 || ((high << 4) | low) == 127) {
        Serial.println(F("ERR invalid name")); return;
      }
      name[i] = (char)((high << 4) | low);
    }
    if (name[0] == ' ' || name[len / 2 - 1] == ' ') {
      Serial.println(F("ERR trim name")); return;
    }
    const char *reserved[] = {"Classic", "Clasic", "Soft", "Delicat", "Bright", "Luminos", "Arcade", "Low", "Grav"};
    for (const char *item : reserved) if (ieq(name, item)) {
      Serial.println(F("ERR name already used")); return;
    }
    for (uint8_t i = 0; i < CUSTOM_TONE_PACKS; i++) if (i != pack - 1 && ieq(name, customToneNames[i])) {
      Serial.println(F("ERR name already used")); return;
    }
    memcpy(customToneNames[pack - 1], name, sizeof(name));
    saveConfig(); Serial.println(F("OK")); return;
  }
  if (ieq(tokens[0], "TESTTONE")) {
    uint16_t freq;
    if (ntok != 2 || !parseUnsigned(tokens[1], freq) || freq > 8000) {
      Serial.println(F("ERR TESTTONE <0..8000>")); return;
    }
    beep(freq, 180); // No EEPROM write; respect global mute and volume.
    Serial.println(F("OK")); return;
  }
  if (ieq(tokens[0], "LIGHTOUTPUT")) {
    uint16_t value;
    if (ntok != 2 || !parseUnsigned(tokens[1], value) || value > 3) {
      Serial.println(F("ERR LIGHTOUTPUT <0=off|1=both|2=RGB|3=external>")); return;
    }
    lightingEnabled = value != 0;
    if (lightingEnabled) ledOutputMode = value - 1;
    saveConfig(); Serial.println(F("OK")); return;
  }
  if (ieq(tokens[0], "TONESET")) {
    uint16_t pack, event, frequency;
    if (ntok != 4 || !parseUnsigned(tokens[1], pack) || pack < 1 || pack > CUSTOM_TONE_PACKS ||
        !parseUnsigned(tokens[2], event) || event >= TONE_EVENTS ||
        !parseUnsigned(tokens[3], frequency) || frequency > 8000) {
      Serial.println(F("ERR TONESET <1..5> <0..17> <0..8000>")); return;
    }
    customTones[pack - 1][event] = frequency;
    if (!configTransaction) ensureToneName(pack - 1);
    saveConfig(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "TONEALL")) {
    uint16_t pack, frequency;
    if (ntok != 4 || !parseUnsigned(tokens[1], pack) || pack < 1 || pack > CUSTOM_TONE_PACKS ||
        (!ieq(tokens[2], "PRESS") && !ieq(tokens[2], "HOLD")) ||
        !parseUnsigned(tokens[3], frequency) || frequency > 8000) {
      Serial.println(F("ERR TONEALL <1..5> <PRESS|HOLD> <0..8000>")); return;
    }
    const uint8_t first = ieq(tokens[2], "PRESS") ? 0 : 7;
    for (uint8_t event = first; event < first + 7; event++) customTones[pack - 1][event] = frequency;
    if (!configTransaction) ensureToneName(pack - 1);
    saveConfig(); Serial.println(F("OK")); return;
  }

  if (ieq(tokens[0], "TABRGB")) {
    uint16_t tab, red, green, blue;
    if (ntok != 5 || !parseUnsigned(tokens[1], tab) || tab < 1 || tab > MAX_TABS ||
        !parseUnsigned(tokens[2], red) || red > 255 ||
        !parseUnsigned(tokens[3], green) || green > 255 ||
        !parseUnsigned(tokens[4], blue) || blue > 255) {
      Serial.println(F("ERR tab color must be: TABRGB <1..5> <0..255> <0..255> <0..255>")); return;
    }
    tabColors[tab - 1] = {(uint8_t)red, (uint8_t)green, (uint8_t)blue};
    if (activeTab == tab - 1) rgbDirty = true;
    saveConfig();
    Serial.println(F("OK"));
    return;
  }

  if (ieq(tokens[0], "GET") && ntok >= 2) {
    int slot = findSlot(tokens[1]);
    if (slot < 0) { Serial.println(F("ERR slot necunoscut")); return; }
    printAction(SLOT_NAMES[slot], actions[slot]);
    return;
  }

  if (ieq(tokens[0], "PRESET") && ntok >= 3) {
    int slot = findSlot(tokens[1]);
    if (slot < 0) { Serial.println(F("ERR slot necunoscut")); return; }
    int preset = findPreset(tokens[2]);
    if (preset < 0) { Serial.println(F("ERR preset necunoscut (vezi PRESETS)")); return; }
    actions[slot] = PRESETS[preset].action;
    saveConfig();
    Serial.println(F("OK"));
    return;
  }

  if (ieq(tokens[0], "SET") && ntok >= 3) {
    int slot = findSlot(tokens[1]);
    if (slot < 0) { Serial.println(F("ERR slot necunoscut")); return; }

    if (ieq(tokens[2], "NONE")) {
      actions[slot] = {ACT_NONE, 0, 0, 0, 0};
      saveConfig();
      Serial.println(F("OK"));
      return;
    }

    if (ieq(tokens[2], "MEDIA")) {
      if (ntok < 4) { Serial.println(F("ERR lipseste numele media (vezi MEDIAS)")); return; }
      uint16_t usage;
      if (!findMedia(tokens[3], usage)) { Serial.println(F("ERR media necunoscuta (vezi MEDIAS)")); return; }
      actions[slot] = {ACT_MEDIA, 0, 0, 0, usage};
      saveConfig();
      Serial.println(F("OK"));
      return;
    }

    if (ieq(tokens[2], "KEY")) {
      if (ntok < 5) { Serial.println(F("ERR foloseste: SET <slot> KEY <mod|NONE> <tasta> [tasta2]")); return; }
      uint8_t mods;
      if (!parseModifiers(tokens[3], mods)) { Serial.println(F("ERR modificator necunoscut (vezi HELP)")); return; }
      uint8_t k1, k2 = 0;
      if (!findKey(tokens[4], k1)) { Serial.println(F("ERR tasta necunoscuta (vezi KEYS)")); return; }
      if (ntok >= 6) {
        if (!findKey(tokens[5], k2)) { Serial.println(F("ERR a doua tasta necunoscuta (vezi KEYS)")); return; }
      }
      actions[slot] = {ACT_KEY, mods, k1, k2, 0};
      saveConfig();
      Serial.println(F("OK"));
      return;
    }

    Serial.println(F("ERR tip necunoscut - foloseste NONE, KEY sau MEDIA"));
    return;
  }

  Serial.println(F("ERR comanda necunoscuta (HELP pentru lista)"));
}

#define LINE_BUF_SIZE 128
char lineBuf[LINE_BUF_SIZE];
uint8_t lineLen = 0;

void pollSerial() {
  while (Serial.available()) {
    char c = (char)Serial.read();
    if (c == '\r') continue;
    if (c == '\n') {
      lineBuf[lineLen] = 0;
      if (lineLen > 0) handleCommand(lineBuf);
      lineLen = 0;
      continue;
    }
    if (lineLen < LINE_BUF_SIZE - 1) {
      lineBuf[lineLen++] = c;
    }
  }
}

// ───────────────────────────────────────────────
// SETUP
// ───────────────────────────────────────────────
void setup() {
  TinyUSBDevice.setManufacturerDescriptor("Adrian");
  TinyUSBDevice.setProductDescriptor("Octoglow Macro");
  usb_hid.begin();

  Serial.begin(SERIAL_BAUD);

  while (!TinyUSBDevice.mounted()) delay(1);

  EEPROM.begin(EEPROM_SIZE);
  loadConfig();

  pinMode(PIN_BUZZER, OUTPUT);
  digitalWrite(PIN_BUZZER, LOW);
  pinMode(PIN_LED, OUTPUT);
  tabPixel.begin();
  tabPixel.setBrightness(72);
  tabPixel.clear();
  tabPixel.show();

  uint8_t allPins[] = {
    PIN_B1, PIN_B2, PIN_B3, PIN_B4, PIN_B5, PIN_B6, PIN_B7, PIN_KNOB_BTN,
#if OCTOGLOW_MACRO_HAS_ENCODER
    PIN_ENC_A, PIN_ENC_B
#endif
  };
  for (uint8_t p : allPins) pinMode(p, INPUT_PULLUP);

  if (OCTOGLOW_MACRO_HAS_ENCODER)
    encState = (digitalRead(PIN_ENC_A) << 1) | digitalRead(PIN_ENC_B);

  playTone(17);
}

// ───────────────────────────────────────────────
// LOOP
// ───────────────────────────────────────────────
void loop() {
  if (configTransaction) {
    pollSerial();
    // No HID/app events or delayed saves can escape while a restore is incomplete.
    if (configTransaction && (uint32_t)(millis() - configTransactionAt) > 30000) {
      configTransaction = false;
      sliderSavePending = false;
      loadConfig();
      rgbDirty = true;
    }
    return;
  }
  pollSimpleButtons();
  if (OCTOGLOW_MACRO_HAS_ENCODER)
    pollHoldButton(PIN_KNOB_BTN, btnKnob, onKnobPress, onKnobHold);
  pollHoldButton(PIN_B7, btnB7, onB7Press, onB7Hold);
  if (OCTOGLOW_MACRO_HAS_ENCODER) pollEncoder();
  updateLed();
  updateAddressableLed();
  updateBuzzer();
  pollSerial();
  if (sliderSavePending && (uint32_t)(millis() - sliderChangedAt) >= 750)
    saveConfig();
}
