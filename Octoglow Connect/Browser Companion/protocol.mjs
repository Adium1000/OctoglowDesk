// Octoglow Macro firmware 0.2, USB CDC configuration protocols 7-12.
// Keep numeric values aligned with Firmware/Firmware.ino and MacroConfiguration.cs.
export const HELLO_REPLY = 'OCTOGLOW MACRO 0.2 READY';
export const SLOTS = ['b1', 'b2', 'b3', 'b4', 'b5', 'b6', 'b7', 'knob', 'cw', 'ccw'];
export const EFFECTS = ['Static', 'Breathing', 'Blink', 'Double pulse', 'Heartbeat', 'Wave', 'Sparkle', 'Strobe'];
export const TONE_PACKS = ['Classic', 'Soft', 'Bright', 'Arcade', 'Low', 'Custom 1', 'Custom 2', 'Custom 3', 'Custom 4', 'Custom 5'];
export const TONE_FREQUENCIES = [0, 220, 330, 440, 523, 660, 784, 880, 1047, 1200, 1500, 1800, 2200, 3000, 4000];
export const MEDIA = [
  ['Play / Pause', 0x00cd], ['Next track', 0x00b5], ['Previous track', 0x00b6], ['Stop', 0x00b7],
  ['Mute', 0x00e2], ['Volume +', 0x00e9], ['Volume -', 0x00ea], ['Calculator', 0x0192],
  ['Screen brightness +', 0x006f], ['Screen brightness -', 0x0070],
  ['Email', 0x018a], ['Browser', 0x0194], ['Web search', 0x0221], ['Web home', 0x0223],
  ['Browser back', 0x0224], ['Browser forward', 0x0225], ['Browser refresh', 0x0227]
];
export const KEYS = [
  ...Array.from({length: 26}, (_, i) => [String.fromCharCode(65 + i), 4 + i]),
  ...Array.from({length: 9}, (_, i) => [String(i + 1), 30 + i]), ['0', 39],
  ['Enter', 40], ['Escape', 41], ['Backspace', 42], ['Tab', 43], ['Space', 44], ['-', 45], ['=', 46],
  ...Array.from({length: 12}, (_, i) => [`F${i + 1}`, 58 + i]),
  ['Print Screen', 70], ['Pause', 72], ['Insert', 73], ['Home', 74], ['Page Up', 75],
  ['Delete', 76], ['End', 77], ['Page Down', 78], ['Right', 79], ['Left', 80], ['Down', 81], ['Up', 82],
  ...Array.from({length: 12}, (_, i) => [`F${i + 13}`, 104 + i])
];

const integer = (value, min, max, label) => {
  if (!/^\d+$/.test(value ?? '')) throw new Error(`Invalid ${label}.`);
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < min || parsed > max) throw new Error(`Invalid ${label}.`);
  return parsed;
};

