#pragma once

#include <WebServer.h>
#include <lwip/sockets.h>
#include <errno.h>
#include <nvs.h>

// WebServer clears uri() before handleClient() returns. Capture it while the
// response header is written so diagnostics can actually identify a slow route.
class MeasuredWebServer : public WebServer {
public:
  using WebServer::WebServer;
  char handledUri[28] = "";

  // FLASH: the sketch names a request argument, a header or a route with a
  // string literal some 750 times - server.arg("x"), server.hasArg("x"),
  // server.on("/x", HTTP_GET, handleX). WebServer takes a String (or a Uri and
  // a std::function), so each of those lines built the temporary, called and
  // destroyed it on the spot. These overloads take the literal as it is and
  // build the temporary here, once, out of line. The calls read the same and do
  // the same; every other form (a String, an index, the upload route) still
  // goes to WebServer through the using-declarations.
  using WebServer::arg;
  using WebServer::hasArg;
  using WebServer::header;
  using WebServer::hasHeader;
  using WebServer::sendHeader;
  using WebServer::on;
  __attribute__((noinline)) String arg(const char* name) const { return WebServer::arg(String(name)); }
  __attribute__((noinline)) bool hasArg(const char* name) const { return WebServer::hasArg(String(name)); }
  __attribute__((noinline)) String header(const char* name) const { return WebServer::header(String(name)); }
  __attribute__((noinline)) bool hasHeader(const char* name) const { return WebServer::hasHeader(String(name)); }
  __attribute__((noinline)) void sendHeader(const char* name, const String& value) { WebServer::sendHeader(String(name), value); }
  __attribute__((noinline)) void sendHeader(const char* name, const char* value) { WebServer::sendHeader(String(name), String(value)); }
  __attribute__((noinline)) RequestHandler& on(const char* uri, HTTPMethod method, void (*fn)()) {
    return WebServer::on(Uri(uri), method, THandlerFunction(fn));
  }
protected:
  size_t _currentClientWrite(const char* data, size_t length) override {
    if (_currentUri.length()) {
      strncpy(handledUri, _currentUri.c_str(), sizeof(handledUri) - 1);
      handledUri[sizeof(handledUri) - 1] = '\0';
    }
    return WebServer::_currentClientWrite(data, length);
  }
};

// Only the loop task owns these clients. A copy keeps the socket alive after
// WebServer releases its request; the body stays in memory-mapped flash.
// Four slots: a browser asks for both fonts while the page is still arriving,
// and a second browser may be loading at the same time.
class GzipPageTransfers {
  struct Transfer {
    WiFiClient client;
    const uint8_t* body = nullptr;
    size_t length = 0, offset = 0;
    uint32_t startedUs = 0, progressMs = 0;
  } slots[4];
public:
  bool available() const {
    for (const auto& s : slots) if (!s.body) return true;
    return false;
  }
  bool start(const WiFiClient& client, const uint8_t* body, size_t length) {
    for (auto& s : slots) {
      if (s.body) continue;
      s.client = client;
      s.body = body;
      s.length = length;
      s.offset = 0;
      s.startedUs = micros();
      s.progressMs = millis();
      return true;
    }
    return false;
  }
  void tick(uint32_t& maxUs) {
    for (auto& s : slots) {
      if (!s.body) continue;
      // One nonblocking socket write per client, per pass. Never wait for an
      // ACK or a slow browser while the matrix needs its next frame.
      const size_t remaining = s.length - s.offset;
      const size_t count = remaining < 1460 ? remaining : 1460;
      const int sent = ::send(s.client.fd(), s.body + s.offset, count, MSG_DONTWAIT);
      const int error = sent < 0 ? errno : 0;
      if (sent > 0) {
        s.offset += (size_t)sent;
        s.progressMs = millis();
      }
      if (s.offset == s.length || sent == 0 ||
          (sent < 0 && error != EAGAIN && error != EWOULDBLOCK && error != EINTR) ||
          (uint32_t)(millis() - s.progressMs) >= 5000UL) {
        uint32_t elapsed = micros() - s.startedUs;
        if (elapsed > maxUs) maxUs = elapsed;
        s.client.stop();
        s.body = nullptr;
      }
    }
  }
};

// PACKED ONE-BYTE SETTINGS
//
// The NVS partition is 20 KB, and every key in it takes at least one 32-byte
// entry of its own. Well over 200 of the "settings" keys are a single byte - a
// font, an icon, a scroll type, a switch - and with the Wi-Fi driver's own data
// beside them the partition was full by the NFS Carbon build: a key that did
// not exist yet could no longer be written, so Game FPS and the three NFS tiles
// came back at every start as if just installed, out of the Tile Manager and
// with their settings gone (and the priority order stayed at its old length).
//
// So every one-byte setting now travels in the one blob "cfgPack", as
// (FNV-1a hash of its key, value) records sorted by hash - some 45 entries for
// all of them. The key names stay as they were: loadSettings() looks a name up
// in the pack first and then as a key of its own (a clock that has not saved
// since the update), and the save that writes the pack erases those keys. A
// hash rather than a list of names in a fixed order, so a new setting needs
// nothing here - putUChar()/putBool() pack whatever they are given.
#define CFG_PACK_KEY  "cfgPack"
#define CFG_PACK_MAX  512   // records - far more one-byte settings than there are
#define CFG_PACK_REC  5     // the key's hash, little-endian, then the value

