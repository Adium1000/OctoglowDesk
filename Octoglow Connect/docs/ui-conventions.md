# Clock and Macro UI conventions

These rules preserve the shared design requested for Octoglow Connect.

- Use Clock as the reference for equivalent Macro pages. Use theme resources,
  not hard-coded gray/blue surfaces. Selected Macro tabs blend into their gray
  content surface; they do not use an accent-blue background.
- Shared navigation glyphs: Integrations `EA86`, brightness/lighting `E706`,
  buzzer `E767`, About `E946`. Macro buttons use the keyboard glyph `E765`.
- Settings cards use a 6 px radius, 68 px minimum height, 16 px horizontal and
  12 px vertical padding. Center icons, text and controls vertically, including
  cards with no description. The primary grid row must absorb spare height.
- Align trailing controls at the right edge. Empty-label ToggleSwitch controls
  have MinWidth=0 and compensate the template's trailing inset, as on Clock.
  Keep toggles inline; move wider controls below the label on narrow cards.
- About uses the same themed overlay, header, close button, divider, scrolling
  sections and cards as Clock. Macro exposes serial port and firmware version.
- Reuse Clock's navigation-card factory and popup XAML rather than wrapping a
  card in another filled button. Popup coordinates and resize bounds come from
  XamlRoot (the whole window), never the device page. Macro About uses auto
  height and a 640 px maximum width; integrations allow up to 880 px.
- Category and inline integration navigation animate in the direction of
  travel. Match Clock's 220 ms inline slide/fade and 167 ms popup fade/scale,
  respect the Windows animation preference, and invalidate queued transitions
  on rapid navigation or unload.
- Both device pages honor InlineClockConfigurationPages. Shift-click keeps the
  Clock shortcut for opening a popup. Inline headers include the integration
  icon, title, translated description and a transparent resting back button.
- Macro Integrations starts with Octoglow devices and Applications navigation
  cards. Each has its own page with a back button. Do not imply that the future
  cross-device permission already implements device-to-device commands.
- Macro tab headers stretch horizontally. Edit and delete are small trailing
  controls next to each other, with translated tooltips and accessible names.
  Preserve rounded content corners, drag reorder, and the last-tab safeguard.
- Macro brightness and volume update their label/preview immediately while
  dragging. Keep one USB command in flight and coalesce to the latest value;
  never disable the slider thumb during a write. Handle failures visibly.
- Firmware applies slider values immediately and delays their flash persistence
  until 750 ms idle. SAVE remains an immediate flush. Public firmware version
  stays 0.2 unless the user explicitly requests a version change.
- Every new user-facing string, including errors, tooltips and accessibility
  names, needs Romanian and English text. Match Clock terminology for shared
  menus. Preserve user settings, mappings and translations during UI changes.
- Before delivery, build Connect and any changed firmware. Check wide and
  narrow layouts, long labels, navigation/back, keyboard focus, all tabs,
  dragging and disconnect behavior. State explicitly which runtime checks
  could not be performed; a successful build is not a visual or hardware test.
