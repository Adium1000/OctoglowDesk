# Octoglow Macro

The firmware in `Firmware/Firmware.ino` is version **0.2** and speaks the
internal configuration protocol 11. Do not increment the public firmware
version until the project owner explicitly requests it. The protocol number
is a data-format revision, not the public firmware version.
The sketch name matches its folder so Arduino IDE and `arduino-cli` can compile it.
Flash this updated firmware before using the configuration pages in Octoglow
Connect. The Waveshare RP2040 Zero build uses the Earle Philhower RP2040 board
package, the Adafruit TinyUSB USB stack, and Adafruit NeoPixel.

## How it works

- USB HID sends keyboard, media and mouse-wheel actions directly. These keep
  working even if Octoglow Connect is closed.
- USB CDC is the configuration and event channel. Connect matches it to the
  already-detected Macro HID interface by the physical USB container ID,
  verifies the version 0.2 (or older V4-V6) `HELLO` reply, and reconnects automatically after unplugging
  or a port-number change. There is no COM-port selection.
- Up to five tabs are stored in the Macro's emulated EEPROM. Every tab has a
  mapping for B1-B7, encoder press, clockwise and counter-clockwise rotation.
  Mappings can select a key, shortcut, media control, wheel scroll, the next or
  previous tab, a specific tab, or an application ID.
- An application ID has no path on the Macro. The executable or shortcut path
  is stored only in Octoglow Connect's `macro-host.json` under the current
  user's OctoglowSender application-data directory. Launching requires Connect
  to be running and connected to the USB configuration channel. Deleted IDs
  are never reused.
- The first flash of the V4 firmware migrated the original V3 single-tab
  mappings, LED mode and buzzer choice. V5 keeps those mappings and adds a
  saved buzzer-volume byte; existing devices default to 100% volume.
- V6 keeps the existing mappings and buzzer settings, then adds one RGB color
  for each tab and a lighting-output mode. The Waveshare RP2040 Zero's onboard
  WS2812B uses GPIO 16. The existing monochrome backlight uses GPIO 2. Select
  either both LEDs or the addressable LED alone; the backlight effect applies
  only when both are enabled. Tab colors change immediately when a tab changes,
  even when Connect is closed. Older configurations migrate with a blue color
  for each tab and both LEDs enabled. RGB output is capped at a comfortable
  brightness rather than driving the onboard LED at full intensity.
- Version 0.2 migrates the V1-V4 EEPROM layouts without deleting mappings. It
  adds an on/off lighting switch, eight effects stored separately per tab,
  three lighting output modes (RGB, external, or both), five built-in buzzer
  tone packs and five editable packs. The editable packs hold separate tones
  for B1-B7 press and hold, K1 press, encoder left/right, and USB startup.
  The public firmware version is deliberately separate from the serial protocol.
- EEPROM layout 6 (protocol 8) retains all existing mappings and tone presets,
  then adds per-tab names (up to 24 UTF-8 bytes), icons, buzzer themes, and a
  shared 0-100% LED brightness setting. Any tab can be removed atomically;
  subsequent tabs shift left and Go-to-tab actions are adjusted. A reference
  to the deleted tab becomes Next tab. Existing configuration is migrated.
- Protocol 9 keeps the same EEPROM layout and public firmware version 0.2.
  `MOVETAB` moves a tab together with its mappings, color, lighting effect,
  buzzer theme, icon and name. Go-to-tab targets and the active tab are remapped
  to follow the same logical tabs. Connect enables drag-and-drop only with this
  protocol, so older firmware cannot show an order it has not saved.
- Set `OCTOGLOW_MACRO_HAS_ENCODER` to `0` near the top of the sketch for an
  eight-key build. GPIO 15 then becomes an ordinary B8 key; encoder polling is
  disabled and Connect hides rotation settings. The serial slot remains
  `knob` solely to preserve saved mappings and protocol compatibility.

The first seven buttons and encoder press/rotation are configurable. Holding
B7 still cycles LED effects; holding the encoder button still toggles the
buzzer. Tab switching changes the active mapping without writing flash on every
press, avoiding unnecessary flash wear. Saved mappings, tab count and LED/buzzer
settings and tab colors survive a restart. Connect asks which physical key should switch tabs
when adding one and prevents removing the last switch key from a tab. Buzzer
volume is saved on the device. Continuous hardware PWM replaces the old audible
125 Hz gating. The period remains constant; duty follows an inverse-sine curve
with a squared volume input. A passive piezo has no analog gain control, so
loudness and harmonic balance still depend on the connected buzzer. Changing
volume plays a short preview ding. Validate the result on the physical board.

Protocol 10 / EEPROM layout 7 adds five independently named custom tone presets
(24 UTF-8 bytes per name). Public firmware version remains **0.2**. Migration
preserves all mappings, sounds, colors and names, and gives previously edited
or assigned custom presets default names. Blank unused slots can be added from
Connect's Buzzer > Custom tones page. Names must be unique, including the English
and Romanian built-in preset names. The tone tabs are not keyboard mapping tabs.
Global lighting/buzzer switches hide irrelevant per-tab controls without clearing
their values. The shared lighting effect applies to either enabled LED output;
the RGB color option appears only when the addressable LED is enabled.