export function parseDump(lines) {
  if (!Array.isArray(lines) || lines.length < 2 || lines.at(-1) !== 'END') throw new Error('Incomplete Macro configuration.');
  const head = lines[0].trim().split(/\s+/);
  const extended = head.length === 12 && ['8', '9', '10', '11', '12'].includes(head[1]);
  if (!((head.length === 11 && head[1] === '7') || extended) || head[0] !== 'CFG')
    throw new Error('Macro firmware 0.2 / protocol 7-12 is required.');
  const config = {
    protocol: Number(head[1]),
    tabCount: integer(head[2], 1, 5, 'tab count'), activeTab: integer(head[3], 1, 5, 'active tab'),
    legacyEffect: integer(head[4], 0, 7, 'LED effect'), buzzer: integer(head[5], 0, 1, 'buzzer') === 1,
    volume: integer(head[6], 0, 100, 'volume'), ledMode: integer(head[7], 0, 2, 'LED mode'),
    lighting: integer(head[8], 0, 1, 'lighting') === 1, tonePack: integer(head[9], 0, 9, 'tone pack'),
    hasEncoder: integer(head[10], 0, 1, 'encoder') === 1,
    brightness: extended ? integer(head[11], 0, 100, 'brightness') : 100,
    colors: Array(5).fill(null), effects: Array(5).fill(null),
    rgbBrightness: null, rgbEffects: Array(5).fill(null),
    tones: Array.from({length: 5}, () => Array(18).fill(null)),
    maps: Array.from({length: 5}, () => Object.create(null)),
    metadata: Array(5).fill(null), toneNames: Array(5).fill(null)
  };
  if (config.activeTab > config.tabCount) throw new Error('Invalid active tab.');
  const seen = new Set();
  for (const line of lines.slice(1, -1)) {
    const cell = line.trim().split(/\s+/);
    const kind = cell[0];
    if (kind === 'RGBBRIGHT' && cell.length === 2 && config.protocol >= 11) {
      if (config.rgbBrightness !== null) throw new Error('Duplicate RGB brightness.');
      config.rgbBrightness = integer(cell[1], 0, 100, 'RGB brightness');
    } else if (kind === 'RGBFX' && cell.length === 3 && config.protocol >= 11) {
      const tab = integer(cell[1], 1, 5, 'RGB effect tab');
      if (config.rgbEffects[tab - 1] !== null) throw new Error('Duplicate RGB effect.');
      config.rgbEffects[tab - 1] = integer(cell[2], 0, 7, 'RGB effect');
    } else if (kind === 'PACK' && cell.length === 3 && config.protocol >= 10) {
      const pack = integer(cell[1], 1, 5, 'named tone pack');
      if (config.toneNames[pack - 1] !== null) throw new Error('Duplicate tone name.');
      const hex = cell[2];
      if (hex !== '-' && (!/^(?:[0-9a-f]{2})+$/i.test(hex) || hex.length > 48))
        throw new Error('Invalid tone name.');
      const bytes = hex === '-' ? new Uint8Array() : Uint8Array.from(hex.match(/../g), byte => parseInt(byte, 16));
      if (bytes.some(b => b < 32 || b === 127)) throw new Error('Invalid tone name.');
      config.toneNames[pack - 1] = new TextDecoder('utf-8', {fatal:true}).decode(bytes);
    } else if (kind === 'COLOR' && cell.length === 5) {
      const tab = integer(cell[1], 1, 5, 'color tab');
      if (config.colors[tab - 1]) throw new Error('Duplicate tab color.');
      config.colors[tab - 1] = [integer(cell[2], 0, 255, 'red'), integer(cell[3], 0, 255, 'green'), integer(cell[4], 0, 255, 'blue')];
    } else if (kind === 'FX' && cell.length === 3) {
      const tab = integer(cell[1], 1, 5, 'effect tab');
      if (config.effects[tab - 1] !== null) throw new Error('Duplicate tab effect.');
      config.effects[tab - 1] = integer(cell[2], 0, 7, 'effect');
    } else if (kind === 'TONE' && cell.length === 4) {
      const pack = integer(cell[1], 1, 5, 'tone pack');
      const event = integer(cell[2], 0, 17, 'tone event');
      if (config.tones[pack - 1][event] !== null) throw new Error('Duplicate custom tone.');
      config.tones[pack - 1][event] = integer(cell[3], 0, 8000, 'frequency');
    } else if (kind === 'META' && cell.length === 5 && extended) {
      const tab = integer(cell[1], 1, 5, 'metadata tab');
      if (config.metadata[tab - 1]) throw new Error('Duplicate tab metadata.');
      const tonePack = integer(cell[2], 0, 9, 'tab tone pack');
      const icon = integer(cell[3], 0, 9, 'tab icon');
      const hex = cell[4];
      if (hex !== '-' && (!/^(?:[0-9a-f]{2})+$/i.test(hex) || hex.length > 48))
        throw new Error('Invalid tab name.');
      const bytes = hex === '-' ? new Uint8Array() : Uint8Array.from(hex.match(/../g), byte => parseInt(byte, 16));
      if (bytes.includes(0)) throw new Error('Invalid tab name.');
      config.metadata[tab - 1] = {tonePack, icon, name: new TextDecoder('utf-8', {fatal:true}).decode(bytes)};
    } else if (kind === 'MAP' && cell.length === 8) {
      const tab = integer(cell[1], 1, 5, 'mapping tab');
      const slot = cell[2];
      if (!SLOTS.includes(slot)) throw new Error('Unknown Macro key.');
      const id = `${tab}:${slot}`;
      if (seen.has(id)) throw new Error('Duplicate key mapping.');
      seen.add(id);
      config.maps[tab - 1][slot] = {
        type: integer(cell[3], 0, 7, 'action type'), modifiers: integer(cell[4], 0, 255, 'modifiers'),
        key1: integer(cell[5], 0, 255, 'key 1'), key2: integer(cell[6], 0, 255, 'key 2'),
        value: integer(cell[7], 0, 65535, 'action value')
      };
    } else throw new Error('Unexpected data in Macro configuration.');
  }
  if (config.colors.some(v => !v) || config.effects.some(v => v === null) ||
      config.tones.some(pack => pack.some(v => v === null)) || seen.size !== 50 ||
      (extended && config.metadata.some(v => !v)) ||
      (config.protocol >= 10 && config.toneNames.some(v => v === null)) ||
      (config.protocol >= 11 && (config.rgbBrightness === null || config.rgbEffects.some(v => v === null))))
    throw new Error('The Macro configuration is incomplete.');
  if (config.protocol < 11) {
    config.rgbBrightness = config.brightness;
    config.rgbEffects = [...config.effects];
  }
  return config;
}

