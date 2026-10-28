import {MacroSerial} from './serial.mjs';
import {SLOTS, EFFECTS, TONE_PACKS, TONE_FREQUENCIES, MEDIA, KEYS,
  parseDump, serializeAction, actionSummary, hasTabSwitch} from './protocol.mjs';

const words = {
  ro: {
    devices:'Dispozitive', wiredDevice:'Dispozitiv USB', localOnly:'Rulează local în browser', language:'Limbă',
    subtitle:'Configurează dispozitivul direct din browser.', disconnected:'Deconectat', connected:'Conectat',
    connecting:'Se conectează…', chooseDevice:'Alege dispozitivul', disconnect:'Deconectează',
    devicePreview:'PREVIZUALIZARE DISPOZITIV', readyTitle:'Macro, gata de configurat.',
    readyText:'Conectează Macro prin USB, apoi alege portul din dialogul browserului. Aplicația desktop trebuie să fie închisă ca portul să fie liber.',
    connectedTitle:'Macro este conectat.', connectedText:'Schimbările sunt trimise direct către dispozitiv și salvate în firmware.',
    noTabs:'Taburi —', tabs:'Taburi', configure:'CONFIGURARE', buttons:'Butoane', lighting:'Iluminare', about:'Despre',
    refresh:'Reîmprospătează', browserFooter:'Doar Octoglow Macro · USB serial', savedToMacro:'Modificările se salvează pe Macro.',
    cancel:'Anulează', save:'Salvează', selectDevice:'Alege un Octoglow Macro',
    noConnectionTitle:'Conectează Octoglow Macro', noConnectionText:'După conectare, aici apar setările salvate pe dispozitiv.',
    unsupportedBrowser:'Acest browser nu oferă Web Serial. Folosește Chrome sau Edge pe desktop, prin localhost ori HTTPS.',
    busyPort:'Portul este ocupat. Închide Octoglow Connect, Arduino Serial Monitor și alte aplicații care folosesc Macro.',
    notMacro:'Portul ales nu este un Octoglow Macro cu firmware 0.2. Nu am trimis nicio setare.',
    refreshed:'Configurația a fost citită din nou.', saved:'Salvat pe Macro.', error:'Eroare',
    keysConfigured:'acțiuni configurate', tab:'Tab', addTab:'Adaugă tab', removeLast:'Elimină ultimul tab',
    removeConfirm:'Elimini ultimul tab și mapările lui? Această operație nu poate fi anulată.',
    chooseSwitch:'Alege tasta care va schimba tabul', chooseSwitchText:'Firmware-ul va atribui acelei taste „Tabul următor” pe toate taburile.',
    create:'Creează', tabColor:'Culoarea tabului', activate:'Activează tabul', physicalTab:'Activ pe dispozitiv',
    pressKeys:'Butoane', rotation:'Rotire encoder', turnRight:'Rotire dreapta', turnLeft:'Rotire stânga',
    noAction:'Fără acțiune', key:'Tastă', shortcut:'Scurtătură', media:'Multimedia', scroll:'Scroll',
    scrollUp:'Scroll în sus', scrollDown:'Scroll în jos', nextTab:'Tabul următor', previousTab:'Tabul anterior',
    goToTab:'Mergi la tabul', application:'Aplicație', unavailableInBrowser:'Aplicațiile locale pot fi lansate doar prin Octoglow Connect desktop.',
    configureKey:'Configurează', action:'Acțiune', primaryKey:'Tastă principală', secondKey:'A doua tastă',
    direction:'Direcție', up:'Sus', down:'Jos', targetTab:'Tab de destinație', modifiers:'Modificatori',
    requiresSwitch:'Fiecare tab trebuie să păstreze cel puțin o tastă pentru schimbarea tabului.',
    lightingIntro:'Culoarea și efectul se salvează separat pentru fiecare tab.',
    lightingOn:'Iluminare activă', lightingOnHint:'Pornește sau oprește toate ieșirile de lumină.',
    outputMode:'LED-uri active', outputModeHint:'Alege ce LED-uri să folosească Macro.',
    combined:'Adresabil + externe', addressable:'Doar LED adresabil', external:'Doar LED-uri externe',
    ledEffect:'Efect LED', ledEffectHint:'Aplică un efect tabului ales sau tuturor taburilor.',
    applyTo:'Aplică pentru', allTabs:'Toate taburile', colorHint:'Culoarea LED-ului adresabil pentru tabul selectat.',
    buzzerOn:'Sunet la apăsare', buzzerOnHint:'Activează feedbackul sonor.',
    volume:'Volumul buzzerului', volumeHint:'Modifică intensitatea fără a schimba frecvența.',
    tonePack:'Pachet de tonuri', tonePackHint:'Cinci preseturi incluse și cinci personalizabile.',
    customTones:'Tonuri personalizate', pressAll:'Toate la apăsare', holdAll:'Toate la Hold',
    press:'Apăsare', hold:'Hold', startup:'Conectare USB / pornire', hz:'Hz', silent:'Silențios',
    firmware:'Versiune firmware', protocol:'Protocol', deviceId:'Identificator USB',
    browserPort:'Port serial', browserPortValue:'Gestionat de browser', aboutHint:'Browserul nu expune numele COM. Dispozitivul este verificat prin răspunsul firmware-ului.',
    limitation:'Limitare browser', limitationText:'Lansarea aplicațiilor de pe PC nu este posibilă din această pagină. Mapările existente pentru aplicații sunt păstrate, dar se execută numai când Connect desktop rulează.',
    noEncoder:'Model cu 8 taste', ready:'Gata'
  },
  en: {
    devices:'Devices', wiredDevice:'USB device', localOnly:'Runs locally in your browser', language:'Language',
    subtitle:'Configure your device directly in the browser.', disconnected:'Disconnected', connected:'Connected',
    connecting:'Connecting…', chooseDevice:'Choose device', disconnect:'Disconnect',
    devicePreview:'DEVICE PREVIEW', readyTitle:'Macro, ready to configure.',
    readyText:'Connect Macro by USB, then choose its port in the browser dialog. Close the desktop app so the port is available.',
    connectedTitle:'Macro is connected.', connectedText:'Changes are sent directly to the device and saved in firmware.',
    noTabs:'Tabs —', tabs:'Tabs', configure:'CONFIGURATION', buttons:'Buttons', lighting:'Lighting', about:'About',
    refresh:'Refresh', browserFooter:'Octoglow Macro only · USB serial', savedToMacro:'Changes are saved to the Macro.',
    cancel:'Cancel', save:'Save', selectDevice:'Choose an Octoglow Macro',
    noConnectionTitle:'Connect Octoglow Macro', noConnectionText:'After connecting, settings saved on the device appear here.',
    unsupportedBrowser:'This browser does not provide Web Serial. Use desktop Chrome or Edge on localhost or HTTPS.',
    busyPort:'The port is in use. Close Octoglow Connect, Arduino Serial Monitor, and other apps using the Macro.',
    notMacro:'The selected port is not an Octoglow Macro with firmware 0.2. No settings were sent.',
    refreshed:'Configuration read again.', saved:'Saved to Macro.', error:'Error',
    keysConfigured:'configured actions', tab:'Tab', addTab:'Add tab', removeLast:'Remove last tab',
    removeConfirm:'Remove the last tab and its mappings? This cannot be undone.',
    chooseSwitch:'Choose the tab-switch key', chooseSwitchText:'Firmware will assign “Next tab” to this key on every tab.',
    create:'Create', tabColor:'Tab color', activate:'Activate tab', physicalTab:'Active on device',
    pressKeys:'Buttons', rotation:'Encoder rotation', turnRight:'Turn right', turnLeft:'Turn left',
    noAction:'No action', key:'Key', shortcut:'Shortcut', media:'Media', scroll:'Scroll',
    scrollUp:'Scroll up', scrollDown:'Scroll down', nextTab:'Next tab', previousTab:'Previous tab',
    goToTab:'Go to tab', application:'Application', unavailableInBrowser:'Local applications can only be launched by Octoglow Connect desktop.',
    configureKey:'Configure', action:'Action', primaryKey:'Primary key', secondKey:'Second key',
    direction:'Direction', up:'Up', down:'Down', targetTab:'Destination tab', modifiers:'Modifiers',
    requiresSwitch:'Each tab must keep at least one key that switches tabs.',
    lightingIntro:'Color and effect are saved separately for each tab.',
    lightingOn:'Lighting on', lightingOnHint:'Turn all lighting outputs on or off.',
    outputMode:'Active LEDs', outputModeHint:'Choose which LEDs the Macro uses.',
    combined:'Addressable + external', addressable:'Addressable LED only', external:'External LEDs only',
    ledEffect:'LED effect', ledEffectHint:'Apply an effect to the selected tab or all tabs.',
    applyTo:'Apply to', allTabs:'All tabs', colorHint:'Addressable LED color for the selected tab.',
    buzzerOn:'Key feedback sound', buzzerOnHint:'Enable audible feedback.',
    volume:'Buzzer volume', volumeHint:'Adjust loudness without changing frequency.',
    tonePack:'Tone pack', tonePackHint:'Five included presets and five editable packs.',
    customTones:'Custom tones', pressAll:'All press tones', holdAll:'All hold tones',
    press:'Press', hold:'Hold', startup:'USB connect / startup', hz:'Hz', silent:'Silent',
    firmware:'Firmware version', protocol:'Protocol', deviceId:'USB identifier',
    browserPort:'Serial port', browserPortValue:'Managed by browser', aboutHint:'Browsers do not expose the COM name. Firmware identification verifies the device.',
    limitation:'Browser limitation', limitationText:'This page cannot launch PC applications. Existing application mappings are preserved, but run only when desktop Connect is active.',
    noEncoder:'Eight-key model', ready:'Ready'
  }
};

