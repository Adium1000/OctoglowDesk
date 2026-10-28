// OCTOGLOW - BACKEND FALS PENTRU DEMO
//
// portal.html vorbeste cu ceasul prin ~60 de endpoint-uri HTTP. Fisierul asta
// le inlocuieste pe toate cu raspunsuri sintetice, ca interfata sa poata fi
// deschisa si testata pe orice dispozitiv, fara placa si fara retea.
//
// Se injecteaza in <head>, inaintea scripturilor paginii, ca window.fetch sa
// fie deja inlocuit cand porneste loadHomeState().
//
// Ce e cu adevarat viu: cronometrul, temporizatorul, scanarea WiFi, monitorul
// hardware si alarmele. Restul salvarilor raspund OK si raman in DOM pana la o
// reincarcare - suficient ca sa poti umbla prin toata interfata.
(function () {
  "use strict";

  var BOOT = Date.now();

  // ---------------------------------------------------------------- /state
  var S = {
    buzzer: true, buzzerVolume: 80, buzzerPreset: "clasic",
    buzzerVolNotif: 80, buzzerVolAuto: 80, buzzerVolAlarm: 80, buzzerVolTouch: 80,
    ssid: "Octoglow-Demo", ip: "192.168.1.57", ap: false, apSsid: "Octoglow-A1B2",
    version: "1.9.0", hostName: "octoglow-a1b2", uptime: 93784, lastTemp: 23.4, bmpOk: true,
    items: [
      { id: 0,  enabled: true,  dur: 5 },
      { id: 1,  enabled: true,  dur: 4 },
      { id: 2,  enabled: true,  dur: 4 },
      { id: 3,  enabled: true,  dur: 8 },
      { id: 4,  enabled: true,  dur: 8 },
      { id: 5,  enabled: false, dur: 8 },
      { id: 6,  enabled: false, dur: 8 },
      { id: 8,  enabled: false, dur: 8 },
      { id: 9,  enabled: false, dur: 15 },
      { id: 11, enabled: true,  dur: 8 },
      { id: 12, enabled: false, dur: 8 },
      { id: 10, enabled: false, dur: 5 },
      { id: 13, enabled: false, dur: 8 },
      { id: 14, enabled: true, dur: 8 },
      { id: 15, enabled: true, dur: 8 }
    ],
    bright: 4, dimAuto: true, dimFrom: "22:00", dimTo: "07:00", dimLevel: 1,
    tempunit: 0, hourformat: 0, hourLeadingZero: true, netTimeSync: true,
    hwSkin: 0, hwContent: 0, hwFormat: 0, hwLeadZero: false, hwBarMode: 1, hwBarPos: 0, hwSwap: false,
    defaultStartMode: 0, dateformat: 0, datelang: 0, customdatefmt: "d MMM",
    wxCity: "Cluj-Napoca", wxPreset: 0, wxLang: "ro", wxHasKey: true,
    iconSelYoutube: 0,
    currencyBase: "EUR", currencyQuote: "RON", currencyCompare: true,
    liveHl: true, uiDark: true, uiLang: "ro", showGrayed: true,
    autoSleep: false, autoSleepSec: 1800, sleeping: false,
    tileHidden: 8192, prioHidden: 1408,
    webEnabled: true, webAlAccess: true, webAlLocked: true, webAlLogin: true, webAlNet: true, notifEnabled: true, ets2Enabled: false,
    voicemeeterEnabled: false, vmMode: 0, vmSensitivity: 60,
    browserDownloadsEnabled: false,
    qbitEnabled: true,
    bambuEnabled: true,
    gameFpsEnabled: true,
    volumeEnabled: true,
    volumeAdaptiveIcon: true,
    hideIconVolume: false,
    iconSelVolume: 0,
    fontTypeVolume: 0,
    scrollTypeVolume: 0,
    scrollSpeedVolume: 40,
    tileTransVolume: 0,
    tileTransSpdVolume: 100,
    nfsu2Enabled: true, nfsu2Mph: false, nfsu2Gauge: 0, nfsu2GaugeFull: 300,
    nfsmwEnabled: true, nfsmwMph: true, nfsmwGauge: 0, nfsmwGaugeFull: 300,
    nfscEnabled: true, nfscMph: false, nfscGauge: 0, nfscGaugeFull: 300,
    obsEnabled: true, obsShowElapsed: true,
    bdBarStyle: 0, qbitBarStyle: 1, bambuBarStyle: 1,
    beamngEnabled: true, ets2Mph: false, beamngMph: false,
    ets2Gauge: 0, beamngGauge: 0, ets2GaugeFull: 130, beamngGaugeFull: 250,
    assettoEnabled: true, assettoMph: false, assettoGauge: 0, assettoGaugeFull: 300,
    lowBatteryEnabled: true,
    abMode: 1,
    ets2OrderFirst: false, nowPlayingIsPriority: true,
    priorityOrder: "alarm,lowbattery,volume,notif,browserdownloads,qbittorrent,bambu,obs,voicemeeter,ets2,beamng,assetto,nfsu2,nfsmw,nfsc,gamefps,nowplaying,stopwatch,timer,webaccess",
    hideIcons: false, npAdaptiveIcon: true,
    scrollType: 0, scrollSpeed: 40, fontType: 0,
    tileTransGlobal: 0, tileTransC2P: 0, tileTransP2C: 0, tileTransSpd: 100,
    timerDurationSec: 300, timerPreset: 0,
    timerMode: 0, pomoWork: 25, pomoShort: 5, pomoLong: 15, pomoRounds: 4,
    evSndPomoBreak: "chime", evSndPomoWork: "bell",
    alarms: [
      { h: 7, m: 30, d: 31,  en: true,  p: true,  tone: "classic" },
      { h: 9, m: 15, d: 96,  en: false, p: true,  tone: "chimes"  },
      { h: 9, m: 0,  d: 127, en: false, p: false, tone: "morning" }
    ],
    pressureHpa: 1014, pressureTrend: 1,
    currencyValid: true, currencyRate: 5.0764, currencyTrend: 1,
    mementoText: "Bea apa",
    // Evenimente: lista (i = iconita evenimentului, 0 = calendarul), iar cdText
    // e ce arata tile-ul acum.
    cdEvents: [{ n: "Craciun", d: "2025-12-25", y: true, i: 0 }, { n: "Concediu", d: "2027-07-15", y: false, i: 9 }],
    // De ce un tile online nu are ce arata (API_ERR_* din firmware); 0 = e in regula.
    wxErr: 0, soc0Err: 0, currencyErr: 0,
    cdText: "", cdFull: false, cdLang: 0,
    canvasBmp: "",
    evSndTile: "none", evSndWifi: "blip", evSndWeb: "blip",
    evSndNotif: "ding", evSndEts2: "none", evSndTouch: "tick", evSndTimer: "alarm",
    touchTapAction: 1, touchDoubleTapAction: 2,
    accentColor: "#d0bcff", uiShape: 0, uiColors: "", uiPalette: 255, ssAnim: 0
  };

  // Cheile pe grupuri: acelasi lucru pe care firmware-ul il scrie in bucle.
  ["Hour","Date","Temp","Np","Wx","Rem","Canvas","Press","Ss","Curr","Yt","Hw",
   "Web","Notif","Ets2","Sw","Tmr","Alarm","Ip","C2P","P2C","Vm","BrowserDownloads","Obs","Beamng","Assetto","Qbit","Bambu","Afterburner","GameFps","Nfsu2","Nfsmw","Nfsc","Countdown"].forEach(function (k) {
    S["tileTransSpd" + k] = 100;
  });
  ["Date","Temp","Reminder","Weather","Notif","NowPlaying","Pressure","Stopwatch",
   "Currency","Youtube","WebAccess","Timer","Ip","Obs","Afterburner","GameFps","Countdown"].forEach(function (k) {
    S["scrollType" + k] = 0;
    S["fontType" + k] = 0;
    S["scrollSpeed" + k] = 40;
  });
  ["Date","Temp","Reminder","Weather","Notif","NowPlaying","Pressure","Currency",
   "Youtube","WebAccess","Ip","Voicemeeter","BrowserDownloads","Obs","Qbit","Bambu","Afterburner","GameFps","Countdown"].forEach(function (k) { S["hideIcon" + k] = false; });
  ["Date","Temp","Reminder","Notif","NpMusic","NpVideo","Pressure","Currency","Ip"]
    .forEach(function (k) { S["iconSel" + k] = 0; });
  S.iconSelVoicemeeter = 25;
  S.iconSelBrowserDownloads = 26;
  S.iconSelQbit = 0;
  S.iconSelBambu = 0;
  S.iconSelObs = 27;
  S.iconSelAfterburner = 0;
  S.iconSelCountdown = 0;
  S.iconSelGameFps = 0;
  // Fonturile tile-urilor fara scroll: vitezele, alarma, cele doua ecrane de baterie.
  ["Ets2", "Beamng", "Assetto", "Nfsu2", "Nfsmw", "Nfsc", "Alarm", "Battery", "LowBattery"].forEach(function (k) { S["fontType" + k] = 0; });
  S.iconSelBattery = 0;
  S.iconSelLowBattery = 0;
  ["Sunny","Cloud","Rain","Storm","Snow","Wind","Night"]
    .forEach(function (k) { S["iconWx" + k] = 0; });
  ["Hour","Date","Temp","Np","Wx","Rem","Canvas","Press","Ss","Curr","Yt","Web",
   "Notif","Ets2","Sw","Tmr","Alarm","Ip","Vm","BrowserDownloads","Obs","Beamng","Assetto","Qbit","Bambu","Afterburner","GameFps","Nfsu2","Nfsmw","Nfsc","Countdown"].forEach(function (k) { S["tileTrans" + k] = 0; });
  [0, 1, 2].forEach(function (i) {
    S["soc" + i + "Handle"] = i === 0 ? "@octoglow" : "";
    S["soc" + i + "ShowName"] = true;
    S["soc" + i + "HasKey"] = i === 0;
    S["soc" + i + "KeyLen"] = i === 0 ? 39 : 0;
    S["soc" + i + "Count"] = i === 0 ? 1240 : 0;
    S["soc" + i + "Valid"] = i === 0;
  });

  // ------------------------------------------------- cronometru si timer viu
  var sw = { running: false, base: 0, since: 0 };
  var tmr = { running: false, finished: false, left: 300, since: 0 };
  var pomo = { inCycle: false, phase: 0, round: 1 };

  function swMs() { return sw.base + (sw.running ? Date.now() - sw.since : 0); }
  function tmrLeft() {
    if (!tmr.running) return tmr.left;
    var l = tmr.left - Math.floor((Date.now() - tmr.since) / 1000);
    if (l <= 0) { tmr.running = false; tmr.finished = true; tmr.left = 0; return 0; }
    return l;
  }
  function pad(n) { return (n < 10 ? "0" : "") + n; }
  function swText(ms) {
    var t = Math.floor(ms / 1000);
    return pad(Math.floor(t / 60)) + ":" + pad(t % 60) + "." + Math.floor((ms % 1000) / 100);
  }
  function tmrText(s) {
    return s >= 3600
      ? Math.floor(s / 3600) + ":" + pad(Math.floor(s / 60) % 60) + ":" + pad(s % 60)
      : pad(Math.floor(s / 60)) + ":" + pad(s % 60);
  }

  // ------------------------------------------------------------ retele WiFi
  var NETS = [
    { ssid: "Octoglow-Demo", rssi: -46, secured: true,  auth: "WPA2" },
    { ssid: "Vecinul_5G",    rssi: -62, secured: true,  auth: "WPA2/WPA3" },
    { ssid: "TP-Link_9C40",  rssi: -71, secured: true,  auth: "WPA2" },
    { ssid: "Cafenea Free",  rssi: -78, secured: false, auth: "Open" },
    { ssid: "DIGI-8fA2",     rssi: -84, secured: true,  auth: "WPA/WPA2" }
  ];
  var scanCalls = 0;

  // --------------------------------------------------------------- alarme
  function alarmNext() {
    var now = new Date(), best = null, bestIdx = -1;
    for (var d = 0; d < 8; d++) {
      for (var i = 0; i < S.alarms.length; i++) {
        var a = S.alarms[i];
        if (!a.p || !a.en) continue;
        var when = new Date(now.getFullYear(), now.getMonth(), now.getDate() + d, a.h, a.m, 0, 0);
        if (when <= now) continue;
        var bit = 1 << ((when.getDay() + 6) % 7);
        if (!(a.d & bit)) continue;
        if (best === null || when < best) { best = when; bestIdx = i; }
      }
      if (best) break;
    }
    if (!best) return { nextMin: -1, nextIdx: -1, nextTime: "" };
    return {
      nextMin: Math.round((best - now) / 60000),
      nextIdx: bestIdx,
      nextTime: pad(best.getHours()) + ":" + pad(best.getMinutes())
    };
  }

  // ---------------------------------------------------- raspunsuri sintetice
  function resp(body, status) {
    var isStr = typeof body === "string";
    var txt = isStr ? body : JSON.stringify(body);
    return Promise.resolve({
      ok: (status || 200) < 400,
      status: status || 200,
      statusText: "OK",
      headers: { get: function () { return isStr ? "text/plain" : "application/json"; } },
      json: function () { return Promise.resolve(isStr ? { ok: true } : body); },
      text: function () { return Promise.resolve(txt); },
      blob: function () { return Promise.resolve(new Blob([txt])); },
      clone: function () { return this; }
    });
  }

  function args(url, init) {
    var out = {};
    var q = url.indexOf("?");
    function eat(s) {
      String(s || "").split("&").forEach(function (kv) {
        if (!kv) return;
        var i = kv.indexOf("=");
        var k = decodeURIComponent((i < 0 ? kv : kv.slice(0, i)).replace(/\+/g, " "));
        var v = i < 0 ? "" : decodeURIComponent(kv.slice(i + 1).replace(/\+/g, " "));
        out[k] = v;
      });
    }
    if (q >= 0) eat(url.slice(q + 1));
    if (init && typeof init.body === "string") eat(init.body);
    return out;
  }

  // Lista din Despre -> Licente; build_demo.py scrie aici licenses_json().
  var DEMO_LICENSES = /*__DEMO_LICENSES__*/null;

  var ROUTES = {
    "/whoami": function () { return resp({ user: "Admin" }); },

    "/licenses": function () { return resp(DEMO_LICENSES); },

    "/state": function () {
      S.uptime = 93784 + Math.floor((Date.now() - BOOT) / 1000);
      return resp(S);
    },

    "/hwstate": function () {
      var up = 93784 + Math.floor((Date.now() - BOOT) / 1000);
      var jitter = function (n, p) { return Math.round(n * (1 + (Math.random() - 0.5) * p)); };
      return resp({
        cpuFreqMhz: 240, cores: 2, chip: "ESP32-S3",
        loopsPerSec: jitter(1180, 0.08),
        loopMaxUs: jitter(4200, 0.3), loopMaxUsEver: 21400,
        httpMaxUs: jitter(38000, 0.2), httpMaxUri: "/state",
        pageUs: 141000, stateUs: jitter(26000, 0.15), stateBuildUs: jitter(9000, 0.15),
        pageFull: 3, page304: 27,
        heapFree: jitter(198000, 0.04), heapSize: 327680,
        heapMinFree: 171200, heapMaxAlloc: 110592,
        psramSize: 0, psramFree: 0,
        sketchSize: 1478099, sketchFree: 487981, flashSize: 8388608,
        uptime: up, hasTemp: true, tempC: 43.2 + Math.random() * 2, tempUnit: 0
      });
    },

    // The demo clock shows the time, seconds and all, and a light running under
    // it, so the live screen view has something that moves. It keeps the
    // firmware's frame history too (since=N): a frame every 40 ms, numbered by
    // the time, so the page plays them back as evenly as from a real clock.
    "/getscreen": function (url) {
      var F = ["111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
               "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111"];
      function frame(ms, n) {
        var d = new Date(ms);
        var t = [d.getHours(), d.getMinutes(), d.getSeconds()].map(function (v) { return (v < 10 ? "0" : "") + v; }).join(":");
        var cols = [], x = 2, i, c, r;
        for (i = 0; i < 32; i++) cols.push(0);
        for (i = 0; i < t.length; i++) {
          var ch = t.charAt(i);
          if (ch === ":") { cols[x] = 0x14; x += 2; continue; }
          var g = F[+ch];
          for (c = 0; c < 3; c++) {
            var b = 0;
            for (r = 0; r < 5; r++) if (g.charAt(r * 3 + c) === "1") b |= 1 << r;
            cols[x + c] = b;
          }
          x += 4;
        }
        // the running light: to the right edge and back, three LEDs long
        var p = n % 62;
        if (p > 31) p = 62 - p;
        for (i = -1; i <= 1; i++) if (p + i >= 0 && p + i < 32) cols[p + i] |= 0x80;
        return cols.map(function (v) { return (v < 16 ? "0" : "") + v.toString(16); }).join("");
      }
      var now = Date.now(), n = Math.floor(now / 40);
      var out = { on: true, b: S.bright || 8, px: frame(now, n) };
      var q = /[?&]since=(\d+)/.exec(url || "");
      if (q) {
        var since = +q[1], f = "";
        if (since > n || since < n - 32) since = n - 32;
        for (var k = since + 1; k <= n; k++) {
          var h = ((k * 40) % 4294967296).toString(16);
          f += "00000000".slice(h.length) + h + "01" + frame(k * 40, k);
        }
        out.seq = n;
        out.t = now % 4294967296;
        out.f = f;
      }
      return resp(out);
    },

    "/npstate": function () {
      return resp({
        active: false, text: "", npmode: 0,
        hourformat: S.hourformat, dateformat: S.dateformat, lastTemp: S.lastTemp,
        wxValid: true, wxTemp: 21.6, wxHumidity: 54, wxDesc: "cer senin",
        wxErr: S.wxErr, soc0Err: S.soc0Err, soc0Valid: S.soc0Valid, soc0Count: S.soc0Count, currencyErr: S.currencyErr
      });
    },

    "/apstate": function () {
      var d = new Date();
      return resp({
        ssid: S.apSsid, hasPass: true, passLen: 9,
        netTime: S.netTimeSync,
        devTime: d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()) +
                 "T" + pad(d.getHours()) + ":" + pad(d.getMinutes()) + ":" + pad(d.getSeconds()),
        timeValid: true, mode: S.ap ? "ap" : "sta"
      });
    },

    "/appass": function () { return resp({ pass: "octoglow" }); },

    "/scan": function () {
      // Prima cerere raspunde ca scaneaza, ca sa vezi si starea de asteptare.
      scanCalls++;
      if (scanCalls % 4 === 1) return resp({ scanning: true, networks: [] });
      return resp({ scanning: false, networks: NETS });
    },

    "/hwsett": function (u, i) {
      var a = args(u, i);
      ["skin", "content", "fmt", "barmode", "barpos"].forEach(function (key, index) {
        var field = ["hwSkin", "hwContent", "hwFormat", "hwBarMode", "hwBarPos"][index];
        if (a[key] !== undefined) S[field] = Math.max(0, Math.min(key === "content" || key === "skin" ? 2 : 1, parseInt(a[key], 10) || 0));
      });
      if (S.hwSkin === 2) { S.hwSkin = 0; S.hwContent = 1; }
      if (a.leadzero !== undefined) S.hwLeadZero = a.leadzero === "1";
      if (a.swap !== undefined) S.hwSwap = a.swap === "1";
      return resp("OK");
    },
    "/wifiinfo": function () {
      return resp({
        connected: !S.ap, ssid: S.ssid, bssid: "A4:2B:B0:9C:14:03",
        rssi: -46 + Math.round(Math.random() * 6 - 3), channel: 6,
        ip: S.ip, gw: "192.168.1.1", mask: "255.255.255.0", dns: "192.168.1.1",
        mac: "3C:84:27:E1:9F:5A", phy: "11n", bw: 20, maxMbps: 72
      });
    },

    "/ets2state": function () {
      return resp({ enabled: S.ets2Enabled, active: false, speed: 0, mph: S.ets2Mph, shown: 0 });
    },
    // BeamNG.drive: o masina care accelereaza si franeaza, odata cu demo-ul.
    "/beamngstate": function () {
      var on = S.beamngEnabled && (S.prioHidden & 2048) === 0;
      var kmh = on ? Math.round(75 + 45 * Math.sin((Date.now() - BOOT) / 4000)) : 0;
      var shown = S.beamngMph ? Math.floor((kmh * 1000000 + 804672) / 1609344) : kmh;
      return resp({ enabled: S.beamngEnabled, active: on, speed: kmh, mph: S.beamngMph, shown: shown });
    },
    "/beamngtoggle": function (u, i) {
      var a = args(u, i);
      S.beamngEnabled = a.enabled === "1";
      return resp("OK");
    },
    // Assetto Corsa: o masina de curse pe un tur, mai repede decat cea din BeamNG.
    "/assettostate": function () {
      var wanted = S.assettoEnabled && (S.prioHidden & 4096) === 0;
      var kmh = wanted ? Math.round(170 + 80 * Math.sin((Date.now() - BOOT) / 5000)) : 0;
      var shown = S.assettoMph ? Math.floor((kmh * 1000000 + 804672) / 1609344) : kmh;
      return resp({ enabled: S.assettoEnabled, wanted: wanted, active: wanted, speed: kmh, mph: S.assettoMph, shown: shown });
    },
    "/lowbatterytoggle": function (u, i) {
      var a = args(u, i);
      S.lowBatteryEnabled = a.enabled === "1";
      return resp("OK");
    },
    "/assettotoggle": function (u, i) {
      var a = args(u, i);
      S.assettoEnabled = a.enabled === "1";
      return resp("OK");
    },
    "/speedunit": function (u, i) {
      var a = args(u, i);
      if (a.ets2Mph !== undefined) S.ets2Mph = a.ets2Mph === "1";
      if (a.beamngMph !== undefined) S.beamngMph = a.beamngMph === "1";
      if (a.assettoMph !== undefined) S.assettoMph = a.assettoMph === "1";
      if (a.nfsu2Mph !== undefined) S.nfsu2Mph = a.nfsu2Mph === "1";
      if (a.nfsmwMph !== undefined) S.nfsmwMph = a.nfsmwMph === "1";
      if (a.nfscMph !== undefined) S.nfscMph = a.nfscMph === "1";
      // The look of each speed tile: 0 plain, 1 arc, 2 dial, 3 bar, and the
      // km/h at the end of the gauge's scale, 40-500.
      ["ets2", "beamng", "assetto", "nfsu2", "nfsmw", "nfsc"].forEach(function (k) {
        if (a[k + "Gauge"] !== undefined) S[k + "Gauge"] = Math.max(0, Math.min(3, parseInt(a[k + "Gauge"], 10) || 0));
        if (a[k + "GaugeFull"] !== undefined) S[k + "GaugeFull"] = Math.max(40, Math.min(500, parseInt(a[k + "GaugeFull"], 10) || 0));
      });
      return resp("OK");
    },

    "/voicemeeterstate": function () {
      return resp({ enabled: S.voicemeeterEnabled, active: S.voicemeeterEnabled, mode: S.vmMode, sensitivity: S.vmSensitivity });
    },
    "/voicemeetertoggle": function (u, i) {
      var a = args(u, i);
      S.voicemeeterEnabled = a.enabled === "1";
      return resp("OK");
    },
    "/browserdownloadstate": function () {
      return resp({ enabled: S.browserDownloadsEnabled, active: S.browserDownloadsEnabled, progress: 62, count: 2 });
    },
    "/browserdownloadtoggle": function (u, i) {
      var a = args(u, i);
      S.browserDownloadsEnabled = a.enabled === "1";
      return resp("OK");
    },
    "/browserdownload": function () { return resp("OK"); },
    // qBittorrent: un torent care urca de la 0 la 99% in doua minute, apoi o ia de la capat.
    "/qbitstate": function () {
      var wanted = S.qbitEnabled && (S.prioHidden & 8192) === 0;
      var pct = Math.floor(((Date.now() - BOOT) / 1200) % 100);
      return resp({ enabled: S.qbitEnabled, wanted: wanted, active: wanted, progress: wanted ? pct : 0 });
    },
    "/qbittoggle": function (u, i) {
      var a = args(u, i);
      S.qbitEnabled = a.enabled === "1";
      return resp("OK");
    },
    "/qbitstatus": function () { return resp("OK"); },
    "/bambustate": function () {
      var wanted = S.bambuEnabled && (S.prioHidden & 16384) === 0;
      var pct = Math.floor(((Date.now() - BOOT) / 1500) % 100);
      return resp({ enabled: S.bambuEnabled, wanted: wanted, active: wanted, progress: wanted ? pct : 0, kind: 0 });
    },
    "/bambutoggle": function (u, i) {
      S.bambuEnabled = args(u, i).enabled === "1";
      return resp("OK");
    },
    "/bambustatus": function () { return resp("OK"); },
    // MSI Afterburner: un joc care merge intre 55 si 144 FPS, CPU 58.4 C, GPU 66.1 C.
    "/abstate": function () {
      var it = S.items.filter(function (x) { return x.id === 14; })[0];
      var tileWanted = !!(it && it.enabled) && (S.tileHidden & 16384) === 0;
      var gameFpsWanted = S.gameFpsEnabled && (S.prioHidden & 32768) === 0;
      var wanted = tileWanted || gameFpsWanted;
      var t = (Date.now() - BOOT) / 1000;
      var fps = Math.round(100 + 44 * Math.sin(t / 4));
      var cpu = 584 + Math.round(20 * Math.sin(t / 9)), gpu = 661 + Math.round(15 * Math.sin(t / 7));
      var d = S.abMode === 1 ? cpu : gpu;
      var value = !wanted ? null : S.abMode === 0 ? fps : S.tempunit === 1 ? Math.round((d * 9 + 1600) / 50) : Math.round(d / 10);
      return resp({ wanted: wanted, mode: S.abMode, fahrenheit: S.tempunit === 1, live: tileWanted, value: tileWanted ? value : null,
                    fps: wanted ? fps : null, cpu: wanted ? cpu : null, gpu: wanted ? gpu : null,
                    game: wanted, tileWanted: tileWanted, gameFpsEnabled: S.gameFpsEnabled,
                    gameFpsWanted: gameFpsWanted, gameLive: gameFpsWanted });
    },
    // NFS Underground 2: o masina care accelereaza in bucla pana la 180 km/h.
    "/nfsu2state": function () {
      var wanted = S.nfsu2Enabled && (S.prioHidden & 65536) === 0;
      var t = (Date.now() - BOOT) / 1000;
      var mps = 25 + 25 * Math.sin(t / 5);
      var kmh = Math.floor(mps * 3.6), mph = Math.floor(mps * 2.2369362921);
      return resp({ enabled: S.nfsu2Enabled, wanted: wanted, active: wanted, speed: wanted ? kmh : 0,
                    mph: S.nfsu2Mph, shown: wanted ? (S.nfsu2Mph ? mph : kmh) : 0 });
    },
    "/nfsu2toggle": function (u, i) {
      S.nfsu2Enabled = args(u, i).enabled === "1";
      return resp("OK");
    },
    "/nfsu2speed": function () { return resp("OK"); },
    // NFS Most Wanted: aceeasi bucla, in mph.
    "/nfsmwstate": function () {
      var wanted = S.nfsmwEnabled && (S.prioHidden & 131072) === 0;
      var t = (Date.now() - BOOT) / 1000;
      var mps = 30 + 20 * Math.sin(t / 4);
      var kmh = Math.floor(mps * 3.6), mph = Math.floor(mps * 2.2369362921);
      return resp({ enabled: S.nfsmwEnabled, wanted: wanted, active: wanted, speed: wanted ? kmh : 0,
                    mph: S.nfsmwMph, shown: wanted ? (S.nfsmwMph ? mph : kmh) : 0 });
    },
    "/nfsmwtoggle": function (u, i) {
      S.nfsmwEnabled = args(u, i).enabled === "1";
      return resp("OK");
    },
    "/nfsmwspeed": function () { return resp("OK"); },
    // NFS Carbon: aceeasi bucla, mai repede.
    "/nfscstate": function () {
      var wanted = S.nfscEnabled && (S.prioHidden & 262144) === 0;
      var t = (Date.now() - BOOT) / 1000;
      var mps = 35 + 20 * Math.sin(t / 3);
      var kmh = Math.floor(mps * 3.6), mph = Math.floor(mps * 2.2369362921);
      return resp({ enabled: S.nfscEnabled, wanted: wanted, active: wanted, speed: wanted ? kmh : 0,
                    mph: S.nfscMph, shown: wanted ? (S.nfscMph ? mph : kmh) : 0 });
    },
    "/nfsctoggle": function (u, i) {
      S.nfscEnabled = args(u, i).enabled === "1";
      return resp("OK");
    },
    "/nfscspeed": function () { return resp("OK"); },
    "/volumetoggle": function (u, i) {
      S.volumeEnabled = args(u, i).enabled === "1";
      return resp("OK");
    },
    "/gamefpstoggle": function (u, i) {
      S.gameFpsEnabled = args(u, i).enabled === "1";
      return resp("OK");
    },
    "/abmode": function (u, i) {
      var m = parseInt(args(u, i).mode, 10);
      if (m >= 0 && m <= 2) S.abMode = m;
      return resp("OK");
    },
    "/abstatus": function () { return resp("OK"); },
    // OBS: o inregistrare care a pornit acum 5:42 si curge odata cu demo-ul.
    "/obsstate": function () {
      var on = S.obsEnabled && (S.prioHidden & 512) === 0;
      return resp({ enabled: S.obsEnabled, showElapsed: S.obsShowElapsed, active: on,
        streaming: false, recording: on, elapsedMs: on ? 342000 + (Date.now() - BOOT) : 0 });
    },
    "/obstoggle": function (u, i) {
      var a = args(u, i);
      S.obsEnabled = a.enabled === "1";
      return resp("OK");
    },
    "/obssett": function (u, i) {
      var a = args(u, i);
      if (a.showElapsed !== undefined) S.obsShowElapsed = a.showElapsed === "1";
      return resp("OK");
    },
    "/obsstatus": function () { return resp("OK"); },

    "/swstate": function () {
      var ms = swMs();
      return resp({ running: sw.running, elapsedMs: ms, text: swText(ms) });
    },
    "/swstart": function () {
      if (!sw.running) { sw.since = Date.now(); sw.running = true; }
      else { sw.base = swMs(); sw.running = false; }
      return resp("OK");
    },
    "/swstop": function () { sw.base = 0; sw.running = false; return resp("OK"); },

    "/timerstate": function () {
      var l = tmrLeft();
      // Pomodoro: the next part starts by itself, as on the clock.
      if (S.timerMode === 1 && tmr.running && l <= 0) {
        if (pomo.phase === 0) pomo.phase = pomo.round % S.pomoRounds === 0 ? 2 : 1;
        else { pomo.phase = 0; pomo.round = pomo.round % S.pomoRounds + 1; }
        tmr.left = (pomo.phase === 0 ? S.pomoWork : pomo.phase === 1 ? S.pomoShort : S.pomoLong) * 60;
        tmr.since = Date.now();
        l = tmrLeft();
      }
      return resp({
        running: tmr.running, finished: tmr.finished,
        remainingSec: l, durationSec: S.timerDurationSec, text: tmrText(l),
        mode: S.timerMode, phase: pomo.phase, round: pomo.round, rounds: S.pomoRounds, inCycle: pomo.inCycle
      });
    },
    "/timerstart": function () {
      tmr.finished = false;
      if (S.timerMode === 1 && !pomo.inCycle) { pomo.inCycle = true; pomo.phase = 0; pomo.round = 1; tmr.left = S.pomoWork * 60; }
      if (!tmr.running) { tmr.left = tmr.left || S.timerDurationSec; tmr.since = Date.now(); tmr.running = true; }
      return resp("OK");
    },
    "/timerpause": function (u, i) {
      var a = args(u, i);
      if (a.reset === "1" || a.action === "reset") {
        tmr.running = false; tmr.finished = false; tmr.left = S.timerDurationSec;
      } else if (tmr.running) { tmr.left = tmrLeft(); tmr.running = false; }
      else { tmr.since = Date.now(); tmr.running = true; }
      return resp("OK");
    },
    "/timersett": function (u, i) {
      var a = args(u, i);
      if (a.durationSec) {
        S.timerDurationSec = parseInt(a.durationSec, 10) || S.timerDurationSec;
        if (!tmr.running) { tmr.left = S.timerDurationSec; tmr.finished = false; }
      }
      if (a.preset !== undefined) S.timerPreset = parseInt(a.preset, 10) || 0;
      if (!tmr.running) {
        if (a.mode !== undefined) S.timerMode = a.mode === "1" ? 1 : 0;
        ["pomoWork", "pomoShort", "pomoLong", "pomoRounds"].forEach(function (k) {
          if (a[k] !== undefined) S[k] = parseInt(a[k], 10) || S[k];
        });
        pomo.inCycle = false; pomo.phase = 0; pomo.round = 1;
        tmr.left = S.timerMode === 1 ? S.pomoWork * 60 : S.timerDurationSec;
      }
      return resp("OK");
    },

    "/alarmstate": function () {
      var n = alarmNext();
      return resp({ ringing: false, idx: -1, nextMin: n.nextMin, nextIdx: n.nextIdx, nextTime: n.nextTime });
    },
    "/alarmsett": function (u, i) {
      var a = args(u, i), k = parseInt(a.idx, 10);
      if (!isNaN(k) && S.alarms[k]) {
        var t = S.alarms[k];
        if (a.hour !== undefined) t.h = parseInt(a.hour, 10) || 0;
        if (a.minute !== undefined) t.m = parseInt(a.minute, 10) || 0;
        if (a.days !== undefined) t.d = parseInt(a.days, 10) || 0;
        if (a.enabled !== undefined) t.en = a.enabled === "1";
        if (a.present !== undefined) t.p = a.present === "1";
        if (a.tone) t.tone = a.tone;
      }
      return resp("OK");
    },
    "/alarmstop": function () { return resp("OK"); },

    "/weatherstate": function () {
      return resp({
        valid: true, city: S.wxCity, lat: 46.77, lon: 23.6,
        hasKey: S.wxHasKey, keyLen: 32, lang: S.wxLang,
        temp: 21.6, humidity: 54, desc: "cer senin"
      });
    },
    "/weathersearch": function (u) {
      var q = (args(u).q || "").toLowerCase();
      var all = [
        { name: "Cluj-Napoca", country: "RO", state: "Cluj", lat: 46.77, lon: 23.60 },
        { name: "Bucuresti", country: "RO", state: "Bucuresti", lat: 44.43, lon: 26.10 },
        { name: "Timisoara", country: "RO", state: "Timis", lat: 45.75, lon: 21.23 },
        { name: "Iasi", country: "RO", state: "Iasi", lat: 47.16, lon: 27.59 },
        { name: "Londra", country: "GB", state: "", lat: 51.51, lon: -0.13 }
      ];
      return resp(q ? all.filter(function (c) { return c.name.toLowerCase().indexOf(q) === 0; }) : all);
    },

    "/tilehidden": function (u, i) {
      var a = args(u, i);
      if (a.circuit !== undefined) S.tileHidden = parseInt(a.circuit, 10) || 0;
      if (a.prio !== undefined) S.prioHidden = parseInt(a.prio, 10) || 0;
      return resp("OK");
    },
    "/priorityorder": function (u, i) {
      var a = args(u, i);
      if (a.order) S.priorityOrder = a.order;
      return resp("OK");
    },
    "/settings": function (u, i) {
      var a = args(u, i);
      if (a.vmMode !== undefined) S.vmMode = Math.max(0, Math.min(4, parseInt(a.vmMode, 10) || 0));
      if (a.vmSensitivity !== undefined) S.vmSensitivity = Math.max(10, Math.min(100, parseInt(a.vmSensitivity, 10) || 60));
      if (a.hideIconVoicemeeter !== undefined) S.hideIconVoicemeeter = a.hideIconVoicemeeter === "1";
      if (a.iconSelVoicemeeter !== undefined) S.iconSelVoicemeeter = parseInt(a.iconSelVoicemeeter, 10) || 25;
      if (a.hideIconBrowserDownloads !== undefined) S.hideIconBrowserDownloads = a.hideIconBrowserDownloads === "1";
      if (a.iconSelBrowserDownloads !== undefined) S.iconSelBrowserDownloads = parseInt(a.iconSelBrowserDownloads, 10) || 26;
      if (a.hideIconQbit !== undefined) S.hideIconQbit = a.hideIconQbit === "1";
      if (a.iconSelQbit !== undefined) S.iconSelQbit = parseInt(a.iconSelQbit, 10) || 0;
      if (a.hideIconBambu !== undefined) S.hideIconBambu = a.hideIconBambu === "1";
      if (a.iconSelBambu !== undefined) S.iconSelBambu = parseInt(a.iconSelBambu, 10) || 0;
      if (a.tileTransBrowserDownloads !== undefined) S.tileTransBrowserDownloads = parseInt(a.tileTransBrowserDownloads, 10) || 0;
      if (a.hideIconObs !== undefined) S.hideIconObs = a.hideIconObs === "1";
      if (a.iconSelObs !== undefined) S.iconSelObs = parseInt(a.iconSelObs, 10) || 27;
      if (a.iconSelBattery !== undefined) S.iconSelBattery = parseInt(a.iconSelBattery, 10) || 0;
      if (a.iconSelLowBattery !== undefined) S.iconSelLowBattery = parseInt(a.iconSelLowBattery, 10) || 0;
      if (a.scrollTypeObs !== undefined) S.scrollTypeObs = parseInt(a.scrollTypeObs, 10) || 0;
      if (a.fontTypeObs !== undefined) S.fontTypeObs = parseInt(a.fontTypeObs, 10) || 0;
      ["Ets2", "Beamng", "Assetto", "Alarm", "Battery", "LowBattery"].forEach(function (k) {
        if (a["fontType" + k] !== undefined) S["fontType" + k] = parseInt(a["fontType" + k], 10) || 0;
      });
      if (a.scrollSpeedObs !== undefined) S.scrollSpeedObs = parseInt(a.scrollSpeedObs, 10) || 40;
      if (a.tileTransObs !== undefined) S.tileTransObs = parseInt(a.tileTransObs, 10) || 0;
      if (a.tileTransBeamng !== undefined) S.tileTransBeamng = parseInt(a.tileTransBeamng, 10) || 0;
      if (a.tileTransAssetto !== undefined) S.tileTransAssetto = parseInt(a.tileTransAssetto, 10) || 0;
      if (a.tileTransQbit !== undefined) S.tileTransQbit = parseInt(a.tileTransQbit, 10) || 0;
      if (a.tileTransBambu !== undefined) S.tileTransBambu = parseInt(a.tileTransBambu, 10) || 0;
      if (a.tileTransSpdObs !== undefined) S.tileTransSpdObs = parseInt(a.tileTransSpdObs, 10) || 100;
      if (a.obsShowElapsed !== undefined) S.obsShowElapsed = a.obsShowElapsed === "1";
      if (a.hideIconAfterburner !== undefined) S.hideIconAfterburner = a.hideIconAfterburner === "1";
      ["iconSel", "scrollType", "fontType", "tileTrans"].forEach(function (p) {
        if (a[p + "Afterburner"] !== undefined) S[p + "Afterburner"] = parseInt(a[p + "Afterburner"], 10) || 0;
      });
      if (a.scrollSpeedAfterburner !== undefined) S.scrollSpeedAfterburner = parseInt(a.scrollSpeedAfterburner, 10) || 40;
      if (a.tileTransSpdAfterburner !== undefined) S.tileTransSpdAfterburner = parseInt(a.tileTransSpdAfterburner, 10) || 100;
      if (a.hideIconCountdown !== undefined) S.hideIconCountdown = a.hideIconCountdown === "1";
      ["iconSel", "scrollType", "fontType", "tileTrans"].forEach(function (p) {
        if (a[p + "Countdown"] !== undefined) S[p + "Countdown"] = parseInt(a[p + "Countdown"], 10) || 0;
      });
      if (a.hideIconGameFps !== undefined) S.hideIconGameFps = a.hideIconGameFps === "1";
      ["iconSel", "scrollType", "fontType", "tileTrans"].forEach(function (p) {
        if (a[p + "GameFps"] !== undefined) S[p + "GameFps"] = parseInt(a[p + "GameFps"], 10) || 0;
      });
      if (a.scrollSpeedGameFps !== undefined) S.scrollSpeedGameFps = parseInt(a.scrollSpeedGameFps, 10) || 40;
      if (a.tileTransSpdGameFps !== undefined) S.tileTransSpdGameFps = parseInt(a.tileTransSpdGameFps, 10) || 100;
      if (a.hideIconVolume !== undefined) S.hideIconVolume = a.hideIconVolume === "1";
      if (a.volumeAdaptiveIcon !== undefined) S.volumeAdaptiveIcon = a.volumeAdaptiveIcon === "1";
      ["iconSel", "scrollType", "fontType", "tileTrans"].forEach(function (p) {
        if (a[p + "Volume"] !== undefined) S[p + "Volume"] = parseInt(a[p + "Volume"], 10) || 0;
      });
      if (a.scrollSpeedVolume !== undefined) S.scrollSpeedVolume = parseInt(a.scrollSpeedVolume, 10) || 40;
      if (a.tileTransSpdVolume !== undefined) S.tileTransSpdVolume = parseInt(a.tileTransSpdVolume, 10) || 100;
      if (a.fontTypeNfsu2 !== undefined) S.fontTypeNfsu2 = parseInt(a.fontTypeNfsu2, 10) || 0;
      if (a.tileTransNfsu2 !== undefined) S.tileTransNfsu2 = parseInt(a.tileTransNfsu2, 10) || 0;
      if (a.tileTransSpdNfsu2 !== undefined) S.tileTransSpdNfsu2 = parseInt(a.tileTransSpdNfsu2, 10) || 100;
      if (a.fontTypeNfsmw !== undefined) S.fontTypeNfsmw = parseInt(a.fontTypeNfsmw, 10) || 0;
      if (a.tileTransNfsmw !== undefined) S.tileTransNfsmw = parseInt(a.tileTransNfsmw, 10) || 0;
      if (a.tileTransSpdNfsmw !== undefined) S.tileTransSpdNfsmw = parseInt(a.tileTransSpdNfsmw, 10) || 100;
      if (a.fontTypeNfsc !== undefined) S.fontTypeNfsc = parseInt(a.fontTypeNfsc, 10) || 0;
      if (a.tileTransNfsc !== undefined) S.tileTransNfsc = parseInt(a.tileTransNfsc, 10) || 0;
      if (a.tileTransSpdNfsc !== undefined) S.tileTransSpdNfsc = parseInt(a.tileTransSpdNfsc, 10) || 100;
      return resp("OK");
    },
    // Stilul barei la Browser Downloads, qBittorrent si Bambu Studio.
    "/countdownsett": function (u, i) {
      var a = args(u, i), list = [];
      for (var k = 0; k < 5; k++) {
        var n = String(a["n" + k] || "").trim(), d = String(a["d" + k] || "");
        if (!n || !/^\d{4}-\d{2}-\d{2}$/.test(d)) continue;
        list.push({ n: n.slice(0, 23), d: d, y: a["y" + k] === "1", i: parseInt(a["i" + k], 10) || 0 });
      }
      S.cdEvents = list;
      S.cdText = demoCountdownText();
      return resp({ ok: true, text: S.cdText });
    },
    // Cum scrie tile-ul Evenimente: unitatea intreaga si limba lui.
    "/countdownopt": function (u, i) {
      var a = args(u, i);
      if (a.full !== undefined) S.cdFull = a.full === "1";
      if (a.lang !== undefined) S.cdLang = a.lang === "1" ? 1 : 0;
      S.cdText = demoCountdownText();
      return resp({ ok: true, text: S.cdText });
    },
    // Alertele tile-ului Alerte interfata web.
    "/websett": function (u, i) {
      var a = args(u, i);
      if (a.access !== undefined) S.webAlAccess = a.access === "1";
      if (a.locked !== undefined) S.webAlLocked = a.locked === "1";
      if (a.login !== undefined) S.webAlLogin = a.login === "1";
      if (a.net !== undefined) S.webAlNet = a.net === "1";
      return resp("OK");
    },
    "/hostname": function (u, i) {
      var n = String(args(u, i).name || "").trim().toLowerCase();
      if (!/^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$/.test(n))
        return resp({ ok: false, err: "Numele poate avea litere mici, cifre si cratima (nu la capete), cel mult 32" }, 400);
      S.hostName = n;
      return resp({ ok: true, host: n });
    },
    "/barstyle": function (u, i) {
      var a = args(u, i);
      ["bdBarStyle", "qbitBarStyle", "bambuBarStyle"].forEach(function (k) {
        if (a[k] !== undefined) S[k] = Math.max(0, Math.min(2, parseInt(a[k], 10) || 0));
      });
      return resp("OK");
    },
    // Previzualizarea din setarile unui tile. Ceasul o deseneaza cu codul lui;
    // aici e facuta cu fonturile si iconitele paginii si valori de exemplu.
    "/tilepreview": function (u) {
      return resp(demoPreview(args(u).t || ""));
    },
    "/brightness": function (u, i) {
      var a = args(u, i);
      if (a.level !== undefined) S.bright = parseInt(a.level, 10);
      return resp("OK");
    },
    "/accentsett": function (u, i) {
      var a = args(u, i);
      if (a.hex) S.accentColor = a.hex;
      if (a.shape !== undefined) S.uiShape = parseInt(a.shape, 10) || 0;
      if (a.colors !== undefined) S.uiColors = a.colors;
      if (a.palette !== undefined) S.uiPalette = parseInt(a.palette, 10);
      if (a.dark !== undefined) S.uiDark = a.dark === "1";
      return resp("OK");
    },
    "/power": function (u, i) {
      var a = args(u, i);
      return resp("Demo: actiunea " + (a.action || "?") + " nu opreste nimic aici.");
    },
    "/logout": function () { return resp("OK"); },
    // Cateva chei din fiecare parte ceruta, in formatul ceasului (backupFormat 2).
    "/backup": function (u) {
      var a = args(u), nvs = {}, parts = [];
      if (a.custom === "1") {
        parts.push("custom");
        nvs.settings = {
          uiDark: ["u8", S.uiDark ? 1 : 0], uiLang: ["str", S.uiLang || "ro"],
          bright: ["u8", S.bright || 8], wxCity: ["str", S.wxCity || ""]
        };
      }
      if (a.conn === "1") {
        parts.push("conn");
        nvs.wifi = { ssid: ["str", "Demo"], pass: ["str", "parola-demo"] };
        nvs.apcfg = {};
      }
      if (a.users === "1") {
        parts.push("users");
        nvs.auth = { user: ["str", "Admin"], hash: ["str", Array(65).join("0")] };
      }
      return resp({ device: "Octoglow", backupFormat: 2, firmwareVersion: S.version, parts: parts, nvs: nvs });
    }
  };

  // Iconita evenimentului cel mai apropiat (0 = calendarul).
  var demoCdIcon = 0;
  function demoCountdownText() {
    var now = new Date(), today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()), best = null;
    (S.cdEvents || []).forEach(function (e) {
      var p = e.d.split("-").map(Number), when;
      if (e.y) {
        when = Date.UTC(now.getFullYear(), p[1] - 1, p[2]);
        if (when < today) when = Date.UTC(now.getFullYear() + 1, p[1] - 1, p[2]);
      } else when = Date.UTC(p[0], p[1] - 1, p[2]);
      var days = Math.round((when - today) / 864e5);
      if (days >= 0 && (!best || days < best.days)) best = { n: e.n, days: days, i: e.i || 0 };
    });
    demoCdIcon = best ? best.i : 0;
    if (!best) return "";
    return best.n + " " + demoCdDays(best.days);
  }
  // countdownText() din Octoglow.ino: "89z" sau "89 de zile", "89d" sau "89 days".
  function demoCdDays(days) {
    var ro = !S.cdLang;
    if (days === 0) return ro ? "azi" : "today";
    if (!S.cdFull) return days + (ro ? "z" : "d");
    if (days === 1) return ro ? "1 zi" : "1 day";
    if (!ro) return days + " days";
    var tail = days % 100;
    return days + (tail === 0 || tail >= 20 ? " de zile" : " zile");
  }
  S.cdText = demoCountdownText();

  // -------------------------------------------------------------- /tilepreview
  function demoCols() { var c = []; for (var i = 0; i < 32; i++) c.push(0); return c; }
  function demoHex(c) {
    return c.map(function (v) { v = v & 255; return (v < 16 ? "0" : "") + v.toString(16); }).join("");
  }
  function demoIcon(c, rows) {
    if (!rows) return;
    for (var x = 0; x < 8; x++)
      for (var y = 0; y < 8; y++) if (rows[y] & (0x80 >> x)) c[x] |= 1 << y;
  }
  function demoRows(key) {
    try { return defaultIconRowsFor("tile", key); } catch (e) { return null; }
  }
  // progressBarFrame() din Octoglow.ino
  function demoBar(style, p, rows) {
    var c = demoCols(), i;
    demoIcon(c, rows);
    if (style === 0) {
      var start = rows ? 10 : 1, width = rows ? 22 : 30, filled = Math.floor((p * width + 99) / 100);
      for (i = 0; i < filled; i++) c[start + i] |= 0x18;
      return c;
    }
    var left = rows ? 10 : 0, inner = 31 - left - 1, n = Math.floor(p * inner / 100);
    if (p > 0 && n === 0) n = 1;
    for (i = left; i <= 31; i++) c[i] |= 0x24;
    c[left] |= 0x3C; c[31] |= 0x3C;
    if (style === 2) { c[left] &= ~0x24; c[31] &= ~0x24; }
    for (i = 0; i < n; i++) c[left + 1 + i] |= 0x18;
    return c;
  }
  function demoBars(level) {
    var c = demoCols();
    for (var x = 0; x < 32; x++) {
      var h = Math.max(1, Math.min(8, Math.round(level(x) * 8)));
      for (var y = 8 - h; y < 8; y++) c[x] |= 1 << y;
    }
    return c;
  }
  function demoPreview(key) {
    var c = demoCols(), now = new Date(), r = { live: false, sample: true }, frames = null, f;
    // Iconita, apoi textul in cele 23 de coloane de dupa ea; cel mai lung deruleaza.
    function text(s, font, full) {
      var cols = fontText(font || 0, s), x = full ? 0 : 9, w = full ? 32 : 23;
      if (cols.length <= w) {
        var off = x + Math.floor((w - cols.length) / 2);
        cols.forEach(function (v, i) { c[off + i] = v; });
      } else {
        cols.slice(0, w).forEach(function (v, i) { c[x + i] = v; });
        r.scroll = { x: x, w: w, wrap: false, ms: 40, pause: 1000, buf: demoHex(cols) };
      }
    }
    var two = function (n) { return ("0" + n).slice(-2); };
    switch (key) {
      case "0": c = fontClock(S.fontType || 0, now.getHours(), now.getMinutes(), true); r.sample = false; break;
      case "1": demoIcon(c, demoRows("date")); text(two(now.getDate()) + "." + two(now.getMonth() + 1)); r.sample = false; break;
      case "2": demoIcon(c, demoRows("temp")); text("23C"); break;
      case "3": case "nowplaying": demoIcon(c, demoRows("nowplayingMusic")); text("Daft Punk - Get Lucky"); break;
      case "4": demoIcon(c, defaultIconRowsFor("wx", "cloud")); text("18C Cluj-Napoca"); break;
      case "5": demoIcon(c, demoRows("reminder")); text("Memento"); break;
      case "8": demoIcon(c, demoRows("pressure")); text("1013"); break;
      case "10": text(two(now.getHours()) + ":" + two(now.getMinutes()) + " SAT", 1, true); r.sample = false; break;
      case "11": demoIcon(c, demoRows("currency")); text("4.97"); break;
      case "12": text("1.2K", 0, true); break;
      case "13": case "lowbattery": demoIcon(c, demoRows("battery")); text("85%"); break;
      case "volume":
        if (!S.hideIconVolume) demoIcon(c, getIconCatalogEntry(S.volumeAdaptiveIcon ? 38 : (S.iconSelVolume || 38)).rows);
        text("50%", S.fontTypeVolume || 0, !!S.hideIconVolume);
        break;
      case "14": case "gamefps": demoIcon(c, demoRows("afterburner")); text("FPS 144"); break;
      case "15": demoIcon(c, (demoCdIcon && getIconCatalogEntry(demoCdIcon) || {}).rows || demoRows("countdown")); text(S.cdText || (S.cdLang ? "Christmas " : "Craciun ") + demoCdDays(89)); break;
      case "obs": demoIcon(c, demoRows("obs")); text(S.obsShowElapsed ? "REC 5:42" : "REC"); break;
      case "browserdownloads": c = demoBar(S.bdBarStyle, 64, demoRows("browserdownloads")); break;
      case "qbittorrent": c = demoBar(S.qbitBarStyle, 64, demoRows("qbittorrent")); break;
      case "bambu": c = demoBar(S.bambuBarStyle, 64, demoRows("bambu")); break;
      case "alarm": case "stopwatch": case "timer": text(key === "timer" ? "05:00" : "00:00:00", 0, true); break;
      case "ets2": case "beamng": case "assetto": case "nfsu2": case "nfsmw": case "nfsc":
        text(S[key + "Mph"] ? "55mph" : "88km", S["fontType" + key.charAt(0).toUpperCase() + key.slice(1)] || 0, true); break;
      case "9": case "voicemeeter":
        frames = [];
        for (f = 0; f < 48; f++) {
          var fc = demoBars(function (x) { return (1 - x / 40) * (0.55 + 0.4 * Math.sin(f * 0.5 + x * 0.7)); });
          if (key === "voicemeeter") {
            var shown = demoCols(), rows = demoRows("voicemeeter");
            demoIcon(shown, rows);
            for (var x = 9; x < 32; x++) shown[x] = fc[Math.floor((x - 9) * 32 / 23)];
            fc = shown;
          }
          frames.push(demoHex(fc));
        }
        r.anim = { ms: 80, frames: frames };
        break;
      default:
        return { live: false, sample: false, px: demoHex(c) };
    }
    r.px = frames ? frames[0] : demoHex(c);
    return r;
  }

  var realFetch = window.fetch ? window.fetch.bind(window) : null;

  window.fetch = function (input, init) {
    var url = typeof input === "string" ? input : (input && input.url) || "";
    // Doar cererile catre ceas sunt interceptate; restul (fonturi etc.) trec.
    if (url.charAt(0) !== "/") return realFetch ? realFetch(input, init) : resp("OK");
    var path = url.split("?")[0];
    var h = ROUTES[path];
    if (h) return h(url, init);
    // Restul salvarilor: raspunde OK, ca interfata sa isi vada schimbarea.
    return resp("OK");
  };

  // -------------------------------------------------------------- eticheta
  function badge() {
    if (document.getElementById("demo-badge")) return;
    // Eticheta pluteste peste pagina, deci ultimul rand din fiecare lista are
    // nevoie de loc sub el ca sa nu ramana ascuns cand ai derulat pana jos.
    var css = document.createElement("style");
    css.textContent = ".content{padding-bottom:64px}";
    document.head.appendChild(css);
    var b = document.createElement("div");
    b.id = "demo-badge";
    b.textContent = "DEMO · fara ceas conectat";
    b.title = "Apasa pentru a ascunde";
    b.setAttribute("style", [
      "position:fixed", "left:50%", "transform:translateX(-50%)",
      "bottom:calc(12px + env(safe-area-inset-bottom,0px))", "z-index:99999",
      "padding:7px 14px", "border-radius:999px",
      "font:500 12px/1 Roboto,system-ui,sans-serif", "letter-spacing:.4px",
      "background:rgba(20,20,24,.86)", "color:#fff",
      "border:1px solid rgba(255,255,255,.18)",
      "box-shadow:0 4px 16px rgba(0,0,0,.35)",
      "cursor:pointer", "-webkit-backdrop-filter:blur(6px)", "backdrop-filter:blur(6px)"
    ].join(";"));
    b.onclick = function () { b.remove(); };
    document.body.appendChild(b);
  }
  if (document.readyState === "loading")
    document.addEventListener("DOMContentLoaded", badge);
  else badge();

  // Inlocuieste navigarile care ar parasi pagina (logout, reset din fabrica).
  window.__demoNav = function () {
    var b = document.getElementById("demo-badge");
    if (!b) { badge(); b = document.getElementById("demo-badge"); }
    b.textContent = "DEMO · nu exista unde naviga";
    setTimeout(function () { b.textContent = "DEMO · fara ceas conectat"; }, 2000);
  };
})();