export function serializeAction(tab, slot, action) {
  const values = [tab, action.type, action.modifiers, action.key1, action.key2, action.value];
  const ranges = [[1, 5], [0, 7], [0, 255], [0, 255], [0, 255], [0, 65535]];
  if (!SLOTS.includes(slot) || values.some((v, i) => !Number.isSafeInteger(v) || v < ranges[i][0] || v > ranges[i][1]))
    throw new Error('Invalid Macro action.');
  return `SETMAP ${tab} ${slot} ${action.type} ${action.modifiers} ${action.key1} ${action.key2} ${action.value}`;
}

export function isTabSwitch(action, tab) {
  return action?.type === 4 || action?.type === 5 || (action?.type === 6 && action.key1 !== tab);
}

export function hasTabSwitch(config, tab, excluding = null) {
  return SLOTS.slice(0, 8).some(slot => slot !== excluding && isTabSwitch(config.maps[tab - 1][slot], tab));
}

export function keyLabel(code) { return KEYS.find(([, value]) => value === code)?.[0] ?? `#${code}`; }
export function mediaLabel(code) { return MEDIA.find(([, value]) => value === code)?.[0] ?? `#${code}`; }
export function actionSummary(action, tab, tr) {
  if (!action) return tr('noAction');
  switch (action.type) {
    case 1: {
      const parts = [];
      if (action.modifiers & 1) parts.push('Ctrl');
      if (action.modifiers & 2) parts.push('Shift');
      if (action.modifiers & 4) parts.push('Alt');
      if (action.modifiers & 8) parts.push('Win');
      if (action.key1) parts.push(keyLabel(action.key1));
      if (action.key2) parts.push(keyLabel(action.key2));
      return parts.join(' + ') || tr('noAction');
    }
    case 2: return mediaLabel(action.value);
    case 3: return action.key1 === 1 ? tr('scrollUp') : tr('scrollDown');
    case 4: return tr('nextTab');
    case 5: return tr('previousTab');
    case 6: return `${tr('goToTab')} ${action.key1}`;
    case 7: return `${tr('application')} #${action.value}`;
    default: return tr('noAction');
  }
}