const $ = id => document.getElementById(id);
const el = (tag, className = '', content = null) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (content !== null) node.textContent = content;
  return node;
};
const state = {language: localStorage.getItem('octoglow-language') === 'en' ? 'en' : 'ro',
  page: 'buttons', selectedTab: 1, config: null, busy: false};
const t = key => words[state.language][key] ?? key;
const serial = new MacroSerial();
const mediaRomanian = new Map([
  [0x00cd,'Redare / Pauză'],[0x00b5,'Piesa următoare'],[0x00b6,'Piesa anterioară'],[0x00b7,'Oprește'],
  [0x00e2,'Fără sunet'],[0x00e9,'Volum +'],[0x00ea,'Volum -'],[0x0192,'Calculator'],
  [0x018a,'E-mail'],[0x0194,'Browser'],[0x0221,'Căutare web'],[0x0223,'Pagina de pornire'],
  [0x0224,'Înapoi în browser'],[0x0225,'Înainte în browser'],[0x0227,'Reîmprospătează browserul']
]);
const localMedia = ([name,value]) => [state.language === 'ro' ? (mediaRomanian.get(value) ?? name) : name,value];
const localActionSummary = (action,tab) => action?.type === 2
  ? localMedia(MEDIA.find(([,value]) => value === action.value) ?? [`#${action.value}`,action.value])[0]
  : actionSummary(action,tab,t);