static inline uint32_t cfgKeyHash(const char* key) {
  uint32_t h = 2166136261u;
  while (*key) {
    h ^= (uint8_t)*key++;
    h *= 16777619u;
  }
  return h;
}

static inline uint32_t cfgPackHashAt(const uint8_t* pack, size_t i) {
  const uint8_t* p = pack + i * CFG_PACK_REC;
  return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

static inline bool cfgPackFind(const uint8_t* pack, size_t count, const char* key, uint8_t& value) {
  if (!pack) return false;
  uint32_t h = cfgKeyHash(key);
  size_t lo = 0, hi = count;
  while (lo < hi) {
    size_t mid = (lo + hi) / 2;
    uint32_t m = cfgPackHashAt(pack, mid);
    if (m == h) {
      value = pack[mid * CFG_PACK_REC + 4];
      return true;
    }
    if (m < h) lo = mid + 1;
    else hi = mid;
  }
  return false;
}

// Same NVS namespace, keys and types as Preferences. Skip unchanged values
// and commit the changed keys together. One-byte values go into the pack above.
class SettingsWriter {
  nvs_handle_t handle = 0;
  esp_err_t error = ESP_OK;
  bool changed = false;
  uint8_t* pack = nullptr;   // CFG_PACK_MAX records, filled by putUChar()
  size_t packCount = 0;
  void record(esp_err_t result) {
    if (result == ESP_OK) changed = true;
    else if (error == ESP_OK) error = result;
  }
  // False when there is no pack to put it in (no memory for it, or full): the
  // value is then written as a key of its own, as before the pack.
  bool packPut(const char* key, uint8_t value) {
    if (!pack) return false;
    uint32_t h = cfgKeyHash(key);
    for (size_t i = 0; i < packCount; i++) {
      if (cfgPackHashAt(pack, i) == h) {
        pack[i * CFG_PACK_REC + 4] = value;
        return true;
      }
    }
    if (packCount >= CFG_PACK_MAX) return false;
    uint8_t* p = pack + packCount++ * CFG_PACK_REC;
    p[0] = (uint8_t)h;
    p[1] = (uint8_t)(h >> 8);
    p[2] = (uint8_t)(h >> 16);
    p[3] = (uint8_t)(h >> 24);
    p[4] = value;
    return true;
  }
  void sortPack() {
    uint8_t rec[CFG_PACK_REC];
    for (size_t i = 1; i < packCount; i++) {
      memcpy(rec, pack + i * CFG_PACK_REC, CFG_PACK_REC);
      uint32_t h = cfgPackHashAt(pack, i);
      size_t j = i;
      for (; j > 0 && cfgPackHashAt(pack, j - 1) > h; j--)
        memcpy(pack + j * CFG_PACK_REC, pack + (j - 1) * CFG_PACK_REC, CFG_PACK_REC);
      memcpy(pack + j * CFG_PACK_REC, rec, CFG_PACK_REC);
    }
  }
  // Erases the one-byte keys of settings the pack holds. The names are gathered
  // first: erasing while iterating would take entries from under the iterator.
  bool purgePacked() {
    char (*names)[NVS_KEY_NAME_MAX_SIZE] =
      (char (*)[NVS_KEY_NAME_MAX_SIZE])malloc((packCount ? packCount : 1) * NVS_KEY_NAME_MAX_SIZE);
    if (!names) return false;
    size_t n = 0;
    nvs_iterator_t it = nullptr;
    esp_err_t res = nvs_entry_find_in_handle(handle, NVS_TYPE_U8, &it);
    while (res == ESP_OK && n < packCount) {
      nvs_entry_info_t info;
      uint8_t v;
      nvs_entry_info(it, &info);
      if (cfgPackFind(pack, packCount, info.key, v)) {
        strncpy(names[n], info.key, NVS_KEY_NAME_MAX_SIZE - 1);
        names[n][NVS_KEY_NAME_MAX_SIZE - 1] = '\0';
        n++;
      }
      res = nvs_entry_next(&it);
    }
    nvs_release_iterator(it);
    for (size_t i = 0; i < n; i++)
      if (nvs_erase_key(handle, names[i]) == ESP_OK) changed = true;
    free(names);
    return n > 0;
  }
  void writePack() {
    sortPack();
    size_t len = packCount * CFG_PACK_REC, oldLen = 0;
    bool same = false;
    if (nvs_get_blob(handle, CFG_PACK_KEY, nullptr, &oldLen) == ESP_OK && oldLen == len) {
      uint8_t* old = (uint8_t*)malloc(len ? len : 1);
      if (old && nvs_get_blob(handle, CFG_PACK_KEY, old, &oldLen) == ESP_OK)
        same = memcmp(old, pack, len) == 0;
      free(old);
    }
    if (!same) {
      esp_err_t r = nvs_set_blob(handle, CFG_PACK_KEY, pack, len);
      // A full partition: the single keys the pack replaces make the room.
      if (r == ESP_ERR_NVS_NOT_ENOUGH_SPACE && purgePacked())
        r = nvs_set_blob(handle, CFG_PACK_KEY, pack, len);
      record(r);
      // Without the pack the single keys are all there is; keep them.
      if (r != ESP_OK) return;
    }
    purgePacked();
  }
public:
  bool begin() {
    error = nvs_open("settings", NVS_READWRITE, &handle);
    if (error == ESP_OK) pack = (uint8_t*)malloc(CFG_PACK_MAX * CFG_PACK_REC);
    return error == ESP_OK;
  }
  void putUChar(const char* key, uint8_t value) {
    if (packPut(key, value)) return;
    uint8_t old;
    if (nvs_get_u8(handle, key, &old) != ESP_OK || old != value)
      record(nvs_set_u8(handle, key, value));
  }
  void putBool(const char* key, bool value) { putUChar(key, value ? 1 : 0); }
  void putUShort(const char* key, uint16_t value) {
    uint16_t old;
    if (nvs_get_u16(handle, key, &old) != ESP_OK || old != value)
      record(nvs_set_u16(handle, key, value));
  }
  void putUInt(const char* key, uint32_t value) {
    uint32_t old;
    if (nvs_get_u32(handle, key, &old) != ESP_OK || old != value)
      record(nvs_set_u32(handle, key, value));
  }
  void putString(const char* key, const char* value) {
    char old[256];
    size_t size = sizeof(old);
    if (nvs_get_str(handle, key, old, &size) != ESP_OK || strcmp(old, value) != 0)
      record(nvs_set_str(handle, key, value));
  }
  void putBytes(const char* key, const void* value, size_t length) {
    uint8_t old[256];
    size_t size = sizeof(old);
    if (length > sizeof(old) || nvs_get_blob(handle, key, old, &size) != ESP_OK ||
        size != length || memcmp(old, value, length) != 0)
      record(nvs_set_blob(handle, key, value, length));
  }
  void putFloat(const char* key, float value) { putBytes(key, &value, sizeof(value)); }
  bool end() {
    if (pack) {
      writePack();
      free(pack);
      pack = nullptr;
    }
    if (changed) {
      esp_err_t result = nvs_commit(handle);
      if (error == ESP_OK) error = result;
    }
    nvs_close(handle);
    return error == ESP_OK;
  }
};

// Fixed-capacity JSON staging buffer: append arbitrary-length fragments without
// growing a String. Numbers are formatted on the stack, with no heap temporary.
class ChunkedResponse {
  WebServer& server;
  char buf[1024];
  size_t used = 0;
public:
  explicit ChunkedResponse(WebServer& srv) : server(srv) {}
  void begin(const char* contentType) {
    server.client().setNoDelay(true);
    server.setContentLength(CONTENT_LENGTH_UNKNOWN);
    server.send(200, contentType, "");
  }
  ChunkedResponse& operator+=(const char* s) { append(s); return *this; }
  ChunkedResponse& operator+=(const String& s) { append(s.c_str()); return *this; }
  ChunkedResponse& operator+=(int value) { return *this += (long)value; }
  ChunkedResponse& operator+=(unsigned int value) { return *this += (unsigned long)value; }
  ChunkedResponse& operator+=(long value) {
    char number[24];
    snprintf(number, sizeof(number), "%ld", value);
    append(number);
    return *this;
  }
  ChunkedResponse& operator+=(unsigned long value) {
    char number[24];
    snprintf(number, sizeof(number), "%lu", value);
    append(number);
    return *this;
  }
  void escaped(const String& s) { escaped(s.c_str()); }
  void escaped(const char* s) {
    for (; *s; ++s) {
      switch (*s) {
        case '"': append("\\\""); break;
        case '\\': append("\\\\"); break;
        case '\n': append("\\n"); break;
        case '\r': append("\\r"); break;
        case '\t': append("\\t"); break;
        default: {
          char fragment[7] = {*s, '\0'};
          if ((uint8_t)*s < 0x20)
            snprintf(fragment, sizeof(fragment), "\\u%04x", (unsigned)(uint8_t)*s);
          append(fragment);
        }
      }
    }
  }
  void append(const char* s) {
    size_t left = strlen(s);
    while (left) {
      size_t n = sizeof(buf) - used;
      if (n > left) n = left;
      memcpy(buf + used, s, n);
      used += n;
      s += n;
      left -= n;
      if (used == sizeof(buf)) flush();
    }
  }
  void flush() {
    if (!used) return;
    server.sendContent(buf, used);
    used = 0;
  }
  void end() { flush(); server.sendContent(""); }
};