## Serial protocol (115200 baud)

Protocol 11 / EEPROM layout 8 separates the two lighting outputs. External
brightness remains in the existing field; RGB brightness and five RGB effects
are appended to flash without moving existing data. Migration copies the old
shared brightness and effects to RGB, preserving the previous appearance.
Output on/off selection never erases either output's settings. Adding, deleting
and reordering tabs also carries their separate RGB and external effects.
In Connect, combined lighting exposes an expandable Brightness card with two
live sliders. Each enabled output has its own effect picker, with the existing
single-tab/all-tabs choice. Firmware version remains **0.2**.

`HELLO` returns `OCTOGLOW MACRO 0.2 READY`. `DUMP` returns `CFG 11 <tab-count>
<active-tab> <legacy-led> <buzzer> <volume> <led-mode> <lighting-on>
<tone-pack> <has-encoder> <external-brightness>`, one `RGBBRIGHT <0..100>` line,
five `RGBFX <tab> <effect>` lines and five `COLOR <tab> <red> <green> <blue>` lines,
five `FX <tab> <effect>` lines, five `META <tab> <tone-pack> <icon> <UTF-8-name-hex-or-dash>`
lines, five `PACK <1..5> <UTF-8-name-hex-or-dash>` lines, ninety `TONE <custom-pack> <event>
<frequency>` lines, fifty `MAP <tab> <slot> <type> <modifiers> <key1>
<key2> <value>` lines and `END`. Tabs are numbered 1-5. Slots are
`b1`...`b7`, `knob`, `cw`, `ccw`.

`SETMAP <tab> <slot> <type> <modifiers> <key1> <key2> <value>` changes one
mapping in RAM. `SETTABCOUNT <1..5>` and `ACTIVE <tab>` update the tab state.
`ADDTAB <b1..b7|knob>` atomically adds a tab, assigns that key `Next tab` on
every tab, and saves the configuration.
`DELTAB <tab>` removes any non-last tab or the last tab, preserving remaining
configurations. `MOVETAB <from> <to>` inserts a tab at a new position in one
saved operation. `TABMETA <tab> <icon 0..9> <UTF-8-name-hex-or-dash>` saves the
name and icon. `TABTONE <tab> <pack 0..9>` saves a tab-specific buzzer theme.
`BRIGHT <0..100>` sets brightness of both active lighting outputs.
`RGBBRIGHT <0..100>` and `EXTBRIGHT <0..100>` set only the chosen output,
with the same delayed flash persistence. `RGBFX <tab> <0..7>` and
`EXTFX <tab> <0..7>` save only that output's effect for a tab;
`RGBFXALL <0..7>` and `EXTFXALL <0..7>` apply it to all five slots.
Legacy `LED`, `TABFX`, `TABFXALL` and `BRIGHT` deliberately still update both
outputs. Updated Connect uses the separate commands; older companion clients
retain their shared-setting semantics. Pair this firmware with a protocol-11
capable Connect or Browser Companion to read its extended configuration.
`PACKNAME <1..5> <UTF-8-name-hex>` adds or renames a custom preset without
changing its sounds or assignments. `TESTTONE <0..8000>` previews for 180 ms,
respects global mute/volume and does not write flash. `LIGHTOUTPUT <0..3>`
atomically selects off / both / addressable / external. Off retains the last
output selection, effects and colors. The older lighting commands remain valid.
`BRIGHT` and `BUZZERVOL <0..100>` apply immediately in RAM and acknowledge with
`OK`. Flash persistence is coalesced until 750 ms after the latest slider
command, avoiding a flash erase for every drag update. Wait at least one second
after dragging before unplugging, or use `SAVE` to commit immediately.
`SAVE` commits the configuration. `TABFX <tab> <0..7>`, `TABFXALL <0..7>`,
`LIGHT ON|OFF`, `LEDMODE <0|1|2>` (both, RGB only, external only),
`TABRGB <tab> <red> <green> <blue>`, `BUZZER ON|OFF`,
`TONEPACK <0..9>`, `TONESET <custom-pack 1..5> <event 0..17> <Hz 0..8000>`
and `TONEALL <custom-pack> <PRESS|HOLD> <Hz>` save immediately. Zero hertz
means silence. Commands return `OK` or `ERR ...`; `SAVE` returns `OK SAVED`.

Action types: 0 none; 1 keyboard; 2 multimedia; 3 scroll (`key1` 1 up or 2
down); 4 next tab; 5 previous tab; 6 go to tab (`key1` target); 7 launch an
application (`value` is its host-side ID). The firmware emits `EVENT <tab>
<slot>` for a physical action, `EVENT <tab> <slot> APP <id>` for application
launches, and `TAB_ACTIVE <tab>` after a physical tab switch.