function translateStatic() {
  document.documentElement.lang = state.language;
  $('language').value = state.language;
  document.querySelectorAll('[data-i18n]').forEach(node => { node.textContent = t(node.dataset.i18n); });
  $('section-title').textContent = t(state.page);
  if (state.config) {
    $('hero-title').textContent = t('connectedTitle');
    $('hero-description').textContent = t('connectedText');
  }
  updateStatus();
}

function announce(message, error = false) {
  const notice = $('notice');
  notice.classList.remove('hidden');
  notice.classList.toggle('error', error);
  $('notice-icon').textContent = error ? '⚠' : 'ⓘ';
  $('notice-text').textContent = message;
}

function updateStatus() {
  const connected = Boolean(state.config && serial.connected);
  const pill = $('connection-pill');
  pill.classList.toggle('connected', connected);
  pill.lastElementChild.textContent = state.busy && !connected ? t('connecting') : t(connected ? 'connected' : 'disconnected');
  $('connect-button').classList.toggle('hidden', connected);
  $('disconnect-button').classList.toggle('hidden', !connected);
  $('connect-button').disabled = state.busy || !('serial' in navigator);
  $('disconnect-button').disabled = state.busy;
  $('refresh-button').disabled = state.busy || !connected;
  $('firmware-chip').textContent = `${t('firmware')} ${connected ? '0.2' : '—'}`;
  $('tab-chip').textContent = connected ? `${t('tabs')} ${state.config.tabCount}` : t('noTabs');
}

function setBusy(busy) { state.busy = busy; updateStatus(); }

function updatePreview() {
  const config = state.config;
  const keys = $('preview-keys');
  keys.replaceChildren();
  const slots = ['b1','b2','b3','knob','b4','b5','b6','b7'];
  slots.forEach(slot => {
    const key = el('span', `preview-key ${slot === 'knob' && config?.hasEncoder !== false ? 'knob' : ''}`,
      slot === 'knob' ? (config?.hasEncoder === false ? 'B8' : 'K1') : slot.toUpperCase());
    keys.append(key);
  });
  const led = $('led-preview');
  const color = config?.colors[state.selectedTab - 1] ?? [86,97,109];
  led.style.backgroundColor = config?.lighting && config.ledMode !== 2 ? `rgb(${color.join(',')})` : '#56616d';
  led.style.boxShadow = config?.lighting && config.ledMode !== 2 ? `0 0 11px rgb(${color.join(',')})` : 'none';
}

