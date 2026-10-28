# Octoglow Connect · Browser companion

A local HTML/CSS/JavaScript companion for **Octoglow Macro only**. It uses the
firmware 0.2 USB CDC protocol 7, 8 or 9; it does not change the firmware or
its public version. Clock and Wi-Fi devices are intentionally not included.

## Run

1. Close Octoglow Connect completely, including its tray process, plus Arduino
   Serial Monitor or any other program holding the Macro's COM port. Only one
   program can use its USB CDC configuration channel at a time.
2. From this directory, run `python -m http.server 8765`.
3. Open `http://localhost:8765` in a desktop browser with Web Serial support,
   such as Chrome or Edge. Do not use a remote HTTP origin: Web Serial requires
   a secure context (localhost or HTTPS).
4. Select **Choose device**, then select the Macro's serial interface in the
   browser permission dialog. The page sends `HELLO` and accepts only
   `OCTOGLOW MACRO 0.2 READY`; it then reads the full configuration using
   `DUMP`. The chosen port is never trusted solely by its USB ID.

The browser can reconnect an already-authorized Macro on a later visit if its
USB vendor/product IDs match the device previously verified by `HELLO`.
New, unapproved ports cannot be scanned silently by a website.

## Features and limits

- Configure B1-B7 and K1/B8 actions, including keyboard shortcuts, media,
  scrolling and tab switches. Encoder rotation appears only on encoder builds.
- Add up to five tabs, choose a tab-switch key, edit tab colors, activate a tab,
  and remove the last tab.
- Configure lighting power, output mode, all eight effects, per-tab color,
  buzzer power/volume, tone pack and the 18 events in each custom tone pack.
- The page never reads paths or launches desktop applications. Existing
  application-ID mappings remain visible but require Octoglow Connect desktop
  to execute them. The HTML editor does not create application mappings.
- The browser does not expose Windows COM names, so About shows the USB ID and
  verified firmware version rather than a COM number.
- Interface strings are available in Romanian and English.

No external packages, fonts or network services are required. All settings
written by this page are saved on the Macro by the existing firmware commands.

## Tests

Run `node --test tests/*.test.mjs` from this directory. The protocol tests use
a complete synthetic firmware dump; they do not alter a real device.
