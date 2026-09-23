// STORY-010 walking skeleton, extended in Phase 2 with device pairing.
// Talks to the Sifra native messaging host, which wraps the
// already-tested CredentialAutofillService (C#). This file only relays
// messages and holds the unlocked vault password for the browser session.
//
// The password lives in chrome.storage.session, not a module-level
// variable: MV3 service workers are unloaded after ~30s idle and lose
// all in-memory state, but chrome.storage.session survives a service
// worker restart while still being memory-only (never written to disk)
// and cleared when the browser closes — the same "session-held
// plaintext credential" trade-off already flagged for VaultSession
// (STORY-004) and GoogleDriveSyncProvider (STORY-007), just persisted
// correctly for this runtime's lifecycle model.
//
// Phase 2: the device id/secret obtained once via pairing lives in
// chrome.storage.local instead — unlike the vault password, it's meant to
// persist across browser restarts (re-pairing every time Chrome reopens
// would defeat the point), and it's not the vault secret itself, just this
// browser's proof that it was deliberately paired.

const NATIVE_HOST_NAME = "com.sifra.nativehost";
const SESSION_KEY = "vaultPassword";
const DEVICE_STORAGE_KEY = "pairedDevice"; // { deviceId, deviceSecret }

// Native-host errors that mean this browser's pairing is no longer valid —
// clearing local storage on these makes the popup fall back to "Pair with
// Desktop" instead of silently failing autofill forever.
const DEVICE_INVALID_ERRORS = new Set([
  "DeviceNotEnrolledException",
  "InvalidDeviceSecretException",
  "DeviceRevokedException",
]);

// Toolbar badge shows how many saved credentials match the current tab's
// page, so a multi-credential site (see content.js's picker) is visible
// before ever opening the popup or a login form's consent banner.
const BADGE_COLOR = "#2563eb";

function setMatchBadge(tabId, count) {
  if (typeof tabId !== "number") return;
  chrome.action.setBadgeBackgroundColor({ tabId, color: BADGE_COLOR });
  chrome.action.setBadgeText({ tabId, text: count > 0 ? String(count) : "" });
}

// Cleared on every navigation start, not just overwritten on the next
// DISCOVER response — a page with no login form at all never sends DISCOVER,
// so without this the badge from the previous page in this tab would stick
// around forever.
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.status === "loading") {
    setMatchBadge(tabId, 0);
  }
});

let nativePort = null;
// Sifra.NativeHost processes one full request/response cycle per loop
// iteration (see Program.cs) — no concurrency on its side — so responses
// arrive strictly in the order requests were sent. A FIFO queue is enough
// to route each response back to its caller without needing the host to
// echo a request id. Pairing can block the host for up to ~90s (see
// PerformPairingAsync), so a request queued behind a pairing request will
// simply wait its turn like any other — there is no separate fast path.
const pendingRequests = [];

function getVaultPassword() {
  return chrome.storage.session.get(SESSION_KEY).then((result) => result[SESSION_KEY] ?? null);
}

function getPairedDevice() {
  return chrome.storage.local.get(DEVICE_STORAGE_KEY).then((result) => result[DEVICE_STORAGE_KEY] ?? null);
}

function clearPairedDevice() {
  return chrome.storage.local.remove(DEVICE_STORAGE_KEY);
}

function getNativePort() {
  if (!nativePort) {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST_NAME);
    nativePort.onMessage.addListener((response) => {
      const resolve = pendingRequests.shift();
      if (resolve) resolve(response);
    });
    nativePort.onDisconnect.addListener(() => {
      nativePort = null;
      const error = chrome.runtime.lastError?.message ?? "Native host disconnected.";
      while (pendingRequests.length > 0) {
        pendingRequests.shift()({ ok: false, error: "NativeHostDisconnected", message: error });
      }
    });
  }
  return nativePort;
}

function sendNativeRequest(message) {
  return new Promise((resolve) => {
    pendingRequests.push(resolve);
    getNativePort().postMessage(message);
  });
}

// Wraps a device-gated request: attaches the paired device's credentials,
// and clears them if the native host reports this device is no longer
// valid — so the next STATUS check correctly falls back to "unpaired".
function sendDeviceRequest(action, extraFields) {
  return getPairedDevice().then((device) => {
    if (!device) {
      return { ok: false, needsPairing: true };
    }

    return sendNativeRequest({
      action,
      deviceId: device.deviceId,
      deviceSecret: device.deviceSecret,
      ...extraFields,
    }).then((response) => {
      if (!response.ok && DEVICE_INVALID_ERRORS.has(response.error)) {
        return clearPairedDevice().then(() => ({ ...response, needsPairing: true }));
      }
      return response;
    });
  });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === "STATUS") {
    Promise.all([getPairedDevice(), getVaultPassword()]).then(([device, password]) => {
      sendResponse({ ok: true, paired: device !== null, unlocked: password !== null });
    });
    return true;
  }

  if (message.type === "PAIR") {
    sendNativeRequest({ action: "pair", browserName: "Chrome" }).then((response) => {
      if (response.ok) {
        chrome.storage.local
          .set({ [DEVICE_STORAGE_KEY]: { deviceId: response.deviceId, deviceSecret: response.deviceSecret } })
          .then(() => sendResponse(response));
      } else {
        sendResponse(response);
      }
    });
    return true;
  }

  if (message.type === "UNLOCK") {
    // Actually verifies the password against the vault (via the native
    // host's "verify" action, which just tries to unwrap the vault's
    // master key) before caching anything — previously this cached
    // whatever was typed unconditionally, so a wrong password silently
    // "unlocked" and only failed later, confusingly, whenever autofill
    // next tried to decrypt something.
    getPairedDevice().then((device) => {
      if (!device) {
        sendResponse({ ok: false, needsPairing: true });
        return;
      }

      sendNativeRequest({
        action: "verify",
        deviceId: device.deviceId,
        deviceSecret: device.deviceSecret,
        vaultCredential: message.password,
      }).then((response) => {
        if (!response.ok && DEVICE_INVALID_ERRORS.has(response.error)) {
          clearPairedDevice().then(() => sendResponse({ ...response, needsPairing: true }));
          return;
        }
        if (!response.ok) {
          sendResponse(response);
          return;
        }
        chrome.storage.session.set({ [SESSION_KEY]: message.password }).then(() => sendResponse({ ok: true }));
      });
    });
    return true;
  }

  if (message.type === "LOCK") {
    chrome.storage.session.remove(SESSION_KEY).then(() => sendResponse({ ok: true }));
    return true;
  }

  if (message.type === "DISCOVER") {
    getVaultPassword().then((vaultPassword) => {
      if (!vaultPassword) {
        sendResponse({ ok: false, needsUnlock: true });
        return;
      }
      sendDeviceRequest("discover", { vaultCredential: vaultPassword, url: message.url }).then((response) => {
        setMatchBadge(sender.tab?.id, response.ok ? response.credentials.length : 0);
        sendResponse(response);
      });
    });
    return true; // keep the message channel open for the async response
  }

  if (message.type === "FILL") {
    getVaultPassword().then((vaultPassword) => {
      if (!vaultPassword) {
        sendResponse({ ok: false, needsUnlock: true });
        return;
      }
      sendDeviceRequest("fill", {
        vaultCredential: vaultPassword,
        url: message.url,
        credentialId: message.credentialId,
        consent: true, // only ever sent in response to an explicit user click — see content.js
      }).then(sendResponse);
    });
    return true;
  }

  return false;
});