function render() {
  updateStatus(); updatePreview();
  $('hero-title').textContent = t(state.config ? 'connectedTitle' : 'readyTitle');
  $('hero-description').textContent = t(state.config ? 'connectedText' : 'readyText');
  const host = $('page-content');
  host.replaceChildren();
  $('section-title').textContent = t(state.page);
  document.querySelectorAll('.nav-tab').forEach(button => button.classList.toggle('active', button.dataset.page === state.page));
  if (!state.config) {
    const empty = el('div', 'empty-state');
    empty.append(el('div', '', '▦'), el('h3', '', t('noConnectionTitle')), el('p', '', t('noConnectionText')));
    host.append(empty);
    return;
  }
  if (state.page === 'buttons') renderButtons(host);
  else if (state.page === 'lighting') renderLighting(host);
  else if (state.page === 'buzzer') renderBuzzer(host);
  else renderAbout(host);
}

function panel(title, hint = null) {
  const outer = el('section', 'panel');
  const head = el('div', 'panel-head');
  const copy = el('div');
  copy.append(el('h3', '', title));
  if (hint) copy.append(el('p', '', hint));
  head.append(copy); outer.append(head);
  return {outer, head};
}

function row(icon, title, hint, control) {
  const outer = el('div', 'setting-row');
  outer.append(el('span', 'setting-icon', icon));
  const copy = el('div', 'setting-text');
  copy.append(el('strong', '', title));
  if (hint) copy.append(el('small', '', hint));
  outer.append(copy);
  const area = el('div', 'setting-control');
  area.append(control);
  outer.append(area);
  return outer;
}

function choice(options, selected, handler) {
  const select = el('select');
  options.forEach(([name, value]) => {
    const option = el('option', '', name);
    option.value = String(value);
    select.append(option);
  });
  select.value = String(selected);
  if (handler) select.addEventListener('change', () => handler(Number(select.value), select));
  return select;
}

async function update(command, success = true) {
  if (state.busy || !state.config) return false;
  setBusy(true);
  try {
    await serial.command(command);
    state.config = parseDump(await serial.command('DUMP'));
    if (state.selectedTab > state.config.tabCount) state.selectedTab = state.config.tabCount;
    if (success) announce(t('saved'));
    return true;
  } catch (error) { announce(error.message, true); return false; }
  finally { setBusy(false); render(); }
}

function renderButtons(host) {
  const config = state.config;
  const strip = el('div', 'tab-strip');
  for (let tab = 1; tab <= config.tabCount; tab++) {
    const button = el('button', `tab-choice ${tab === state.selectedTab ? 'active' : ''}`, `${t('tab')} ${tab}`);
    button.type = 'button';
    button.addEventListener('click', () => { state.selectedTab = tab; render(); });
    strip.append(button);
  }
  if (config.tabCount < 5) {
    const add = el('button', 'tab-add', '+');
    add.type = 'button'; add.title = t('addTab'); add.setAttribute('aria-label', t('addTab'));
    add.addEventListener('click', showAddTab);
    strip.append(add);
  }
  host.append(strip);
  const body = el('section', 'tab-body');
  const toolbar = el('div', 'tab-toolbar');
  const copy = el('div');
  const actions = SLOTS.slice(0, 8).filter(slot => config.maps[state.selectedTab - 1][slot]?.type !== 0).length;
  copy.append(el('h3', '', `${t('tab')} ${state.selectedTab}`), el('p', '', `${actions} ${t('keysConfigured')}${config.activeTab === state.selectedTab ? ` · ${t('physicalTab')}` : ''}`));
  const buttons = el('div', 'toolbar-actions');
  const color = el('input', 'color-input');
  color.type = 'color'; color.setAttribute('aria-label', t('tabColor'));
  color.value = '#' + config.colors[state.selectedTab - 1].map(v => v.toString(16).padStart(2, '0')).join('');
  color.addEventListener('change', () => {
    const hex = color.value;
    update(`TABRGB ${state.selectedTab} ${parseInt(hex.slice(1,3),16)} ${parseInt(hex.slice(3,5),16)} ${parseInt(hex.slice(5,7),16)}`);
  });
  const colorWrap = el('label', 'button subtle color-button');
  colorWrap.append(color, el('span', '', t('tabColor'))); buttons.append(colorWrap);
  if (config.activeTab !== state.selectedTab) {
    const activate = el('button', 'button subtle', t('activate'));
    activate.type = 'button'; activate.addEventListener('click', () => update(`ACTIVE ${state.selectedTab}`));
    buttons.append(activate);
  }
  if (config.tabCount > 1 && state.selectedTab === config.tabCount) {
    const remove = el('button', 'button subtle', '×');
    remove.type = 'button'; remove.title = t('removeLast'); remove.setAttribute('aria-label', t('removeLast'));
    remove.addEventListener('click', () => { if (confirm(t('removeConfirm'))) update(`SETTABCOUNT ${config.tabCount - 1}`); });
    buttons.append(remove);
  }
  toolbar.append(copy, buttons); body.append(toolbar);
  body.append(el('div', 'subheading', t('pressKeys')));
  const grid = el('div', 'key-grid');
  const mainSlots = SLOTS.slice(0, 8);
  mainSlots.forEach(slot => grid.append(keyCard(slot, slot === 'knob' ? (config.hasEncoder ? 'K1' : 'B8') : slot.toUpperCase())));
  body.append(grid);
  if (config.hasEncoder) {
    body.append(el('div', 'subheading', t('rotation')));
    const encoder = el('div', 'encoder-grid');
    encoder.append(keyCard('ccw', t('turnLeft')), keyCard('cw', t('turnRight')));
    body.append(encoder);
  }
  host.append(body);
  host.append(el('p', 'small-note', t('limitationText')));
}

