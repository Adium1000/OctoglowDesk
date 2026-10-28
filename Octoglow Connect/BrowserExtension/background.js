const ENDPOINT = "http://127.0.0.1:38742/v1/downloads";
const HEARTBEAT = "octoglow-heartbeat";

async function clientId() {
  const stored = await chrome.storage.local.get("clientId");
  if (stored.clientId) return stored.clientId;
  const id = crypto.randomUUID();
  await chrome.storage.local.set({ clientId: id });
  return id;
}

async function publish() {
  try {
    const downloads = await chrome.downloads.search({ state: "in_progress" });
    let received = 0;
    let total = 0;
    let fallbackSum = 0;
    let fallbackCount = 0;
    for (const item of downloads) {
      const done = Math.max(0, Number(item.bytesReceived) || 0);
      const size = Math.max(0, Number(item.totalBytes) || 0);
      received += Math.min(done, size || done);
      if (size > 0) {
        total += size;
        fallbackSum += Math.min(100, done * 100 / size);
        fallbackCount++;
      }
    }
    const progress = total > 0 ? received * 100 / total : (fallbackCount ? fallbackSum / fallbackCount : 0);
    await fetch(ENDPOINT, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        clientId: await clientId(),
        activeCount: downloads.length,
        receivedBytes: received,
        totalBytes: total,
        progress
      })
    });
  } catch (_) {
    // Octoglow Connect may be closed; the next event or heartbeat tries again.
  }
}

chrome.downloads.onCreated.addListener(publish);
chrome.downloads.onChanged.addListener(publish);
chrome.downloads.onErased.addListener(publish);
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === HEARTBEAT) publish(); });
chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(HEARTBEAT, { periodInMinutes: 0.5 });
  publish();
});
chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create(HEARTBEAT, { periodInMinutes: 0.5 });
  publish();
});
chrome.alarms.create(HEARTBEAT, { periodInMinutes: 0.5 });
publish();
