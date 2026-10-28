# Macro configuration backups and Clock actions

Public Macro firmware version stays **0.2**. The new configuration protocol is **12**; EEPROM layout stays **8**. Installing this firmware does not intentionally reset existing settings. Always keep an exported backup before updating hardware.

## About → Configuration backup

Export reads all five tab slots, every key/encoder mapping, names/icons/colors, independent lighting levels/effects, buzzer settings and all 90 custom tone values from the board. The JSON also contains Macro host settings: application paths and IDs, Clock action bindings, integration permission, local tone-name overrides, tab appearance cache and preview divider position. Clock login credentials, session cookies and unrelated Connect settings are excluded.

Treat the file as private: it includes local application paths and device identifiers. Import only trusted files. Imported applications are not launched by the import itself; an assigned physical key can launch them afterward. Application paths must be absolute `.exe`/`.lnk` paths. Missing files remain configured so they can work after moving/installing the application; they cannot launch until the file exists.

Export supports older firmware. Import requires protocol 12 so Connect can stage the entire restore before saving. The UI explains this requirement and disables import on older boards. A recovery JSON is written to `%LOCALAPPDATA%\OctoglowSender\Backups` before modifying the board. Import replaces Macro host settings, not Clock accounts.

## Restore transaction

Connect validates the format and the complete DUMP, constructs a whitelist of commands from parsed values, acquires exclusive serial access and issues `BEGINCFG`. Firmware stages changes in RAM, suppresses HID/host events and EEPROM writes, and retains its previous persisted configuration. Connect compares a fresh DUMP with the requested settings before `COMMITCFG`. The final commit writes EEPROM once. `ABORTCFG`, power loss before commit, or 30 seconds without commands discards the staged state. Power loss during the final physical flash write is not guaranteed recoverable; keep the recovery JSON. A lost commit acknowledgement is explicitly reported as an uncertain outcome, not a successful rollback.

Public version remains 0.2. Do not increase it without the owner's explicit request. Browser Companion accepts protocol 12 and continues to support 7–11.

## Macro → Clock

Enable Integrations → Octoglow devices, then edit a key and choose **Control an Octoglow device**. Select the signed-in Clock and one of six explicit actions: screen on/off/toggle or buzzer on/off/toggle. The selected device ID and command remain in Connect; Macro stores only a host-action ID using the existing application-event mechanism. Connect must be running and the Clock must be reachable.

Screen off uses Clock's Sleep action; touch/activity/notifications can wake it according to Clock settings. Commands are allowlisted. They never silently fall back to a different Clock. Turning the integration off prevents execution without erasing bindings. The current Connect account model has one signed-in Clock: the picker lists saved hub Clocks, and commands require that target to match the configured account. Errors appear in the activity log. Repeated input while a request is in flight is dropped instead of queuing stale actions for an offline Clock.

## Interface

- Brightness uses the same expander/header/subrows as Clock volume. The header slider sets both outputs; the expanded sliders operate independently. When values differ, the header displays their maximum. Writes are coalesced at approximately 100 ms while dragging, then persisted by firmware after the existing idle interval.
- Buttons, lighting and buzzer sections inside each tab can be expanded/collapsed; state is retained for the current page session.
- Application removal uses a smaller, borderless button with a tooltip and accessibility label.
- Media actions include standard HID screen brightness up/down. Windows/monitor support determines whether those keys affect the PC display; they are separate from Macro LED brightness.
- User-facing new labels are provided in Romanian and English. Shared controls intentionally reuse Clock design resources.

## Verification and limits

Unit checks cover parser completeness, duplicate/injected records, backup validation, host cloning/serialization, legacy tone names, all restored field groups, explicit Clock routing and the disabled-integration guard. Browser tests include protocol 12. Firmware is compiled for Waveshare RP2040 Zero with TinyUSB. UI is checked in an isolated no-USB harness at wide/narrow sizes.

No real board import, firmware flash, application launch or live Clock control is performed as part of these automated checks. Those remain hardware acceptance tests after installing the new firmware.