function keyCard(slot, label) {
  const action = state.config.maps[state.selectedTab - 1][slot];
  const card = el('button', 'key-card'); card.type = 'button';
  const icon = action.type === 2 ? '♫' : action.type === 3 ? '↕' : [4,5,6].includes(action.type) ? '▣' : action.type === 7 ? '▣' : action.type === 1 ? '⌨' : '＋';
  card.append(el('span', 'card-icon', icon));
  const content = el('span', 'card-content');
  content.append(el('strong', '', label), el('span', '', localActionSummary(action, state.selectedTab)));
  if (action.type === 7) content.append(el('small', '', t('unavailableInBrowser')));
  card.append(content);
  card.setAttribute('aria-label', `${t('configureKey')} ${label}: ${localActionSummary(action, state.selectedTab)}`);
  card.addEventListener('click', () => showActionEditor(slot, label));
  return card;
}

function openEditor(title, build, save) {
  $('editor-title').textContent = title;
  const body = $('editor-fields'); body.replaceChildren();
  build(body);
  const dialog = $('editor');
  $('editor-form').onsubmit = async event => {
    event.preventDefault();
    $('editor-save').disabled = true;
    try { await save(body); dialog.close(); }
    catch (error) { announce(error.message, true); }
    finally { $('editor-save').disabled = false; }
  };
  dialog.showModal();
}

function field(title, control) {
  const wrapper = el('div', 'field');
  wrapper.append(el('label', '', title), control);
  return wrapper;
}

function showAddTab() {
  const config = state.config;
  openEditor(t('chooseSwitch'), body => {
    body.append(el('p', 'hint', t('chooseSwitchText')));
    const slots = SLOTS.slice(0, 8).map(slot => [slot === 'knob' ? (config.hasEncoder ? 'K1' : 'B8') : slot.toUpperCase(), slot]);
    const select = el('select'); select.id = 'new-tab-key';
    slots.forEach(([label, slot]) => { const option = el('option', '', label); option.value = slot; select.append(option); });
    select.value = 'b7'; body.append(field(t('pressKeys'), select));
  }, async body => {
    const slot = body.querySelector('#new-tab-key').value;
    if (!await update(`ADDTAB ${slot}`)) throw new Error(t('error'));
    if (state.config?.tabCount > config.tabCount) state.selectedTab = state.config.tabCount;
    render();
  });
  $('editor-save').textContent = t('create');
}

