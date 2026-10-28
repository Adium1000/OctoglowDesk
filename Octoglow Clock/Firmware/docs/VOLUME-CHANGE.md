# Volume Change priority tile

The Romanian name is **Schimbare volum**. Add it from **Tile Manager → Add a tile** in the priority list, then enable it. It starts hidden and disabled on an existing clock, consistently with the other optional Connect integrations. Both the clock firmware and Octoglow Connect must include this feature.

Octoglow Connect observes the Windows default multimedia output's master volume and mute state. It sends changes only; it does not capture audio, change system volume, or send device names. Startup, unplugging an output, and switching the default output establish a new baseline without showing a notification. Per-application volume is intentionally not monitored.

The clock shows an icon followed by `0%`–`100%`, or `Mute` when Windows reports mute. The tile expires 3,000 ms after the last received update. Repeated changes redraw the existing tile without replaying its entrance transition. Alarm and other priority ordering remains user-controlled. Updates are transient RAM data and do not write flash.

## Appearance

The tile's settings are available from its gear in the web UI and Connect:

- Follow the output icon: speakers, headphones/headset, or Bluetooth. Core Audio form-factor and connected-adapter metadata identify the source. Drivers that do not expose their transport fall back to their form factor; choose a fixed icon if necessary.
- Fixed icon when adaptation is off. The previous fixed selection remains saved.
- Font and entrance transition, also available in the common Appearance pages.
- Common icon visibility, scrolling and transition-speed controls also include the tile.

The standard preview displays an example while no live volume event is present. The demo uses simulated data and never reads or changes the host volume.

## Protocol and persistence

Authenticated `POST /volumestatus`: form fields `pct=0..100`, `mute=0|1`, `source=0|1|2` (speakers, headphones, Bluetooth). Missing or invalid fields return 400. Disabled or hidden tiles ignore events. No event queue is stored on the clock.

Authenticated `POST /volumetoggle`: `enabled=0|1`. Enabling/disabling clears any old transient event. `/settings` and `/state` use `volumeAdaptiveIcon`, `hideIconVolume`, `iconSelVolume`, `fontTypeVolume`, `scrollTypeVolume`, `scrollSpeedVolume`, `tileTransVolume` and `tileTransSpdVolume`. `/state` also includes `volumeEnabled`. Settings participate in persistence and backup restore.

Priority ID 19, scroll-speed index 17, and transition-speed index 34 are appended, preserving existing stored IDs. The migration marker `volPrioInit` prevents a previously added tile from being hidden again on restart.

Connect samples at 100 ms. A one-entry mailbox replaces outdated pending values while a network request is in flight. Requests have a one-second deadline and old events are discarded. HTTP 404 stops this sender until Connect reconnects, so older firmware does not receive repeated unsupported requests. Session expiry follows Connect's existing authenticated transport.

## Building and verification

Run `python builders/!buildall.py` from `Firmware`, then `python builders/!buildall.py --check`. `portal.html` and `demo/demo_mock.js` are sources; `web_assets.h` and `demo/index.html` are generated.

Regression checks cover expiry at the three-second boundary, repeated updates, mute snapshots, hidden/disabled state, timer wraparound and priority preemption; Connect checks cover startup baselines, endpoint changes, reconnect, latest-value coalescing and older firmware. A read-only Windows probe verifies the real COM interface without modifying volume. Device flashing and an end-to-end physical display test remain separate from these build/test steps.