function showActionEditor(slot, label) {
  const action = state.config.maps[state.selectedTab - 1][slot];
  const kind = action.type === 1 ? (action.modifiers || action.key2 ? 'shortcut' : 'key') :
    ({0:'none',2:'media',3:'scroll',4:'next',5:'previous',6:'goto',7:'application'}[action.type] ?? 'none');
  openEditor(`${t('configureKey')} ${label} · ${t('tab')} ${state.selectedTab}`, body => {
    const types = [['none',t('noAction')],['key',t('key')],['shortcut',t('shortcut')],['media',t('media')],
      ['scroll',t('scroll')],['next',t('nextTab')],['previous',t('previousTab')],['goto',t('goToTab')]];
    if (kind === 'application') types.push(['application',t('application')]);
    const type = el('select'); type.id = 'action-type';
    types.forEach(([value, name]) => { const option = el('option', '', name); option.value = value; type.append(option); });
    type.value = kind;
    const details = el('div', 'field');
    const rebuild = () => {
      details.replaceChildren();
      const selected = type.value;
      if (selected === 'application') { details.append(el('p', 'warning-note', t('unavailableInBrowser'))); return; }
      if (selected === 'key' || selected === 'shortcut') {
        const first = choice(KEYS, action.key1 || 4); first.id = 'action-key1';
        details.append(field(t('primaryKey'), first));
        if (selected === 'shortcut') {
          const checks = el('div', 'checks');
          [['Ctrl',1],['Shift',2],['Alt',4],['Win',8]].forEach(([name, bit]) => {
            const label = el('label'); const check = el('input'); check.type = 'checkbox'; check.dataset.bit = String(bit);
            check.checked = Boolean(action.modifiers & bit); label.append(check, el('span', '', name)); checks.append(label);
          });
          details.append(field(t('modifiers'), checks));
          const second = choice([[t('noAction'),0], ...KEYS], action.key2 || 0); second.id = 'action-key2';
          details.append(field(t('secondKey'), second));
        }
      } else if (selected === 'media') {
        const media = choice(MEDIA.map(localMedia), action.value || MEDIA[0][1]); media.id = 'action-media';
        details.append(field(t('media'), media));
      } else if (selected === 'scroll') {
        const direction = choice([[t('up'),1],[t('down'),2]], action.key1 || 1); direction.id = 'action-direction';
        details.append(field(t('direction'), direction));
      } else if (selected === 'goto') {
        const target = choice(Array.from({length: state.config.tabCount}, (_, i) => [`${t('tab')} ${i + 1}`,i + 1]), action.key1 || 1);
        target.id = 'action-target'; details.append(field(t('targetTab'), target));
      }
    };
    type.addEventListener('change', rebuild);
    body.append(field(t('action'), type), details); rebuild();
  }, async body => {
    const kindNow = body.querySelector('#action-type').value;
    if (kindNow === 'application') throw new Error(t('unavailableInBrowser'));
    const next = {type:0,modifiers:0,key1:0,key2:0,value:0};
    if (kindNow === 'key' || kindNow === 'shortcut') {
      next.type = 1; next.key1 = Number(body.querySelector('#action-key1').value);
      if (kindNow === 'shortcut') {
        next.modifiers = [...body.querySelectorAll('input[data-bit]:checked')].reduce((sum, checkbox) => sum | Number(checkbox.dataset.bit), 0);
        next.key2 = Number(body.querySelector('#action-key2').value);
      }
    } else if (kindNow === 'media') { next.type = 2; next.value = Number(body.querySelector('#action-media').value); }
    else if (kindNow === 'scroll') { next.type = 3; next.key1 = Number(body.querySelector('#action-direction').value); }
    else if (kindNow === 'next') next.type = 4;
    else if (kindNow === 'previous') next.type = 5;
    else if (kindNow === 'goto') { next.type = 6; next.key1 = Number(body.querySelector('#action-target').value); }
    const tab = state.selectedTab;
    if (state.config.tabCount > 1 && SLOTS.slice(0,8).includes(slot) &&
      ![4,5].includes(next.type) && !(next.type === 6 && next.key1 !== tab) &&
      !hasTabSwitch(state.config, tab, slot)) throw new Error(t('requiresSwitch'));
    setBusy(true);
    try {
      await serial.command(serializeAction(tab, slot, next));
      await serial.command('SAVE');
      state.config = parseDump(await serial.command('DUMP'));
      announce(t('saved')); render();
    } finally { setBusy(false); render(); }
  });
  $('editor-save').textContent = t('save');
  $('editor-save').disabled = kind === 'application';
  $('editor-fields').querySelector('#action-type').addEventListener('change', event => {
    $('editor-save').disabled = event.target.value === 'application';
  });
}

function renderLighting(host) {
  const config = state.config;
  const {outer} = panel(t('lighting'), t('lightingIntro'));
  const power = el('input', 'toggle'); power.type = 'checkbox'; power.checked = config.lighting;
  power.setAttribute('aria-label', t('lightingOn'));
  power.addEventListener('change', () => update(`LIGHT ${power.checked ? 'ON' : 'OFF'}`));
  outer.append(row('☼', t('lightingOn'), t('lightingOnHint'), power));
  outer.append(row('◈', t('outputMode'), t('outputModeHint'), choice([
    [t('combined'),0],[t('addressable'),1],[t('external'),2]], config.ledMode, value => update(`LEDMODE ${value}`))));
  const target = choice([[t('allTabs'),0],...Array.from({length:config.tabCount},(_,i)=>[`${t('tab')} ${i+1}`,i+1])],state.selectedTab);
  const effect = choice(EFFECTS.map((_,i)=>[effectName(i),i]),config.effects[state.selectedTab-1]);
  target.addEventListener('change', () => { effect.value = String(config.effects[(Number(target.value)||state.selectedTab)-1]); });
  effect.addEventListener('change', () => update(Number(target.value) === 0 ? `TABFXALL ${effect.value}` : `TABFX ${target.value} ${effect.value}`));
  const controls = el('div', 'setting-control'); controls.append(target, effect);
  const effectRow = row('✧', t('ledEffect'), t('ledEffectHint'), controls);
  // row() wraps the controls; flatten to keep responsive spacing.
  effectRow.lastElementChild.replaceChildren(target, effect);
  outer.append(effectRow);
  const color = el('input', 'color-input'); color.type = 'color';
  color.value = '#' + config.colors[state.selectedTab-1].map(v=>v.toString(16).padStart(2,'0')).join('');
  color.addEventListener('change', () => update(`TABRGB ${state.selectedTab} ${parseInt(color.value.slice(1,3),16)} ${parseInt(color.value.slice(3,5),16)} ${parseInt(color.value.slice(5,7),16)}`));
  outer.append(row('◉',`${t('tabColor')} · ${t('tab')} ${state.selectedTab}`,t('colorHint'),color));
  host.append(outer);
}

function effectName(index) {
  const names = state.language === 'ro' ? ['Static','Respirație','Clipire','Puls dublu','Bătăi de inimă','Val','Sclipiri','Stroboscop'] : EFFECTS;
  return names[index];
}

function renderBuzzer(host) {
  const config = state.config;
  const {outer} = panel('Buzzer');
  const power = el('input','toggle'); power.type='checkbox'; power.checked=config.buzzer;
  power.setAttribute('aria-label',t('buzzerOn'));
  power.addEventListener('change',()=>update(`BUZZER ${power.checked?'ON':'OFF'}`));
  outer.append(row('◖',t('buzzerOn'),t('buzzerOnHint'),power));
  const slider = el('input'); slider.type='range'; slider.min='0'; slider.max='100'; slider.value=String(config.volume);
  slider.setAttribute('aria-label',t('volume'));
  const output=el('output','',`${config.volume}%`);
  slider.addEventListener('input',()=>{output.textContent=`${slider.value}%`;});
  slider.addEventListener('change',()=>update(`BUZZERVOL ${slider.value}`));
  const controls=el('span');controls.append(slider,output);
  const volumeRow=row('♫',t('volume'),t('volumeHint'),controls);
  volumeRow.lastElementChild.replaceChildren(slider,output);outer.append(volumeRow);
  const names=state.language==='ro' ? ['Clasic','Delicat','Luminos','Arcade','Grav','Personalizat 1','Personalizat 2','Personalizat 3','Personalizat 4','Personalizat 5'] : TONE_PACKS;
  outer.append(row('♪',t('tonePack'),t('tonePackHint'),choice(names.map((name,i)=>[name,i]),config.tonePack,value=>update(`TONEPACK ${value}`))));
  host.append(outer);
  if(config.tonePack>=5)renderCustomTones(host,config.tonePack-4);
}

function renderCustomTones(host,pack) {
  const config=state.config;
  const {outer}=panel(`${t('customTones')} ${pack}`);
  const freqOptions=current=>[...new Set([...TONE_FREQUENCIES,current])].sort((a,b)=>a-b).map(value=>[value===0?t('silent'):`${value} ${t('hz')}`,value]);
  for(const [title,range] of [[t('pressAll'),'PRESS'],[t('holdAll'),'HOLD']]){
    outer.append(row('♫',title,null,choice(freqOptions(0),0,value=>update(`TONEALL ${pack} ${range} ${value}`))));
  }
  const grid=el('div','tone-grid');
  for(let event=0;event<18;event++){
    if(!config.hasEncoder&&(event===15||event===16))continue;
    const title=event<7?`B${event+1} · ${t('press')}`:event<14?`B${event-6} · ${t('hold')}`:
      event===14?(config.hasEncoder?'K1':'B8'):event===15?t('turnRight'):event===16?t('turnLeft'):t('startup');
    const value=config.tones[pack-1][event];
    grid.append(row('♪',title,null,choice(freqOptions(value),value,next=>update(`TONESET ${pack} ${event} ${next}`))));
  }
  outer.append(el('div','subheading',t('customTones')),grid);host.append(outer);
}

function renderAbout(host) {
  const config=state.config;
  const {outer}=panel(t('about'),t('aboutHint'));
  const grid=el('div','info-grid');
  const info=serial.port?.getInfo?.()??{};
  const usb=(info.usbVendorId!==undefined&&info.usbProductId!==undefined)
    ? `${info.usbVendorId.toString(16).padStart(4,'0').toUpperCase()}:${info.usbProductId.toString(16).padStart(4,'0').toUpperCase()}`:'—';
  [[t('firmware'),'0.2'],[t('protocol'),String(state.config?.protocol ?? 7)],[t('deviceId'),usb],[t('browserPort'),t('browserPortValue')],
    [t('pressKeys'),config.hasEncoder?'B1–B7 + K1':'B1–B8']].forEach(([label,value])=>{
      const item=el('div','info-row');item.append(el('small','',label),el('strong','',value));grid.append(item);
    });
  outer.append(grid);host.append(outer);
  const limit=panel(t('limitation'));limit.outer.append(el('p','hint',t('limitationText')));host.append(limit.outer);
}

async function connectPort(port) {
  setBusy(true);
  try {
    const config=await serial.connect(port);
    state.config=config;
    state.selectedTab=config.activeTab;
    const info=port.getInfo?.()??{};
    if(info.usbVendorId!==undefined&&info.usbProductId!==undefined)
      localStorage.setItem('octoglow-macro-usb',`${info.usbVendorId}:${info.usbProductId}`);
    $('notice').classList.add('hidden');
  }catch(error){
    state.config=null;
    const message=error.name==='NetworkError'||error.name==='InvalidStateError'?t('busyPort'):
      error.message.includes('not an Octoglow Macro')?t('notMacro'):error.message;
    announce(message,true);
  }finally{setBusy(false);render();}
}

$('connect-button').addEventListener('click',async()=>{
  try { const port=await navigator.serial.requestPort(); await connectPort(port); }
  catch(error){ if(error.name!=='NotFoundError')announce(error.message,true); }
});
$('disconnect-button').addEventListener('click',()=>serial.disconnect());
$('refresh-button').addEventListener('click',async()=>{
  if(state.busy||!state.config)return;
  setBusy(true);
  try{state.config=parseDump(await serial.command('DUMP'));announce(t('refreshed'));}
  catch(error){announce(error.message,true);}
  finally{setBusy(false);render();}
});
$('notice-close').addEventListener('click',()=>$('notice').classList.add('hidden'));
$('language').addEventListener('change',event=>{
  state.language=event.target.value;localStorage.setItem('octoglow-language',state.language);translateStatic();render();
});
document.querySelectorAll('.nav-tab').forEach(button=>button.addEventListener('click',()=>{state.page=button.dataset.page;render();}));
$('editor-cancel').addEventListener('click',()=>$('editor').close());
$('editor-x').addEventListener('click',()=>$('editor').close());
serial.addEventListener('error',event=>announce(event.detail.message,true));
serial.addEventListener('disconnected',()=>{
  state.config=null;
  $('hero-title').textContent=t('readyTitle');$('hero-description').textContent=t('readyText');render();
});
serial.addEventListener('device-event',event=>{
  if(event.detail.startsWith('TAB_ACTIVE ')&&state.config){
    const tab=Number(event.detail.slice(11));
    if(tab>=1&&tab<=state.config.tabCount){state.config.activeTab=tab;render();}
  }
});
if('serial'in navigator){
  navigator.serial.addEventListener('disconnect',event=>{if(event.target===serial.port)void serial.disconnect();});
  // getPorts only returns devices this origin was granted previously. Never probe unrelated ports.
  const known=localStorage.getItem('octoglow-macro-usb');
  if(known)navigator.serial.getPorts().then(ports=>{
    const matches=ports.filter(port=>{const info=port.getInfo?.()??{};return `${info.usbVendorId}:${info.usbProductId}`===known;});
    if(matches.length===1&&!serial.port)void connectPort(matches[0]);
  }).catch(()=>{});
}else announce(t('unsupportedBrowser'),true);
translateStatic();render();
